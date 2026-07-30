/*
LuxuryCloud - auditoria read-only de registros/tenants sospechosos
Uso: ejecutar contra una copia read-only, staging o una sesion de solo lectura.
No modifica datos. No llama proveedores. No expone secretos.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
DECLARE @Since7d datetime2(7) = DATEADD(day, -7, @NowUtc);
DECLARE @Since30d datetime2(7) = DATEADD(day, -30, @NowUtc);
DECLARE @PendingVerificationMode int = 3;

PRINT '1. Tenants creados en los ultimos 7/30 dias';
SELECT
    t.Id AS TenantId,
    t.Nombre AS TenantName,
    t.FechaCreacion,
    CASE WHEN t.FechaCreacion >= @Since7d THEN 1 ELSE 0 END AS CreatedLast7d,
    CASE WHEN t.FechaCreacion >= @Since30d THEN 1 ELSE 0 END AS CreatedLast30d,
    t.Activo,
    t.CommercialAccessMode,
    t.ForcedPlanId,
    owner.Email AS OwnerEmail,
    owner.EmailConfirmed,
    owner.State AS OwnerState,
    latestSub.Estado AS LatestSubscriptionStatus,
    latestSub.FechaInicio AS LatestSubscriptionStart,
    latestSub.FechaFin AS LatestSubscriptionEnd,
    latestPayment.Estado AS LatestPaymentStatus,
    latestPayment.FechaCreacionUtc AS LatestPaymentCreatedUtc,
    usage.CitasCount,
    usage.CobrosCount,
    usage.BookingRequestsCount,
    usage.LastActivityUtc
FROM dbo.Tenants t
OUTER APPLY (
    SELECT TOP (1)
        u.Id,
        u.Email,
        u.EmailConfirmed,
        u.State
    FROM dbo.AspNetUsers u
    WHERE u.TenantId = t.Id
    ORDER BY u.Email
) owner
OUTER APPLY (
    SELECT TOP (1)
        s.Estado,
        s.FechaInicio,
        s.FechaFin,
        s.FechaUltimaActualizacionUtc
    FROM dbo.Suscripciones s
    WHERE s.TenantId = t.Id
    ORDER BY COALESCE(s.FechaUltimaActualizacionUtc, s.FechaInicio) DESC, s.FechaInicio DESC
) latestSub
OUTER APPLY (
    SELECT TOP (1)
        p.Estado,
        p.FechaCreacionUtc
    FROM dbo.PagosSuscripcion p
    WHERE p.TenantId = t.Id
    ORDER BY p.FechaCreacionUtc DESC
) latestPayment
OUTER APPLY (
    SELECT
        (SELECT COUNT_BIG(*) FROM dbo.Citas c WHERE c.TenantId = t.Id) AS CitasCount,
        (SELECT COUNT_BIG(*) FROM dbo.Cobros c WHERE c.TenantId = t.Id) AS CobrosCount,
        (SELECT COUNT_BIG(*) FROM dbo.BookingRequests b WHERE b.TenantId = t.Id) AS BookingRequestsCount,
        (
            SELECT MAX(v.ActivityUtc)
            FROM (VALUES
                ((SELECT MAX(c.FechaHoraCita) FROM dbo.Citas c WHERE c.TenantId = t.Id)),
                ((SELECT MAX(c.FechaCobro) FROM dbo.Cobros c WHERE c.TenantId = t.Id)),
                ((SELECT MAX(b.CreatedAtUtc) FROM dbo.BookingRequests b WHERE b.TenantId = t.Id))
            ) v(ActivityUtc)
        ) AS LastActivityUtc
) usage
WHERE t.FechaCreacion >= @Since30d
ORDER BY t.FechaCreacion DESC;

PRINT '2. Usuarios asociados a tenants recientes (AspNetUsers no tiene CreatedAt propio)';
SELECT
    u.Id AS UserId,
    u.Email,
    u.NormalizedEmail,
    u.EmailConfirmed,
    u.State,
    u.TenantId,
    t.Nombre AS TenantName,
    t.FechaCreacion AS TenantCreatedUtc,
    t.CommercialAccessMode
FROM dbo.AspNetUsers u
JOIN dbo.Tenants t ON t.Id = u.TenantId
WHERE t.FechaCreacion >= @Since30d
ORDER BY t.FechaCreacion DESC, u.Email;

PRINT '3. Emails raros/repetitivos';
WITH UserEmailParts AS (
    SELECT
        u.Id,
        u.Email,
        u.TenantId,
        t.Nombre AS TenantName,
        t.FechaCreacion,
        LOWER(LTRIM(RTRIM(u.Email))) AS EmailLower,
        LEFT(LOWER(LTRIM(RTRIM(u.Email))), NULLIF(CHARINDEX('@', LOWER(LTRIM(RTRIM(u.Email)))) - 1, -1)) AS LocalPart,
        RIGHT(LOWER(LTRIM(RTRIM(u.Email))), LEN(LOWER(LTRIM(RTRIM(u.Email)))) - CHARINDEX('@', LOWER(LTRIM(RTRIM(u.Email))))) AS DomainPart
    FROM dbo.AspNetUsers u
    JOIN dbo.Tenants t ON t.Id = u.TenantId
    WHERE u.Email IS NOT NULL AND CHARINDEX('@', u.Email) > 1
)
SELECT
    Id AS UserId,
    Email,
    TenantId,
    TenantName,
    FechaCreacion AS TenantCreatedUtc,
    LEN(LocalPart) - LEN(REPLACE(LocalPart, '.', '')) AS LocalDotCount,
    LEN(LocalPart) - LEN(REPLACE(LocalPart, '+', '')) AS LocalPlusCount,
    DomainPart,
    CASE
        WHEN DomainPart IN ('10minutemail.com', 'guerrillamail.com', 'mailinator.com', 'tempmail.com', 'temp-mail.org', 'yopmail.com') THEN 'disposable-domain'
        WHEN LEN(LocalPart) - LEN(REPLACE(LocalPart, '.', '')) >= 7 THEN 'many-dots'
        WHEN EmailLower LIKE '%..%' THEN 'double-dot'
        WHEN EmailLower LIKE '%test%' OR EmailLower LIKE '%asdf%' OR EmailLower LIKE '%qwerty%' THEN 'test-pattern'
        ELSE 'review'
    END AS SuspicionReason
FROM UserEmailParts
WHERE DomainPart IN ('10minutemail.com', 'guerrillamail.com', 'mailinator.com', 'tempmail.com', 'temp-mail.org', 'yopmail.com')
   OR LEN(LocalPart) - LEN(REPLACE(LocalPart, '.', '')) >= 7
   OR EmailLower LIKE '%..%'
   OR EmailLower LIKE '%test%'
   OR EmailLower LIKE '%asdf%'
   OR EmailLower LIKE '%qwerty%'
ORDER BY FechaCreacion DESC;

PRINT '4. Multiples tenants por mismo email';
SELECT
    LOWER(LTRIM(RTRIM(u.Email))) AS Email,
    COUNT(DISTINCT u.TenantId) AS TenantCount,
    MIN(t.FechaCreacion) AS FirstTenantCreatedUtc,
    MAX(t.FechaCreacion) AS LastTenantCreatedUtc,
    STRING_AGG(CONVERT(varchar(36), u.TenantId), ', ') AS TenantIds
FROM dbo.AspNetUsers u
JOIN dbo.Tenants t ON t.Id = u.TenantId
WHERE u.Email IS NOT NULL
GROUP BY LOWER(LTRIM(RTRIM(u.Email)))
HAVING COUNT(DISTINCT u.TenantId) > 1
ORDER BY TenantCount DESC, LastTenantCreatedUtc DESC;

PRINT '5. Tenants sin pago confirmado, sin actividad y sin acceso comercial manual';
SELECT
    t.Id AS TenantId,
    t.Nombre,
    t.FechaCreacion,
    t.Activo,
    t.CommercialAccessMode,
    owner.Email AS OwnerEmail,
    owner.EmailConfirmed,
    ISNULL(activity.CitasCount, 0) AS CitasCount,
    ISNULL(activity.CobrosCount, 0) AS CobrosCount,
    ISNULL(activity.BookingRequestsCount, 0) AS BookingRequestsCount,
    payments.ConfirmedPaymentCount,
    payments.PendingPaymentCount
FROM dbo.Tenants t
OUTER APPLY (
    SELECT TOP (1) u.Email, u.EmailConfirmed
    FROM dbo.AspNetUsers u
    WHERE u.TenantId = t.Id
    ORDER BY u.Email
) owner
OUTER APPLY (
    SELECT
        SUM(CASE WHEN p.Estado = 1 THEN 1 ELSE 0 END) AS ConfirmedPaymentCount,
        SUM(CASE WHEN p.Estado = 0 THEN 1 ELSE 0 END) AS PendingPaymentCount
    FROM dbo.PagosSuscripcion p
    WHERE p.TenantId = t.Id
) payments
OUTER APPLY (
    SELECT
        (SELECT COUNT_BIG(*) FROM dbo.Citas c WHERE c.TenantId = t.Id) AS CitasCount,
        (SELECT COUNT_BIG(*) FROM dbo.Cobros c WHERE c.TenantId = t.Id) AS CobrosCount,
        (SELECT COUNT_BIG(*) FROM dbo.BookingRequests b WHERE b.TenantId = t.Id) AS BookingRequestsCount
) activity
WHERE t.CommercialAccessMode NOT IN (1, 2)
  AND ISNULL(payments.ConfirmedPaymentCount, 0) = 0
  AND ISNULL(activity.CitasCount, 0) = 0
  AND ISNULL(activity.CobrosCount, 0) = 0
  AND ISNULL(activity.BookingRequestsCount, 0) = 0
ORDER BY t.FechaCreacion DESC;

PRINT '6. Registros pendientes de verificacion / usuarios sin email confirmado';
SELECT
    t.Id AS TenantId,
    t.Nombre,
    t.FechaCreacion,
    t.Activo,
    t.CommercialAccessMode,
    u.Id AS UserId,
    u.Email,
    u.EmailConfirmed,
    u.State
FROM dbo.Tenants t
JOIN dbo.AspNetUsers u ON u.TenantId = t.Id
WHERE t.CommercialAccessMode = @PendingVerificationMode
   OR u.EmailConfirmed = 0
ORDER BY t.FechaCreacion DESC;

PRINT '7. Evidencia de IP/UserAgent en aceptacion de contrato';
SELECT TOP (200)
    car.AcceptedAtUtc,
    car.UserId,
    u.Email,
    u.TenantId,
    t.Nombre AS TenantName,
    car.IpAddress,
    LEFT(car.UserAgent, 400) AS UserAgent
FROM dbo.ContractAcceptanceRecords car
LEFT JOIN dbo.AspNetUsers u ON u.Id = car.UserId
LEFT JOIN dbo.Tenants t ON t.Id = u.TenantId
WHERE car.AcceptedAtUtc >= @Since30d
ORDER BY car.AcceptedAtUtc DESC;

PRINT '8. Auditoria plataforma relacionada con cambios criticos recientes';
SELECT TOP (300)
    pal.CreatedAtUtc,
    pal.ActorEmail,
    pal.Action,
    pal.EntityType,
    pal.EntityId,
    pal.TenantId,
    pal.TenantName,
    pal.TargetUserEmail,
    pal.IpAddress,
    LEFT(pal.UserAgent, 400) AS UserAgent,
    pal.Reason
FROM dbo.PlatformAuditLogs pal
WHERE pal.CreatedAtUtc >= @Since30d
ORDER BY pal.CreatedAtUtc DESC;

PRINT '9. Tenants creados sin completar pago';
SELECT
    t.Id AS TenantId,
    t.Nombre,
    t.FechaCreacion,
    t.CommercialAccessMode,
    owner.Email AS OwnerEmail,
    owner.EmailConfirmed,
    COUNT(p.Id) AS PaymentAttempts,
    SUM(CASE WHEN p.Estado = 1 THEN 1 ELSE 0 END) AS ConfirmedPayments,
    MAX(p.FechaCreacionUtc) AS LastPaymentAttemptUtc
FROM dbo.Tenants t
OUTER APPLY (
    SELECT TOP (1) u.Email, u.EmailConfirmed
    FROM dbo.AspNetUsers u
    WHERE u.TenantId = t.Id
    ORDER BY u.Email
) owner
LEFT JOIN dbo.PagosSuscripcion p ON p.TenantId = t.Id
WHERE t.FechaCreacion >= @Since30d
GROUP BY t.Id, t.Nombre, t.FechaCreacion, t.CommercialAccessMode, owner.Email, owner.EmailConfirmed
HAVING SUM(CASE WHEN p.Estado = 1 THEN 1 ELSE 0 END) IS NULL
    OR SUM(CASE WHEN p.Estado = 1 THEN 1 ELSE 0 END) = 0
ORDER BY t.FechaCreacion DESC;
