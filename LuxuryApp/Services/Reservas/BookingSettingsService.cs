using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.Tenant;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Reservas
{
    public sealed partial class BookingSettingsService : IBookingSettingsService
    {
        // Palabras reservadas que no pueden usarse como slug (chocarían con rutas del sistema).
        private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
        {
            "admin", "login", "api", "app", "reservar", "dashboard", "plataforma",
            "account", "accounts", "identity", "static", "assets", "www",
            "home", "billing", "platform", "comprobantes", "calendar", "miportal",
            "soporte", "privacidad", "contrato", "calendario", "reservas",
            "clientes", "funcionarios", "productos", "ingresos", "egresos", "informacion"
        };

        private const int SlugMinLength = 3;
        private const int SlugMaxLength = 60;

        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly ITenantDisplayNameService _tenantDisplayNameService;

        public BookingSettingsService(
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            ITenantDisplayNameService tenantDisplayNameService)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _tenantDisplayNameService = tenantDisplayNameService;
        }

        public async Task<BookingSettingsViewModel> BuildSettingsViewModelAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _context.TenantBookingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            var nombreNegocio = await GetTenantNameAsync(cancellationToken);

            if (settings is null)
            {
                // Sugerencia de slug único derivada del nombre del negocio (sin chocar con otros tenants).
                var sugerido = await GenerateUniqueSlugAsync(nombreNegocio, GetCurrentTenantId(), cancellationToken);

                var defaults = new BookingSettingsViewModel
                {
                    NombreNegocio = nombreNegocio,
                    PublicBookingSlug = sugerido,
                    DiasLaborales = MaskToDays(TenantBookingSettings.DefaultWorkingDaysMask)
                };
                return defaults;
            }

            return new BookingSettingsViewModel
            {
                PublicBookingEnabled = settings.PublicBookingEnabled,
                PublicBookingSlug = settings.PublicBookingSlug,
                PublicBookingAllowEmployeeSelection = settings.PublicBookingAllowEmployeeSelection,
                PublicBookingAllowAnyEmployee = settings.PublicBookingAllowAnyEmployee,
                PublicBookingMinAdvanceMinutes = settings.PublicBookingMinAdvanceMinutes,
                PublicBookingMaxDaysAhead = settings.PublicBookingMaxDaysAhead,
                PublicBookingWelcomeMessage = settings.PublicBookingWelcomeMessage,
                PublicBookingConfirmationMessage = settings.PublicBookingConfirmationMessage,
                OpenTime = settings.OpenTime,
                CloseTime = settings.CloseTime,
                SlotIntervalMinutes = settings.SlotIntervalMinutes,
                DiasLaborales = MaskToDays(settings.WorkingDaysMask),
                NombreNegocio = nombreNegocio
            };
        }

        public async Task SaveSettingsAsync(
            BookingSettingsViewModel input,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            if (input.CloseTime <= input.OpenTime)
            {
                throw new BookingValidationException("La hora de cierre debe ser posterior a la de apertura.");
            }

            if (input.SlotIntervalMinutes < 5 || input.SlotIntervalMinutes > 240)
            {
                throw new BookingValidationException("El intervalo entre citas debe estar entre 5 y 240 minutos.");
            }

            var mask = DaysToMask(input.DiasLaborales);

            string? slug = null;
            if (input.PublicBookingEnabled || !string.IsNullOrWhiteSpace(input.PublicBookingSlug))
            {
                slug = await ResolveValidSlugAsync(input.PublicBookingSlug, cancellationToken);

                if (input.PublicBookingEnabled && string.IsNullOrWhiteSpace(slug))
                {
                    throw new BookingValidationException("Necesitas un enlace válido para activar las reservas online.", nameof(BookingSettingsViewModel.PublicBookingSlug));
                }
            }

            if (input.PublicBookingEnabled && mask == 0)
            {
                throw new BookingValidationException("Selecciona al menos un día laboral para activar las reservas.");
            }

            var settings = await _context.TenantBookingSettings
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                settings = new TenantBookingSettings();
                _context.TenantBookingSettings.Add(settings);
            }

            settings.PublicBookingEnabled = input.PublicBookingEnabled;
            settings.PublicBookingSlug = slug;
            settings.PublicBookingMode = PublicBookingModes.ManualApproval;
            settings.PublicBookingAllowEmployeeSelection = input.PublicBookingAllowEmployeeSelection;
            settings.PublicBookingAllowAnyEmployee = input.PublicBookingAllowAnyEmployee;
            settings.PublicBookingMinAdvanceMinutes = Math.Clamp(input.PublicBookingMinAdvanceMinutes, 0, 43200);
            settings.PublicBookingMaxDaysAhead = Math.Clamp(input.PublicBookingMaxDaysAhead, 1, 365);
            settings.PublicBookingWelcomeMessage = Trim(input.PublicBookingWelcomeMessage, 500);
            settings.PublicBookingConfirmationMessage = Trim(input.PublicBookingConfirmationMessage, 500);
            settings.OpenTime = input.OpenTime;
            settings.CloseTime = input.CloseTime;
            settings.SlotIntervalMinutes = input.SlotIntervalMinutes;
            settings.WorkingDaysMask = mask;
            settings.UpdatedAtUtc = DateTime.UtcNow;
            settings.UpdatedByUserId = userId;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<PublicBookingTenantContext?> ResolvePublicBySlugAsync(
            string slug,
            CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeSlug(slug);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            // IgnoreQueryFilters: la resolución ocurre antes de tener contexto de tenant.
            var match = await _context.TenantBookingSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.PublicBookingSlug == normalized && s.PublicBookingEnabled)
                .Select(s => new
                {
                    s.TenantId,
                    s.PublicBookingSlug,
                    s.PublicBookingWelcomeMessage,
                    s.PublicBookingConfirmationMessage,
                    s.PublicBookingAllowEmployeeSelection,
                    s.PublicBookingAllowAnyEmployee,
                    s.PublicBookingMinAdvanceMinutes,
                    s.PublicBookingMaxDaysAhead,
                    TenantActivo = _context.Tenants
                        .Any(t => t.Id == s.TenantId && t.Activo)
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (match is null || !match.TenantActivo)
            {
                return null;
            }

            var nombreNegocio = await _tenantDisplayNameService.GetTenantDisplayNameAsync(
                match.TenantId,
                cancellationToken);

            return new PublicBookingTenantContext
            {
                TenantId = match.TenantId,
                NombreNegocio = nombreNegocio,
                Slug = match.PublicBookingSlug!,
                MensajeBienvenida = match.PublicBookingWelcomeMessage,
                MensajeConfirmacion = match.PublicBookingConfirmationMessage,
                PermiteElegirFuncionario = match.PublicBookingAllowEmployeeSelection,
                PermiteCualquierFuncionario = match.PublicBookingAllowAnyEmployee,
                MinAdvanceMinutes = match.PublicBookingMinAdvanceMinutes,
                MaxDaysAhead = match.PublicBookingMaxDaysAhead
            };
        }

        public async Task<string?> GetCurrentSlugAsync(CancellationToken cancellationToken = default)
        {
            return await _context.TenantBookingSettings
                .AsNoTracking()
                .Where(s => s.PublicBookingEnabled)
                .Select(s => s.PublicBookingSlug)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<string?> ResolveValidSlugAsync(string? rawSlug, CancellationToken cancellationToken)
        {
            var currentTenantId = GetCurrentTenantId();
            var slug = NormalizeSlug(rawSlug);

            // Si el usuario lo dejó vacío, autogeneramos una variante única desde el nombre del negocio.
            if (string.IsNullOrEmpty(slug))
            {
                return await GenerateUniqueSlugAsync(
                    await GetTenantNameAsync(cancellationToken),
                    currentTenantId,
                    cancellationToken);
            }

            if (slug.Length < SlugMinLength)
            {
                throw new BookingValidationException("El enlace debe tener al menos 3 caracteres.", nameof(BookingSettingsViewModel.PublicBookingSlug));
            }

            if (ReservedSlugs.Contains(slug))
            {
                throw new BookingValidationException("Ese enlace está reservado por el sistema. Probá con otro nombre.", nameof(BookingSettingsViewModel.PublicBookingSlug));
            }

            // Único entre tenants (excluyendo el propio tenant actual, que puede conservar su slug).
            if (await SlugInUseAsync(slug, currentTenantId, cancellationToken))
            {
                throw new BookingValidationException("Este enlace ya está en uso. Probá con otro nombre.", nameof(BookingSettingsViewModel.PublicBookingSlug));
            }

            return slug;
        }

        /// <summary>
        /// Genera un slug único a partir del nombre del negocio. Si la base ya existe (o es
        /// reservada), agrega un sufijo incremental: barberia-elite, barberia-elite-2, etc.
        /// </summary>
        private async Task<string> GenerateUniqueSlugAsync(
            string? baseName,
            Guid currentTenantId,
            CancellationToken cancellationToken)
        {
            var baseSlug = NormalizeSlug(baseName);

            if (baseSlug.Length < SlugMinLength)
            {
                baseSlug = "negocio";
            }

            // Deja espacio para el sufijo "-NN" sin exceder el máximo.
            if (baseSlug.Length > SlugMaxLength - 4)
            {
                baseSlug = baseSlug[..(SlugMaxLength - 4)].Trim('-');
            }

            var candidate = baseSlug;
            var sufijo = 2;

            while (ReservedSlugs.Contains(candidate) ||
                   await SlugInUseAsync(candidate, currentTenantId, cancellationToken))
            {
                candidate = $"{baseSlug}-{sufijo}";
                sufijo++;

                if (sufijo > 1000)
                {
                    // Salida de seguridad: sufijo aleatorio para no entrar en bucle.
                    candidate = $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(SlugMaxLength, baseSlug.Length + 9)];
                    break;
                }
            }

            return candidate;
        }

        private Task<bool> SlugInUseAsync(string slug, Guid currentTenantId, CancellationToken cancellationToken) =>
            _context.TenantBookingSettings
                .IgnoreQueryFilters()
                .AnyAsync(s => s.PublicBookingSlug == slug && s.TenantId != currentTenantId, cancellationToken);

        private Guid GetCurrentTenantId() =>
            _tenantProvider.HasTenant() ? _tenantProvider.GetTenantId() : Guid.Empty;

        private async Task<string> GetTenantNameAsync(CancellationToken cancellationToken)
        {
            var tenantId = GetCurrentTenantId();
            if (tenantId == Guid.Empty)
            {
                return string.Empty;
            }

            return await _tenantDisplayNameService.GetTenantDisplayNameAsync(tenantId, cancellationToken);
        }

        // ─────────────── Helpers de slug y días ───────────────

        public static string NormalizeSlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = RemoveDiacritics(value.Trim().ToLowerInvariant());
            var cleaned = SlugInvalidCharsRegex().Replace(lower, "-");
            cleaned = SlugMultiHyphenRegex().Replace(cleaned, "-").Trim('-');

            if (cleaned.Length > SlugMaxLength)
            {
                cleaned = cleaned[..SlugMaxLength].Trim('-');
            }

            return cleaned;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool[] MaskToDays(int mask)
        {
            var dias = new bool[7];
            for (var i = 0; i < 7; i++)
            {
                dias[i] = (mask & (1 << i)) != 0;
            }

            return dias;
        }

        private static int DaysToMask(bool[]? dias)
        {
            if (dias is null)
            {
                return 0;
            }

            var mask = 0;
            for (var i = 0; i < Math.Min(7, dias.Length); i++)
            {
                if (dias[i])
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        private static string? Trim(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
        }

        [GeneratedRegex("[^a-z0-9]+")]
        private static partial Regex SlugInvalidCharsRegex();

        [GeneratedRegex("-{2,}")]
        private static partial Regex SlugMultiHyphenRegex();
    }
}
