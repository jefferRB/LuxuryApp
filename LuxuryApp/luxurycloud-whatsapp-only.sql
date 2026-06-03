BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [CanceladaPorWhatsAppUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [ConfirmacionWhatsAppEnviadaUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [ConfirmadaPorWhatsAppUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [EstadoConfirmacionWhatsApp] nvarchar(30) NOT NULL DEFAULT N'Pendiente';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [RecordatorioWhatsAppTresHorasEnviadoUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [UltimaRespuestaWhatsAppUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    ALTER TABLE [Citas] ADD [UltimoMetaMessageId] nvarchar(128) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE TABLE [WhatsAppMessageLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CitaId] int NULL,
        [Direction] nvarchar(20) NOT NULL,
        [NotificationType] nvarchar(40) NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [MetaMessageId] nvarchar(128) NULL,
        [ContextMessageId] nvarchar(128) NULL,
        [RecipientPhoneE164] nvarchar(32) NULL,
        [SenderPhoneE164] nvarchar(32) NULL,
        [WaId] nvarchar(64) NULL,
        [TemplateName] nvarchar(128) NULL,
        [PayloadJson] nvarchar(max) NULL,
        [Status] nvarchar(30) NOT NULL,
        [ErrorCode] nvarchar(80) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [AttemptCount] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [SentAtUtc] datetime2 NULL,
        [DeliveredAtUtc] datetime2 NULL,
        [ReadAtUtc] datetime2 NULL,
        [FailedAtUtc] datetime2 NULL,
        [ProcessedAtUtc] datetime2 NULL,
        [ProcessingStartedAtUtc] datetime2 NULL,
        [LastAttemptAtUtc] datetime2 NULL,
        [NextAttemptAtUtc] datetime2 NULL,
        CONSTRAINT [PK_WhatsAppMessageLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WhatsAppMessageLogs_Citas_CitaId] FOREIGN KEY ([CitaId]) REFERENCES [Citas] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_CitaId] ON [WhatsAppMessageLogs] ([CitaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_ContextMessageId] ON [WhatsAppMessageLogs] ([ContextMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_TenantId] ON [WhatsAppMessageLogs] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_TenantId_CitaId] ON [WhatsAppMessageLogs] ([TenantId], [CitaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_TenantId_CreatedAtUtc] ON [WhatsAppMessageLogs] ([TenantId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_TenantId_NotificationType_Status] ON [WhatsAppMessageLogs] ([TenantId], [NotificationType], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessageLogs_TenantId_RecipientPhone_CreatedAtUtc] ON [WhatsAppMessageLogs] ([TenantId], [RecipientPhoneE164], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WhatsAppMessageLogs_MetaMessageId] ON [WhatsAppMessageLogs] ([MetaMessageId]) WHERE [MetaMessageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    IF OBJECT_ID(N'[dbo].[fnTenantAccess]') IS NOT NULL
       AND OBJECT_ID(N'[dbo].[WhatsAppMessageLogs]', N'U') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.security_predicates
           WHERE target_object_id = OBJECT_ID(N'[dbo].[WhatsAppMessageLogs]', N'U')
       )
    BEGIN
        DECLARE @policySchema sysname;
        DECLARE @policyName sysname;
        DECLARE @qualifiedPolicy nvarchar(300);
        DECLARE @sql nvarchar(max);
        DECLARE @wasEnabled bit;

        SELECT TOP (1)
            @policySchema = SCHEMA_NAME(policy.schema_id),
            @policyName = policy.name,
            @wasEnabled = policy.is_enabled
        FROM sys.security_policies AS policy
        INNER JOIN sys.security_predicates AS predicate
            ON predicate.object_id = policy.object_id
        WHERE predicate.predicate_definition LIKE N'%fnTenantAccess%'
        ORDER BY policy.name;

        IF @policyName IS NOT NULL
        BEGIN
            SET @qualifiedPolicy = QUOTENAME(@policySchema) + N'.' + QUOTENAME(@policyName);

            IF @wasEnabled = 1
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N' WITH (STATE = OFF);';
                EXEC sp_executesql @sql;
            END

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD FILTER PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[WhatsAppMessageLogs];';
            EXEC sp_executesql @sql;

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[WhatsAppMessageLogs] AFTER INSERT;';
            EXEC sp_executesql @sql;

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[WhatsAppMessageLogs] AFTER UPDATE;';
            EXEC sp_executesql @sql;

            IF @wasEnabled = 1
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N' WITH (STATE = ON);';
                EXEC sp_executesql @sql;
            END
        END
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528190337_AddMetaWhatsAppNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260528190337_AddMetaWhatsAppNotifications', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    CREATE TABLE [TenantWhatsAppSettings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(0 AS bit),
        [SendConfirmationOnCreate] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SendReminderThreeHoursBefore] bit NOT NULL DEFAULT CAST(1 AS bit),
        [DailyMessageLimit] int NOT NULL DEFAULT 30,
        [TimeZoneId] nvarchar(100) NOT NULL DEFAULT N'America/Costa_Rica',
        [Notes] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [UpdatedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_TenantWhatsAppSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantWhatsAppSettings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WhatsAppMessageLogs_ActiveOutboundNotification] ON [WhatsAppMessageLogs] ([TenantId], [CitaId], [NotificationType], [Direction]) WHERE [Direction] = ''Outbound'' AND [CitaId] IS NOT NULL AND [Status] IN (''Pending'', ''Processing'', ''Sent'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    CREATE INDEX [IX_TenantWhatsAppSettings_TenantId_IsEnabled] ON [TenantWhatsAppSettings] ([TenantId], [IsEnabled]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TenantWhatsAppSettings_TenantId] ON [TenantWhatsAppSettings] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    IF OBJECT_ID(N'[dbo].[fnTenantAccess]') IS NOT NULL
       AND OBJECT_ID(N'[dbo].[TenantWhatsAppSettings]', N'U') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.security_predicates
           WHERE target_object_id = OBJECT_ID(N'[dbo].[TenantWhatsAppSettings]', N'U')
       )
    BEGIN
        DECLARE @policySchema sysname;
        DECLARE @policyName sysname;
        DECLARE @qualifiedPolicy nvarchar(300);
        DECLARE @sql nvarchar(max);
        DECLARE @wasEnabled bit;

        SELECT TOP (1)
            @policySchema = SCHEMA_NAME(policy.schema_id),
            @policyName = policy.name,
            @wasEnabled = policy.is_enabled
        FROM sys.security_policies AS policy
        INNER JOIN sys.security_predicates AS predicate
            ON predicate.object_id = policy.object_id
        WHERE predicate.predicate_definition LIKE N'%fnTenantAccess%'
        ORDER BY policy.name;

        IF @policyName IS NOT NULL
        BEGIN
            SET @qualifiedPolicy = QUOTENAME(@policySchema) + N'.' + QUOTENAME(@policyName);

            IF @wasEnabled = 1
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N' WITH (STATE = OFF);';
                EXEC sp_executesql @sql;
            END

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD FILTER PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[TenantWhatsAppSettings];';
            EXEC sp_executesql @sql;

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[TenantWhatsAppSettings] AFTER INSERT;';
            EXEC sp_executesql @sql;

            SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N'
                ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId])
                ON [dbo].[TenantWhatsAppSettings] AFTER UPDATE;';
            EXEC sp_executesql @sql;

            IF @wasEnabled = 1
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy + N' WITH (STATE = ON);';
                EXEC sp_executesql @sql;
            END
        END
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260601195214_AddTenantWhatsAppSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260601195214_AddTenantWhatsAppSettings', N'10.0.2');
END;

COMMIT;
GO

