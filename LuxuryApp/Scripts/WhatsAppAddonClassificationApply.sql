/* =============================================================================
   WhatsAppAddonClassificationApply.sql — Clasificación EXPLÍCITA de datos actuales.

   Correr MANUALMENTE (una vez) DESPUÉS de aplicar la migración
   AddWhatsAppAddonBillingSource. NO desactiva ni borra add-ons: solo asigna
   BillingSource y copia la metadata de grant desde señales existentes.

   Antes de correr esto: revisar WhatsAppAddonClassification.sql (read-only).

   Reglas (idénticas al reporte read-only):
     - ProviderSubscriptionId presente        -> ProviderRecurring (pagado TiloPay). compra1/compra2.
     - Sin provider sub PERO recurrente        -> ProviderRecurring (queda como ProviderRisk, ver reporte).
     - ProviderTransactionId LIKE 'MANUAL-%'   -> ManualGrant (metadata desde FechaInicio/FechaFin).
     - Resto                                    -> Legacy.

   Todo dentro de UNA transacción con vista previa. Revisar los SELECT, y si todo
   cuadra, quitar el comentario del COMMIT (por defecto hace ROLLBACK para ensayar).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

/* --- Vista previa ANTES --- */
SELECT 'ANTES' AS Fase, BillingSource, COUNT(*) AS Filas
FROM dbo.TenantSubscriptionAddons GROUP BY BillingSource;

/* 1) ProviderRecurring: tiene suscriptor del proveedor (pagado TiloPay real). */
UPDATE dbo.TenantSubscriptionAddons
SET BillingSource = 0 /* ProviderRecurring */
WHERE ProviderSubscriptionId IS NOT NULL AND ProviderSubscriptionId <> '';

/* 2) ProviderRecurring (RIESGO): recurrente que perdió el id_suscriptor. Se deja ProviderRecurring
      para que el clasificador lo marque como ProviderRisk (dinero) — NO se toca su estado. */
UPDATE dbo.TenantSubscriptionAddons
SET BillingSource = 0 /* ProviderRecurring */
WHERE (ProviderSubscriptionId IS NULL OR ProviderSubscriptionId = '')
      AND TilopayRecurringPlanId IS NOT NULL;

/* 3) ManualGrant: marcador legacy MANUAL-*, sin recurrente. Metadata desde lo disponible.
      ManualGrantType = 4 (Other): el DUEÑO reclasifica (Barter/Courtesy/etc.) desde el modal.
      Vigencia = FechaFin actual (los vencidos SIGUEN vencidos; NO se ponen indefinidos aquí). */
UPDATE dbo.TenantSubscriptionAddons
SET BillingSource            = 1,  /* ManualGrant */
    ManualGrantType          = 4,  /* Other */
    GrantedAtUtc             = ISNULL(GrantedAtUtc, FechaInicio),
    ManualGrantExpiresAtUtc  = FechaFin,
    IsManualGrantIndefinite  = 0,
    ManualGrantReason        = ISNULL(ManualGrantReason, 'Backfill: acceso manual histórico (revisar/tipificar desde plataforma).')
WHERE (ProviderSubscriptionId IS NULL OR ProviderSubscriptionId = '')
      AND (TilopayRecurringPlanId IS NULL)
      AND ProviderTransactionId LIKE 'MANUAL-%';

/* 4) Legacy: sin provider, sin recurrente, sin marcador MANUAL. Nunca es entitlement efectivo. */
UPDATE dbo.TenantSubscriptionAddons
SET BillingSource = 2 /* Legacy */
WHERE (ProviderSubscriptionId IS NULL OR ProviderSubscriptionId = '')
      AND (TilopayRecurringPlanId IS NULL)
      AND (ProviderTransactionId IS NULL OR ProviderTransactionId NOT LIKE 'MANUAL-%');

/* --- Vista previa DESPUÉS --- */
SELECT 'DESPUES' AS Fase, BillingSource, COUNT(*) AS Filas
FROM dbo.TenantSubscriptionAddons GROUP BY BillingSource;

SELECT a.TenantId, t.Nombre AS TenantName, a.AddonCode, a.BillingSource,
       a.IsManualGrantIndefinite, a.ManualGrantExpiresAtUtc, a.ManualGrantType,
       RIGHT(ISNULL(a.ProviderSubscriptionId,''),6) AS ProviderSubSuffix
FROM dbo.TenantSubscriptionAddons a
LEFT JOIN dbo.Tenants t ON t.Id = a.TenantId
ORDER BY a.BillingSource, t.Nombre;

/* Por defecto ROLLBACK (ensayo). Cuando los SELECT se vean correctos, comentar ROLLBACK
   y descomentar COMMIT. */
ROLLBACK TRAN;
-- COMMIT TRAN;

/* =============================================================================
   PASO MANUAL POSTERIOR (desde /Platform → modal WhatsApp del tenant Luxe):
     - luxecentrodebelleza2025@gmail.com → tipo = Barter (Canje), vigencia = Indefinido.
   Y reclasificar cualquier fila que sea basura de prueba a Legacy o revocarla.
   ============================================================================= */
