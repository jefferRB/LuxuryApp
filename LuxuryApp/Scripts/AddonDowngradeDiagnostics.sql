/* =============================================================================
   AddonDowngradeDiagnostics.sql — Diagnóstico READ-ONLY del downgrade de add-on
   WhatsApp que quedó a medias (caso compra2, WA800 -> WA400, 2026-07-29).

   SEGURO PARA PRODUCCIÓN: SOLO SELECT. No modifica una sola fila.

   Qué NO puede responder este script: el estado real en TiloPay. Los suscriptores
   viven en el proveedor, no en esta base. Para eso está la auditoría del proveedor:
     Platform > BillingHealth > "Auditar proveedor (add-ons)"  (getSuscriptorRepeat)
   o el bloque 9, que muestra el último snapshot ya persistido.

   Uso:
     sqlcmd -S <server> -d LuxuryApp -E -i Scripts\AddonDowngradeDiagnostics.sql
   ============================================================================= */

SET NOCOUNT ON;

-- ── Parámetros del caso a diagnosticar ───────────────────────────────────────
DECLARE @Tenant      uniqueidentifier = 'EE744446-05D3-4B59-0BD9-08DEE1CE2353'; -- compra2
DECLARE @EventId     uniqueidentifier = 'D3118389-6377-40C4-AC8E-2D3041C1FF7C';
DECLARE @PaymentId   uniqueidentifier = '1A95C227-550B-4D21-9525-25000CBD1CB3';
DECLARE @ProviderTxn nvarchar(100)    = '5502398';
DECLARE @OrderNumber nvarchar(200)    = 'PFC026726-PRE10922711785375299';

/* 1) PLAN BASE — debe seguir intacto: LC_M_03, Activa (1), sin recovery. ---- */
SELECT '1. BASE (no se toca: debe seguir Activa=1 y sin recovery)' AS Check_,
       s.CodigoPlan,
       s.Estado,                                             -- 1 = Activa
       s.PaymentRecoveryStatus,                              -- NULL
       s.ProviderStatusRaw,                                  -- Active
       s.TilopayRecurringPlanId,                             -- 6127
       RIGHT(ISNULL(s.ProviderSubscriptionId, ''), 4) AS BaseSubSuffix, -- 4370
       s.FechaFin,
       s.FechaProximoCobroUtc,
       s.CancelAtPeriodEnd
FROM dbo.Suscripciones s
WHERE s.TenantId = @Tenant;

/* 2) ADD-ON local actual — qué paquete cree el sistema que está vigente. ----
      OJO con ProviderCancellation: por sí solo NO dice si el suscriptor ACTUAL
      está de baja. Eso lo dice ProviderCancellationSubscriptionId. */
SELECT '2. ADD-ON local' AS Check_,
       a.AddonCode,
       a.Estado,                                             -- 1 = Activa
       a.BillingSource,                                      -- 0 ProviderRecurring / 1 ManualGrant / 2 Legacy
       a.TilopayRecurringPlanId,
       a.ProviderSubscriptionId,
       a.ProviderTransactionId,
       a.PrecioMensual,
       a.MonthlyMessageLimit,
       a.FechaFin,
       a.FechaProximoCobroUtc,
       a.CancelAtPeriodEnd,
       a.ProviderCancellation,                               -- 0 NotRequired / 1 Pending / 2 Cancelled
       a.ProviderCancellationSubscriptionId,                 -- ¿a QUIÉN se refiere el 2?
       a.ProviderCancelledAtUtc,
       a.PendingCancellationProviderSubscriptionId,
       a.PendingCancellationTilopayRecurringPlanId,
       a.PreviousProviderSubscriptionId,                     -- a quién reemplazó
       a.PreviousProviderCancelledAtUtc,
       a.ProviderCancellationAttemptCount,
       a.ProviderCancellationNextRetryUtc,
       a.UpdatedAtUtc
FROM dbo.TenantSubscriptionAddons a
WHERE a.TenantId = @Tenant;

/* 2b) LECTURA de la semántica: ¿consta que el suscriptor VIGENTE esté de baja? */
SELECT '2b. ¿El suscriptor ACTUAL consta como cancelado?' AS Check_,
       a.ProviderSubscriptionId,
       a.ProviderCancellationSubscriptionId,
       CASE
           WHEN a.ProviderCancellation = 2
                AND a.ProviderCancellationSubscriptionId IS NOT NULL
                AND a.ProviderCancellationSubscriptionId = a.ProviderSubscriptionId
               THEN 'SI — el vigente esta dado de baja'
           WHEN a.ProviderCancellation = 2
               THEN 'NO — el 2 se refiere a OTRO suscriptor (o es fila legacy sin scope)'
           ELSE 'NO — no hay cancelacion registrada del vigente'
       END AS Interpretacion
FROM dbo.TenantSubscriptionAddons a
WHERE a.TenantId = @Tenant;

/* 3) EVENTO del webhook rechazado — payload CRUDO incluido.
      Es la evidencia que dice de QUÉ campo salió el monto 459. */
SELECT '3. Evento del webhook' AS Check_,
       e.Id,
       e.Tipo,
       e.Procesado,
       e.EstadoProcesamiento,
       e.Error,
       e.ProviderTransactionId,
       e.ProviderSubscriberId,
       e.TilopayRecurringPlanId,
       e.Monto,
       e.Moneda,
       e.ReferenciaExterna,
       e.PagoSuscripcionId,
       e.FechaRecepcionUtc,
       e.FechaProcesamientoUtc,
       e.Payload                                             -- ← PAYLOAD CRUDO (redactado)
FROM dbo.EventosPago e
WHERE e.Id = @EventId
   OR e.ProviderTransactionId = @ProviderTxn
   OR e.ReferenciaExterna = @OrderNumber
ORDER BY e.FechaRecepcionUtc DESC;

/* 3b) TODO evento del tenant en las últimas 72h (para ver registration/anulación). */
SELECT '3b. Eventos del tenant (72h)' AS Check_,
       e.Id, e.Tipo, e.EstadoProcesamiento, e.Procesado,
       e.ProviderTransactionId, e.TilopayRecurringPlanId, e.Monto,
       e.ReferenciaExterna, e.FechaRecepcionUtc,
       LEFT(ISNULL(e.Error, ''), 200) AS ErrorCorto
FROM dbo.EventosPago e
WHERE e.TenantId = @Tenant
  AND e.FechaRecepcionUtc >= DATEADD(HOUR, -72, SYSUTCDATETIME())
ORDER BY e.FechaRecepcionUtc DESC;

/* 4) PAGO local del intento — incluye el payload que guardó el proveedor. */
SELECT '4. PagoSuscripcion del intento' AS Check_,
       p.Id,
       p.Estado,                                             -- 5 = ManualReview
       p.Monto                    AS MontoEsperadoLocal,     -- 6000.00
       p.Moneda,
       p.TilopayRecurringPlanId,                             -- 5831
       p.ProviderSubscriberId,
       p.ProviderTransactionId,
       p.ProviderReference,
       p.CorrelationToken,
       p.ReferenciaInterna,
       p.ProviderResultCode,                                 -- MANUAL_REVIEW
       p.ProviderResultMessage,
       p.ClienteEmail,
       p.Descripcion,
       p.FechaCreacionUtc,
       p.FechaActualizacionUtc,
       p.UltimoPayloadProveedor
FROM dbo.PagosSuscripcion p
WHERE p.Id = @PaymentId
   OR (p.TenantId = @Tenant AND p.FechaCreacionUtc >= DATEADD(HOUR, -72, SYSUTCDATETIME()))
ORDER BY p.FechaCreacionUtc DESC;

/* 5) INCIDENTES del tenant — base y add-on por separado.
      "Open incidents = 0" NO significa sano si el proveedor tiene doble activo:
      ese caso abre un incidente de add-on con Status=3 (ManualReview). */
SELECT '5. Incidentes' AS Check_,
       i.Id,
       i.Scope,                                              -- 0 BasePlan / 1 WhatsAppAddon
       i.Status,                                             -- 0 Open / 1 Resolved / 2 GraceExpired / 3 ManualReview / 4 Ignored
       i.PlanCode,
       i.TilopayRecurringPlanId,
       i.ProviderSubscriptionId,
       i.ProviderResultCode,                                 -- PROVIDER_DOUBLE_ACTIVE = doble cobro en TiloPay
       i.ProviderResultMessage,
       i.FailureCount,
       i.FailureDetectedAtUtc,
       i.UpdatedAtUtc
FROM dbo.SubscriptionPaymentIncidents i
WHERE i.TenantId = @Tenant
ORDER BY i.UpdatedAtUtc DESC;

/* 6) AUDITORÍA de plataforma del tenant (últimos 7 días). */
SELECT TOP (60) '6. PlatformAuditLogs' AS Check_,
       l.CreatedAtUtc,
       l.Action,
       l.EntityType,
       l.EntityId,
       LEFT(ISNULL(l.Reason, ''), 300) AS Reason
FROM dbo.PlatformAuditLogs l
WHERE l.TenantId = @Tenant
  AND l.CreatedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
ORDER BY l.CreatedAtUtc DESC;

/* 7) Pagos del tenant en revisión manual (dinero potencialmente cobrado sin aplicar). */
SELECT '7. Pagos en ManualReview' AS Check_,
       p.Id, p.Monto, p.TilopayRecurringPlanId, p.ProviderTransactionId,
       p.ProviderResultCode, LEFT(ISNULL(p.ProviderResultMessage, ''), 200) AS Mensaje,
       p.FechaActualizacionUtc
FROM dbo.PagosSuscripcion p
WHERE p.TenantId = @Tenant AND p.Estado = 5 /* ManualReview */
ORDER BY p.FechaActualizacionUtc DESC;

/* 8) GUARDA: nada de esto puede haber tocado accesos manuales (Luxe/canje). */
SELECT '8. Add-ons MANUALES (deben quedar intactos)' AS Check_,
       a.TenantId, a.AddonCode, a.Estado, a.BillingSource, a.ManualGrantType,
       a.IsManualGrantIndefinite, a.ManualGrantExpiresAtUtc, a.RevokedAtUtc,
       a.ProviderSubscriptionId  -- debe ser NULL en un manual puro
FROM dbo.TenantSubscriptionAddons a
WHERE a.BillingSource = 1 /* ManualGrant */;

/* 9) ÚLTIMO SNAPSHOT del proveedor (lo escribe el sondeo del webhook / la
      reconciliación / el botón de Mission Control). Si no hay fila, el verde del
      tablero NO está verificado contra TiloPay. */
SELECT '9. Snapshot del proveedor' AS Check_,
       s.TenantId,
       s.CapturedAtUtc,
       s.ActiveAddonSubscriberCount,                         -- debe ser 1
       s.HasDoubleActive,                                    -- debe ser 0
       s.IsInconclusive,
       s.ActiveRecurringPlanIds,
       s.ActiveSubscriberIds,
       s.LocalProviderSubscriptionId,
       s.Source,
       s.Detail
FROM dbo.ProviderAddonAuditSnapshots s
WHERE s.TenantId = @Tenant;

/* 10) RIESGO GLOBAL cross-tenant: doble add-on cobrable en el proveedor. */
SELECT '10. Tenants con doble add-on cobrable en TiloPay (debe ser 0)' AS Check_,
       COUNT(*) AS Tenants
FROM dbo.ProviderAddonAuditSnapshots
WHERE HasDoubleActive = 1;
