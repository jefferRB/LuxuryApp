using System.Globalization;
using System.Text;
using System.Text.Json;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Calendar
{
    /// <inheritdoc cref="IAppointmentCancellationWhatsAppService"/>
    public sealed class AppointmentCancellationWhatsAppService : IAppointmentCancellationWhatsAppService
    {
        /// <summary>
        /// Motivo por defecto cuando la cancelacion no trae uno. El template exige los 8 parametros
        /// y Meta rechaza los vacios, asi que se usa un texto corto y neutro. Es el mismo valor con
        /// el que la agenda precarga el campo, y el usuario puede reemplazarlo antes de cancelar.
        /// </summary>
        public const string DefaultCancellationReason = "Colaborador no disponible";

        /// <summary>
        /// Texto de {{8}} cuando el negocio todavia no cargo su telefono publico. No se bloquea el
        /// aviso por esto: el cliente con consentimiento debe enterarse igual de la cancelacion.
        /// </summary>
        private const string MissingBusinessPhoneFallback = "mismo número de siempre";

        /// <summary>Tope del motivo. Meta admite mas, pero un parrafo largo no aporta y sí rompe la lectura.</summary>
        private const int MaxReasonLength = 300;

        private const int MaxParameterLength = 300;

        private static readonly CultureInfo CostaRica = CultureInfo.GetCultureInfo("es-CR");

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Estados que significan "ya hay un aviso de cancelacion vivo o entregado para esta cita".
        /// Ver <see cref="PrepareAsync"/> para por que esto basta como idempotencia.
        /// </summary>
        private static readonly string[] ExistingCancellationStatuses =
        [
            WhatsAppMessageStatuses.Pending,
            WhatsAppMessageStatuses.Processing,
            WhatsAppMessageStatuses.Sent,
            WhatsAppMessageStatuses.Delivered,
            WhatsAppMessageStatuses.Read
        ];

        private readonly ApplicationDbContext _context;
        private readonly IMetaWhatsAppClient _metaClient;
        private readonly IOptionsMonitor<MetaWhatsAppOptions> _options;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantWhatsAppSettingsService _tenantSettingsService;
        private readonly ITenantDisplayNameService _tenantDisplayNameService;
        private readonly ILogger<AppointmentCancellationWhatsAppService> _logger;

        public AppointmentCancellationWhatsAppService(
            ApplicationDbContext context,
            IMetaWhatsAppClient metaClient,
            IOptionsMonitor<MetaWhatsAppOptions> options,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantWhatsAppSettingsService tenantSettingsService,
            ITenantDisplayNameService tenantDisplayNameService,
            ILogger<AppointmentCancellationWhatsAppService> logger)
        {
            _context = context;
            _metaClient = metaClient;
            _options = options;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tenantSettingsService = tenantSettingsService;
            _tenantDisplayNameService = tenantDisplayNameService;
            _logger = logger;
        }

        public async Task<PreparedAppointmentCancellation?> PrepareAsync(
            int citaId,
            string? motivoCancelacion,
            CancellationToken cancellationToken = default)
        {
            // La cita se lee por el DbContext con el filtro de tenant activo: nunca se acepta un
            // TenantId de afuera, se deriva de la entidad.
            var cita = await _context.Citas
                .AsNoTracking()
                .Include(c => c.Servicio)
                .FirstOrDefaultAsync(c => c.Id == citaId, cancellationToken);

            if (cita is null)
            {
                return null;
            }

            var booking = await ResolveOnlineBookingAsync(citaId, cancellationToken);
            if (booking is null)
            {
                // Cita manual / interna: no hay a quien avisarle una reserva que nunca existio.
                // No se registra fila de log: ensuciaria la bandeja de todos los tenants por cada
                // cita borrada, incluso los que no tienen WhatsApp.
                _logger.LogInformation(
                    "Cancelacion WhatsApp omitida: la cita no provino de una reserva online. TenantId {TenantId}. CitaId {CitaId}.",
                    cita.TenantId,
                    citaId);
                return null;
            }

            var bookingRequestId = booking.Id;

            // Sin complemento contratado no hay flujo de WhatsApp: se sale en silencio, sin fila de
            // log y sin avisos. No es un error ni una falta de consentimiento del cliente.
            if (!await _tenantSettingsService.HasActiveWhatsAppAddonAsync(cita.TenantId, cancellationToken))
            {
                _logger.LogDebug(
                    "Cancelacion WhatsApp omitida en silencio: el negocio no tiene el complemento activo. TenantId {TenantId}. CitaId {CitaId}.",
                    cita.TenantId,
                    citaId);
                return null;
            }

            // Idempotencia: mientras la cita existe, su CitaId es la clave. Un segundo intento ve
            // el aviso ya registrado y se detiene; ademas el indice unico filtrado
            // UX_WhatsAppMessageLogs_ActiveOutboundNotification (TenantId, CitaId, NotificationType,
            // Direction) lo impide a nivel de base de datos ante una carrera.
            var alreadyRegistered = await _context.WhatsAppMessageLogs
                .AsNoTracking()
                .AnyAsync(message =>
                    message.CitaId == citaId &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    message.NotificationType == WhatsAppNotificationTypes.Cancellation &&
                    ExistingCancellationStatuses.Contains(message.Status),
                    cancellationToken);

            if (alreadyRegistered)
            {
                _logger.LogInformation(
                    "Cancelacion WhatsApp duplicada evitada: ya existe un aviso para la cita. TenantId {TenantId}. CitaId {CitaId}.",
                    cita.TenantId,
                    citaId);
                return null;
            }

            if (!string.Equals(cita.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!await HasWhatsAppConsentAsync(cita, booking, cancellationToken))
            {
                await RegisterSkippedAsync(
                    cita,
                    WhatsAppMessageStatuses.SkippedConsentMissing,
                    WhatsAppErrorCodes.ConsentMissing,
                    BuildManualContactMessage(cita.TelefonoCliente),
                    bookingRequestId,
                    cancellationToken);
                return null;
            }

            var phoneE164 = _metaClient.NormalizePhoneNumber(cita.TelefonoCliente);
            if (phoneE164 is null)
            {
                await RegisterSkippedAsync(
                    cita,
                    WhatsAppMessageStatuses.SkippedInvalidPhone,
                    WhatsAppErrorCodes.InvalidPhone,
                    "Telefono invalido.",
                    bookingRequestId,
                    cancellationToken);
                return null;
            }

            // Misma politica comercial y de cuotas que confirmaciones y recordatorios.
            var decision = await _tenantSettingsService.CanSendNotificationAsync(
                cita.TenantId,
                WhatsAppNotificationTypes.Cancellation,
                cancellationToken: cancellationToken);

            if (!decision.CanSend)
            {
                await RegisterSkippedAsync(
                    cita,
                    ResolveSkippedStatus(decision.ErrorCode),
                    decision.ErrorCode ?? WhatsAppErrorCodes.ConfigurationDisabled,
                    decision.ErrorMessage ?? "El mensaje WhatsApp fue omitido por configuracion.",
                    bookingRequestId,
                    cancellationToken);
                return null;
            }

            // {{8}} es el telefono publico del negocio. Si no esta cargado NO se bloquea el aviso:
            // se usa un texto de relleno y queda el warning para que el dueño complete su pagina.
            var businessPhone = await ResolveBusinessPublicPhoneAsync(cancellationToken);
            if (businessPhone is null)
            {
                _logger.LogWarning(
                    "El negocio no tiene telefono publico configurado; el aviso de cancelacion sale con texto de relleno en {{8}}. TenantId {TenantId}. CitaId {CitaId}.",
                    cita.TenantId,
                    cita.Id);
                businessPhone = MissingBusinessPhoneFallback;
            }

            var businessName = await _tenantDisplayNameService.GetTenantDisplayNameAsync(
                cita.TenantId,
                cancellationToken);

            var parameters = BuildParameters(cita, businessName, businessPhone, motivoCancelacion);

            var nowUtc = _businessDateTimeProvider.NowOffset().UtcDateTime;
            var log = new WhatsAppMessageLog
            {
                CitaId = cita.Id,
                Direction = WhatsAppMessageDirections.Outbound,
                NotificationType = WhatsAppNotificationTypes.Cancellation,
                Provider = WhatsAppProviders.Meta,
                RecipientPhoneE164 = phoneE164,
                TemplateName = _options.CurrentValue.CancellationTemplateName,
                Status = WhatsAppMessageStatuses.Pending,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    phase = "queued",
                    notificationType = WhatsAppNotificationTypes.Cancellation,
                    citaId = cita.Id,
                    citaFechaHora = cita.FechaHoraCita,
                    bookingRequestId = bookingRequestId,
                    source = WhatsAppConsentPolicy.ResolveSource(cita),
                    hasClienteId = cita.ClienteId.HasValue
                }, JsonOptions),
                CreatedAtUtc = nowUtc
            };

            _context.WhatsAppMessageLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);

            // La fila queda rastreada a proposito: al eliminar la cita, EF le pone CitaId en NULL
            // (DeleteBehavior.SetNull) y la fila sobrevive al borrado. El envio posterior la cierra
            // por Id con ExecuteUpdate, sin tocar la FK.
            return new PreparedAppointmentCancellation(
                log.Id,
                cita.TenantId,
                cita.Id,
                bookingRequestId,
                phoneE164,
                parameters);
        }

        public async Task SendAsync(
            PreparedAppointmentCancellation prepared,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(prepared);

            var nowUtc = _businessDateTimeProvider.NowOffset().UtcDateTime;

            try
            {
                var sendResult = await _metaClient.SendCancellationTemplateAsync(
                    prepared.RecipientPhoneE164,
                    prepared.Parameters,
                    cancellationToken);

                if (sendResult.Success && !string.IsNullOrWhiteSpace(sendResult.MetaMessageId))
                {
                    await _context.WhatsAppMessageLogs
                        .Where(message => message.Id == prepared.MessageLogId)
                        .ExecuteUpdateAsync(updates => updates
                            .SetProperty(message => message.Status, WhatsAppMessageStatuses.Sent)
                            .SetProperty(message => message.MetaMessageId, sendResult.MetaMessageId)
                            .SetProperty(message => message.SentAtUtc, nowUtc)
                            .SetProperty(message => message.LastAttemptAtUtc, nowUtc)
                            .SetProperty(message => message.ProcessedAtUtc, nowUtc)
                            .SetProperty(message => message.AttemptCount, 1)
                            .SetProperty(message => message.PayloadJson, BuildSendResultPayloadJson(prepared, sendResult)),
                            cancellationToken);

                    _logger.LogInformation(
                        "Cancelacion WhatsApp enviada. TenantId {TenantId}. CitaId {CitaId}. BookingRequestId {BookingRequestId}. MetaMessageId {MetaMessageId}.",
                        prepared.TenantId,
                        prepared.CitaId,
                        prepared.BookingRequestId,
                        sendResult.MetaMessageId);
                    return;
                }

                await MarkFailedAsync(prepared, sendResult, nowUtc, cancellationToken);

                _logger.LogWarning(
                    "Fallo el envio de la cancelacion WhatsApp. TenantId {TenantId}. CitaId {CitaId}. BookingRequestId {BookingRequestId}. ErrorCode {ErrorCode}.",
                    prepared.TenantId,
                    prepared.CitaId,
                    prepared.BookingRequestId,
                    sendResult.ErrorCode);
            }
            catch (Exception ex)
            {
                // La cancelacion de la cita ya esta confirmada en base de datos: WhatsApp jamas la
                // revierte. Solo se deja constancia del fallo.
                _logger.LogError(
                    ex,
                    "Error inesperado enviando la cancelacion WhatsApp. TenantId {TenantId}. CitaId {CitaId}. BookingRequestId {BookingRequestId}.",
                    prepared.TenantId,
                    prepared.CitaId,
                    prepared.BookingRequestId);

                try
                {
                    await MarkFailedAsync(
                        prepared,
                        MetaWhatsAppSendResult.Failed("UNEXPECTED_ERROR", ex.Message),
                        nowUtc,
                        cancellationToken);
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(
                        logEx,
                        "No fue posible registrar el fallo de la cancelacion WhatsApp. MessageLogId {MessageLogId}.",
                        prepared.MessageLogId);
                }
            }
        }

        public async Task<AppointmentCancellationNoticePreview> PreviewAsync(
            int citaId,
            CancellationToken cancellationToken = default)
        {
            var cita = await _context.Citas
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == citaId, cancellationToken);

            if (cita is null || !string.Equals(cita.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                return AppointmentCancellationNoticePreview.NoAplica;
            }

            var booking = await ResolveOnlineBookingAsync(citaId, cancellationToken);
            if (booking is null)
            {
                // Cita creada a mano: nunca hubo canal de WhatsApp con el cliente por esta via.
                return AppointmentCancellationNoticePreview.NoAplica;
            }

            // Sin complemento el negocio no eligió esta funcionalidad: no se le acusa al cliente de
            // no haber autorizado algo que nunca se le ofreció.
            if (!await _tenantSettingsService.HasActiveWhatsAppAddonAsync(cita.TenantId, cancellationToken))
            {
                return AppointmentCancellationNoticePreview.NoAplica;
            }

            var telefonoCliente = Sanitize(cita.TelefonoCliente, 40);

            if (!await HasWhatsAppConsentAsync(cita, booking, cancellationToken))
            {
                return new AppointmentCancellationNoticePreview(
                    NotificaraPorWhatsApp: false,
                    TelefonoCliente: telefonoCliente,
                    Mensaje: telefonoCliente is null
                        ? "El cliente no autorizó notificaciones por WhatsApp. Contactalo manualmente."
                        : $"El cliente no autorizó notificaciones por WhatsApp. Contactalo manualmente al {telefonoCliente}.",
                    MensajeContactoManual: BuildManualContactMessage(cita.TelefonoCliente));
            }

            return new AppointmentCancellationNoticePreview(
                NotificaraPorWhatsApp: true,
                TelefonoCliente: telefonoCliente,
                Mensaje: "Se enviará una notificación de cancelación por WhatsApp al cliente.",
                MensajeContactoManual: string.Empty);
        }

        /// <summary>
        /// UNICA fuente de verdad del origen: la relacion real <c>BookingRequest.ConvertedCitaId</c>,
        /// que solo se escribe al confirmar una solicitud del link publico. Nada de textos, nombres
        /// ni estados ambiguos. La consulta queda acotada al tenant por el filtro global. Se trae
        /// tambien <c>AceptaWhatsApp</c>: es la autorizacion que el cliente marco explicitamente en
        /// el formulario publico.
        /// </summary>
        private Task<OnlineBookingOrigin?> ResolveOnlineBookingAsync(int citaId, CancellationToken cancellationToken) =>
            _context.BookingRequests
                .AsNoTracking()
                .Where(request => request.ConvertedCitaId == citaId)
                .OrderBy(request => request.Id)
                .Select(request => new OnlineBookingOrigin(request.Id, request.AceptaWhatsApp))
                .FirstOrDefaultAsync(cancellationToken);

        /// <summary>
        /// Consentimiento para avisarle la cancelacion a un cliente que reservo por el link publico.
        ///
        /// <para>
        /// La marca que el cliente puso en el formulario publico (<c>BookingRequest.AceptaWhatsApp</c>)
        /// es autorizacion explicita y vale por si sola. Hace falta mirarla porque al confirmar la
        /// reserva de un cliente YA REGISTRADO, <c>ConfirmAsync</c> guarda la cita con
        /// <c>WhatsAppConsentAtCreation = false</c> y delega el consentimiento en
        /// <c>ClientesModel.AceptaMensajesWhatsApp</c>, campo que la reserva online nunca escribe: sin
        /// esto, el cliente que autorizo en el formulario quedaba sin aviso.
        /// </para>
        ///
        /// <para>
        /// Si la reserva no trae la marca, se cae a la politica general compartida (cliente
        /// registrado que autoriza / consentimiento capturado en la cita). Nunca se envia cuando
        /// ninguna de las dos autoriza.
        /// </para>
        /// </summary>
        private async Task<bool> HasWhatsAppConsentAsync(
            Cita cita,
            OnlineBookingOrigin booking,
            CancellationToken cancellationToken)
        {
            if (booking.AceptaWhatsApp)
            {
                return true;
            }

            bool? clienteAcepta = null;
            if (cita.ClienteId.HasValue)
            {
                clienteAcepta = await _context.Clientes
                    .AsNoTracking()
                    .Where(current => current.Id == cita.ClienteId.Value)
                    .Select(current => (bool?)current.AceptaMensajesWhatsApp)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return WhatsAppConsentPolicy.Evaluate(cita, clienteAcepta).CanSend;
        }

        /// <summary>
        /// Lo que el negocio necesita leer cuando no se puede avisar por WhatsApp: a que numero del
        /// CLIENTE llamar. No tiene nada que ver con el telefono publico del negocio que viaja en la
        /// plantilla.
        /// </summary>
        private static string BuildManualContactMessage(string? telefonoCliente)
        {
            var telefono = Sanitize(telefonoCliente, 40);
            return telefono is null
                ? "Sin autorización de WhatsApp. Contactar manualmente."
                : $"Sin autorización de WhatsApp. Contactar manualmente al {telefono}.";
        }

        private sealed record OnlineBookingOrigin(int Id, bool AceptaWhatsApp);

        /// <summary>
        /// Telefono publico/habitual del negocio: el de su pagina publica. Nunca el numero central
        /// de automatizaciones de LuxuryCloud.
        /// </summary>
        private async Task<string?> ResolveBusinessPublicPhoneAsync(CancellationToken cancellationToken)
        {
            var page = await _context.TenantPublicPages
                .AsNoTracking()
                .Select(current => new { current.WhatsAppPhone, current.Phone })
                .FirstOrDefaultAsync(cancellationToken);

            if (page is null)
            {
                return null;
            }

            var phone = Sanitize(page.WhatsAppPhone) ?? Sanitize(page.Phone);
            return string.IsNullOrWhiteSpace(phone) ? null : phone;
        }

        private static WhatsAppCancellationTemplateParameters BuildParameters(
            Cita cita,
            string businessName,
            string businessPhone,
            string? motivoCancelacion)
        {
            var negocio = Sanitize(businessName) ?? "el negocio";
            var servicio = Sanitize(cita.Servicio?.Nombre)
                ?? Sanitize(cita.ServicioNombrePersonalizado)
                ?? "el servicio reservado";

            var motivo = Sanitize(motivoCancelacion, MaxReasonLength) ?? DefaultCancellationReason;

            return new WhatsAppCancellationTemplateParameters
            {
                CustomerName = Sanitize(cita.NombreCliente) ?? "Cliente",
                BusinessName = negocio,
                ServiceName = servicio,
                AppointmentDate = cita.FechaHoraCita.ToString("dd/MM/yyyy", CostaRica),
                AppointmentTime = cita.FechaHoraCita.ToString("hh:mm tt", CostaRica),
                CancellationReason = motivo,
                // Mismo nombre a proposito: Meta no deja repetir {{2}} en dos posiciones.
                BusinessNameRepeated = negocio,
                BusinessPhone = businessPhone
            };
        }

        /// <summary>
        /// Deja el valor apto para un parametro de plantilla: Meta rechaza saltos de linea,
        /// tabuladores y espacios repetidos. Devuelve null cuando no queda nada util.
        /// </summary>
        private static string? Sanitize(string? value, int maxLength = MaxParameterLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var builder = new StringBuilder(value.Length);
            var previousWasSpace = false;

            foreach (var character in value)
            {
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                {
                    if (builder.Length > 0 && !previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }

                    continue;
                }

                builder.Append(character);
                previousWasSpace = false;
            }

            var normalized = builder.ToString().Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd();
        }

        private async Task RegisterSkippedAsync(
            Cita cita,
            string status,
            string errorCode,
            string reason,
            int bookingRequestId,
            CancellationToken cancellationToken)
        {
            var nowUtc = _businessDateTimeProvider.NowOffset().UtcDateTime;

            _context.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
            {
                CitaId = cita.Id,
                Direction = WhatsAppMessageDirections.Outbound,
                NotificationType = WhatsAppNotificationTypes.Cancellation,
                Provider = WhatsAppProviders.Meta,
                RecipientPhoneE164 = _metaClient.NormalizePhoneNumber(cita.TelefonoCliente),
                TemplateName = _options.CurrentValue.CancellationTemplateName,
                Status = status,
                ErrorCode = errorCode,
                ErrorMessage = Sanitize(reason, 1000),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    phase = "skipped",
                    reason = errorCode,
                    notificationType = WhatsAppNotificationTypes.Cancellation,
                    citaId = cita.Id,
                    bookingRequestId,
                    source = WhatsAppConsentPolicy.ResolveSource(cita),
                    hasClienteId = cita.ClienteId.HasValue
                }, JsonOptions),
                CreatedAtUtc = nowUtc,
                ProcessedAtUtc = nowUtc
            });

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Cancelacion WhatsApp omitida por regla de WhatsApp. TenantId {TenantId}. CitaId {CitaId}. BookingRequestId {BookingRequestId}. Motivo {SkipReason}.",
                cita.TenantId,
                cita.Id,
                bookingRequestId,
                errorCode);
        }

        private Task MarkFailedAsync(
            PreparedAppointmentCancellation prepared,
            MetaWhatsAppSendResult sendResult,
            DateTime nowUtc,
            CancellationToken cancellationToken) =>
            _context.WhatsAppMessageLogs
                .Where(message => message.Id == prepared.MessageLogId)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(message => message.Status, WhatsAppMessageStatuses.Failed)
                    .SetProperty(message => message.ErrorCode, Truncate(sendResult.ErrorCode, 80))
                    .SetProperty(message => message.ErrorMessage, Truncate(sendResult.ErrorMessage, 1000))
                    .SetProperty(message => message.FailedAtUtc, nowUtc)
                    .SetProperty(message => message.LastAttemptAtUtc, nowUtc)
                    .SetProperty(message => message.ProcessedAtUtc, nowUtc)
                    .SetProperty(message => message.AttemptCount, 1)
                    .SetProperty(message => message.PayloadJson, BuildSendResultPayloadJson(prepared, sendResult)),
                    cancellationToken);

        private static string BuildSendResultPayloadJson(
            PreparedAppointmentCancellation prepared,
            MetaWhatsAppSendResult sendResult) =>
            JsonSerializer.Serialize(new
            {
                phase = sendResult.Success ? "sent" : "send_failed",
                notificationType = WhatsAppNotificationTypes.Cancellation,
                citaId = prepared.CitaId,
                bookingRequestId = prepared.BookingRequestId,
                sendResult.MetaMessageId,
                statusCode = sendResult.StatusCode.HasValue ? (int?)sendResult.StatusCode.Value : null,
                sendResult.ErrorCode,
                sendResult.ErrorType,
                sendResult.ErrorSubcode,
                sendResult.ErrorMessage,
                sendResult.FbTraceId,
                sendResult.ShouldRetry,
                sendResult.Endpoint
            }, JsonOptions);

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string ResolveSkippedStatus(string? errorCode) =>
            errorCode switch
            {
                WhatsAppErrorCodes.ConsentMissing => WhatsAppMessageStatuses.SkippedConsentMissing,
                WhatsAppErrorCodes.TenantDisabled => WhatsAppMessageStatuses.SkippedTenantDisabled,
                WhatsAppErrorCodes.DailyLimitExceeded => WhatsAppMessageStatuses.SkippedDailyLimitExceeded,
                WhatsAppErrorCodes.NoActiveWhatsAppAddon => WhatsAppMessageStatuses.SkippedSubscriptionRequired,
                WhatsAppErrorCodes.NoActiveBaseSubscription => WhatsAppMessageStatuses.SkippedSubscriptionRequired,
                WhatsAppErrorCodes.NotConfigured => WhatsAppMessageStatuses.SkippedConfiguration,
                WhatsAppErrorCodes.SubscriptionRequired => WhatsAppMessageStatuses.SkippedSubscriptionRequired,
                WhatsAppErrorCodes.MonthlyLimitExceeded => WhatsAppMessageStatuses.SkippedMonthlyLimitExceeded,
                WhatsAppErrorCodes.InsufficientBalance => WhatsAppMessageStatuses.SkippedMonthlyLimitExceeded,
                WhatsAppErrorCodes.UserDisabled => WhatsAppMessageStatuses.SkippedUserDisabled,
                WhatsAppErrorCodes.NotificationTypeDisabled => WhatsAppMessageStatuses.SkippedUserDisabled,
                WhatsAppErrorCodes.InvalidPhone => WhatsAppMessageStatuses.SkippedInvalidPhone,
                _ => WhatsAppMessageStatuses.SkippedConfiguration
            };
    }
}
