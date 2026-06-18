using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Reservas
{
    public sealed class PublicBookingService : IPublicBookingService
    {
        private const string ResolvedTenantItemKey = "__resolved_tenant_id";
        private const int MaxPendingPerPhone = 3;

        private readonly ApplicationDbContext _context;
        private readonly IBookingSettingsService _settingsService;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantWhatsAppFeatureService _whatsAppFeatureService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PublicBookingService(
            ApplicationDbContext context,
            IBookingSettingsService settingsService,
            IBookingAvailabilityService availabilityService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantWhatsAppFeatureService whatsAppFeatureService,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _settingsService = settingsService;
            _availabilityService = availabilityService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _whatsAppFeatureService = whatsAppFeatureService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<PublicBookingTenantContext?> ResolveContextAsync(
            string slug,
            CancellationToken cancellationToken = default)
        {
            var context = await _settingsService.ResolvePublicBySlugAsync(slug, cancellationToken);
            if (context is null)
            {
                return null;
            }

            // Fija el tenant en el request para que el resto de consultas tenant-scoped
            // (servicios, funcionarios, citas) y el SaveChanges blindado lo apliquen.
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is not null)
            {
                httpContext.Items[ResolvedTenantItemKey] = context.TenantId;
            }

            return context;
        }

        public async Task<PublicBookingPageViewModel> BuildPageAsync(
            PublicBookingTenantContext context,
            CancellationToken cancellationToken = default)
        {
            var servicios = await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Activo)
                .OrderBy(s => s.Nombre)
                .Select(s => new PublicBookingServiceOption
                {
                    Id = s.Id,
                    Nombre = s.Nombre,
                    DuracionMinutos = s.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes
                })
                .ToListAsync(cancellationToken);

            var funcionarios = context.PermiteElegirFuncionario
                ? await _context.Funcionarios
                    .AsNoTracking()
                    .Where(f => f.Activo)
                    .OrderBy(f => f.Nombre)
                    .Select(f => new PublicBookingEmployeeOption
                    {
                        Id = f.IdFuncionario,
                        Nombre = f.Nombre
                    })
                    .ToListAsync(cancellationToken)
                : new List<PublicBookingEmployeeOption>();

            var today = _businessDateTimeProvider.Today();
            var maxDate = today.AddDays(Math.Max(1, context.MaxDaysAhead));

            // Solo ofrecemos el checkbox de WhatsApp si el tenant lo tiene activo,
            // para no prometer una confirmación que no se enviará.
            var mostrarWhatsApp = await _whatsAppFeatureService
                .IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);

            return new PublicBookingPageViewModel
            {
                Slug = context.Slug,
                NombreNegocio = context.NombreNegocio,
                MensajeBienvenida = context.MensajeBienvenida,
                PermiteElegirFuncionario = context.PermiteElegirFuncionario,
                PermiteCualquierFuncionario = context.PermiteCualquierFuncionario,
                MostrarWhatsApp = mostrarWhatsApp,
                MinAdvanceMinutes = context.MinAdvanceMinutes,
                MaxDaysAhead = context.MaxDaysAhead,
                MinDateIso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MaxDateIso = maxDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Servicios = servicios,
                Funcionarios = funcionarios
            };
        }

        public async Task<BookingAvailabilityResult> GetAvailabilityAsync(
            PublicBookingTenantContext context,
            int servicioId,
            string? fecha,
            int? funcionarioId,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseDate(fecha, out var fechaParsed))
            {
                return new BookingAvailabilityResult
                {
                    Fecha = fecha ?? string.Empty,
                    Mensaje = "Selecciona una fecha válida."
                };
            }

            var funcionarioFiltro = ResolveFuncionarioFiltro(context, funcionarioId);

            var horas = await _availabilityService.GetAvailableSlotsAsync(
                servicioId,
                fechaParsed,
                funcionarioFiltro,
                cancellationToken);

            return new BookingAvailabilityResult
            {
                Fecha = fechaParsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Horas = horas,
                Mensaje = horas.Count == 0
                    ? "No hay espacios disponibles para ese día. Probá con otra fecha."
                    : null
            };
        }

        public async Task<PublicBookingSubmitResult> SubmitAsync(
            PublicBookingTenantContext context,
            PublicBookingRequestInput input,
            CancellationToken cancellationToken = default)
        {
            var mensajeExito = string.IsNullOrWhiteSpace(context.MensajeConfirmacion)
                ? "Tu solicitud fue enviada. El negocio la revisará y te confirmará pronto."
                : context.MensajeConfirmacion!.Trim();

            // Honeypot: si un bot rellenó el campo oculto, se simula éxito sin crear nada.
            if (!string.IsNullOrWhiteSpace(input.Website))
            {
                return PublicBookingSubmitResult.Ok(mensajeExito);
            }

            var nombre = CollapseWhitespace(input.Nombre);
            var telefono = (input.Telefono ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return PublicBookingSubmitResult.Fail("Indicá tu nombre completo.");
            }

            if (nombre.Length > 100)
            {
                return PublicBookingSubmitResult.Fail("El nombre es demasiado largo.");
            }

            if (string.IsNullOrWhiteSpace(telefono) || telefono.Length < 6 || telefono.Length > 30)
            {
                return PublicBookingSubmitResult.Fail("Indicá un número de teléfono válido.");
            }

            if (!TryParseDate(input.Fecha, out var fecha) || !TryParseTime(input.Hora, out var hora))
            {
                return PublicBookingSubmitResult.Fail("Selecciona una fecha y hora válidas.");
            }

            var inicio = fecha.ToDateTime(hora);

            // Validaciones de rango (revalidadas en backend, no se confía en el frontend).
            var now = _businessDateTimeProvider.Now();
            if (inicio <= now)
            {
                return PublicBookingSubmitResult.Fail("No podés reservar en una fecha u hora pasada.");
            }

            if (inicio < now.AddMinutes(Math.Max(0, context.MinAdvanceMinutes)))
            {
                return PublicBookingSubmitResult.Fail("Ese horario es demasiado pronto. Elegí uno más adelante.");
            }

            var maxFecha = DateOnly.FromDateTime(_businessDateTimeProvider.Today()).AddDays(Math.Max(1, context.MaxDaysAhead));
            if (fecha > maxFecha)
            {
                return PublicBookingSubmitResult.Fail("Esa fecha está fuera del rango permitido.");
            }

            // Funcionario solicitado (respeta las reglas del tenant).
            var funcionarioSolicitado = ResolveFuncionarioFiltro(context, input.FuncionarioId);
            if (input.FuncionarioId.HasValue && input.FuncionarioId.Value > 0 && !context.PermiteElegirFuncionario)
            {
                // Se ignora la selección si el tenant no la permite.
                funcionarioSolicitado = null;
            }

            // Revalida disponibilidad real en backend.
            var resolucion = await _availabilityService.ResolveSlotAsync(
                input.ServicioId,
                inicio,
                funcionarioSolicitado,
                cancellationToken);

            if (!resolucion.Disponible)
            {
                return PublicBookingSubmitResult.Fail(
                    resolucion.Motivo ?? "Ese horario ya no está disponible. Probá con otro.");
            }

            // Anti-spam: máximo de solicitudes Pending por teléfono por tenant.
            var pendientesMismoTelefono = await _context.BookingRequests
                .AsNoTracking()
                .CountAsync(
                    r => r.TelefonoCliente == telefono && r.Estado == BookingRequestStates.Pending,
                    cancellationToken);

            if (pendientesMismoTelefono >= MaxPendingPerPhone)
            {
                return PublicBookingSubmitResult.Fail(
                    "Ya tenés varias solicitudes pendientes. Esperá a que el negocio te confirme.");
            }

            // Duplicado obvio: misma fecha/hora/servicio/teléfono aún pendiente.
            var duplicada = await _context.BookingRequests
                .AsNoTracking()
                .AnyAsync(
                    r => r.TelefonoCliente == telefono &&
                         r.ServicioId == input.ServicioId &&
                         r.FechaHoraInicioSolicitada == inicio &&
                         r.Estado == BookingRequestStates.Pending,
                    cancellationToken);

            if (duplicada)
            {
                return PublicBookingSubmitResult.Ok(mensajeExito);
            }

            var clienteId = await TryMatchClienteAsync(telefono, cancellationToken);

            var solicitud = new BookingRequest
            {
                ServicioId = input.ServicioId,
                FuncionarioId = funcionarioSolicitado,
                ClienteId = clienteId,
                NombreCliente = nombre,
                TelefonoCliente = telefono,
                CorreoCliente = null,
                NotasCliente = null,
                FechaHoraInicioSolicitada = inicio,
                FechaHoraFinCalculada = inicio.AddMinutes(resolucion.DuracionMinutos),
                DuracionMinutos = resolucion.DuracionMinutos,
                Estado = BookingRequestStates.Pending,
                Origen = BookingRequestOrigins.PublicLink,
                AceptaWhatsApp = input.AceptaWhatsApp,
                CreatedAtUtc = DateTime.UtcNow,
                IpHash = HashIp(),
                UserAgent = ResolveUserAgent()
            };

            _context.BookingRequests.Add(solicitud);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return PublicBookingSubmitResult.Fail(
                    "No pudimos registrar tu solicitud en este momento. Intentá de nuevo.");
            }

            return PublicBookingSubmitResult.Ok(mensajeExito);
        }

        private static int? ResolveFuncionarioFiltro(PublicBookingTenantContext context, int? funcionarioId)
        {
            if (!context.PermiteElegirFuncionario)
            {
                return null;
            }

            return funcionarioId.HasValue && funcionarioId.Value > 0 ? funcionarioId : null;
        }

        private async Task<int?> TryMatchClienteAsync(string telefono, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(telefono))
            {
                return null;
            }

            return await _context.Clientes
                .AsNoTracking()
                .Where(c => c.NumeroTelefono == telefono)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private string? HashIp()
        {
            var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(ip))
            {
                return null;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
            return Convert.ToHexString(bytes)[..64].ToLowerInvariant();
        }

        private string? ResolveUserAgent()
        {
            var ua = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua))
            {
                return null;
            }

            return ua.Length > 400 ? ua[..400] : ua;
        }

        private static bool TryParseDate(string? value, out DateOnly date) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static bool TryParseTime(string? value, out TimeOnly time) =>
            TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

        private static string CollapseWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(
                ' ',
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
}
