using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Reservas
{
    public sealed class BookingRequestService : IBookingRequestService
    {
        private const string WhatsAppConsentSourceReserva = "ReservaOnline";

        private const string WhatsAppSourceReservaAprobada = "ReservaOnlineAprobada";

        /// <summary>Éxito de la operación principal. WhatsApp, si aplica, se agrega aparte.</summary>
        private const string AprobacionMensajeBase = "Reserva aprobada y cita creada.";

        private readonly ApplicationDbContext _context;
        private readonly ICalendarCommandService _calendarCommandService;
        private readonly ICalendarWhatsAppNotificationService _notificationService;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingSettingsService _settingsService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantWhatsAppFeatureService _whatsAppFeatureService;
        private readonly IBookingRejectionWhatsAppService _rejectionNotificationService;
        private readonly ILogger<BookingRequestService> _logger;

        public BookingRequestService(
            ApplicationDbContext context,
            ICalendarCommandService calendarCommandService,
            ICalendarWhatsAppNotificationService notificationService,
            IBookingAvailabilityService availabilityService,
            IBookingSettingsService settingsService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            IHttpContextAccessor httpContextAccessor,
            ITenantWhatsAppFeatureService whatsAppFeatureService,
            IBookingRejectionWhatsAppService rejectionNotificationService,
            ILogger<BookingRequestService> logger)
        {
            _context = context;
            _calendarCommandService = calendarCommandService;
            _notificationService = notificationService;
            _availabilityService = availabilityService;
            _settingsService = settingsService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _httpContextAccessor = httpContextAccessor;
            _whatsAppFeatureService = whatsAppFeatureService;
            _rejectionNotificationService = rejectionNotificationService;
            _logger = logger;
        }

        public async Task<BookingRequestsPageViewModel> BuildPageAsync(
            string? estado,
            string? rango,
            CancellationToken cancellationToken = default)
        {
            // Allowlist: cualquier valor desconocido o manipulado cae al default seguro.
            var estadoFiltro = BookingRequestFilters.ParseStatus(estado);
            var rangoFiltro = BookingRequestFilters.ParseRange(rango);

            // El rango se resuelve en hora local del negocio y se consulta en UTC (como se guarda).
            var businessNow = _businessDateTimeProvider.NowOffset();
            var ventana = BookingRequestDateRangeResolver.Resolve(rangoFiltro, businessNow);

            // Dataset base de la pantalla: manda sobre TODO lo que se muestra (conteos y tarjetas).
            // El tenant lo aplica el global query filter.
            var baseQuery = ApplyPanelScope(_context.BookingRequests.AsNoTracking(), ventana);

            // Conteos por estado, agrupados en SQL, sobre exactamente el mismo dataset que el
            // listado. No dependen de la pestaña activa: cambiar de pestaña no puede alterarlos.
            var counts = await baseQuery
                .GroupBy(r => r.Estado)
                .Select(g => new { Estado = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            // La pestaña solo decide qué tarjetas se ven.
            var estadoPersistido = estadoFiltro.ToPersistedState();
            var visibles = estadoPersistido is null
                ? baseQuery
                : baseQuery.Where(r => r.Estado == estadoPersistido);

            var solicitudes = await visibles
                .OrderByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.Id)
                .Select(r => new BookingRequestListItemViewModel
                {
                    Id = r.Id,
                    NombreCliente = r.NombreCliente,
                    TelefonoCliente = r.TelefonoCliente,
                    CorreoCliente = r.CorreoCliente,
                    ServicioNombre = r.Servicio != null ? r.Servicio.Nombre : "Servicio",
                    FuncionarioNombre = r.FuncionarioId != null && r.Funcionario != null
                        ? r.Funcionario.Nombre
                        : "Cualquier profesional",
                    // Recurso que el sistema reservó para esta solicitud. Es OTRA cosa que lo
                    // solicitado: con "cualquier profesional" el servidor ya eligió a alguien.
                    FuncionarioAsignadoNombre = r.FuncionarioAsignadoId != null && r.FuncionarioAsignado != null
                        ? r.FuncionarioAsignado.Nombre
                        : null,
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

            // "Recibida": UTC persistido → hora local del negocio. Se hace una sola vez, ya
            // materializado, para no arrastrar aritmética de zona horaria a SQL.
            var offset = businessNow.Offset;
            foreach (var solicitud in solicitudes)
            {
                solicitud.RecibidaLocal = BookingRequestDateRangeResolver.ToBusinessLocal(
                    solicitud.CreatedAtUtc,
                    offset);
            }

            var slug = await _settingsService.GetCurrentSlugAsync(cancellationToken);

            // Una sola consulta por request; la vista no vuelve a razonar sobre el complemento.
            var whatsAppActivo = await _whatsAppFeatureService.HasWhatsAppAddonAsync(cancellationToken);

            return new BookingRequestsPageViewModel
            {
                EstadoFiltro = estadoFiltro,
                RangoFiltro = rangoFiltro,
                // Backlog completo: es el mismo criterio con el que se listan las pendientes.
                PendientesCount = counts.Where(c => c.Estado == BookingRequestStates.Pending).Sum(c => c.Count),
                ConfirmadasCount = counts.Where(c => c.Estado == BookingRequestStates.Confirmed).Sum(c => c.Count),
                RechazadasCount = counts.Where(c => c.Estado == BookingRequestStates.Rejected).Sum(c => c.Count),
                TotalCount = counts.Sum(c => c.Count),
                ReservasActivas = !string.IsNullOrWhiteSpace(slug),
                WhatsAppActivo = whatsAppActivo,
                Slug = slug,
                LinkPublico = BookingLinkBuilder.Build(_httpContextAccessor.HttpContext?.Request, slug),
                Solicitudes = solicitudes
            };
        }

        /// <summary>
        /// Regla única del panel de solicitudes, compartida por los conteos y por el listado.
        ///
        /// <para>
        /// Una solicitud <c>Pending</c> es TRABAJO SIN RESOLVER: no la limita ningún rango de
        /// fechas y solo sale de la lista cuando alguien la confirma o la rechaza. El resto de
        /// estados (ya resueltos) sí se filtra por fecha de recepción, que es lo que el selector
        /// de período controla.
        /// </para>
        /// </summary>
        private static IQueryable<BookingRequest> ApplyPanelScope(
            IQueryable<BookingRequest> query,
            BookingRequestUtcRange ventana)
        {
            var desdeUtc = ventana.StartUtc;
            var hastaUtc = ventana.EndUtc;

            return query.Where(r =>
                r.Estado == BookingRequestStates.Pending ||
                (r.CreatedAtUtc >= desdeUtc && r.CreatedAtUtc < hastaUtc));
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

            // Orden de preferencia: lo que el admin eligió a mano → el recurso que el sistema ya
            // tenía reservado para el hold → lo que pidió el cliente. Confirmar NO vuelve a
            // sortear a otra persona: quien venía ocupando ese espacio es quien atiende.
            var funcionarioDeseado = funcionarioIdOverride.HasValue && funcionarioIdOverride.Value > 0
                ? funcionarioIdOverride
                : solicitud.FuncionarioAsignadoId ?? solicitud.FuncionarioId;

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

            // Enlaza la cita creada (sin tocar Estado: ya está Confirmed por el claim). Se escribe
            // el funcionario ASIGNADO; FuncionarioId conserva lo que pidió el cliente para que la
            // administración siga distinguiendo "pidió cualquiera" de "pidió a esta persona".
            await _context.BookingRequests
                .Where(r => r.Id == requestId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ConvertedCitaId, citaCreada.Id)
                    .SetProperty(r => r.FuncionarioAsignadoId, resolucion.FuncionarioId.Value)
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
            var existe = await _context.BookingRequests
                .AsNoTracking()
                .AnyAsync(r => r.Id == requestId, cancellationToken);

            if (!existe)
            {
                return BookingActionResult.Fail("La solicitud no existe o no pertenece a tu negocio.");
            }

            // El motivo viaja al cliente en el aviso de WhatsApp: nunca puede quedar vacío, y la
            // garantía es del backend (no del required del formulario).
            var motivo = BookingRejectionDefaults.NormalizarMotivo(reason);

            // Claim atómico Pending → Rejected, igual que ConfirmAsync. Es la fuente de verdad de
            // "estoy rechazando por primera vez": solo el request que gana avisa al cliente, así un
            // doble click o un reintento no mandan dos WhatsApp.
            var rejectedAtUtc = DateTime.UtcNow;
            var claimed = await _context.BookingRequests
                .Where(r => r.Id == requestId && r.Estado == BookingRequestStates.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Estado, BookingRequestStates.Rejected)
                    .SetProperty(r => r.RejectedAtUtc, (DateTime?)rejectedAtUtc)
                    .SetProperty(r => r.RejectedByUserId, userId)
                    .SetProperty(r => r.RejectedReason, motivo),
                    cancellationToken);

            if (claimed == 0)
            {
                return BookingActionResult.Fail("Esta solicitud ya fue procesada.");
            }

            // El rechazo ya está confirmado en base de datos. El aviso al cliente es un efecto
            // secundario: no lanza y nunca revierte el rechazo.
            await _rejectionNotificationService.NotifyRejectionAsync(requestId, cancellationToken);

            return BookingActionResult.Ok("Solicitud rechazada.");
        }

        public async Task<IReadOnlyList<CalendarPendingBookingResponse>> GetPendingForCalendarAsync(
            DateOnly fecha,
            CancellationToken cancellationToken = default)
        {
            var desde = fecha.ToDateTime(TimeOnly.MinValue);
            var hasta = desde.AddDays(1);

            // Sólo Pending: una confirmada ya es Cita (la pinta el read model de citas) y una
            // rechazada no ocupa nada. Tenant-safe por el global query filter de BookingRequest.
            return await _context.BookingRequests
                .AsNoTracking()
                .Where(r =>
                    r.Estado == BookingRequestStates.Pending &&
                    r.FechaHoraInicioSolicitada >= desde &&
                    r.FechaHoraInicioSolicitada < hasta &&
                    (r.FuncionarioAsignadoId != null || r.FuncionarioId != null))
                .OrderBy(r => r.FechaHoraInicioSolicitada)
                .Select(r => new CalendarPendingBookingResponse
                {
                    Id = r.Id,
                    FuncionarioId = r.FuncionarioAsignadoId != null
                        ? r.FuncionarioAsignadoId.Value
                        : r.FuncionarioId!.Value,
                    FuncionarioNombre = r.FuncionarioAsignadoId != null
                        ? (r.FuncionarioAsignado != null ? r.FuncionarioAsignado.Nombre : string.Empty)
                        : (r.Funcionario != null ? r.Funcionario.Nombre : string.Empty),
                    NombreCliente = r.NombreCliente,
                    TelefonoCliente = r.TelefonoCliente,
                    ServicioNombre = r.Servicio != null ? r.Servicio.Nombre : null,
                    FechaHoraInicio = r.FechaHoraInicioSolicitada,
                    DuracionMinutos = r.DuracionMinutos > 0
                        ? r.DuracionMinutos
                        : ((r.Servicio != null ? r.Servicio.DuracionMinutos : null) ?? 30),
                    SolicitoCualquierFuncionario = r.FuncionarioId == null,
                    AceptaWhatsApp = r.AceptaWhatsApp,
                    NotasCliente = r.NotasCliente,
                    CreatedAtUtc = r.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
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

            if (result.Reason == WhatsAppNotificationReason.ConsentMissing)
            {
                // El cliente sí pudo autorizar y no lo hizo: es dato útil, no una advertencia.
                return (
                    $"{AprobacionMensajeBase} El cliente no autorizó notificaciones por WhatsApp.",
                    "skipped");
            }

            // WhatsApp es un side effect opcional: si el negocio no contrató el complemento, la
            // aprobación se informa como cualquier otra y no se menciona WhatsApp.
            if (result.Reason.IsSilent())
            {
                return (AprobacionMensajeBase, null);
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
                    ($"{AprobacionMensajeBase} {result.Message}", "skipped")
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

    }
}
