/* =============================================================================
   WhatsAppAddonSourceVerification.sql — Verificación READ-ONLY post-deploy de la
   formalización de add-ons manuales (BillingSource). Solo SELECT.

   Correr DESPUÉS de: (1) aplicar la migración AddWhatsAppAddonBillingSource y
   (2) correr WhatsAppAddonClassificationApply.sql (clasificación de datos) y
   (3) fijar Luxe como Barter/indefinido desde el modal de plataforma.
   ============================================================================= */

SET NOCOUNT ON;
DECLARE @Now datetime2 = SYSUTCDATETIME();

/* 1) compra1 / compra2: add-ons WhatsApp pagados por TiloPay deben quedar ProviderRecurring
      CON ProviderSubscriptionId (no manual, no riesgo). */
SELECT 'PaidAddons compra1/compra2 (BillingSource=0 ProviderRecurring, con provider sub)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.BillingSource,
       RIGHT(ISNULL(a.ProviderSubscriptionId,''),6) AS ProviderSubSuffix,
       a.TilopayRecurringPlanId, a.Estado
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.ProviderSubscriptionId IS NOT NULL AND a.ProviderSubscriptionId <> ''
ORDER BY t.Nombre;

/* 2) Luxe: acceso manual/canje (Barter) SIN provider y SIN riesgo. Idealmente indefinido. */
SELECT 'Luxe ManualGrant/Barter (BillingSource=1, sin provider, indefinido o vigente)' AS Check_,
       t.Nombre AS TenantName, u.Email, a.AddonCode, a.BillingSource, a.ManualGrantType,
       a.IsManualGrantIndefinite, a.ManualGrantExpiresAtUtc, a.RevokedAtUtc,
       CASE WHEN a.ProviderSubscriptionId IS NULL OR a.ProviderSubscriptionId = '' THEN 'sin provider' ELSE 'CON provider (revisar)' END AS ProviderState
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
OUTER APPLY (SELECT TOP 1 Email FROM dbo.AspNetUsers x WHERE x.TenantId = a.TenantId ORDER BY x.Email) u
WHERE u.Email = 'luxecentrodebelleza2025@gmail.com'
   OR t.Nombre LIKE '%Luxe%';

/* 3) RIESGO DE DINERO = add-on ProviderRecurring ACTIVO, recurrente, SIN provider sub. Debe ser 0. */
SELECT 'PaidAddonsActiveWithoutProvider (RIESGO — debe ser 0)' AS Check_,
       COUNT(*) AS RiesgoDinero
FROM dbo.TenantSubscriptionAddons a
WHERE a.BillingSource = 0 /* ProviderRecurring */
      AND a.Estado = 1
      AND a.TilopayRecurringPlanId IS NOT NULL
      AND (a.ProviderSubscriptionId IS NULL OR a.ProviderSubscriptionId = '');

/* 4) INFORMATIVO: accesos manuales VIGENTES (indefinidos o con fecha futura, no revocados). */
SELECT 'ManualGrantsActive (informativo, NO dinero)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.ManualGrantType,
       CASE WHEN a.IsManualGrantIndefinite = 1 THEN 'indefinido'
            ELSE 'hasta ' + CONVERT(varchar(10), a.ManualGrantExpiresAtUtc, 23) END AS Vigencia
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.BillingSource = 1 /* ManualGrant */
      AND a.Estado = 1
      AND a.RevokedAtUtc IS NULL
      AND (a.IsManualGrantIndefinite = 1 OR a.ManualGrantExpiresAtUtc IS NULL OR a.ManualGrantExpiresAtUtc >= @Now)
ORDER BY t.Nombre;

/* 5) ALERTA OPERATIVA: accesos manuales VENCIDOS con la fila aún activa (no envían, no cobran). */
SELECT 'ManualGrantsExpiredStillActive (operativo, NO dinero)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.ManualGrantExpiresAtUtc
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.BillingSource = 1 /* ManualGrant */
      AND a.Estado = 1
      AND a.RevokedAtUtc IS NULL
      AND a.IsManualGrantIndefinite = 0
      AND a.ManualGrantExpiresAtUtc IS NOT NULL
      AND a.ManualGrantExpiresAtUtc < @Now
ORDER BY t.Nombre;

/* 6) INFORMATIVO/LIMPIEZA: add-ons Legacy con Estado activo (nunca son entitlement efectivo). */
SELECT 'LegacyAddonsActive (informativo/limpieza)' AS Check_,
       COUNT(*) AS Total
FROM dbo.TenantSubscriptionAddons a
WHERE a.BillingSource = 2 /* Legacy */ AND a.Estado = 1;

/* 7) ALERTA OPERATIVA: settings habilitados SIN entitlement comercial efectivo (envíos bloqueados). */
SELECT 'SettingsEnabledWithoutEffectiveEntitlement (operativo)' AS Check_,
       t.Nombre AS TenantName
FROM dbo.TenantWhatsAppSettings w
LEFT JOIN dbo.Tenants t ON t.Id = w.TenantId
WHERE w.IsEnabled = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TenantSubscriptionAddons a
          WHERE a.TenantId = w.TenantId
                AND a.Estado = 1
                AND a.RevokedAtUtc IS NULL
                AND (
                     /* ProviderRecurring activo */
                     (a.BillingSource = 0 AND (a.FechaFin IS NULL OR a.FechaFin >= @Now))
                     OR
                     /* ManualGrant vigente */
                     (a.BillingSource = 1 AND (a.IsManualGrantIndefinite = 1
                                               OR a.ManualGrantExpiresAtUtc IS NULL
                                               OR a.ManualGrantExpiresAtUtc >= @Now))
                    )
      );

/* 8) Resumen global por fuente. */
SELECT 'Resumen por BillingSource' AS Check_,
       a.BillingSource,
       COUNT(*) AS Total,
       SUM(CASE WHEN a.Estado = 1 THEN 1 ELSE 0 END) AS ActivosEstado1
FROM dbo.TenantSubscriptionAddons a
GROUP BY a.BillingSource
ORDER BY a.BillingSource;
