/* =============================================================================
   Compra2RecoveryVerification.sql — Verificación READ-ONLY post-deploy del caso
   compra2usuarios@gmail.com (TenantId EE744446-05D3-4B59-0BD9-08DEE1CE2353).

   SEGURO PARA PRODUCCIÓN: solo SELECT. La sanación de la base y la reconciliación
   del evento las hace la reconciliación (fases HealRecoveredBaseSubscriptions y
   ReconcileOrphanedRenewalSuccessEvents) tras verificar contra getSuscriptorRepeat
   que TiloPay está Active y renovado. Este script SOLO comprueba el resultado.

   Estado TiloPay del suscriptor base (6127 / 384370) y del add-on (5831 / 393795) se
   verifica en el DASHBOARD de TiloPay (getSuscriptorRepeat), no por SQL.
   ============================================================================= */

SET NOCOUNT ON;
DECLARE @Tenant uniqueidentifier = 'EE744446-05D3-4B59-0BD9-08DEE1CE2353';
DECLARE @Txn nvarchar(100) = '5483055';   -- ProviderTransactionId del success de url_renew

/* 1) La base de compra2 debe estar Activa (Estado=1), sin recovery, con fechas avanzadas. */
SELECT 'BaseCompra2 (Estado debe=1 Activa, sin recovery)' AS Check_,
       s.Estado,                       -- 1 = Activa
       s.PaymentRecoveryStatus,        -- debe ser NULL
       s.FechaFinGraciaUtc,            -- debe ser NULL
       s.FechaFin,
       s.FechaProximoCobroUtc,
       s.ProviderExpiresAtUtc,
       s.ProviderStatusRaw,
       RIGHT(ISNULL(s.ProviderSubscriptionId,''),4) AS SubSuffix,  -- 4370
       s.TilopayRecurringPlanId        -- 6127
FROM dbo.Suscripciones s
WHERE s.TenantId = @Tenant;

/* 2) Incidente base (2139C7DB...) debe seguir cerrado (Resolved=1). */
SELECT 'IncidenteBase (Status debe=1 Resolved)' AS Check_,
       i.Id, i.Scope, i.Status, i.ResolvedAtUtc, i.TilopayRecurringPlanId, i.PlanCode
FROM dbo.SubscriptionPaymentIncidents i
WHERE i.TenantId = @Tenant AND i.Scope = 0 /* BasePlan */;

/* 3) PUNTO 1 — El evento success (776D32C2 / txn 5483055) YA NO debe quedar SinRelacion.
      Debe estar Procesado=1 con EstadoProcesamiento='ReconciliadoPorProveedor', Error NULL,
      y ligado a un PagoSuscripcion (PagoSuscripcionId no nulo). */
SELECT 'EventoSuccess (debe estar ReconciliadoPorProveedor, no SinRelacion)' AS Check_,
       e.Id, e.Tipo, e.Procesado, e.EstadoProcesamiento,
       e.ProviderTransactionId, e.PagoSuscripcionId, e.Error, e.FechaProcesamientoUtc
FROM dbo.EventosPago e
WHERE e.ProviderTransactionId = @Txn
   OR (e.TilopayRecurringPlanId = 6127 AND e.EstadoProcesamiento = 'SinRelacion')
ORDER BY e.FechaRecepcionUtc DESC;

/* 3b) PUNTO 1 — Trazabilidad financiera: debe existir EXACTAMENTE 1 PagoSuscripcion Confirmado
       (Estado=1) para el cobro real (txn 5483055), sin duplicados. */
SELECT 'PagoReconciliado (debe existir 1 Confirmado, sin duplicar)' AS Check_,
       COUNT(*)                                         AS PagosParaTxn,
       MIN(p.Estado)                                    AS EstadoMin,   -- 1 = Confirmado
       MAX(p.Estado)                                    AS EstadoMax,
       MAX(CAST(p.Monto AS decimal(18,2)))              AS Monto
FROM dbo.PagosSuscripcion p
WHERE p.TenantId = @Tenant AND p.ProviderTransactionId = @Txn;

/* 4) Add-on WA400 debe seguir activo (Estado=1) e intacto. */
SELECT 'AddonWA400 (Estado debe=1 Activa)' AS Check_,
       a.AddonCode, a.Estado, a.TilopayRecurringPlanId,
       RIGHT(ISNULL(a.ProviderSubscriptionId,''),4) AS SubSuffix, -- 3795
       a.MonthlyMessageLimit, a.PrecioMensual, a.FechaFin
FROM dbo.TenantSubscriptionAddons a
WHERE a.TenantId = @Tenant;

/* 5) PUNTO 2 (Opción A) — Entitlement ≠ configuración: comprar WA400 NO crea TenantWhatsAppSettings.
      Para compra2 (que aún no configuró WhatsApp) esto debe ser 0 filas. NO es riesgo de dinero:
      el paquete está bien cobrado; falta que el cliente entre a "Configurar WhatsApp". */
SELECT 'WhatsAppSettings Opción A (debe ser 0 filas hasta configurar)' AS Check_,
       COUNT(*) AS SettingsRows
FROM dbo.TenantWhatsAppSettings w
WHERE w.TenantId = @Tenant;

/* 6) GLOBAL money-risk — no debe haber incidentes abiertos (base ni add-on). */
SELECT 'OpenPaymentIncidents (base + add-on, debe ser 0)' AS Check_,
       SUM(CASE WHEN i.Scope = 0 AND i.Status = 0 THEN 1 ELSE 0 END) AS OpenBaseIncidents,
       SUM(CASE WHEN i.Scope = 1 AND i.Status = 0 THEN 1 ELSE 0 END) AS OpenAddonIncidents,
       SUM(CASE WHEN i.Status = 0 THEN 1 ELSE 0 END)                 AS OpenTotalIncidents
FROM dbo.SubscriptionPaymentIncidents i;

/* 7) GLOBAL — eventos success recurrentes que SIGAN SinRelacion (debe ser 0 tras la reconciliación). */
SELECT 'SuccessSinRelacionRestantes (debe ser 0)' AS Check_,
       COUNT(*) AS Restantes
FROM dbo.EventosPago e
WHERE e.Procesado = 0
      AND e.EstadoProcesamiento = 'SinRelacion'
      AND e.Tipo IN ('repeat_payment_success','repeat_payment_paid');

/* 8) PUNTO 2 (Opción A) — diferenciación aviso informativo vs riesgo operativo (GLOBAL).
      add-on activo sin configurar = informativo; settings habilitados sin add-on = operativo.
      Ninguno es riesgo de dinero. */
SELECT 'AddonActivoSinConfigurar (informativo, no dinero)' AS Check_,
       COUNT(*) AS Tenants
FROM dbo.TenantSubscriptionAddons a
WHERE a.Estado = 1 /* Activa */
      AND NOT EXISTS (SELECT 1 FROM dbo.TenantWhatsAppSettings w WHERE w.TenantId = a.TenantId);

SELECT 'SettingsHabilitadosSinAddon (operativo, no dinero)' AS Check_,
       COUNT(*) AS Tenants
FROM dbo.TenantWhatsAppSettings w
WHERE w.IsEnabled = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TenantSubscriptionAddons a
          WHERE a.TenantId = w.TenantId AND a.Estado = 1 /* Activa */);

/* 9) Auditoría de la sanación de la base y de la reconciliación del evento. */
SELECT TOP 20 'AuditCompra2' AS Check_,
       l.CreatedAtUtc, l.Action, LEFT(ISNULL(l.Reason,''),200) AS ReasonPreview
FROM dbo.PlatformAuditLogs l
WHERE l.TenantId = @Tenant
      AND l.Action IN ('PaymentRecoveryResolvedByProviderRenewal',
                       'PaymentRecoveryResolvedByWebhookSuccess',
                       'PaymentEventReconciledByProviderRenewal',
                       'SubscriptionPaymentRecoveryResolved')
ORDER BY l.CreatedAtUtc DESC;
