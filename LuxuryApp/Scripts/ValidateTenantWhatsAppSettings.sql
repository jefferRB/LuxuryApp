SET NOCOUNT ON;

DECLARE @TenantId uniqueidentifier = '00000000-0000-0000-0000-000000000000';
-- Ajustar cuando el tenant use otra zona. SQL Server usa nombres de zona de Windows.
DECLARE @SqlServerTimeZoneId sysname = N'Central America Standard Time';
DECLARE @TenantToday date = CONVERT(date, SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE @SqlServerTimeZoneId);
DECLARE @DayStartUtc datetime2 = CONVERT(
    datetime2,
    CONVERT(datetime2, @TenantToday) AT TIME ZONE @SqlServerTimeZoneId AT TIME ZONE 'UTC');
DECLARE @DayEndUtc datetime2 = CONVERT(
    datetime2,
    CONVERT(datetime2, DATEADD(day, 1, @TenantToday)) AT TIME ZONE @SqlServerTimeZoneId AT TIME ZONE 'UTC');

PRINT 'Configuracion WhatsApp por tenant';
SELECT
    t.Id AS TenantId,
    t.Nombre AS TenantName,
    ISNULL(settings.IsEnabled, 0) AS IsEnabled,
    ISNULL(settings.SendConfirmationOnCreate, 1) AS SendConfirmationOnCreate,
    ISNULL(settings.SendReminderThreeHoursBefore, 1) AS SendReminderThreeHoursBefore,
    ISNULL(settings.DailyMessageLimit, 30) AS DailyMessageLimit,
    ISNULL(settings.TimeZoneId, N'America/Costa_Rica') AS TimeZoneId,
    settings.Notes,
    settings.UpdatedAtUtc,
    settings.UpdatedByUserId
FROM Tenants AS t
LEFT JOIN TenantWhatsAppSettings AS settings
    ON settings.TenantId = t.Id
ORDER BY t.Nombre;

PRINT 'Uso outbound del tenant en la ventana UTC indicada';
SELECT
    logs.TenantId,
    COUNT(*) AS TodayUsage
FROM WhatsAppMessageLogs AS logs
WHERE logs.TenantId = @TenantId
  AND logs.Direction = N'Outbound'
  AND logs.NotificationType IN (N'Confirmation', N'Reminder3Hours')
  AND logs.Status IN (N'Pending', N'Processing', N'Sent')
  AND logs.CreatedAtUtc >= @DayStartUtc
  AND logs.CreatedAtUtc < @DayEndUtc
GROUP BY logs.TenantId;

PRINT 'Ultimos errores u omisiones del tenant';
SELECT TOP (20)
    logs.TenantId,
    logs.CitaId,
    logs.NotificationType,
    logs.Status,
    logs.ErrorCode,
    logs.ErrorMessage,
    logs.CreatedAtUtc
FROM WhatsAppMessageLogs AS logs
WHERE logs.TenantId = @TenantId
  AND logs.Direction = N'Outbound'
  AND logs.ErrorCode IS NOT NULL
ORDER BY logs.CreatedAtUtc DESC;

PRINT 'Predicados RLS de TenantWhatsAppSettings';
SELECT
    policy.name AS PolicyName,
    predicate.type_desc AS PredicateType,
    predicate.predicate_definition AS PredicateDefinition
FROM sys.security_predicates AS predicate
INNER JOIN sys.security_policies AS policy
    ON policy.object_id = predicate.object_id
WHERE predicate.target_object_id = OBJECT_ID(N'[dbo].[TenantWhatsAppSettings]');
