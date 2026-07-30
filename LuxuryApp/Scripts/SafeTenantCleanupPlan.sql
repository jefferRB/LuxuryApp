/*
LuxuryCloud - plan de limpieza segura de tenants basura

IMPORTANTE:
- ROLLBACK por defecto.
- No borra filas.
- No cancela suscripciones reales.
- No llama TiloPay ni Meta.
- Ejecutar primero en staging/copia read-only para revisar candidatos.
- Cambiar ROLLBACK por COMMIT solo despues de aprobacion manual tenant por tenant.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
DECLARE @OlderThanDays int = 7;
DECLARE @PendingVerificationMode int = 3;
DECLARE @Reason nvarchar(250) = CONCAT(
    'Soft-disable propuesto por limpieza segura de registros pendientes/sin pago. Preview ',
    CONVERT(varchar(33), @NowUtc, 126),
    ' UTC.'
);

BEGIN TRANSACTION;

;WITH CandidateTenants AS (
    SELECT
        t.Id,
        t.Nombre,
        t.FechaCreacion,
        t.CommercialAccessMode,
        owner.Email AS OwnerEmail,
        owner.EmailConfirmed
    FROM dbo.Tenants t
    OUTER APPLY (
        SELECT TOP (1) u.Email, u.EmailConfirmed
        FROM dbo.AspNetUsers u
        WHERE u.TenantId = t.Id
        ORDER BY u.Email
    ) owner
    WHERE t.Activo = 1
      AND t.CommercialAccessMode = @PendingVerificationMode
      AND t.FechaCreacion < DATEADD(day, -@OlderThanDays, @NowUtc)
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.PagosSuscripcion p
          WHERE p.TenantId = t.Id
            AND p.Estado = 1
      )
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.Citas c
          WHERE c.TenantId = t.Id
      )
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.Cobros c
          WHERE c.TenantId = t.Id
      )
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.BookingRequests b
          WHERE b.TenantId = t.Id
      )
)
SELECT
    'CANDIDATE' AS RowType,
    Id AS TenantId,
    Nombre,
    FechaCreacion,
    CommercialAccessMode,
    OwnerEmail,
    EmailConfirmed
FROM CandidateTenants
ORDER BY FechaCreacion;

;WITH CandidateTenants AS (
    SELECT t.Id
    FROM dbo.Tenants t
    WHERE t.Activo = 1
      AND t.CommercialAccessMode = @PendingVerificationMode
      AND t.FechaCreacion < DATEADD(day, -@OlderThanDays, @NowUtc)
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.PagosSuscripcion p
          WHERE p.TenantId = t.Id
            AND p.Estado = 1
      )
      AND NOT EXISTS (SELECT 1 FROM dbo.Citas c WHERE c.TenantId = t.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.Cobros c WHERE c.TenantId = t.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.BookingRequests b WHERE b.TenantId = t.Id)
)
UPDATE t
SET
    Activo = 0,
    CommercialNotes = @Reason,
    CommercialUpdatedUtc = @NowUtc
OUTPUT
    'TENANT_SOFT_DISABLED_PREVIEW' AS RowType,
    inserted.Id AS TenantId,
    deleted.Activo AS OldActivo,
    inserted.Activo AS NewActivo,
    inserted.CommercialNotes
FROM dbo.Tenants t
JOIN CandidateTenants c ON c.Id = t.Id;

;WITH CandidateTenants AS (
    SELECT t.Id
    FROM dbo.Tenants t
    WHERE t.Activo = 0
      AND t.CommercialNotes = @Reason
)
UPDATE u
SET
    State = 0,
    SecurityStamp = NEWID()
OUTPUT
    'USER_DISABLED_PREVIEW' AS RowType,
    inserted.Id AS UserId,
    inserted.TenantId,
    inserted.Email,
    deleted.State AS OldState,
    inserted.State AS NewState
FROM dbo.AspNetUsers u
JOIN CandidateTenants c ON c.Id = u.TenantId;

ROLLBACK TRANSACTION;
PRINT 'ROLLBACK ejecutado. No se persistio ningun cambio.';

-- Para aplicar despues de aprobacion manual:
-- 1. Copiar este script a una ventana nueva.
-- 2. Reducir CandidateTenants a una lista explicita de TenantId aprobados.
-- 3. Ejecutar en una ventana de mantenimiento.
-- 4. Cambiar ROLLBACK TRANSACTION por COMMIT TRANSACTION.
