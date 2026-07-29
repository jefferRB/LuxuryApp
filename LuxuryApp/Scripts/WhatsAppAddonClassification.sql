/* =============================================================================
   WhatsAppAddonClassification.sql — Reporte READ-ONLY de add-ons WhatsApp actuales
   y su clasificación PROPUESTA (ProviderRecurring / ProviderRisk / ManualGrant /
   ExpiredManualGrant / Legacy).

   100% SEGURO: solo SELECT. NO modifica ni borra nada. Correr ANTES de aplicar la
   migración AddWhatsAppAddonBillingSource para revisar cómo quedaría clasificado
   cada add-on. La clasificación real (business) de ManualGrant vs Legacy la decide
   el dueño: dos filas "MANUAL-*" se ven idénticas (una puede ser canje real y otra
   basura de prueba); este reporte propone, el dueño confirma.

   Reglas de la propuesta (mismas que aplicará la migración):
     - ProviderSubscriptionId presente               -> ProviderRecurring (pagado TiloPay).
     - Sin provider sub PERO TilopayRecurringPlanId   -> ProviderRisk (recurrente que perdió
       el id_suscriptor: ESTE sí es riesgo de dinero).
     - ProviderTransactionId LIKE 'MANUAL-%', vigente -> ManualGrant.
     - ProviderTransactionId LIKE 'MANUAL-%', vencido -> ExpiredManualGrant.
     - Resto (sin provider, sin recurrente, sin MANUAL) -> Legacy.
   ============================================================================= */

SET NOCOUNT ON;
DECLARE @Now datetime2 = SYSUTCDATETIME();

/* 1) Detalle por add-on con clasificación propuesta. */
SELECT
    a.TenantId,
    t.Nombre                                   AS TenantName,
    adminEmail.Email                           AS AdminEmail,
    a.Id                                        AS AddonId,
    a.AddonCode,
    a.Estado                                    AS AddonEstado,        -- 1 = Activa
    a.TilopayRecurringPlanId,
    RIGHT(ISNULL(a.ProviderSubscriptionId,''), 6) AS ProviderSubSuffix,
    a.ProviderTransactionId,
    a.FechaInicio,
    a.FechaFin,
    a.MonthlyMessageLimit,
    a.PrecioMensual,
    baseSub.Estado                              AS BaseSubEstado,      -- 1 = Activa
    baseSub.CodigoPlan                          AS BasePlanCode,
    CASE
        WHEN a.ProviderSubscriptionId IS NOT NULL AND a.ProviderSubscriptionId <> ''
            THEN 'ProviderRecurring'
        WHEN a.TilopayRecurringPlanId IS NOT NULL
            THEN 'ProviderRisk'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
             AND (a.FechaFin IS NULL OR a.FechaFin >= @Now)
            THEN 'ManualGrant'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
            THEN 'ExpiredManualGrant'
        ELSE 'Legacy'
    END AS ProposedClassification,
    CASE WHEN a.FechaFin IS NOT NULL AND a.FechaFin < @Now THEN 1 ELSE 0 END AS IsExpiredByFechaFin
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
/* Correo representativo: primer Administrador; si no hay, cualquier usuario del tenant. */
OUTER APPLY (
    SELECT TOP 1 u.Email
    FROM dbo.AspNetUsers u
    LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE u.TenantId = a.TenantId
    ORDER BY CASE WHEN r.Name = 'Administrador' THEN 0 ELSE 1 END, u.Email
) adminEmail
/* Suscripción base más reciente del tenant (para ver el estado del plan base). */
OUTER APPLY (
    SELECT TOP 1 s.Estado, s.CodigoPlan
    FROM dbo.Suscripciones s
    WHERE s.TenantId = a.TenantId
    ORDER BY ISNULL(s.FechaUltimaActualizacionUtc, s.FechaInicio) DESC, s.FechaInicio DESC
) baseSub
ORDER BY ProposedClassification, t.Nombre;

/* 2) Resumen por clasificación propuesta (para el conteo global). */
SELECT
    CASE
        WHEN a.ProviderSubscriptionId IS NOT NULL AND a.ProviderSubscriptionId <> ''
            THEN 'ProviderRecurring'
        WHEN a.TilopayRecurringPlanId IS NOT NULL
            THEN 'ProviderRisk'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
             AND (a.FechaFin IS NULL OR a.FechaFin >= @Now)
            THEN 'ManualGrant'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
            THEN 'ExpiredManualGrant'
        ELSE 'Legacy'
    END AS ProposedClassification,
    COUNT(*)                                                     AS Total,
    SUM(CASE WHEN a.Estado = 1 THEN 1 ELSE 0 END)                AS ActivosEstado1
FROM dbo.TenantSubscriptionAddons a
GROUP BY
    CASE
        WHEN a.ProviderSubscriptionId IS NOT NULL AND a.ProviderSubscriptionId <> ''
            THEN 'ProviderRecurring'
        WHEN a.TilopayRecurringPlanId IS NOT NULL
            THEN 'ProviderRisk'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
             AND (a.FechaFin IS NULL OR a.FechaFin >= @Now)
            THEN 'ManualGrant'
        WHEN a.ProviderTransactionId LIKE 'MANUAL-%'
            THEN 'ExpiredManualGrant'
        ELSE 'Legacy'
    END
ORDER BY ProposedClassification;

/* 3) RIESGO DE DINERO REAL: add-on activo, recurrente (TilopayRecurringPlanId presente),
      SIN provider sub. Debe listar SOLO casos genuinos (no manuales ni legacy). */
SELECT 'ProviderRisk (activo, recurrente, sin provider sub) — riesgo de dinero' AS Check_,
       a.TenantId, t.Nombre AS TenantName, a.AddonCode, a.TilopayRecurringPlanId,
       a.ProviderTransactionId, a.FechaFin, a.MonthlyMessageLimit
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.Estado = 1
      AND (a.ProviderSubscriptionId IS NULL OR a.ProviderSubscriptionId = '')
      AND a.TilopayRecurringPlanId IS NOT NULL;
