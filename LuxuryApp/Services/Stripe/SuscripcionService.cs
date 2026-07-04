using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    public class SuscripcionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ITenantCommercialAccessCache _accessCache;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly TilopayRepeatOptions _tilopayRepeatOptions;
        private readonly ILogger<SuscripcionService> _logger;

        public SuscripcionService(
            ApplicationDbContext db,
            IMemoryCache cache,
            ITenantCommercialAccessCache accessCache,
            IBusinessDateTimeProvider businessDateTimeProvider,
            IOptions<TilopayRepeatOptions> tilopayRepeatOptions,
            ILogger<SuscripcionService> logger)
        {
            _db = db;
            _cache = cache;
            _accessCache = accessCache;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tilopayRepeatOptions = tilopayRepeatOptions.Value;
            _logger = logger;
        }

        public async Task ActivarSuscripcionAsync(
            Guid tenantId,
            Guid planId,
            PaymentProviderType provider,
            string? providerCustomerId,
            string? providerSubscriptionId,
            string? providerPaymentLinkId,
            string? providerTransactionId,
            string? providerReference,
            DateTime? trialEnd = null,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["PlanId"] = planId,
                ["Provider"] = provider,
                ["ProviderReference"] = providerReference
            });

            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            var nowUtc = GetUtcNow();
            var nuevoEstado = trialEnd.HasValue ? EstadoSuscripcion.Trial : EstadoSuscripcion.Activa;
            var (periodStartUtc, periodEndUtc) = ResolveNextBillingPeriod(
                nowUtc,
                suscripcion?.FechaFin,
                shouldExtendExistingPeriod: provider == PaymentProviderType.Tilopay && !trialEnd.HasValue);

            if (suscripcion is null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    Proveedor = provider,
                    ProviderCustomerId = providerCustomerId,
                    ProviderSubscriptionId = providerSubscriptionId,
                    ProviderPaymentLinkId = providerPaymentLinkId,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Estado = nuevoEstado,
                    FechaInicio = periodStartUtc,
                    FechaFin = provider == PaymentProviderType.Tilopay && !trialEnd.HasValue
                        ? periodEndUtc
                        : null,
                    FechaTrialFin = trialEnd,
                    FechaProximoCobroUtc = provider == PaymentProviderType.Tilopay && !trialEnd.HasValue
                        ? periodEndUtc
                        : null,
                    FechaFinGraciaUtc = null,
                    FechaCancelacionUtc = null,
                    FechaUltimaActualizacionUtc = nowUtc,
                    MotivoEstado = motivo,
                    CancelAtPeriodEnd = false
                };

                _db.Suscripciones.Add(suscripcion);
            }
            else
            {
                var planAnterior = suscripcion.PlanId;
                var estadoAnterior = suscripcion.Estado;

                suscripcion.PlanId = planId;
                suscripcion.Proveedor = provider;
                suscripcion.ProviderCustomerId = providerCustomerId ?? suscripcion.ProviderCustomerId;
                suscripcion.ProviderSubscriptionId = providerSubscriptionId ?? suscripcion.ProviderSubscriptionId;
                suscripcion.ProviderPaymentLinkId = providerPaymentLinkId ?? suscripcion.ProviderPaymentLinkId;
                suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
                suscripcion.ProviderReference = providerReference ?? suscripcion.ProviderReference;
                suscripcion.Estado = nuevoEstado;
                suscripcion.FechaInicio = periodStartUtc;
                suscripcion.FechaFin = provider == PaymentProviderType.Tilopay && !trialEnd.HasValue
                    ? periodEndUtc
                    : null;
                suscripcion.FechaTrialFin = trialEnd;
                suscripcion.FechaProximoCobroUtc = provider == PaymentProviderType.Tilopay && !trialEnd.HasValue
                    ? periodEndUtc
                    : null;
                suscripcion.FechaFinGraciaUtc = null;
                suscripcion.FechaCancelacionUtc = null;
                suscripcion.CancelAtPeriodEnd = false;
                suscripcion.FechaUltimaActualizacionUtc = nowUtc;
                suscripcion.MotivoEstado = motivo;

                if (planAnterior != planId || estadoAnterior != nuevoEstado)
                {
                    _db.HistorialSuscripciones.Add(new HistorialSuscripcion
                    {
                        Id = Guid.NewGuid(),
                        SuscripcionId = suscripcion.Id,
                        PlanIdAnterior = planAnterior,
                        PlanIdNuevo = planId,
                        FechaCambio = nowUtc,
                        Proveedor = provider,
                        Motivo = motivo ?? "Actualizacion de suscripcion"
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogInformation(
                "Suscripcion activada o actualizada correctamente. Estado {Estado}.",
                suscripcion.Estado);
        }

        public async Task RegistrarPagoConfirmadoAsync(
            Guid tenantId,
            Guid planId,
            Guid? pagoSuscripcionId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            string? providerAuthorizationCode,
            decimal monto,
            string moneda,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await EnsureSubscriptionForPaymentAsync(
                tenantId,
                planId,
                provider,
                providerReference,
                providerTransactionId,
                cancellationToken);

            var nowUtc = GetUtcNow();
            var facturaExiste = await _db.Facturas
                .IgnoreQueryFilters()
                .AnyAsync(
                    factura => factura.Proveedor == provider &&
                    (
                        (!string.IsNullOrWhiteSpace(providerTransactionId) &&
                         factura.ProviderTransactionId == providerTransactionId) ||
                        factura.ProviderReference == providerReference
                    ),
                    cancellationToken);

            if (!facturaExiste)
            {
                _db.Facturas.Add(new Factura
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SuscripcionId = suscripcion.Id,
                    PagoSuscripcionId = pagoSuscripcionId,
                    Proveedor = provider,
                    ProviderInvoiceId = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Monto = monto,
                    Moneda = moneda,
                    Estado = "Pagado",
                    Fecha = nowUtc
                });
            }

            suscripcion.Estado = EstadoSuscripcion.Activa;
            suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
            suscripcion.ProviderReference = providerReference;
            suscripcion.FechaUltimoPagoUtc = nowUtc;
            suscripcion.FechaUltimaActualizacionUtc = nowUtc;
            suscripcion.MotivoEstado = motivo ?? "Pago confirmado";
            suscripcion.CancelAtPeriodEnd = false;
            suscripcion.FechaFinGraciaUtc = null;
            suscripcion.FechaCancelacionUtc = null;

            if (provider == PaymentProviderType.Tilopay &&
                suscripcion.Estado != EstadoSuscripcion.Trial)
            {
                var (periodStartUtc, periodEndUtc) = ResolveNextBillingPeriod(
                    nowUtc,
                    suscripcion.FechaFin,
                    shouldExtendExistingPeriod: true);

                suscripcion.FechaInicio = periodStartUtc;
                suscripcion.FechaFin = periodEndUtc;
                suscripcion.FechaProximoCobroUtc = periodEndUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogInformation("Pago confirmado registrado correctamente.");
        }

        public async Task RegistrarPagoFallidoAsync(
            Guid tenantId,
            Guid planId,
            Guid? pagoSuscripcionId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            decimal monto,
            string moneda,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            var nowUtc = GetUtcNow();

            if (suscripcion is null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    Proveedor = provider,
                    ProviderReference = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    Estado = EstadoSuscripcion.Fallida,
                    FechaInicio = nowUtc,
                    FechaUltimaActualizacionUtc = nowUtc,
                    MotivoEstado = motivo ?? "Pago fallido"
                };

                _db.Suscripciones.Add(suscripcion);
            }
            else
            {
                suscripcion.PlanId = planId;
                suscripcion.Proveedor = provider;
                suscripcion.ProviderReference = providerReference;
                suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
                suscripcion.Estado = suscripcion.Estado == EstadoSuscripcion.Activa || suscripcion.Estado == EstadoSuscripcion.Trial
                    ? EstadoSuscripcion.Morosa
                    : EstadoSuscripcion.Fallida;
                suscripcion.FechaFinGraciaUtc = ResolveGracePeriodEndsUtc(suscripcion.FechaFin, nowUtc);
                suscripcion.FechaUltimaActualizacionUtc = nowUtc;
                suscripcion.MotivoEstado = motivo ?? "Pago fallido";
            }

            var facturaExiste = await _db.Facturas
                .IgnoreQueryFilters()
                .AnyAsync(
                    factura => factura.Proveedor == provider &&
                    (
                        (!string.IsNullOrWhiteSpace(providerTransactionId) &&
                         factura.ProviderTransactionId == providerTransactionId) ||
                        factura.ProviderReference == providerReference
                    ),
                    cancellationToken);

            if (!facturaExiste)
            {
                _db.Facturas.Add(new Factura
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SuscripcionId = suscripcion.Id,
                    PagoSuscripcionId = pagoSuscripcionId,
                    Proveedor = provider,
                    ProviderInvoiceId = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Monto = monto,
                    Moneda = moneda,
                    Estado = "Fallido",
                    Fecha = nowUtc
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogWarning("Pago fallido registrado. Estado de suscripcion actualizado a {Estado}.", suscripcion.Estado);
        }

        public async Task CancelarSuscripcionAsync(
            string providerSubscriptionId,
            bool cancelAtPeriodEnd,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == providerSubscriptionId,
                    cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;
            suscripcion.FechaUltimaActualizacionUtc = GetUtcNow();
            suscripcion.MotivoEstado = cancelAtPeriodEnd
                ? "Cancelacion programada"
                : "Cancelacion inmediata";

            if (!cancelAtPeriodEnd)
            {
                suscripcion.Estado = EstadoSuscripcion.Cancelada;
                suscripcion.FechaCancelacionUtc = GetUtcNow();
                suscripcion.FechaFin = suscripcion.FechaCancelacionUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(suscripcion.TenantId);
        }

        public async Task ActualizarEstadoDesdeStripeAsync(
            string providerSubscriptionId,
            EstadoSuscripcion nuevoEstado,
            bool cancelAtPeriodEnd,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == providerSubscriptionId,
                    cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.Estado = nuevoEstado;
            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;
            suscripcion.FechaUltimaActualizacionUtc = GetUtcNow();
            suscripcion.MotivoEstado = "Actualizacion de estado desde proveedor externo";

            if (nuevoEstado == EstadoSuscripcion.Cancelada)
            {
                suscripcion.FechaCancelacionUtc = GetUtcNow();
                suscripcion.FechaFin = suscripcion.FechaCancelacionUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(suscripcion.TenantId);
        }

        public async Task ActivarSuscripcionRecurrenteAsync(
            Guid tenantId,
            Plan plan,
            int tilopayRecurringPlanId,
            string? providerSubscriberId,
            string? providerTransactionId,
            string? providerReference,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            var nowUtc = GetUtcNow();
            var previousPlanId = suscripcion?.PlanId;
            var previousState = suscripcion?.Estado;
            // Solo se extiende el periodo vigente cuando es el MISMO plan; un cambio de plan
            // (p.ej. upgrade de funcionarios o mensual<->anual) reinicia el periodo segun el
            // ciclo del plan nuevo para no arrastrar vigencia de un ciclo distinto.
            var isSamePlan = suscripcion is not null && suscripcion.PlanId == plan.Id;
            var (periodStartUtc, periodEndUtc) = ResolveNextBillingPeriod(
                nowUtc,
                suscripcion?.FechaFin,
                shouldExtendExistingPeriod: isSamePlan,
                billingCycle: plan.BillingCycle);

            if (suscripcion is null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId
                };

                _db.Suscripciones.Add(suscripcion);
            }

            suscripcion.PlanId = plan.Id;
            suscripcion.CodigoPlan = plan.Codigo ?? plan.Nombre;
            suscripcion.Proveedor = PaymentProviderType.Tilopay;
            suscripcion.TilopayRecurringPlanId = tilopayRecurringPlanId;
            suscripcion.ProviderSubscriptionId = providerSubscriberId ?? suscripcion.ProviderSubscriptionId;
            suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
            suscripcion.ProviderReference = providerReference ?? suscripcion.ProviderReference;
            suscripcion.Estado = EstadoSuscripcion.Activa;
            suscripcion.FechaInicio = periodStartUtc;
            suscripcion.FechaFin = periodEndUtc;
            suscripcion.FechaTrialFin = null;
            suscripcion.FechaProximoCobroUtc = periodEndUtc;
            suscripcion.FechaFinGraciaUtc = null;
            suscripcion.FechaCancelacionUtc = null;
            // Guardar SIEMPRE el equivalente mensual en Suscripcion.PrecioMensual (no el total
            // anual) para que reportes/portal no se ensucien con un cobro anual de golpe.
            suscripcion.PrecioMensual = ResolveMonthlyEquivalent(plan);
            suscripcion.MonedaFacturacion = string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda;
            suscripcion.MaxFuncionarios = plan.MaxFuncionarios;
            suscripcion.FechaUltimoPagoUtc = nowUtc;
            suscripcion.FechaUltimaActualizacionUtc = nowUtc;
            suscripcion.MotivoEstado = motivo ?? "Suscripcion recurrente activada por Tilopay Repeat.";
            suscripcion.CancelAtPeriodEnd = false;

            if (previousPlanId.HasValue &&
                previousState.HasValue &&
                (previousPlanId.Value != plan.Id || previousState.Value != EstadoSuscripcion.Activa))
            {
                _db.HistorialSuscripciones.Add(new HistorialSuscripcion
                {
                    Id = Guid.NewGuid(),
                    SuscripcionId = suscripcion.Id,
                    PlanIdAnterior = previousPlanId,
                    PlanIdNuevo = plan.Id,
                    FechaCambio = nowUtc,
                    Proveedor = PaymentProviderType.Tilopay,
                    Motivo = motivo ?? "Activacion recurrente Tilopay Repeat."
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);
        }

        public async Task ActivarAddonWhatsAppRecurrenteAsync(
            Guid tenantId,
            Plan plan,
            int tilopayRecurringPlanId,
            string? providerSubscriberId,
            string? providerTransactionId,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var addonWasPersisted = true;
            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);
            if (addon is null)
            {
                addonWasPersisted = false;
            }

            var nowUtc = GetUtcNow();
            var isSameAddonPlan = addon is not null &&
                                  (addon.PlanId == plan.Id ||
                                   addon.TilopayRecurringPlanId == tilopayRecurringPlanId ||
                                   string.Equals(
                                       addon.AddonCode,
                                       plan.Codigo ?? plan.Nombre,
                                       StringComparison.OrdinalIgnoreCase));
            var (periodStartUtc, periodEndUtc) = ResolveNextBillingPeriod(
                nowUtc,
                addon?.FechaFin,
                shouldExtendExistingPeriod: isSameAddonPlan && addon is not null && IsWhatsAppAddonActive(addon));

            if (addon is null)
            {
                addon = new TenantSubscriptionAddon
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CreatedAtUtc = nowUtc
                };

                _db.TenantSubscriptionAddons.Add(addon);
            }

            addon.PlanId = plan.Id;
            addon.AddonCode = plan.Codigo ?? plan.Nombre;
            addon.Estado = EstadoSuscripcion.Activa;
            addon.TilopayRecurringPlanId = tilopayRecurringPlanId;
            addon.ProviderSubscriptionId = providerSubscriberId ?? addon.ProviderSubscriptionId;
            addon.ProviderTransactionId = providerTransactionId ?? addon.ProviderTransactionId;
            addon.PrecioMensual = plan.PrecioMensual;
            addon.MonedaFacturacion = string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda;
            addon.MonthlyMessageLimit = plan.LimiteMensajesMensual ?? 0;
            addon.FechaInicio = periodStartUtc;
            addon.FechaFin = periodEndUtc;
            addon.FechaProximoCobroUtc = periodEndUtc;
            addon.FechaFinGraciaUtc = null;
            addon.FechaCancelacionUtc = null;
            addon.UpdatedAtUtc = nowUtc;

            var settings = await _db.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);
            var effectiveDailyLimit = ResolveWhatsAppDailyMessageLimit(
                addon,
                settings?.DailyMessageLimit ?? TenantWhatsAppSettings.DefaultDailyMessageLimit);

            if (settings is null)
            {
                settings = new TenantWhatsAppSettings
                {
                    TenantId = tenantId,
                    CreatedAtUtc = nowUtc
                };
                _db.TenantWhatsAppSettings.Add(settings);
            }

            settings.DailyMessageLimit = effectiveDailyLimit;
            settings.TimeZoneId = string.IsNullOrWhiteSpace(settings.TimeZoneId)
                ? TenantWhatsAppSettings.DefaultTimeZoneId
                : settings.TimeZoneId;

            if (!addonWasPersisted || !isSameAddonPlan)
            {
                settings.IsEnabled = true;
                settings.SendConfirmationOnCreate = true;
                settings.SendReminderThreeHoursBefore = true;
            }

            settings.UpdatedAtUtc = nowUtc;

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task AssignManualWhatsAppAddonAsync(
            Guid tenantId,
            string? addonCode,
            string assignedByUserId,
            string? observation,
            bool sendConfirmationOnCreate,
            bool sendReminderThreeHoursBefore,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = GetUtcNow();
            var revoking = string.IsNullOrWhiteSpace(addonCode) ||
                           addonCode.Equals("NONE", StringComparison.OrdinalIgnoreCase);

            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);

            _logger.LogInformation(
                "AssignManualWhatsAppAddon. TenantId {TenantId}. AssignedBy {UserId}. PreviousAddonCode {Prev}. PreviousEstado {PrevEstado}. NewAddonCode {New}.",
                tenantId,
                assignedByUserId,
                addon?.AddonCode,
                addon?.Estado,
                revoking ? "NONE" : addonCode);

            if (revoking)
            {
                if (addon is not null)
                {
                    addon.Estado = EstadoSuscripcion.Cancelada;
                    addon.FechaCancelacionUtc = nowUtc;
                    addon.FechaFin = nowUtc;
                    addon.UpdatedAtUtc = nowUtc;
                }

                await UpsertWhatsAppSettingsForManualAsync(
                    tenantId,
                    isEnabled: false,
                    sendConfirmationOnCreate: false,
                    sendReminderThreeHoursBefore: false,
                    dailyLimit: TenantWhatsAppSettings.DefaultDailyMessageLimit,
                    observation: observation,
                    nowUtc: nowUtc,
                    cancellationToken: cancellationToken);

                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (!PlanCodes.WhatsAppAddons.Contains(addonCode!, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Código de paquete WhatsApp no válido: {addonCode}", nameof(addonCode));
            }

            var plan = await _db.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Codigo == addonCode && p.Activo, cancellationToken)
                ?? throw new InvalidOperationException($"Plan {addonCode} no encontrado o inactivo en la BD.");

            if (addon is null)
            {
                addon = new TenantSubscriptionAddon
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CreatedAtUtc = nowUtc
                };
                _db.TenantSubscriptionAddons.Add(addon);
            }

            addon.PlanId = plan.Id;
            addon.AddonCode = plan.Codigo!;
            addon.Estado = EstadoSuscripcion.Activa;
            addon.TilopayRecurringPlanId = null;
            addon.ProviderSubscriptionId = null;
            addon.ProviderTransactionId = $"MANUAL-{addonCode}-{nowUtc:yyyyMMddHHmmss}";
            addon.PrecioMensual = null;
            addon.MonedaFacturacion = "CRC";
            addon.MonthlyMessageLimit = plan.LimiteMensajesMensual ?? 0;
            addon.FechaInicio = nowUtc;
            addon.FechaFin = nowUtc.AddMonths(1);
            addon.FechaProximoCobroUtc = nowUtc.AddMonths(1);
            addon.FechaFinGraciaUtc = null;
            addon.FechaCancelacionUtc = null;
            addon.UpdatedAtUtc = nowUtc;

            var dailyLimit = ResolveWhatsAppDailyMessageLimit(addonCode);
            var isEnabled = sendConfirmationOnCreate || sendReminderThreeHoursBefore;

            await UpsertWhatsAppSettingsForManualAsync(
                tenantId,
                isEnabled,
                sendConfirmationOnCreate,
                sendReminderThreeHoursBefore,
                dailyLimit,
                observation,
                nowUtc,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task UpsertWhatsAppSettingsForManualAsync(
            Guid tenantId,
            bool isEnabled,
            bool sendConfirmationOnCreate,
            bool sendReminderThreeHoursBefore,
            int dailyLimit,
            string? observation,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var settings = await _db.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            if (settings is null)
            {
                settings = new TenantWhatsAppSettings
                {
                    TenantId = tenantId,
                    CreatedAtUtc = nowUtc,
                    TimeZoneId = TenantWhatsAppSettings.DefaultTimeZoneId
                };
                _db.TenantWhatsAppSettings.Add(settings);
            }

            settings.IsEnabled = isEnabled;
            settings.SendConfirmationOnCreate = sendConfirmationOnCreate;
            settings.SendReminderThreeHoursBefore = sendReminderThreeHoursBefore;
            settings.DailyMessageLimit = dailyLimit;
            settings.Notes = string.IsNullOrWhiteSpace(observation) ? settings.Notes : observation.Trim();
            settings.TimeZoneId = string.IsNullOrWhiteSpace(settings.TimeZoneId)
                ? TenantWhatsAppSettings.DefaultTimeZoneId
                : settings.TimeZoneId;
            settings.UpdatedAtUtc = nowUtc;
        }

        public async Task RegistrarPagoFallidoAddonAsync(
            Guid tenantId,
            string addonCode,
            string? providerSubscriberId,
            string? providerTransactionId,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current =>
                    current.TenantId == tenantId &&
                    (current.ProviderSubscriptionId == providerSubscriberId ||
                     current.AddonCode == addonCode),
                    cancellationToken);

            if (addon is null)
            {
                return;
            }

            var nowUtc = GetUtcNow();
            addon.ProviderTransactionId = providerTransactionId ?? addon.ProviderTransactionId;
            addon.Estado = addon.Estado == EstadoSuscripcion.Activa || addon.Estado == EstadoSuscripcion.Trial
                ? EstadoSuscripcion.Morosa
                : EstadoSuscripcion.Fallida;
            addon.FechaFinGraciaUtc = ResolveGracePeriodEndsUtc(addon.FechaFin, nowUtc);
            addon.UpdatedAtUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(motivo))
            {
                _logger.LogWarning(
                    "Pago recurrente WhatsApp fallido. TenantId {TenantId}. AddonCode {AddonCode}. Motivo {Motivo}.",
                    tenantId,
                    addonCode,
                    motivo);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task MarcarSuscripcionCanceladaRecurrenteAsync(
            Guid tenantId,
            string? providerSubscriberId,
            bool isAddon,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = GetUtcNow();

            if (isAddon)
            {
                var addonQuery = _db.TenantSubscriptionAddons
                    .IgnoreQueryFilters()
                    .Where(current => current.TenantId == tenantId);

                if (!string.IsNullOrWhiteSpace(providerSubscriberId))
                {
                    addonQuery = addonQuery.Where(current => current.ProviderSubscriptionId == providerSubscriberId);
                }

                var addon = await addonQuery
                    .OrderByDescending(current => current.UpdatedAtUtc)
                    .ThenByDescending(current => current.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (addon is null)
                {
                    return;
                }

                addon.Estado = EstadoSuscripcion.Cancelada;
                addon.FechaCancelacionUtc = nowUtc;
                addon.FechaFin = nowUtc;
                addon.UpdatedAtUtc = nowUtc;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.Estado = EstadoSuscripcion.Cancelada;
            suscripcion.ProviderSubscriptionId = providerSubscriberId ?? suscripcion.ProviderSubscriptionId;
            suscripcion.FechaCancelacionUtc = nowUtc;
            suscripcion.FechaFin = nowUtc;
            suscripcion.FechaUltimaActualizacionUtc = nowUtc;
            suscripcion.MotivoEstado = motivo ?? "Cancelacion recibida desde Tilopay Repeat.";

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);
        }

        public EstadoSuscripcion GetEffectiveStatus(Suscripcion suscripcion)
        {
            ArgumentNullException.ThrowIfNull(suscripcion);

            return GetEffectiveStatusInternal(
                suscripcion.Estado,
                suscripcion.FechaFin,
                suscripcion.FechaTrialFin,
                suscripcion.FechaFinGraciaUtc,
                GetUtcNow());
        }

        public EstadoSuscripcion GetEffectiveStatus(TenantSubscriptionAddon addon)
        {
            ArgumentNullException.ThrowIfNull(addon);

            return GetEffectiveStatusInternal(
                addon.Estado,
                addon.FechaFin,
                trialEndsUtc: null,
                addon.FechaFinGraciaUtc,
                GetUtcNow());
        }

        public bool CanAccessApp(Suscripcion suscripcion)
        {
            var effectiveStatus = GetEffectiveStatus(suscripcion);
            return effectiveStatus == EstadoSuscripcion.Activa ||
                   effectiveStatus == EstadoSuscripcion.Trial ||
                   effectiveStatus == EstadoSuscripcion.Morosa;
        }

        public bool IsWhatsAppAddonActive(TenantSubscriptionAddon addon)
        {
            var effectiveStatus = GetEffectiveStatus(addon);
            return effectiveStatus == EstadoSuscripcion.Activa ||
                   effectiveStatus == EstadoSuscripcion.Morosa;
        }

        public async Task<int> GetWhatsAppUsageInCurrentPeriodAsync(
            Guid tenantId,
            DateTime? periodStartUtc,
            DateTime? periodEndUtc,
            CancellationToken cancellationToken = default)
        {
            if (!periodStartUtc.HasValue || !periodEndUtc.HasValue)
            {
                return 0;
            }

            return await _db.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message =>
                    message.TenantId == tenantId &&
                    message.Direction == "Outbound" &&
                    (message.NotificationType == "Confirmation" ||
                     message.NotificationType == "Reminder3Hours") &&
                    (message.Status == "Sent" ||
                     message.Status == "Delivered" ||
                     message.Status == "Read") &&
                    (message.SentAtUtc ?? message.DeliveredAtUtc ?? message.ReadAtUtc ?? message.CreatedAtUtc) >= periodStartUtc.Value &&
                    (message.SentAtUtc ?? message.DeliveredAtUtc ?? message.ReadAtUtc ?? message.CreatedAtUtc) < periodEndUtc.Value)
                .CountAsync(cancellationToken);
        }

        public int ResolveWhatsAppDailyMessageLimit(
            TenantSubscriptionAddon? addon,
            int configuredDailyLimit = TenantWhatsAppSettings.DefaultDailyMessageLimit) =>
            ResolveWhatsAppDailyMessageLimit(
                addon?.AddonCode ?? addon?.Plan?.Codigo,
                configuredDailyLimit);

        public int ResolveWhatsAppDailyMessageLimit(
            string? addonCode,
            int configuredDailyLimit = TenantWhatsAppSettings.DefaultDailyMessageLimit)
        {
            var configuredLimit = _tilopayRepeatOptions.FindByCode(addonCode)?.DailyMessageLimit;
            if (configuredLimit is > 0)
            {
                return configuredLimit.Value;
            }

            return addonCode switch
            {
                PlanCodes.WhatsApp400 => 15,
                PlanCodes.WhatsApp800 => 30,
                PlanCodes.WhatsApp1200 => 45,
                _ => configuredDailyLimit > 0 ? configuredDailyLimit : TenantWhatsAppSettings.DefaultDailyMessageLimit
            };
        }

        // Compatibilidad razonable con el codigo legado de Stripe.
        public Task ActivarSuscripcionAsync(
            Guid tenantId,
            Guid planId,
            string subscriptionId,
            string customerId,
            DateTime? trialEnd = null) =>
            ActivarSuscripcionAsync(
                tenantId,
                planId,
                PaymentProviderType.Stripe,
                customerId,
                subscriptionId,
                providerPaymentLinkId: null,
                providerTransactionId: null,
                providerReference: subscriptionId,
                trialEnd: trialEnd,
                motivo: "Activacion desde Stripe");

        public async Task RegistrarPagoAsync(
            string subscriptionId,
            string invoiceId,
            decimal monto,
            string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionId);

            if (suscripcion is null)
            {
                return;
            }

            await RegistrarPagoConfirmadoAsync(
                suscripcion.TenantId,
                suscripcion.PlanId,
                pagoSuscripcionId: null,
                provider: PaymentProviderType.Stripe,
                providerReference: invoiceId,
                providerTransactionId: invoiceId,
                providerAuthorizationCode: null,
                monto: monto,
                moneda: moneda,
                motivo: "Pago confirmado desde Stripe");
        }

        public async Task MarcarPagoFallidoAsync(
            string subscriptionId,
            string invoiceId,
            decimal monto,
            string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionId);

            if (suscripcion is null)
            {
                return;
            }

            await RegistrarPagoFallidoAsync(
                suscripcion.TenantId,
                suscripcion.PlanId,
                pagoSuscripcionId: null,
                provider: PaymentProviderType.Stripe,
                providerReference: invoiceId,
                providerTransactionId: invoiceId,
                monto: monto,
                moneda: moneda,
                motivo: "Pago fallido desde Stripe");
        }

        private async Task<Suscripcion> EnsureSubscriptionForPaymentAsync(
            Guid tenantId,
            Guid planId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            CancellationToken cancellationToken)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            if (suscripcion is not null)
            {
                return suscripcion;
            }

            suscripcion = new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = provider,
                ProviderReference = providerReference,
                ProviderTransactionId = providerTransactionId,
                Estado = EstadoSuscripcion.Pendiente,
                FechaInicio = GetUtcNow(),
                FechaUltimaActualizacionUtc = GetUtcNow(),
                MotivoEstado = "Suscripcion creada desde confirmacion de pago"
            };

            _db.Suscripciones.Add(suscripcion);
            await _db.SaveChangesAsync(cancellationToken);

            return suscripcion;
        }

        private void InvalidateSubscriptionCache(Guid tenantId)
        {
            _cache.Remove($"suscripcion_{tenantId}");
            _accessCache.Invalidate(tenantId);
        }

        private DateTime GetUtcNow() => _businessDateTimeProvider.NowOffset().UtcDateTime;

        /// <summary>
        /// Equivalente mensual de un plan para guardar en Suscripcion.PrecioMensual.
        /// Mensual => PrecioMensual; Anual => MonthlyEquivalentAmount configurado o total/12.
        /// </summary>
        private static decimal ResolveMonthlyEquivalent(Plan plan)
        {
            if (plan.BillingCycle != BillingCycle.Annual)
            {
                return plan.PrecioMensual;
            }

            if (plan.MonthlyEquivalentAmount is > 0)
            {
                return plan.MonthlyEquivalentAmount.Value;
            }

            return plan.PrecioMensual > 0
                ? decimal.Round(plan.PrecioMensual / 12m, 2)
                : plan.PrecioMensual;
        }

        private static (DateTime PeriodStartUtc, DateTime PeriodEndUtc) ResolveNextBillingPeriod(
            DateTime nowUtc,
            DateTime? currentPeriodEndUtc,
            bool shouldExtendExistingPeriod,
            BillingCycle billingCycle = BillingCycle.Monthly)
        {
            var periodStartUtc = shouldExtendExistingPeriod &&
                                 currentPeriodEndUtc.HasValue &&
                                 currentPeriodEndUtc.Value > nowUtc
                ? currentPeriodEndUtc.Value
                : nowUtc;

            var periodEndUtc = billingCycle == BillingCycle.Annual
                ? periodStartUtc.AddYears(1)
                : periodStartUtc.AddMonths(1);

            return (periodStartUtc, periodEndUtc);
        }

        private DateTime ResolveGracePeriodEndsUtc(DateTime? currentPeriodEndUtc, DateTime nowUtc)
        {
            var graceStartUtc = currentPeriodEndUtc.HasValue && currentPeriodEndUtc.Value > nowUtc
                ? currentPeriodEndUtc.Value
                : nowUtc;

            var graceDays = _tilopayRepeatOptions.GracePeriodDays <= 0
                ? 3
                : _tilopayRepeatOptions.GracePeriodDays;

            return graceStartUtc.AddDays(graceDays);
        }

        private static EstadoSuscripcion GetEffectiveStatusInternal(
            EstadoSuscripcion status,
            DateTime? currentPeriodEndUtc,
            DateTime? trialEndsUtc,
            DateTime? graceEndsUtc,
            DateTime nowUtc)
        {
            if (status == EstadoSuscripcion.Trial)
            {
                return trialEndsUtc.HasValue && trialEndsUtc.Value < nowUtc
                    ? EstadoSuscripcion.Suspendida
                    : EstadoSuscripcion.Trial;
            }

            if (status == EstadoSuscripcion.Activa)
            {
                if (!currentPeriodEndUtc.HasValue || currentPeriodEndUtc.Value >= nowUtc)
                {
                    return EstadoSuscripcion.Activa;
                }

                return graceEndsUtc.HasValue && graceEndsUtc.Value >= nowUtc
                    ? EstadoSuscripcion.Morosa
                    : EstadoSuscripcion.Suspendida;
            }

            if (status == EstadoSuscripcion.Morosa)
            {
                return graceEndsUtc.HasValue && graceEndsUtc.Value >= nowUtc
                    ? EstadoSuscripcion.Morosa
                    : EstadoSuscripcion.Suspendida;
            }

            if (status == EstadoSuscripcion.Vencida)
            {
                return EstadoSuscripcion.Suspendida;
            }

            return status;
        }
    }
}
