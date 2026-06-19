using LuxuryApp.Models.Platform;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Platform
{
    public sealed class PlatformTenantProfileService : IPlatformTenantProfileService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantCommercialAccessResolver _accessResolver;
        private readonly IPlatformMetricsService _metricsService;
        private readonly IPlatformHealthService _healthService;
        private readonly IPlatformWhatsAppStatusService _whatsAppStatusService;

        public PlatformTenantProfileService(
            ApplicationDbContext context,
            ITenantCommercialAccessResolver accessResolver,
            IPlatformMetricsService metricsService,
            IPlatformHealthService healthService,
            IPlatformWhatsAppStatusService whatsAppStatusService)
        {
            _context = context;
            _accessResolver = accessResolver;
            _metricsService = metricsService;
            _healthService = healthService;
            _whatsAppStatusService = whatsAppStatusService;
        }

        public async Task<PlatformTenantFichaViewModel?> GetFichaAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.ForcedPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

            if (tenant is null)
                return null;

            // EF Core DbContext no es thread-safe: queries secuenciales
            var access = await _accessResolver.ResolveAsync(tenantId, cancellationToken: cancellationToken);
            var usage = await _metricsService.GetTenantUsageAsync(tenantId, cancellationToken);
            var (ownerEmail, ownerName) = await GetOwnerAsync(tenantId, cancellationToken);
            var billing = await GetBillingAsync(tenantId, cancellationToken);
            var whatsAppState = await _whatsAppStatusService.GetSingleStatusAsync(tenantId, cancellationToken);
            var whatsApp = new PlatformTenantWhatsAppFichaViewModel
            {
                IsEnabled = whatsAppState.SettingsEnabled,
                AddonActive = whatsAppState.AddonActive,
                AddonCode = whatsAppState.AddonCode,
                AddonFechaFin = whatsAppState.AddonFechaFin,
                DailyMessageLimit = whatsAppState.DailyMessageLimit,
                TodayUsage = whatsAppState.TodayUsage,
                MonthlyUsage30d = whatsAppState.MonthlyUsage30d,
                MonthlyMessageLimit = whatsAppState.AddonMonthlyLimit,
                HasRecentError = whatsAppState.LastErrorCode is not null,
                LastErrorCode = whatsAppState.LastErrorCode,
                LastErrorMessage = whatsAppState.LastErrorMessage,
                LastErrorAtUtc = whatsAppState.LastErrorAtUtc,
                LastMessageSentUtc = whatsAppState.LastMessageSentUtc,
                TimeZoneId = whatsAppState.TimeZoneId,
                Notes = whatsAppState.Notes
            };
            var reservations = await GetReservationsAsync(tenantId, cancellationToken);
            var (users, totalUsers) = await GetUsersPreviewAsync(tenantId, cancellationToken);
            var auditPreview = await GetAuditPreviewAsync(tenantId, cancellationToken);

            var health = _healthService.ComputeHealth(
                access.CanAccessApp,
                usage,
                whatsApp.IsEnabled,
                whatsApp.HasRecentError,
                billing.HasPendingCheckout,
                billing.IsExpiringSoon);

            return new PlatformTenantFichaViewModel
            {
                TenantId = tenantId,
                TenantName = tenant.Nombre,
                OwnerEmail = ownerEmail,
                OwnerName = ownerName,
                FechaCreacion = tenant.FechaCreacion,
                Activo = tenant.Activo,
                CanAccessApp = access.CanAccessApp,
                EffectivePlanName = access.EffectivePlanName,
                CommercialReason = access.Reason,
                CommercialAccessMode = tenant.CommercialAccessMode,
                CommercialNotes = tenant.CommercialNotes,
                Health = health,
                Usage = usage,
                Billing = billing,
                WhatsApp = whatsApp,
                Reservations = reservations,
                Users = users,
                TotalUsersCount = totalUsers,
                AuditPreview = auditPreview
            };
        }

        private async Task<(string? email, string? name)> GetOwnerAsync(Guid tenantId, CancellationToken ct)
        {
            var owner = await _context.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && !u.IsPlatformSuperAdmin)
                .OrderBy(u => u.Email)
                .Select(u => new { u.Email, u.Name })
                .FirstOrDefaultAsync(ct);
            return (owner?.Email, owner?.Name);
        }

        private async Task<PlatformTenantBillingFichaViewModel> GetBillingAsync(Guid tenantId, CancellationToken ct)
        {
            var nowUtc = DateTime.UtcNow;
            var expirySoonCutoff = nowUtc.AddDays(7);

            var activeSub = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId &&
                            (s.Estado == EstadoSuscripcion.Activa ||
                             s.Estado == EstadoSuscripcion.Trial ||
                             s.Estado == EstadoSuscripcion.Morosa))
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .FirstOrDefaultAsync(ct);

            var recentPayments = await _context.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(p => p.Plan)
                .Where(p => p.TenantId == tenantId)
                .OrderByDescending(p => p.FechaCreacionUtc)
                .Take(10)
                .Select(p => new PlatformTenantPaymentRowViewModel
                {
                    FechaUtc = p.FechaCreacionUtc,
                    Estado = p.Estado.ToString(),
                    Monto = p.Monto,
                    Moneda = p.Moneda ?? "CRC",
                    PlanName = p.Plan != null ? p.Plan.Nombre : null
                })
                .ToListAsync(ct);

            var pendingCount = await _context.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(p => p.TenantId == tenantId &&
                                 (p.Estado == EstadoPagoProveedor.Pendiente ||
                                  p.Estado == EstadoPagoProveedor.ManualReview), ct);

            var activeAddonNames = await _context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.Plan)
                .Where(a => a.TenantId == tenantId &&
                            (a.Estado == EstadoSuscripcion.Activa || a.Estado == EstadoSuscripcion.Morosa))
                .Select(a => a.Plan != null ? a.Plan.Nombre : a.AddonCode ?? "Add-on activo")
                .ToListAsync(ct);

            var isExpiringSoon = activeSub?.FechaFin.HasValue == true
                && activeSub.FechaFin!.Value >= nowUtc
                && activeSub.FechaFin.Value <= expirySoonCutoff;

            return new PlatformTenantBillingFichaViewModel
            {
                ActivePlanName = activeSub?.Plan?.Nombre,
                ActivePlanCode = activeSub?.CodigoPlan ?? activeSub?.Plan?.Codigo,
                SuscripcionEstado = activeSub?.Estado,
                SuscripcionFechaFin = activeSub?.FechaFin,
                SuscripcionProximoCobro = activeSub?.FechaProximoCobroUtc,
                IsTrial = activeSub?.Estado == EstadoSuscripcion.Trial,
                TrialFin = activeSub?.FechaTrialFin,
                HasPendingCheckout = pendingCount > 0,
                IsExpiringSoon = isExpiringSoon,
                PendingCheckoutsCount = pendingCount,
                RecentPayments = recentPayments,
                ActiveAddonNames = activeAddonNames
            };
        }

        private async Task<PlatformTenantReservationsFichaViewModel> GetReservationsAsync(Guid tenantId, CancellationToken ct)
        {
            var cutoff30d = DateTime.UtcNow.AddDays(-30);

            var bookingSettings = await _context.TenantBookingSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Select(s => new { s.PublicBookingEnabled, s.PublicBookingSlug })
                .FirstOrDefaultAsync(ct);

            var counts = await _context.BookingRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.CreatedAtUtc >= cutoff30d)
                .GroupBy(r => r.TenantId)
                .Select(g => new
                {
                    Total = g.Count(),
                    Confirmed = g.Count(r => r.Estado == BookingRequestStates.Confirmed),
                    Rejected = g.Count(r => r.Estado == BookingRequestStates.Rejected)
                })
                .FirstOrDefaultAsync(ct);

            var pending = await _context.BookingRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(r => r.TenantId == tenantId && r.Estado == BookingRequestStates.Pending, ct);

            var lastRequest = await _context.BookingRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => (DateTime?)r.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var total30d = counts?.Total ?? 0;
            var confirmed30d = counts?.Confirmed ?? 0;
            var rate = total30d > 0 ? Math.Round((double)confirmed30d / total30d * 100, 1) : 0;

            return new PlatformTenantReservationsFichaViewModel
            {
                PublicBookingEnabled = bookingSettings?.PublicBookingEnabled ?? false,
                PublicBookingSlug = bookingSettings?.PublicBookingSlug,
                Total30d = total30d,
                Pending = pending,
                Confirmed30d = confirmed30d,
                Rejected30d = counts?.Rejected ?? 0,
                ConfirmationRate = rate,
                LastRequestUtc = lastRequest
            };
        }

        private async Task<(IReadOnlyList<PlatformTenantUserPreviewViewModel> users, int total)> GetUsersPreviewAsync(
            Guid tenantId, CancellationToken ct)
        {
            var total = await _context.Users
                .AsNoTracking()
                .CountAsync(u => u.TenantId == tenantId, ct);

            var pageUsers = await _context.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId)
                .OrderByDescending(u => u.State)
                .ThenBy(u => u.Email)
                .Take(10)
                .Select(u => new { u.Id, u.Email, u.Name, u.State, u.IsPlatformSuperAdmin })
                .ToListAsync(ct);

            if (pageUsers.Count == 0)
                return (Array.Empty<PlatformTenantUserPreviewViewModel>(), total);

            var ids = pageUsers.Select(u => u.Id).ToList();
            var rolesByUser = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => ids.Contains(ur.UserId))
                .Join(_context.Roles.AsNoTracking(),
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => new { ur.UserId, RoleName = r.Name ?? string.Empty })
                .ToListAsync(ct);

            var roleLookup = rolesByUser
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.RoleName)));

            var users = pageUsers.Select(u => new PlatformTenantUserPreviewViewModel
            {
                UserId = u.Id,
                Email = u.Email ?? string.Empty,
                Name = u.Name,
                State = u.State,
                IsPlatformSuperAdmin = u.IsPlatformSuperAdmin,
                Roles = roleLookup.TryGetValue(u.Id, out var roles) ? roles : null
            }).ToList();

            return (users, total);
        }

        private async Task<IReadOnlyList<PlatformTenantAuditPreviewViewModel>> GetAuditPreviewAsync(
            Guid tenantId, CancellationToken ct)
        {
            return await _context.PlatformAuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(log => log.TenantId == tenantId)
                .OrderByDescending(log => log.CreatedAtUtc)
                .Take(10)
                .Select(log => new PlatformTenantAuditPreviewViewModel
                {
                    CreatedAtUtc = log.CreatedAtUtc,
                    ActorEmail = log.ActorEmail,
                    Action = log.Action,
                    TargetUserEmail = log.TargetUserEmail,
                    Reason = log.Reason
                })
                .ToListAsync(ct);
        }
    }
}
