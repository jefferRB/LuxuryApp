using System.Globalization;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.WhatsApp
{
    /// <summary>
    /// Proyección de solo lectura de la bandeja WhatsApp. Todas las consultas quedan
    /// filtradas por tenant automáticamente por el global query filter de ITenantEntity.
    /// Reutiliza el WhatsAppMessageLog existente; no crea ni modifica entidades.
    /// </summary>
    public sealed class WhatsAppInboxService : IWhatsAppInboxService
    {
        private static readonly CultureInfo CostaRica = CultureInfo.GetCultureInfo("es-CR");

        private static readonly string[] SentLikeStatuses =
        [
            WhatsAppMessageStatuses.Sent,
            WhatsAppMessageStatuses.Delivered,
            WhatsAppMessageStatuses.Read
        ];

        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public WhatsAppInboxService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<WhatsAppInboxResponse> GetInboxAsync(
            DateTime date,
            int? funcionarioId,
            bool whatsAppEnabled,
            CancellationToken cancellationToken = default)
        {
            var startDay = date.Date;
            var endDay = startDay.AddDays(1);

            var query = _context.Citas
                .AsNoTracking()
                .Where(c => c.Tipo == "CITA" && c.FechaHoraCita >= startDay && c.FechaHoraCita < endDay);

            if (funcionarioId.HasValue && funcionarioId.Value > 0)
            {
                query = query.Where(c => c.FuncionarioId == funcionarioId.Value);
            }

            var citas = await query
                .OrderBy(c => c.FechaHoraCita)
                .Select(c => new CitaProjection
                {
                    Id = c.Id,
                    NombreCliente = c.NombreCliente,
                    Telefono = c.TelefonoCliente,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : null,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    FechaHoraCita = c.FechaHoraCita,
                    EstadoConfirmacionWhatsApp = c.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = c.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = c.RecordatorioWhatsAppTresHorasEnviadoUtc,
                    ConfirmadaPorWhatsAppUtc = c.ConfirmadaPorWhatsAppUtc,
                    ClienteId = c.ClienteId,
                    ClienteAcepta = c.Cliente != null ? (bool?)c.Cliente.AceptaMensajesWhatsApp : null,
                    WhatsAppConsentAtCreation = c.WhatsAppConsentAtCreation
                })
                .ToListAsync(cancellationToken);

            var latestLogs = await LoadLatestOutboundLogMapAsync(
                citas.Select(c => c.Id),
                cancellationToken);

            var nowUtc = _businessDateTimeProvider.NowOffset().UtcDateTime;

            var items = citas
                .Select(c => BuildItem(c, latestLogs.GetValueOrDefault(c.Id), whatsAppEnabled, nowUtc))
                .ToList();

            var stats = new WhatsAppInboxStats
            {
                Enviados = items.Count(i => i.WaStatusKey is "sent" or "reminder" or "confirmed"),
                Confirmados = items.Count(i => i.EstadoCitaKey == "confirmed"),
                Pendientes = items.Count(i => i.EstadoCitaKey == "pending"),
                Fallidos = items.Count(i => i.WaStatusKey == "failed")
            };

            return new WhatsAppInboxResponse
            {
                WhatsAppEnabled = whatsAppEnabled,
                Stats = stats,
                Items = items
            };
        }

        public async Task<IReadOnlyList<WhatsAppChatLogItem>?> GetCitaChatAsync(
            int citaId,
            CancellationToken cancellationToken = default)
        {
            // El global query filter garantiza que sólo veamos citas del tenant actual.
            var citaExists = await _context.Citas
                .AsNoTracking()
                .AnyAsync(c => c.Id == citaId, cancellationToken);

            if (!citaExists)
            {
                return null;
            }

            var logs = await _context.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message => message.CitaId == citaId)
                .OrderBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Id)
                .Select(message => new
                {
                    message.CreatedAtUtc,
                    message.Direction,
                    message.NotificationType,
                    message.Status,
                    message.ErrorMessage,
                    message.ErrorCode,
                    message.MetaMessageId
                })
                .ToListAsync(cancellationToken);

            return logs
                .Select(log => new WhatsAppChatLogItem
                {
                    FechaHoraUtc = log.CreatedAtUtc,
                    FechaHoraLocal = ToLocal(log.CreatedAtUtc).ToString("dd/MM/yyyy hh:mm tt", CostaRica),
                    Direccion = DescribeDirection(log.Direction),
                    Tipo = DescribeType(log.NotificationType),
                    Estado = DescribeStatus(log.Status),
                    Error = string.IsNullOrWhiteSpace(log.ErrorMessage) ? log.ErrorCode : log.ErrorMessage,
                    ReferenciaMensaje = MaskMessageId(log.MetaMessageId)
                })
                .ToList();
        }

        private WhatsAppInboxItem BuildItem(
            CitaProjection cita,
            OutboundLogProjection? latestLog,
            bool whatsAppEnabled,
            DateTime nowUtc)
        {
            var nombre = string.IsNullOrWhiteSpace(cita.NombreCliente) ? "Cliente" : cita.NombreCliente!.Trim();
            var hasPhone = !string.IsNullOrWhiteSpace(cita.Telefono);
            var hasConsent = cita.ClienteId.HasValue ? cita.ClienteAcepta == true : cita.WhatsAppConsentAtCreation;

            var estado = cita.EstadoConfirmacionWhatsApp;
            var isCancelled = string.Equals(estado, WhatsAppConfirmationStates.Cancelada, StringComparison.OrdinalIgnoreCase);
            var isConfirmed = string.Equals(estado, WhatsAppConfirmationStates.Confirmada, StringComparison.OrdinalIgnoreCase);

            var latestStatus = latestLog?.Status;
            var isFailed = string.Equals(latestStatus, WhatsAppMessageStatuses.Failed, StringComparison.Ordinal) ||
                           string.Equals(estado, WhatsAppConfirmationStates.ErrorEnvio, StringComparison.OrdinalIgnoreCase);
            var isPending = string.Equals(latestStatus, WhatsAppMessageStatuses.Pending, StringComparison.Ordinal) ||
                            string.Equals(latestStatus, WhatsAppMessageStatuses.Processing, StringComparison.Ordinal);
            var reminderSent = cita.RecordatorioWhatsAppTresHorasEnviadoUtc.HasValue ||
                               (latestLog is not null &&
                                string.Equals(latestLog.NotificationType, WhatsAppNotificationTypes.Reminder3Hours, StringComparison.Ordinal) &&
                                SentLikeStatuses.Contains(latestLog.Status));
            var confirmationSent = cita.ConfirmacionWhatsAppEnviadaUtc.HasValue ||
                                   (latestLog is not null &&
                                    string.Equals(latestLog.NotificationType, WhatsAppNotificationTypes.Confirmation, StringComparison.Ordinal) &&
                                    SentLikeStatuses.Contains(latestLog.Status));

            string statusKey;
            string statusLabel;
            string subText;

            if (isCancelled)
            {
                statusKey = "cancelled";
                statusLabel = "Cancelado";
                subText = "Cita cancelada";
            }
            else if (!hasConsent)
            {
                statusKey = "no_consent";
                statusLabel = "Sin autorización";
                subText = "Cliente sin autorización WhatsApp";
            }
            else if (isFailed)
            {
                statusKey = "failed";
                statusLabel = "No entregado";
                subText = "Intento fallido";
            }
            else if (isConfirmed)
            {
                statusKey = "confirmed";
                statusLabel = "Confirmado por cliente";
                subText = "Respondido " + FormatRelative(cita.ConfirmadaPorWhatsAppUtc, nowUtc);
            }
            else if (isPending)
            {
                statusKey = "pending";
                statusLabel = "Pendiente de envío";
                subText = "En cola de envío";
            }
            else if (reminderSent)
            {
                statusKey = "reminder";
                statusLabel = "Recordatorio enviado";
                subText = "Enviado " + FormatRelative(cita.RecordatorioWhatsAppTresHorasEnviadoUtc ?? latestLog?.SentAtUtc, nowUtc);
            }
            else if (confirmationSent)
            {
                statusKey = "sent";
                statusLabel = "Confirmación enviada";
                subText = "Enviado " + FormatRelative(cita.ConfirmacionWhatsAppEnviadaUtc ?? latestLog?.SentAtUtc, nowUtc);
            }
            else if (!hasPhone)
            {
                statusKey = "no_phone";
                statusLabel = "Sin teléfono";
                subText = "Sin teléfono registrado";
            }
            else
            {
                statusKey = "not_sent";
                statusLabel = "Pendiente de envío";
                subText = "Programado para " + cita.FechaHoraCita.ToString("hh:mm tt", CostaRica);
            }

            // Reglas de acción (también exigen que el tenant tenga WhatsApp habilitado).
            var puedeEnviar = whatsAppEnabled && hasPhone && hasConsent && !isCancelled && statusKey == "not_sent";
            var puedeReenviar = whatsAppEnabled && hasPhone && hasConsent && !isCancelled && statusKey == "failed";

            return new WhatsAppInboxItem
            {
                CitaId = cita.Id,
                NombreCliente = nombre,
                Iniciales = BuildInitials(nombre),
                Telefono = cita.Telefono,
                TieneTelefono = hasPhone,
                ServicioNombre = cita.ServicioNombre ?? string.Empty,
                FuncionarioNombre = cita.FuncionarioNombre,
                FechaHoraCita = cita.FechaHoraCita,
                HoraLocal = cita.FechaHoraCita.ToString("hh:mm tt", CostaRica),
                EstadoCitaKey = isCancelled ? "cancelled" : isConfirmed ? "confirmed" : "pending",
                EstadoCitaLabel = isCancelled ? "Cancelada" : isConfirmed ? "Confirmada" : "Pendiente",
                WaStatusKey = statusKey,
                WaStatusLabel = statusLabel,
                WaSubText = subText,
                PuedeEnviar = puedeEnviar,
                PuedeReenviar = puedeReenviar
            };
        }

        private async Task<Dictionary<int, OutboundLogProjection>> LoadLatestOutboundLogMapAsync(
            IEnumerable<int> citaIds,
            CancellationToken cancellationToken)
        {
            var ids = citaIds.Where(id => id > 0).Distinct().ToArray();
            if (ids.Length == 0)
            {
                return new Dictionary<int, OutboundLogProjection>();
            }

            var logs = await _context.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message =>
                    message.CitaId.HasValue &&
                    ids.Contains(message.CitaId.Value) &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    (message.NotificationType == WhatsAppNotificationTypes.Confirmation ||
                     message.NotificationType == WhatsAppNotificationTypes.Reminder3Hours))
                .OrderByDescending(message => message.CreatedAtUtc)
                .ThenByDescending(message => message.Id)
                .Select(message => new OutboundLogProjection
                {
                    CitaId = message.CitaId!.Value,
                    Status = message.Status,
                    NotificationType = message.NotificationType,
                    SentAtUtc = message.SentAtUtc
                })
                .ToListAsync(cancellationToken);

            return logs
                .GroupBy(log => log.CitaId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private string FormatRelative(DateTime? utc, DateTime nowUtc)
        {
            if (!utc.HasValue)
            {
                return "recientemente";
            }

            var delta = nowUtc - utc.Value;
            if (delta < TimeSpan.Zero)
            {
                delta = TimeSpan.Zero;
            }

            if (delta.TotalMinutes < 1)
            {
                return "hace instantes";
            }

            if (delta.TotalMinutes < 60)
            {
                return $"hace {(int)delta.TotalMinutes} min";
            }

            if (delta.TotalHours < 24)
            {
                return $"hace {(int)delta.TotalHours} h";
            }

            var days = (int)delta.TotalDays;
            return days == 1 ? "hace 1 día" : $"hace {days} días";
        }

        private DateTime ToLocal(DateTime utc)
        {
            // El offset del negocio (Costa Rica) lo provee el BusinessDateTimeProvider.
            var offset = _businessDateTimeProvider.NowOffset().Offset;
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(offset);
        }

        private static string BuildInitials(string nombre)
        {
            var parts = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            var first = parts[0].Length > 0 ? parts[0][0].ToString() : string.Empty;
            var second = parts.Length > 1 && parts[1].Length > 0 ? parts[1][0].ToString() : string.Empty;
            var initials = (first + second).ToUpperInvariant();
            return string.IsNullOrEmpty(initials) ? "?" : initials;
        }

        private static string? MaskMessageId(string? messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return messageId.Length <= 6 ? "…" + messageId : "…" + messageId[^6..];
        }

        private static string DescribeDirection(string direction) => direction switch
        {
            WhatsAppMessageDirections.Outbound => "Enviado",
            WhatsAppMessageDirections.Inbound => "Recibido",
            WhatsAppMessageDirections.Status => "Estado",
            _ => direction
        };

        private static string DescribeType(string type) => type switch
        {
            WhatsAppNotificationTypes.Confirmation => "Confirmación",
            WhatsAppNotificationTypes.Reminder3Hours => "Recordatorio 3h",
            WhatsAppNotificationTypes.Reply => "Respuesta",
            WhatsAppNotificationTypes.Status => "Estado",
            _ => type
        };

        private static string DescribeStatus(string status) => status switch
        {
            WhatsAppMessageStatuses.Pending => "Pendiente",
            WhatsAppMessageStatuses.Processing => "Procesando",
            WhatsAppMessageStatuses.Sent => "Enviado",
            WhatsAppMessageStatuses.Delivered => "Entregado",
            WhatsAppMessageStatuses.Read => "Leído",
            WhatsAppMessageStatuses.Failed => "Fallido",
            WhatsAppMessageStatuses.Received => "Recibido",
            WhatsAppMessageStatuses.Ignored => "Ignorado",
            _ when status.StartsWith("Skipped", StringComparison.Ordinal) => "Omitido",
            _ => status
        };

        private sealed class CitaProjection
        {
            public int Id { get; init; }
            public string? NombreCliente { get; init; }
            public string? Telefono { get; init; }
            public string? ServicioNombre { get; init; }
            public string FuncionarioNombre { get; init; } = string.Empty;
            public DateTime FechaHoraCita { get; init; }
            public string EstadoConfirmacionWhatsApp { get; init; } = string.Empty;
            public DateTime? ConfirmacionWhatsAppEnviadaUtc { get; init; }
            public DateTime? RecordatorioWhatsAppTresHorasEnviadoUtc { get; init; }
            public DateTime? ConfirmadaPorWhatsAppUtc { get; init; }
            public int? ClienteId { get; init; }
            public bool? ClienteAcepta { get; init; }
            public bool WhatsAppConsentAtCreation { get; init; }
        }

        private sealed class OutboundLogProjection
        {
            public int CitaId { get; init; }
            public string Status { get; init; } = string.Empty;
            public string NotificationType { get; init; } = string.Empty;
            public DateTime? SentAtUtc { get; init; }
        }
    }
}
