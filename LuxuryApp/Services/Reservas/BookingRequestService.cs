using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Reservas
{
    public sealed class BookingRequestService : IBookingRequestService
    {
        private const string WhatsAppConsentSourceReserva = "ReservaOnline";

        private const string WhatsAppSourceReservaAprobada = "ReservaOnlineAprobada";

        private readonly ApplicationDbContext _context;
        private readonly ICalendarCommandService _calendarCommandService;
        private readonly ICalendarWhatsAppNotificationService _notificationService;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingSettingsService _settingsService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<BookingRequestService> _logger;

        public BookingRequestService(
            ApplicationDbContext context,
            ICalendarCommandService calendarCommandService,
            ICalendarWhatsAppNotificationService notificationService,
            IBookingAvailabilityService availabilityService,
            IBookingSettingsService settingsService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            IHttpContextAccessor httpContextAccessor,
            ILogger<BookingRequestService> logger)
        {
            _context = context;
            _calendarCommandService = calendarCommandService;
            _notificationService = notificationService;
            _availabilityService = availabilityService;
            _settingsService = settingsService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<BookingRequestsPageViewModel> BuildPageAsync(
            string? estado,
            string? rango,
            CancellationToken cancellationToken = default)
        {
            var estadoFiltro = NormalizeEstado(estado);
            var rangoFiltro = NormalizeRango(rango);

            // Conteos globales por estado (badges siempre accionables).
            var counts = await _context.BookingRequests
                .AsNoTracking()
                .GroupBy(r => r.Estado)
                .Select(g => new { Estado = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var query = _context.BookingRequests.AsNoTracking().AsQueryable();

            if (!string.Equals(estadoFiltro, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.Estado == estadoFiltro);
            }

            var (desde, hasta) = ResolveRango(rangoFiltro);
            query = query.Where(r => r.FechaHoraInicioSolicitada >= desde && r.FechaHoraInicioSolicitada < hasta);

            var solicitudes = await query
                .OrderBy(r => r.Estado == BookingRequestStates.Pending ? 0 : 1)
                .ThenBy(r => r.FechaHoraInicioSolicitada)
                .Select(r => new BookingRequestListItemViewModel
                {
                    Id = r.Id,
                    NombreCliente = r.NombreCliente,
                    TelefonoCliente = r.TelefonoCliente,
                    CorreoCliente = r.CorreoCliente,
                    ServicioNombre = r.Servicio != null ? r.Servicio.Nombre : "Servicio",
                    FuncionarioNombre = r.FuncionarioId != null && r.Funcionario != null
                        ? r.Funcionario.Nombre
                        : "Cualquier funcionario",
                    SolicitoCualquierFuncionario = r.FuncionarioId == null,
                    FechaHoraInicioSolicitada = r.FechaHoraInicioSolicitada,
                    DuracionMinutos = r.DuracionMinutos,
                    NotasCliente = r.NotasCliente,
                    Estado = r.Estado,
                    CreatedAtUtc = r.CreatedAtUtc,
                    RejectedReason = r.RejectedReason,
                    ConvertedCitaId = r.ConvertedCitaId,
                    AceptaWhatsApp = r.AceptaWhatsApp,
                    // Estado del envío WhatsApp de la cita creada (subconsulta tenant-safe por RLS).
                    ConfirmacionWhatsAppEnviadaUtc = r.ConvertedCitaId == null
                        ? null
                        : _context.Citas
                            .Where(c => c.Id == r.ConvertedCitaId)
                            .Select(c => c.ConfirmacionWhatsAppEnviadaUtc)
                            .FirstOrDefault(),
                    ConfirmacionWhatsAppEstado = r.ConvertedCitaId == null
                        ? null
                        : _context.Citas
                            .Where(c => c.Id == r.ConvertedCitaId)
                            .Select(c => c.EstadoConfirmacionWhatsApp)
                            .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var slug = await _settingsService.GetCurrentSlugAsync(cancellationToken);

            return new BookingRequestsPageViewModel
            {
                EstadoFiltro = estadoFiltro,
                RangoFiltro = rangoFiltro,
                PendientesCount = counts.Where(c => c.Estado == BookingRequestStates.Pending).Sum(c => c.Count),
                ConfirmadasCount = counts.Where(c => c.Estado == BookingRequestStates.Confirmed).Sum(c => c.Count),
                RechazadasCount = counts.Where(c => c.Estado == BookingRequestStates.Rejected).Sum(c => c.Count),
                ReservasActivas = !string.IsNullOrWhiteSpace(slug),
                Slug = slug,
                LinkPublico = BookingLinkBuilder.Build(_httpContextAccessor.HttpContext?.Request, slug),
                Solicitudes = solicitudes
            };
        }

        public async Task<BookingActionResult> ConfirmAsync(
            int requestId,
            int? funcionarioIdOverride,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            // Lectura sin tracking: datos para validar y armar la cita. Las escrituras se hacen con
            // ExecuteUpdate (atómicas) para evitar carreras/doble confirmación.
            var solicitud = await _context.BookingRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (solicitud is null)
            {
                return BookingActionResult.Fail("La solicitud no existe o no pertenece a tu negocio.");
            }

            // Idempotencia: si ya estaba confirmada, no se crea otra cita ni se reenvía WhatsApp.
            if (solicitud.Estado == BookingRequestStates.Confirmed)
            {
                return BookingActionResult.Ok("Esta solicitud ya fue confirmada.", solicitud.ConvertedCitaId);
            }

            if (solicitud.Estado != BookingRequestStates.Pending)
            {
                return BookingActionResult.Fail("Esta solicitud ya fue procesada.");
            }

            // Claim atómico Pending → Confirmed: solo un request concurrente gana (anti doble click/carrera).
            var confirmedAtUtc = DateTime.UtcNow;
            var claimed = await _context.BookingRequests
                .Where(r => r.Id == requestId && r.Estado == BookingRequestStates.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Estado, BookingRequestStates.Confirmed)
                    .SetProperty(r => r.ConfirmedAtUtc, confirmedAtUtc)
                    .SetProperty(r => r.ConfirmedByUserId, userId),
                    cancellationToken);

            if (claimed == 0)
            {
                var actual = await _context.BookingRequests
                    .AsNoTracking()
                    .Where(r => r.Id == requestId)
                    .Select(r => new { r.Estado, r.ConvertedCitaId })
                    .FirstOrDefaultAsync(cancellationToken);

                return actual?.Estado == BookingRequestStates.Confirmed
                    ? BookingActionResult.Ok("Esta solicitud ya fue confirmada.", actual.ConvertedCitaId)
                    : BookingActionResult.Fail("Esta solicitud ya fue procesada.");
            }

            var funcionarioDeseado = funcionarioIdOverride.HasValue && funcionarioIdOverride.Value > 0
                ? funcionarioIdOverride
                : solicitud.FuncionarioId;

            // Revalida disponibilidad y resuelve el funcionario (si era "cualquiera").
            var resolucion = await _availabilityService.ResolveSlotAsync(
                solicitud.ServicioId,
                solicitud.FechaHoraInicioSolicitada,
                funcionarioDeseado,
                cancellationToken);

            if (!resolucion.Disponible || !resolucion.FuncionarioId.HasValue)
            {
                await RevertClaimAsync(requestId, cancellationToken);
                return BookingActionResult.Fail(
                    "Ese espacio acaba de dejar de estar disponible. Por favor elegí otro horario o rechazá la solicitud.");
            }

            var clienteId = await ResolveClienteIdAsync(solicitud, cancellationToken);

            var upsert = new CalendarUpsertRequest
            {
                Tipo = "CITA",
                ServicioId = solicitud.ServicioId,
                FuncionarioId = resolucion.FuncionarioId.Value,
                FechaHoraCita = solicitud.FechaHoraInicioSolicitada,
                ClienteId = clienteId,
                NombreCliente = clienteId.HasValue ? null : solicitud.NombreCliente,
                TelefonoCliente = clienteId.HasValue ? null : solicitud.TelefonoCliente,
                WhatsAppConsentAtCreation = clienteId.HasValue ? false : solicitud.AceptaWhatsApp,
                WhatsAppConsentSource = clienteId.HasValue ? null : WhatsAppConsentSourceReserva,
                WhatsAppConsentCapturedAtUtc = (!clienteId.HasValue && solicitud.AceptaWhatsApp)
                    ? DateTime.UtcNow
                    : null
            };

            CalendarAppointmentResponse citaCreada;
            try
            {
                // Reutiliza el flujo de creación de citas: valida solapamiento (doble seguridad) y
                // encola el recordatorio según la lógica del tenant. La confirmación inmediata se
                // fuerza más abajo con SendConfirmationNowAsync (idempotente frente a este encolado).
                citaCreada = await _calendarCommandService.CreateAsync(upsert, cancellationToken);
            }
            catch (CalendarValidationException ex)
            {
                await RevertClaimAsync(requestId, cancellationToken);
                return BookingActionResult.Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "No fue posible crear la cita al confirmar la solicitud {RequestId}.", requestId);
                await RevertClaimAsync(requestId, cancellationToken);
                return BookingActionResult.Fail("No fue posible crear la cita. Intentá de nuevo.");
            }

            // Enlaza la cita creada (sin tocar Estado: ya está Confirmed por el claim).
            await _context.BookingRequests
                .Where(r => r.Id == requestId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ConvertedCitaId, citaCreada.Id)
                    .SetProperty(r => r.FuncionarioId, resolucion.FuncionarioId.Value)
                    .SetProperty(r => r.ClienteId, clienteId),
                    cancellationToken);

            // Reserva aprobada = cita confirmada: enviar YA la confirmación por WhatsApp reutilizando
            // el flujo/plantilla existente. Es idempotente y deja la cita marcada como confirmada,
            // por lo que el lote/scheduler de confirmaciones no la reenviará. El recordatorio sigue
            // su curso normal. Un fallo de WhatsApp NO revierte la aprobación: la cita ya está creada.
            WhatsAppConfirmationSendResult? whatsAppResult = null;
            try
            {
                whatsAppResult = await _notificationService.SendConfirmationNowAsync(
                    citaCreada.Id,
                    WhatsAppSourceReservaAprobada,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "La reserva {RequestId} se confirmó y la cita {CitaId} se creó, pero falló el envío inmediato de la confirmación de WhatsApp.",
                    requestId,
                    citaCreada.Id);
            }

            var (mensaje, whatsAppStatus) = ComposeConfirmationMessage(whatsAppResult);
            return BookingActionResult.Ok(mensaje, citaCreada.Id, whatsAppStatus);
        }

        public async Task<BookingActionResult> RejectAsync(
            int requestId,
            string? reason,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            var solicitud = await _context.BookingRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (solicitud is null)
            {
                return BookingActionResult.Fail("La solicitud no existe o no pertenece a tu negocio.");
            }

            if (solicitud.Estado != BookingRequestStates.Pending)
            {
                return BookingActionResult.Fail("Esta solicitud ya fue procesada.");
            }

            solicitud.Estado = BookingRequestStates.Rejected;
            solicitud.RejectedAtUtc = DateTime.UtcNow;
            solicitud.RejectedByUserId = userId;
            solicitud.RejectedReason = string.IsNullOrWhiteSpace(reason)
                ? null
                : reason.Trim().Length > 300 ? reason.Trim()[..300] : reason.Trim();

            await _context.SaveChangesAsync(cancellationToken);

            return BookingActionResult.Ok("Solicitud rechazada.");
        }

        private static (string Message, string? WhatsAppStatus) ComposeConfirmationMessage(
            WhatsAppConfirmationSendResult? result)
        {
            if (result is null)
            {
                return (
                    "Reserva aprobada y cita creada, pero no se pudo enviar la confirmación de WhatsApp.",
                    "failed");
            }

            return result.Outcome switch
            {
                WhatsAppConfirmationOutcome.Sent =>
                    ("Reserva aprobada, cita creada y confirmación de WhatsApp enviada.", "sent"),

                WhatsAppConfirmationOutcome.AlreadySent =>
                    ("Reserva aprobada y cita creada. La confirmación de WhatsApp ya había sido enviada.", "sent"),

                WhatsAppConfirmationOutcome.Pending =>
                    ("Reserva aprobada y cita creada. La confirmación de WhatsApp se enviará en breve.", "pending"),

                WhatsAppConfirmationOutcome.Failed =>
                    ("Reserva aprobada y cita creada, pero no se pudo enviar la confirmación de WhatsApp.", "failed"),

                _ =>
                    ($"Reserva aprobada y cita creada. {result.Message}", "skipped")
            };
        }

        /// <summary>
        /// Revierte el claim (Confirmed → Pending) cuando la creación de la cita falla, solo si aún
        /// no se enlazó una cita (ConvertedCitaId nulo). Deja la solicitud reprocesable.
        /// </summary>
        private Task RevertClaimAsync(int requestId, CancellationToken cancellationToken) =>
            _context.BookingRequests
                .Where(r => r.Id == requestId &&
                            r.Estado == BookingRequestStates.Confirmed &&
                            r.ConvertedCitaId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Estado, BookingRequestStates.Pending)
                    .SetProperty(r => r.ConfirmedAtUtc, (DateTime?)null)
                    .SetProperty(r => r.ConfirmedByUserId, (string?)null),
                    cancellationToken);

        private async Task<int?> ResolveClienteIdAsync(BookingRequest solicitud, CancellationToken cancellationToken)
        {
            // Si ya estaba asociada a un cliente y sigue existiendo, se reutiliza.
            if (solicitud.ClienteId.HasValue)
            {
                var existe = await _context.Clientes
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == solicitud.ClienteId.Value, cancellationToken);

                if (existe)
                {
                    return solicitud.ClienteId;
                }
            }

            // Reintenta por teléfono dentro del tenant.
            return await _context.Clientes
                .AsNoTracking()
                .Where(c => c.NumeroTelefono == solicitud.TelefonoCliente)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static string NormalizeEstado(string? estado)
        {
            if (string.IsNullOrWhiteSpace(estado))
            {
                return BookingRequestStates.Pending;
            }

            return estado.ToLowerInvariant() switch
            {
                "confirmed" or "confirmadas" => BookingRequestStates.Confirmed,
                "rejected" or "rechazadas" => BookingRequestStates.Rejected,
                "all" or "todas" => "all",
                _ => BookingRequestStates.Pending
            };
        }

        private static string NormalizeRango(string? rango)
        {
            if (string.IsNullOrWhiteSpace(rango))
            {
                return "mes";
            }

            return rango.ToLowerInvariant() switch
            {
                "hoy" => "hoy",
                "semana" => "semana",
                _ => "mes"
            };
        }

        private (DateTime Desde, DateTime Hasta) ResolveRango(string rango)
        {
            var hoy = _businessDateTimeProvider.Today();

            return rango switch
            {
                "hoy" => (hoy, hoy.AddDays(1)),
                "semana" => (hoy, hoy.AddDays(7)),
                _ => (hoy.AddDays(-31), hoy.AddDays(62))
            };
        }
    }
}
