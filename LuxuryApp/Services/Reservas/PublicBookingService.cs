using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Notifications;
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
        private readonly IBookingCatalogService _catalogService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantWhatsAppFeatureService _whatsAppFeatureService;
        private readonly INotificationService _notificationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<PublicBookingService> _logger;

        public PublicBookingService(
            ApplicationDbContext context,
            IBookingSettingsService settingsService,
            IBookingAvailabilityService availabilityService,
            IBookingCatalogService catalogService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantWhatsAppFeatureService whatsAppFeatureService,
            INotificationService notificationService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<PublicBookingService> logger)
        {
            _context = context;
            _settingsService = settingsService;
            _availabilityService = availabilityService;
            _catalogService = catalogService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _whatsAppFeatureService = whatsAppFeatureService;
            _notificationService = notificationService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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
            int? preselectedServiceId = null,
            CancellationToken cancellationToken = default)
        {
            // Solo servicios publicados (con fallback a todos los activos si no hay configuración).
            var servicios = await _catalogService.GetPublicServicesAsync(cancellationToken);
            var preselectedService = preselectedServiceId.HasValue
                ? servicios.FirstOrDefault(servicio => servicio.Id == preselectedServiceId.Value)
                : null;

            var funcionarios = context.PermiteElegirFuncionario
                ? await _context.Funcionarios
                    .AsNoTracking()
                    .Where(f => f.Activo)
                    .OrderBy(f => f.Nombre)
                    .Select(f => new PublicBookingEmployeeOption
                    {
                        Id = f.IdFuncionario,
                        Nombre = f.Nombre,
                        Puesto = f.Puesto != null ? f.Puesto.NombrePuesto : null,
                        // Foto solo si el negocio la habilita y el funcionario lo permite.
                        FotoUrl = (context.MostrarFotosFuncionarios && f.MostrarFotoEnReservas) ? f.FotoUrl : null,
                        ColorAvatar = f.ColorCalendario
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
                SubmissionToken = Guid.NewGuid().ToString("N"),
                PreselectedServiceId = preselectedService?.Id,
                PreselectedServiceName = preselectedService?.Nombre,
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
                    Success = false,
                    Fecha = fecha ?? string.Empty,
                    Mensaje = "Selecciona una fecha válida."
                };
            }

            // Seguridad: el servicio debe estar publicado online (bloquea ids manipulados/ocultos).
            var servicios = await _catalogService.GetPublicServicesAsync(cancellationToken);
            var servicio = servicios.FirstOrDefault(s => s.Id == servicioId);
            if (servicio is null)
            {
                return new BookingAvailabilityResult
                {
                    Success = false,
                    Fecha = fechaParsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Mensaje = "Ese servicio no está disponible para reservas online."
                };
            }

            var fechaIso = fechaParsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var funcionarioFiltro = ResolveFuncionarioFiltro(context, funcionarioId);

            // Nombre del profesional elegido (solo si es compatible con el servicio).
            string? nombreFuncionario = null;
            if (funcionarioFiltro.HasValue)
            {
                if (!servicio.FuncionarioIds.Contains(funcionarioFiltro.Value))
                {
                    // Profesional no compatible con el servicio: no exponemos horarios.
                    return new BookingAvailabilityResult
                    {
                        Success = true,
                        Fecha = fechaIso,
                        DurationMinutes = servicio.DuracionMinutos,
                        ServiceName = servicio.Nombre,
                        Horas = Array.Empty<string>(),
                        Mensaje = "Ese profesional no atiende este servicio. Elegí otro profesional."
                    };
                }

                nombreFuncionario = await _context.Funcionarios
                    .AsNoTracking()
                    .Where(f => f.IdFuncionario == funcionarioFiltro.Value && f.Activo)
                    .Select(f => f.Nombre)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var horas = await _availabilityService.GetAvailableSlotsAsync(
                servicioId,
                fechaParsed,
                funcionarioFiltro,
                cancellationToken);

            var result = new BookingAvailabilityResult
            {
                Success = true,
                Fecha = fechaIso,
                DurationMinutes = servicio.DuracionMinutos,
                ServiceName = servicio.Nombre,
                SelectedEmployeeName = nombreFuncionario,
                Horas = horas
            };

            if (horas.Count > 0)
            {
                return result;
            }

            // ── Sin horas ese día: construir mensaje inteligente + próximas disponibilidades ──
            var fechaLabel = FormatFechaLabel(fechaParsed);

            if (funcionarioFiltro.HasValue)
            {
                // ¿Hay disponibilidad con otros profesionales compatibles esa misma fecha?
                var horasCualquiera = await _availabilityService.GetAvailableSlotsAsync(
                    servicioId, fechaParsed, funcionarioId: null, cancellationToken);
                result.HasAvailabilityWithOtherEmployees = horasCualquiera.Count > 0;

                var nombre = nombreFuncionario ?? "ese profesional";
                result.Mensaje =
                    $"No encontramos espacios de {servicio.DuracionMinutos} min para {servicio.Nombre} con {nombre} el {fechaLabel}. " +
                    $"Este servicio requiere un bloque continuo de {servicio.DuracionMinutos} minutos.";
            }
            else
            {
                result.Mensaje =
                    $"No encontramos espacios de {servicio.DuracionMinutos} min para {servicio.Nombre} en esa fecha. " +
                    "Probá con otra fecha o revisá los próximos espacios disponibles.";
            }

            result.NextAvailableSlots = await BuildNextAvailableSlotsAsync(
                servicioId, fechaParsed, funcionarioFiltro, cancellationToken);

            return result;
        }

        private async Task<IReadOnlyList<NextAvailableSlot>> BuildNextAvailableSlotsAsync(
            int servicioId,
            DateOnly fromDate,
            int? funcionarioId,
            CancellationToken cancellationToken)
        {
            var sugerencias = await _availabilityService.GetNextAvailableSlotsAsync(
                servicioId, fromDate, funcionarioId, maxSuggestions: 5, cancellationToken);

            if (sugerencias.Count == 0)
            {
                return Array.Empty<NextAvailableSlot>();
            }

            // Nombres de funcionarios de las sugerencias en una sola consulta.
            var ids = sugerencias.Select(s => s.FuncionarioId).Distinct().ToList();
            var nombres = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => ids.Contains(f.IdFuncionario))
                .Select(f => new { f.IdFuncionario, f.Nombre })
                .ToDictionaryAsync(f => f.IdFuncionario, f => f.Nombre, cancellationToken);

            return sugerencias.Select(s => new NextAvailableSlot
            {
                Fecha = s.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FechaLabel = FormatFechaLabel(s.Fecha),
                Hora = s.Hora.ToString("HH:mm", CultureInfo.InvariantCulture),
                HoraLabel = FormatHoraLabel(s.Hora),
                FuncionarioId = s.FuncionarioId,
                FuncionarioNombre = nombres.TryGetValue(s.FuncionarioId, out var n) ? n : null
            }).ToList();
        }

        private static readonly CultureInfo CrCulture = CultureInfo.GetCultureInfo("es-CR");

        private static string FormatFechaLabel(DateOnly fecha)
        {
            var label = fecha.ToDateTime(TimeOnly.MinValue).ToString("dddd dd/MM", CrCulture);
            return label.Length > 0 ? char.ToUpper(label[0], CrCulture) + label[1..] : label;
        }

        private static string FormatHoraLabel(TimeOnly hora) =>
            hora.ToString("h:mm tt", CrCulture);

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

            // Idempotencia: si este token ya creó una solicitud (doble click, reintento, JS duplicado),
            // devolvemos éxito con la solicitud existente y NO creamos otra ni reenviamos notificación.
            var token = NormalizeToken(input.SubmissionToken);
            if (token is not null)
            {
                var existentePorToken = await _context.BookingRequests
                    .AsNoTracking()
                    .AnyAsync(r => r.PublicSubmissionToken == token, cancellationToken);

                if (existentePorToken)
                {
                    return PublicBookingSubmitResult.Ok(mensajeExito);
                }
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

            // Seguridad: no se puede reservar un servicio oculto/inactivo manipulando el request.
            if (!await _catalogService.IsServiceVisibleOnlineAsync(input.ServicioId, cancellationToken))
            {
                return PublicBookingSubmitResult.Fail("El servicio seleccionado no está disponible para reservas online.");
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
                PublicSubmissionToken = token,
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
                // Carrera: otro POST con el mismo token insertó primero (índice único). Es idempotente:
                // devolvemos éxito sin crear duplicado ni reenviar notificación.
                _context.Entry(solicitud).State = EntityState.Detached;
                if (token is not null &&
                    await _context.BookingRequests.AsNoTracking()
                        .AnyAsync(r => r.PublicSubmissionToken == token, cancellationToken))
                {
                    return PublicBookingSubmitResult.Ok(mensajeExito);
                }

                return PublicBookingSubmitResult.Fail(
                    "No pudimos registrar tu solicitud en este momento. Intentá de nuevo.");
            }

            // Centro de Notificaciones: avisa al negocio que llegó una solicitud nueva.
            // Nunca debe romper el flujo público si la notificación falla.
            try
            {
                await _notificationService.CreateBookingRequestReceivedAsync(solicitud, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo generar la notificación de la solicitud de reserva {RequestId}.",
                    solicitud.Id);
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

        /// <summary>Normaliza el token de envío: solo alfanuméricos/guiones, máx. 64. Vacío → null.</summary>
        private static string? NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var cleaned = new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            if (cleaned.Length == 0)
            {
                return null;
            }

            return cleaned.Length > 64 ? cleaned[..64] : cleaned;
        }

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
