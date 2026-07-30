/* =============================================================================
   AddonDowngradeRepair.sql — Reparación del add-on de WhatsApp que quedó a medias
   (caso compra2, WA800 -> WA400, 2026-07-29).

   ⚠️  ROLLBACK POR DEFECTO. Tal como está, este script NO cambia nada: corre,
       muestra el antes/después y deshace todo. Para aplicar de verdad hay que
       poner @Apply = 1 A PROPÓSITO, y solo después de haber corrido
       Scripts\AddonDowngradeDiagnostics.sql y la auditoría del proveedor.

   ⚠️  ESTE SCRIPT NO HABLA CON TILOPAY. Solo alinea el estado LOCAL. Dar de baja
       un suscriptor en el proveedor se hace por el flujo VERIFICADO (que confirma
       con getSuscriptorRepeat que quedó inactivo), nunca por SQL:
         · Platform > BillingHealth > "Auditar proveedor (add-ons)" para ver estado.
         · Dejar el suscriptor a cancelar en PendingCancellation* (modo B de abajo)
           y que el worker RetryPendingAddonCancellations haga la baja verificada.

   ORDEN OBLIGATORIO DE LA REPARACIÓN:
     1. Diagnóstico read-only (AddonDowngradeDiagnostics.sql).
     2. Auditoría del proveedor: ¿cuántos suscriptores cobran hoy?
     3. Decidir QUÉ paquete se queda (ver "DECISIÓN" abajo).
     4. Correr este script con @Apply = 0 y leer el diff.
     5. Recién entonces, @Apply = 1.
     6. Verificar con el diagnóstico + auditoría del proveedor.

   ─────────────────────────────────────────────────────────────────────────────
   DECISIÓN (caso compra2): TiloPay dejó WA400 (393795) y WA800 (394655) activos,
   pero la transacción de ₡459 fue una AUTORIZACIÓN NO CAPTURADA que además se
   reversó ("Re-PFC…"). Es decir: NO hay evidencia de un cobro real de ₡6.000.

   Por lo tanto NO se debe marcar el pago 1A95C227 como confirmado de ₡6.000.
   Hay dos salidas legítimas, y las dos dejan UN SOLO suscriptor cobrable:

     MODO A — "revertir el downgrade" (recomendado si no hubo cobro real):
       El add-on sigue siendo WA800 (que sí se pagó). Se da de baja el suscriptor
       WA400 393795 en TiloPay porque quedó activo SIN cobro que lo respalde.
       Local: no cambia el paquete; solo se deja 393795 pendiente de baja.

     MODO B — "honrar el downgrade" (si comercialmente se decide dejar WA400):
       Se adopta WA400/393795 como el add-on vigente y se da de baja WA800 394655.
       Ojo: el cliente pagó ₡12.000 por el ciclo de WA800 en curso; bajarlo a
       WA400 sin cobro nuevo es una decisión COMERCIAL, no una confirmación de
       pago. El script no inventa un pago: deja el pago en ManualReview.

   Elegí el modo en @Mode. Ninguno de los dos toca el plan base ni a compra1 ni a
   los accesos manuales (Luxe).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── PARÁMETROS ───────────────────────────────────────────────────────────────
DECLARE @Apply     bit = 0;          -- 0 = ensayo con ROLLBACK (default). 1 = aplicar.
DECLARE @Mode      char(1) = 'A';    -- 'A' revertir downgrade / 'B' honrar downgrade

DECLARE @Tenant    uniqueidentifier = 'EE744446-05D3-4B59-0BD9-08DEE1CE2353';
DECLARE @PaymentId uniqueidentifier = '1A95C227-550B-4D21-9525-25000CBD1CB3';
DECLARE @EventId   uniqueidentifier = 'D3118389-6377-40C4-AC8E-2D3041C1FF7C';

DECLARE @Wa400Sub  nvarchar(100) = '393795';
DECLARE @Wa400Plan int           = 5831;
DECLARE @Wa800Sub  nvarchar(100) = '394655';
DECLARE @Wa800Plan int           = 5832;

DECLARE @Actor     nvarchar(450) = 'platform-repair';
DECLARE @Now       datetime2(7)  = SYSUTCDATETIME();

BEGIN TRAN;

BEGIN TRY

    /* ── ANTES ──────────────────────────────────────────────────────────── */
    SELECT 'ANTES — add-on' AS Fase, AddonCode, Estado, BillingSource,
           TilopayRecurringPlanId, ProviderSubscriptionId, PrecioMensual,
           MonthlyMessageLimit, ProviderCancellation,
           ProviderCancellationSubscriptionId, PendingCancellationProviderSubscriptionId,
           PendingCancellationTilopayRecurringPlanId, PreviousProviderSubscriptionId
    FROM dbo.TenantSubscriptionAddons WHERE TenantId = @Tenant;

    SELECT 'ANTES — base (NO se toca)' AS Fase, CodigoPlan, Estado,
           TilopayRecurringPlanId, ProviderSubscriptionId, PaymentRecoveryStatus
    FROM dbo.Suscripciones WHERE TenantId = @Tenant;

    /* ── GUARDA: solo se repara un add-on PAGADO por el proveedor ─────────
       Un ManualGrant (Luxe/canje) no tiene suscriptor recurrente y este script
       no debe rozarlo nunca. */
    IF NOT EXISTS (SELECT 1 FROM dbo.TenantSubscriptionAddons
                   WHERE TenantId = @Tenant AND BillingSource = 0 /* ProviderRecurring */)
    BEGIN
        RAISERROR('El tenant no tiene un add-on ProviderRecurring: este script no aplica.', 16, 1);
    END

    /* ── MODO A: el add-on sigue en WA800; se da de baja el WA400 huérfano ── */
    IF @Mode = 'A'
    BEGIN
        UPDATE dbo.TenantSubscriptionAddons
        SET PendingCancellationProviderSubscriptionId = @Wa400Sub,
            PendingCancellationTilopayRecurringPlanId = @Wa400Plan,
            ProviderCancellation = 1,   -- PendingManualCancellation: el worker hará la baja VERIFICADA
            ProviderCancellationSubscriptionId = NULL,
            ProviderCancelledAtUtc = NULL,
            ProviderCancellationAttemptCount = 0,
            ProviderCancellationLastAttemptUtc = NULL,
            ProviderCancellationNextRetryUtc = NULL,
            UpdatedAtUtc = @Now
        WHERE TenantId = @Tenant
          AND BillingSource = 0
          AND ProviderSubscriptionId = @Wa800Sub;   -- guarda: solo si el vigente es WA800
    END

    /* ── MODO B: se adopta WA400 y se da de baja el WA800 ─────────────────
       El monto/limite se toman del catálogo de Planes para no hardcodear precios. */
    IF @Mode = 'B'
    BEGIN
        DECLARE @Wa400PlanRow uniqueidentifier, @Wa400Precio decimal(18,2), @Wa400Limite int;

        SELECT TOP (1) @Wa400PlanRow = Id, @Wa400Precio = PrecioMensual, @Wa400Limite = LimiteMensajesMensual
        FROM dbo.Planes WHERE Codigo = 'WA400' AND Activo = 1;

        IF @Wa400PlanRow IS NULL
            RAISERROR('No se encontro el plan WA400 activo en dbo.Planes.', 16, 1);

        UPDATE dbo.TenantSubscriptionAddons
        SET PlanId = @Wa400PlanRow,
            AddonCode = 'WA400',
            TilopayRecurringPlanId = @Wa400Plan,
            ProviderSubscriptionId = @Wa400Sub,
            PrecioMensual = @Wa400Precio,
            MonthlyMessageLimit = @Wa400Limite,
            -- El suscriptor VIEJO queda pendiente de baja verificada (Strategy B).
            PreviousProviderSubscriptionId = @Wa800Sub,
            PendingCancellationProviderSubscriptionId = @Wa800Sub,
            PendingCancellationTilopayRecurringPlanId = @Wa800Plan,
            ProviderCancellation = 1,
            ProviderCancellationSubscriptionId = NULL,
            ProviderCancelledAtUtc = NULL,
            ProviderCancellationAttemptCount = 0,
            ProviderCancellationLastAttemptUtc = NULL,
            ProviderCancellationNextRetryUtc = NULL,
            UpdatedAtUtc = @Now
        WHERE TenantId = @Tenant
          AND BillingSource = 0
          AND ProviderSubscriptionId = @Wa800Sub;

        -- NO se toca ProviderTransactionId: no hay transacción capturada que lo respalde.
    END

    /* ── El pago y el evento NO se marcan como confirmados ────────────────
       La transacción de ₡459 fue autorización no capturada + reverso: no existe
       un cobro de ₡6.000. Se documenta el cierre manual, sin inventar dinero. */
    UPDATE dbo.PagosSuscripcion
    SET ProviderResultMessage = LEFT(
            'Cerrado por reparacion manual: la transaccion del proveedor fue una autorizacion NO capturada y reversada (no hubo cobro de 6000 CRC). Modo ' + @Mode + '.', 300),
        FechaActualizacionUtc = @Now
    WHERE Id = @PaymentId
      AND TenantId = @Tenant
      AND Estado = 5;   -- sigue en ManualReview a propósito

    UPDATE dbo.EventosPago
    SET EstadoProcesamiento = 'ResueltoManualmente',
        Procesado = 1,
        FechaProcesamientoUtc = @Now,
        Error = LEFT('Resuelto manualmente: autorizacion no capturada/reversada, sin cobro real. Modo ' + @Mode + '.', 500)
    WHERE Id = @EventId
      AND Procesado = 0;

    /* ── Auditoría obligatoria ────────────────────────────────────────────── */
    INSERT INTO dbo.PlatformAuditLogs (Id, ActorUserId, ActorEmail, Action, EntityType, EntityId, TenantId, Reason, CreatedAtUtc)
    SELECT NEWID(), @Actor, @Actor,
           'AddonProviderRepairApplied',   -- PlatformAuditActions.AddonProviderRepairApplied
           'WhatsAppAddon',
           CAST(a.Id AS nvarchar(50)),
           @Tenant,
           LEFT('Reparacion manual del downgrade de add-on. Modo ' + @Mode +
                '. Suscriptor vigente ' + ISNULL(a.ProviderSubscriptionId, '(null)') +
                '. Pendiente de baja ' + ISNULL(a.PendingCancellationProviderSubscriptionId, '(ninguno)') +
                '. El pago queda en ManualReview: no hubo cobro capturado.', 500),
           @Now
    FROM dbo.TenantSubscriptionAddons a
    WHERE a.TenantId = @Tenant;

    /* ── DESPUÉS ────────────────────────────────────────────────────────── */
    SELECT 'DESPUES — add-on' AS Fase, AddonCode, Estado, BillingSource,
           TilopayRecurringPlanId, ProviderSubscriptionId, PrecioMensual,
           MonthlyMessageLimit, ProviderCancellation,
           ProviderCancellationSubscriptionId, PendingCancellationProviderSubscriptionId,
           PendingCancellationTilopayRecurringPlanId, PreviousProviderSubscriptionId
    FROM dbo.TenantSubscriptionAddons WHERE TenantId = @Tenant;

    SELECT 'DESPUES — base (debe ser IDENTICA al ANTES)' AS Fase, CodigoPlan, Estado,
           TilopayRecurringPlanId, ProviderSubscriptionId, PaymentRecoveryStatus
    FROM dbo.Suscripciones WHERE TenantId = @Tenant;

    /* ── Commit SOLO si se pidió explícitamente ──────────────────────────── */
    IF @Apply = 1
    BEGIN
        COMMIT TRAN;
        PRINT '>>> APLICADO. Siguiente paso OBLIGATORIO: correr el worker de cancelacion';
        PRINT '>>> (RetryPendingAddonCancellations) o esperar la reconciliacion, y luego';
        PRINT '>>> auditar el proveedor para confirmar que queda 1 solo add-on cobrable.';
    END
    ELSE
    BEGIN
        ROLLBACK TRAN;
        PRINT '>>> ENSAYO: se revirtio todo (@Apply = 0). Revisa el diff de arriba.';
    END

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    PRINT '>>> ERROR: se revirtio todo.';
    THROW;
END CATCH
