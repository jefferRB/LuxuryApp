BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Clientes] ADD [AceptaMensajesWhatsApp] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Clientes] ADD [WhatsAppConsentCapturedByUserId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Clientes] ADD [WhatsAppConsentSource] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Clientes] ADD [WhatsAppConsentTextVersion] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Clientes] ADD [WhatsAppConsentUpdatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Citas] ADD [ClienteId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Citas] ADD [WhatsAppConsentAtCreation] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Citas] ADD [WhatsAppConsentCapturedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Citas] ADD [WhatsAppConsentSource] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    CREATE INDEX [IX_Citas_ClienteId] ON [Citas] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    ALTER TABLE [Citas] ADD CONSTRAINT [FK_Citas_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603203751_AddWhatsAppConsentOptIn'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603203751_AddWhatsAppConsentOptIn', N'10.0.2');
END;

COMMIT;
GO

