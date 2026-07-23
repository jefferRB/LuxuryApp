/* =============================================================================
   WhatsAppAddonVerification.sql  —  Verificación READ-ONLY del flujo de add-ons
   WhatsApp recurrentes (WA400 / WA800 / WA1200).

   SEGURO PARA PRODUCCIÓN: solo SELECT. No modifica nada. No usa eventos fake.
   NO expone tokens: los ProviderSubscriptionId se muestran enmascarados (últimos 4).
   El id_suscriptor completo NO es un access_token; si hace falta para conciliar
   contra TiloPay, consultarlo puntualmente, nunca pegarlo en documentación/logs.

   Ejecutar en la BD de la app. Todos los conteos "de riesgo" DEBEN dar 0.
   ============================================================================= */

SET NOCOUNT ON;

/* ---------------------------------------------------------------------------
   0) Fotografía general de add-ons por estado.
   --------------------------------------------------------------------------- */
SELECT 'AddonsPorEstado' AS Check_,
       a.Estado,
       COUNT(*) AS Cantidad
FROM dbo.TenantSubscriptionAddons a
GROUP BY a.Estado
ORDER BY a.Estado;

/* ---------------------------------------------------------------------------
   1) RIESGO — Más de un add-on por tenant.
      El índice único por TenantId lo impide estructuralmente ⇒ DEBE dar 0.
   --------------------------------------------------------------------------- */
SELECT 'DoubleAddonPerTenant (debe ser 0)' AS Check_,
       a.TenantId,
       COUNT(*) AS Addons
FROM dbo.TenantSubscriptionAddons a
GROUP BY a.TenantId
HAVING COUNT(*) > 1;

/* ---------------------------------------------------------------------------
   2) RIESGO — Suscriptor de add-on pendiente de baja en TiloPay
      (Strategy B / cascada / cambio manual). Cada fila = posible DOBLE COBRO
      del add-on hasta que se cancele el suscriptor. DEBE tender a 0.
      Si crece o no baja, revisar si el API admin de TiloPay está habilitado.
   --------------------------------------------------------------------------- */
SELECT 'AddonPendingProviderCancellation (money risk)' AS Check_,
       a.TenantId,
       a.AddonCode,
       a.PendingCancellationTilopayRecurringPlanId          AS ViejoPlanId,
       RIGHT(ISNULL(a.PendingCancellationProviderSubscriptionId,''),4) AS ViejoSubSuffix,
       a.ProviderCancellationAttemptCount                   AS Intentos,
       a.ProviderCancellationNextRetryUtc                   AS ProxReintentoUtc,
       a.ProviderCancellationLastAttemptUtc                 AS UltimoIntentoUtc
FROM dbo.TenantSubscriptionAddons a
WHERE a.ProviderCancellation = 1  /* PendingManualCancellation */
      AND a.PendingCancellationProviderSubscriptionId IS NOT NULL
ORDER BY a.ProviderCancellationAttemptCount DESC;

/* ---------------------------------------------------------------------------
   3) RIESGO — Add-on ACTIVO/MOROSO con plan base cancelado/vencido (regla 11).
      El add-on no debería seguir cobrando sin SaaS. Revisar caso por caso.
      (Estado efectivo del base se calcula en la app; acá se aproxima por fechas.)
   --------------------------------------------------------------------------- */
SELECT 'AddonActiveWithoutActiveBase' AS Check_,
       a.TenantId,
       a.AddonCode,
       a.Estado                          AS AddonEstado,
       a.FechaFin                        AS AddonFechaFin,
       s.CodigoPlan                      AS BasePlan,
       s.Estado                          AS BaseEstadoAlmacenado,
       s.FechaFin                        AS BaseFechaFin,
       s.CancelAtPeriodEnd               AS BaseCancelAtPeriodEnd
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Suscripciones s ON s.TenantId = a.TenantId
WHERE a.Estado IN (0 /*Trial*/, 1 /*Activa*/, 2 /*Morosa*/)
      AND (
            s.Id IS NULL
         OR s.Estado IN (3 /*Cancelada*/, 5 /*Fallida*/, 6 /*Vencida*/, 7 /*Suspendida*/)
         OR (s.FechaFin IS NOT NULL AND s.FechaFin < SYSUTCDATETIME()
             AND (s.ProviderExpiresAtUtc IS NULL OR s.ProviderExpiresAtUtc < SYSUTCDATETIME()))
      );

/* ---------------------------------------------------------------------------
   4) Incidentes de recuperación de pago del ADD-ON (Scope = 1) abiertos.
      Separados del base (Scope = 0): no deben mezclarse.
   --------------------------------------------------------------------------- */
SELECT 'OpenAddonPaymentIncidents' AS Check_,
       i.TenantId,
       i.PlanCode,
       i.Status,
       i.FailureCount,
       i.FailureDetectedAtUtc,
       i.GraceEndsAtUtc
FROM dbo.SubscriptionPaymentIncidents i
WHERE i.Scope = 1 /* WhatsAppAddon */
      AND i.Status = 0 /* Open */
ORDER BY i.FailureDetectedAtUtc DESC;

/* ---------------------------------------------------------------------------
   5) CONCILIACIÓN DB ↔ TiloPay — add-ons recurrentes ACTIVOS con suscriptor.
      Lista los add-ons que TiloPay DEBERÍA estar cobrando (uno por tenant).
      Cruzar el id_suscriptor (getSuscriptorRepeat del plan) contra el dashboard:
      cada fila aquí = exactamente un suscriptor Active en TiloPay para ese plan.
   --------------------------------------------------------------------------- */
SELECT 'ActiveRecurringAddons (conciliar vs TiloPay)' AS Check_,
       a.TenantId,
       a.AddonCode,
       a.TilopayRecurringPlanId,
       RIGHT(ISNULL(a.ProviderSubscriptionId,''),4) AS SubSuffix,
       a.FechaProximoCobroUtc                       AS ProxCobroUtc,
       a.CancelAtPeriodEnd
FROM dbo.TenantSubscriptionAddons a
WHERE a.Estado IN (1 /*Activa*/, 2 /*Morosa*/)
      AND a.TilopayRecurringPlanId IS NOT NULL
      AND a.ProviderSubscriptionId IS NOT NULL
ORDER BY a.TilopayRecurringPlanId;

/* ---------------------------------------------------------------------------
   6) Alertas recientes de add-on generadas por la reconciliación / cancelación
      (PlatformAuditLog append-only). Últimos 7 días.
   --------------------------------------------------------------------------- */
SELECT 'AddonAuditLast7d' AS Check_,
       l.CreatedAtUtc,
       l.Action,
       l.TenantId,
       LEFT(ISNULL(l.Reason,''), 200) AS ReasonPreview
FROM dbo.PlatformAuditLogs l
WHERE l.EntityType = 'WhatsAppAddon'
      AND l.CreatedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
ORDER BY l.CreatedAtUtc DESC;

/* ---------------------------------------------------------------------------
   7) Resumen de una línea: si RISK_COUNT = 0, no hay dinero-en-riesgo de add-on.
   --------------------------------------------------------------------------- */
SELECT 'ADDON_RISK_COUNT (debe ser 0)' AS Check_,
       (SELECT COUNT(*) FROM dbo.TenantSubscriptionAddons a
        WHERE a.ProviderCancellation = 1
              AND a.PendingCancellationProviderSubscriptionId IS NOT NULL)
     + (SELECT COUNT(*) FROM (
            SELECT a.TenantId FROM dbo.TenantSubscriptionAddons a
            GROUP BY a.TenantId HAVING COUNT(*) > 1
        ) dup)
       AS RISK_COUNT;
