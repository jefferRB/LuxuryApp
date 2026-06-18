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

        private readonly ApplicationDbContext _context;
        private readonly ICalendarCommandService _calendarCommandService;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingSettingsService _settingsService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<BookingRequestService> _logger;

        public BookingRequestService(
            ApplicationDbContext context,
            ICalendarCommandService calendarCommandService,
            IBookingAvailabilityService availabilityService,
            IBookingSettingsService settingsService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            IHttpContextAccessor httpContextAccessor,
            ILogger<BookingRequestService> logger)
        {
            _context = context;
            _calendarCommandService = calendarCommandService;
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
                    AceptaWhatsApp = r.AceptaWhatsApp
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
                return BookingActionResult.Fail(
                    "Ese horario ya no está disponible. Selecciona otra hora o rechaza la solicitud.");
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
                // Reutiliza el flujo de creación de citas: valida solapamiento (doble seguridad)
                // y dispara la confirmación/recordatorio por WhatsApp según la lógica actual del tenant.
                citaCreada = await _calendarCommandService.CreateAsync(upsert, cancellationToken);
            }
            catch (CalendarValidationException ex)
            {
                return BookingActionResult.Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "No fue posible crear la cita al confirmar la solicitud {RequestId}.", requestId);
                return BookingActionResult.Fail("No fue posible crear la cita. Intentá de nuevo.");
            }

            solicitud.Estado = BookingRequestStates.Confirmed;
            solicitud.FuncionarioId = resolucion.FuncionarioId.Value;
            solicitud.ClienteId = clienteId;
            solicitud.ConvertedCitaId = citaCreada.Id;
            solicitud.ConfirmedAtUtc = DateTime.UtcNow;
            solicitud.ConfirmedByUserId = userId;

            await _context.SaveChangesAsync(cancellationToken);

            return BookingActionResult.Ok("Solicitud confirmada y cita creada.", citaCreada.Id);
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
