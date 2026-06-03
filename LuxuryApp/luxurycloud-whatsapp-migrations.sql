IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121200257_creacionInicialIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260121200257_creacionInicialIdentity', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121201933_camposUsuarios'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [Discriminator] nvarchar(13) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121201933_camposUsuarios'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [Name] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121201933_camposUsuarios'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [State] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260121201933_camposUsuarios'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260121201933_camposUsuarios', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[Tenants]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Tenants](
            [Id] uniqueidentifier NOT NULL,
            [Nombre] nvarchar(150) NOT NULL,
            [FechaCreacion] datetime2 NOT NULL,
            [Activo] bit NOT NULL,
            CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
        );
    END
    ELSE
    BEGIN
        UPDATE [Tenants] SET [FechaCreacion] = SYSUTCDATETIME() WHERE [FechaCreacion] IS NULL;
        UPDATE [Tenants] SET [Activo] = 1 WHERE [Activo] IS NULL;
        ALTER TABLE [Tenants] ALTER COLUMN [Nombre] nvarchar(150) NOT NULL;
        ALTER TABLE [Tenants] ALTER COLUMN [FechaCreacion] datetime2 NOT NULL;
        ALTER TABLE [Tenants] ALTER COLUMN [Activo] bit NOT NULL;
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[Features]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Features](
            [Id] uniqueidentifier NOT NULL,
            [Codigo] nvarchar(max) NOT NULL,
            [Nombre] nvarchar(max) NOT NULL,
            CONSTRAINT [PK_Features] PRIMARY KEY ([Id])
        );
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[Planes]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Planes](
            [Id] uniqueidentifier NOT NULL,
            [Nombre] nvarchar(50) NOT NULL,
            [ProviderProductId] nvarchar(100) NULL,
            [ProviderPriceId] nvarchar(100) NULL,
            [Moneda] nvarchar(100) NOT NULL,
            [PrecioMensual] decimal(18,2) NOT NULL,
            [Activo] bit NOT NULL,
            [MaxFuncionarios] int NULL,
            CONSTRAINT [PK_Planes] PRIMARY KEY ([Id])
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'[Planes]', 'StripeProductId') IS NOT NULL AND COL_LENGTH(N'[Planes]', 'ProviderProductId') IS NULL
            EXEC sp_rename N'[Planes].[StripeProductId]', N'ProviderProductId', N'COLUMN';

        IF COL_LENGTH(N'[Planes]', 'StripePriceId') IS NOT NULL AND COL_LENGTH(N'[Planes]', 'ProviderPriceId') IS NULL
            EXEC sp_rename N'[Planes].[StripePriceId]', N'ProviderPriceId', N'COLUMN';

        IF COL_LENGTH(N'[Planes]', 'ProviderProductId') IS NULL
            ALTER TABLE [Planes] ADD [ProviderProductId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Planes]', 'ProviderPriceId') IS NULL
            ALTER TABLE [Planes] ADD [ProviderPriceId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Planes]', 'MaxFuncionarios') IS NULL
            ALTER TABLE [Planes] ADD [MaxFuncionarios] int NULL;

        UPDATE [Planes] SET [Moneda] = 'CRC' WHERE [Moneda] IS NULL;
        UPDATE [Planes] SET [Activo] = 1 WHERE [Activo] IS NULL;

        ALTER TABLE [Planes] ALTER COLUMN [ProviderProductId] nvarchar(100) NULL;
        ALTER TABLE [Planes] ALTER COLUMN [ProviderPriceId] nvarchar(100) NULL;
        ALTER TABLE [Planes] ALTER COLUMN [Moneda] nvarchar(100) NOT NULL;
        ALTER TABLE [Planes] ALTER COLUMN [Activo] bit NOT NULL;
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[PlanFeatures]', N'U') IS NULL
    BEGIN
        CREATE TABLE [PlanFeatures](
            [PlanId] uniqueidentifier NOT NULL,
            [FeatureId] uniqueidentifier NOT NULL,
            [Limite] int NULL,
            CONSTRAINT [PK_PlanFeatures] PRIMARY KEY ([PlanId], [FeatureId]),
            CONSTRAINT [FK_PlanFeatures_Features_FeatureId] FOREIGN KEY ([FeatureId]) REFERENCES [Features]([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_PlanFeatures_Planes_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Planes]([Id]) ON DELETE CASCADE
        );
    END;

    IF OBJECT_ID(N'[PlanFeatures]', N'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PlanFeatures]')
          AND name = N'IX_PlanFeatures_FeatureId')
    BEGIN
        CREATE INDEX [IX_PlanFeatures_FeatureId] ON [PlanFeatures]([FeatureId]);
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF COL_LENGTH(N'[AspNetUsers]', 'TenantId') IS NULL
    BEGIN
        ALTER TABLE [AspNetUsers]
        ADD [TenantId] uniqueidentifier NOT NULL
            CONSTRAINT [DF_AspNetUsers_TenantId] DEFAULT ('00000000-0000-0000-0000-000000000000');
    END;

    UPDATE [AspNetUsers] SET [State] = 0 WHERE [State] IS NULL;
    ALTER TABLE [AspNetUsers] ALTER COLUMN [State] bit NOT NULL;

    IF COL_LENGTH(N'[AspNetUsers]', 'Discriminator') IS NOT NULL
    BEGIN
        DECLARE @DropDiscriminatorDefault nvarchar(max);
        SELECT @DropDiscriminatorDefault =
            N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT [' + dc.name + N'];'
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON dc.parent_object_id = c.object_id
           AND dc.parent_column_id = c.column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'[AspNetUsers]')
          AND c.name = N'Discriminator';

        IF @DropDiscriminatorDefault IS NOT NULL
            EXEC sp_executesql @DropDiscriminatorDefault;

        ALTER TABLE [AspNetUsers] DROP COLUMN [Discriminator];
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[AspNetUsers]')
          AND name = N'IX_AspNetUsers_TenantId')
    BEGIN
        CREATE INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers]([TenantId]);
    END;

    IF EXISTS (
        SELECT 1
        FROM [AspNetUsers] u
        LEFT JOIN [Tenants] t ON u.[TenantId] = t.[Id]
        WHERE u.[TenantId] <> '00000000-0000-0000-0000-000000000000'
          AND t.[Id] IS NULL)
    BEGIN
        THROW 51000, 'No se puede crear la FK AspNetUsers->Tenants porque existen usuarios con TenantId inexistente.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_AspNetUsers_Tenants_TenantId')
    BEGIN
        ALTER TABLE [AspNetUsers]
        ADD CONSTRAINT [FK_AspNetUsers_Tenants_TenantId]
            FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE;
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[Suscripciones]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Suscripciones](
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [PlanId] uniqueidentifier NOT NULL,
            [Proveedor] int NOT NULL,
            [ProviderCustomerId] nvarchar(100) NULL,
            [ProviderSubscriptionId] nvarchar(100) NULL,
            [ProviderTransactionId] nvarchar(100) NULL,
            [ProviderPaymentLinkId] nvarchar(100) NULL,
            [ProviderReference] nvarchar(100) NULL,
            [UltimoEventoProveedorId] nvarchar(100) NULL,
            [Estado] int NOT NULL,
            [FechaInicio] datetime2 NOT NULL,
            [FechaFin] datetime2 NULL,
            [FechaTrialFin] datetime2 NULL,
            [CancelAtPeriodEnd] bit NOT NULL,
            [FechaUltimoPagoUtc] datetime2 NULL,
            [FechaUltimaActualizacionUtc] datetime2 NULL,
            [MotivoEstado] nvarchar(250) NULL,
            CONSTRAINT [PK_Suscripciones] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Suscripciones_Planes_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Planes]([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_Suscripciones_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'[Suscripciones]', 'StripeCustomerId') IS NOT NULL AND COL_LENGTH(N'[Suscripciones]', 'ProviderCustomerId') IS NULL
            EXEC sp_rename N'[Suscripciones].[StripeCustomerId]', N'ProviderCustomerId', N'COLUMN';

        IF COL_LENGTH(N'[Suscripciones]', 'StripeSubscriptionId') IS NOT NULL AND COL_LENGTH(N'[Suscripciones]', 'ProviderSubscriptionId') IS NULL
            EXEC sp_rename N'[Suscripciones].[StripeSubscriptionId]', N'ProviderSubscriptionId', N'COLUMN';

        IF COL_LENGTH(N'[Suscripciones]', 'Proveedor') IS NULL
            ALTER TABLE [Suscripciones] ADD [Proveedor] int NOT NULL CONSTRAINT [DF_Suscripciones_Proveedor] DEFAULT (1);

        IF COL_LENGTH(N'[Suscripciones]', 'ProviderCustomerId') IS NULL
            ALTER TABLE [Suscripciones] ADD [ProviderCustomerId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'ProviderSubscriptionId') IS NULL
            ALTER TABLE [Suscripciones] ADD [ProviderSubscriptionId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'ProviderTransactionId') IS NULL
            ALTER TABLE [Suscripciones] ADD [ProviderTransactionId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'ProviderPaymentLinkId') IS NULL
            ALTER TABLE [Suscripciones] ADD [ProviderPaymentLinkId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'ProviderReference') IS NULL
            ALTER TABLE [Suscripciones] ADD [ProviderReference] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'UltimoEventoProveedorId') IS NULL
            ALTER TABLE [Suscripciones] ADD [UltimoEventoProveedorId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'FechaUltimoPagoUtc') IS NULL
            ALTER TABLE [Suscripciones] ADD [FechaUltimoPagoUtc] datetime2 NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'FechaUltimaActualizacionUtc') IS NULL
            ALTER TABLE [Suscripciones] ADD [FechaUltimaActualizacionUtc] datetime2 NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'MotivoEstado') IS NULL
            ALTER TABLE [Suscripciones] ADD [MotivoEstado] nvarchar(250) NULL;

        IF COL_LENGTH(N'[Suscripciones]', 'Proveedor') IS NOT NULL
            EXEC(N'UPDATE [Suscripciones] SET [Proveedor] = 1 WHERE [Proveedor] = 0;');
        UPDATE [Suscripciones] SET [CancelAtPeriodEnd] = 0 WHERE [CancelAtPeriodEnd] IS NULL;

        ALTER TABLE [Suscripciones] ALTER COLUMN [CancelAtPeriodEnd] bit NOT NULL;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Suscripciones]')
          AND name = N'IX_Suscripciones_TenantId'
          AND is_unique = 0)
    BEGIN
        DROP INDEX [IX_Suscripciones_TenantId] ON [Suscripciones];
    END;

    IF EXISTS (
        SELECT [TenantId]
        FROM [Suscripciones]
        GROUP BY [TenantId]
        HAVING COUNT(*) > 1)
    BEGIN
        THROW 51001, 'No se puede crear la unicidad por tenant en Suscripciones porque existen multiples filas para el mismo tenant.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Suscripciones]')
          AND name = N'IX_Suscripciones_TenantId')
    BEGIN
        CREATE UNIQUE INDEX [IX_Suscripciones_TenantId] ON [Suscripciones]([TenantId]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Suscripciones]')
          AND name = N'IX_Suscripciones_PlanId')
    BEGIN
        CREATE INDEX [IX_Suscripciones_PlanId] ON [Suscripciones]([PlanId]);
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[HistorialSuscripciones]', N'U') IS NULL
    BEGIN
        CREATE TABLE [HistorialSuscripciones](
            [Id] uniqueidentifier NOT NULL,
            [SuscripcionId] uniqueidentifier NOT NULL,
            [PlanIdAnterior] uniqueidentifier NULL,
            [PlanIdNuevo] uniqueidentifier NULL,
            [FechaCambio] datetime2 NOT NULL,
            [Proveedor] int NULL,
            [Motivo] nvarchar(250) NULL,
            CONSTRAINT [PK_HistorialSuscripciones] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_HistorialSuscripciones_Suscripciones_SuscripcionId] FOREIGN KEY ([SuscripcionId]) REFERENCES [Suscripciones]([Id]) ON DELETE CASCADE
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'[HistorialSuscripciones]', 'Proveedor') IS NULL
            ALTER TABLE [HistorialSuscripciones] ADD [Proveedor] int NULL;

        IF COL_LENGTH(N'[HistorialSuscripciones]', 'Motivo') IS NULL
            ALTER TABLE [HistorialSuscripciones] ADD [Motivo] nvarchar(250) NULL;

        UPDATE [HistorialSuscripciones] SET [FechaCambio] = SYSUTCDATETIME() WHERE [FechaCambio] IS NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[HistorialSuscripciones]')
          AND name = N'IX_HistorialSuscripciones_SuscripcionId')
    BEGIN
        CREATE INDEX [IX_HistorialSuscripciones_SuscripcionId] ON [HistorialSuscripciones]([SuscripcionId]);
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[StripeEventos]', N'U') IS NOT NULL AND OBJECT_ID(N'[EventosPago]', N'U') IS NULL
    BEGIN
        EXEC sp_rename N'[StripeEventos]', N'EventosPago';
    END;

    IF OBJECT_ID(N'[EventosPago]', N'U') IS NULL
    BEGIN
        CREATE TABLE [EventosPago](
            [Id] uniqueidentifier NOT NULL,
            [Proveedor] int NOT NULL,
            [TenantId] uniqueidentifier NULL,
            [PlanId] uniqueidentifier NULL,
            [PagoSuscripcionId] uniqueidentifier NULL,
            [ProveedorEventId] nvarchar(100) NOT NULL,
            [Tipo] nvarchar(100) NOT NULL,
            [ReferenciaExterna] nvarchar(100) NULL,
            [ProviderTransactionId] nvarchar(100) NULL,
            [CorrelationId] nvarchar(100) NULL,
            [Procesado] bit NOT NULL,
            [EstadoProcesamiento] nvarchar(50) NOT NULL,
            [Payload] nvarchar(max) NOT NULL,
            [FechaRecepcionUtc] datetime2 NOT NULL,
            [FechaProcesamientoUtc] datetime2 NULL,
            [Error] nvarchar(500) NULL,
            CONSTRAINT [PK_EventosPago] PRIMARY KEY ([Id])
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'[EventosPago]', 'StripeEventId') IS NOT NULL AND COL_LENGTH(N'[EventosPago]', 'ProveedorEventId') IS NULL
            EXEC sp_rename N'[EventosPago].[StripeEventId]', N'ProveedorEventId', N'COLUMN';

        IF COL_LENGTH(N'[EventosPago]', 'Proveedor') IS NULL
            ALTER TABLE [EventosPago] ADD [Proveedor] int NOT NULL CONSTRAINT [DF_EventosPago_Proveedor] DEFAULT (1);

        IF COL_LENGTH(N'[EventosPago]', 'TenantId') IS NULL
            ALTER TABLE [EventosPago] ADD [TenantId] uniqueidentifier NULL;

        IF COL_LENGTH(N'[EventosPago]', 'PlanId') IS NULL
            ALTER TABLE [EventosPago] ADD [PlanId] uniqueidentifier NULL;

        IF COL_LENGTH(N'[EventosPago]', 'PagoSuscripcionId') IS NULL
            ALTER TABLE [EventosPago] ADD [PagoSuscripcionId] uniqueidentifier NULL;

        IF COL_LENGTH(N'[EventosPago]', 'ReferenciaExterna') IS NULL
            ALTER TABLE [EventosPago] ADD [ReferenciaExterna] nvarchar(100) NULL;

        IF COL_LENGTH(N'[EventosPago]', 'ProviderTransactionId') IS NULL
            ALTER TABLE [EventosPago] ADD [ProviderTransactionId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[EventosPago]', 'CorrelationId') IS NULL
            ALTER TABLE [EventosPago] ADD [CorrelationId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[EventosPago]', 'EstadoProcesamiento') IS NULL
            ALTER TABLE [EventosPago] ADD [EstadoProcesamiento] nvarchar(50) NOT NULL CONSTRAINT [DF_EventosPago_EstadoProcesamiento] DEFAULT ('Pendiente');

        IF COL_LENGTH(N'[EventosPago]', 'FechaRecepcionUtc') IS NULL
            ALTER TABLE [EventosPago] ADD [FechaRecepcionUtc] datetime2 NOT NULL CONSTRAINT [DF_EventosPago_FechaRecepcionUtc] DEFAULT (SYSUTCDATETIME());

        IF COL_LENGTH(N'[EventosPago]', 'FechaProcesamientoUtc') IS NULL
            ALTER TABLE [EventosPago] ADD [FechaProcesamientoUtc] datetime2 NULL;

        IF COL_LENGTH(N'[EventosPago]', 'Error') IS NULL
            ALTER TABLE [EventosPago] ADD [Error] nvarchar(500) NULL;

        IF COL_LENGTH(N'[EventosPago]', 'EstadoProcesamiento') IS NOT NULL
            EXEC(N'UPDATE [EventosPago]
                  SET [EstadoProcesamiento] = CASE WHEN [Procesado] = 1 THEN ''Procesado'' ELSE ''Recibido'' END
                  WHERE [EstadoProcesamiento] IS NULL OR [EstadoProcesamiento] = '''';');

        IF COL_LENGTH(N'[EventosPago]', 'Fecha') IS NOT NULL
        BEGIN
            EXEC(N'UPDATE [EventosPago] SET [FechaRecepcionUtc] = [Fecha] WHERE [FechaRecepcionUtc] IS NULL;');
            EXEC(N'UPDATE [EventosPago] SET [FechaProcesamientoUtc] = [Fecha] WHERE [Procesado] = 1 AND [FechaProcesamientoUtc] IS NULL;');
            DECLARE @DropFechaDefault nvarchar(max);
            SELECT @DropFechaDefault =
                N'ALTER TABLE [EventosPago] DROP CONSTRAINT [' + dc.name + N'];'
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c
                ON dc.parent_object_id = c.object_id
               AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[EventosPago]')
              AND c.name = N'Fecha';

            IF @DropFechaDefault IS NOT NULL
                EXEC sp_executesql @DropFechaDefault;

            ALTER TABLE [EventosPago] DROP COLUMN [Fecha];
        END;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[EventosPago]')
          AND name = N'IX_EventosPago_Proveedor_ProveedorEventId')
    BEGIN
        CREATE UNIQUE INDEX [IX_EventosPago_Proveedor_ProveedorEventId]
            ON [EventosPago]([Proveedor], [ProveedorEventId]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[EventosPago]')
          AND name = N'IX_EventosPago_Proveedor_ReferenciaExterna')
    BEGIN
        CREATE INDEX [IX_EventosPago_Proveedor_ReferenciaExterna]
            ON [EventosPago]([Proveedor], [ReferenciaExterna]);
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[PagosSuscripcion]', N'U') IS NULL
    BEGIN
        CREATE TABLE [PagosSuscripcion](
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [PlanId] uniqueidentifier NOT NULL,
            [Proveedor] int NOT NULL,
            [Estado] int NOT NULL,
            [ReferenciaInterna] nvarchar(100) NOT NULL,
            [ProviderCheckoutId] nvarchar(100) NULL,
            [ProviderTransactionId] nvarchar(100) NULL,
            [ProviderReference] nvarchar(100) NULL,
            [ProviderResultCode] nvarchar(50) NULL,
            [ProviderResultMessage] nvarchar(300) NULL,
            [ProviderAuthorizationCode] nvarchar(100) NULL,
            [ProviderCardBrand] nvarchar(50) NULL,
            [ProviderCardLast4] nvarchar(20) NULL,
            [CheckoutUrl] nvarchar(500) NULL,
            [ClienteNombre] nvarchar(150) NULL,
            [ClienteEmail] nvarchar(200) NULL,
            [Descripcion] nvarchar(250) NOT NULL,
            [Monto] decimal(18,2) NOT NULL,
            [Moneda] nvarchar(10) NOT NULL,
            [FechaCreacionUtc] datetime2 NOT NULL,
            [FechaActualizacionUtc] datetime2 NULL,
            [FechaConfirmacionUtc] datetime2 NULL,
            [UltimoPayloadProveedor] nvarchar(max) NULL,
            CONSTRAINT [PK_PagosSuscripcion] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_PagosSuscripcion_Planes_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Planes]([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_PagosSuscripcion_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_TenantId')
    BEGIN
        CREATE INDEX [IX_PagosSuscripcion_TenantId] ON [PagosSuscripcion]([TenantId]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_PlanId')
    BEGIN
        CREATE INDEX [IX_PagosSuscripcion_PlanId] ON [PagosSuscripcion]([PlanId]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_Proveedor_ReferenciaInterna')
    BEGIN
        CREATE UNIQUE INDEX [IX_PagosSuscripcion_Proveedor_ReferenciaInterna]
            ON [PagosSuscripcion]([Proveedor], [ReferenciaInterna]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_Proveedor_ProviderReference')
    BEGIN
        CREATE UNIQUE INDEX [IX_PagosSuscripcion_Proveedor_ProviderReference]
            ON [PagosSuscripcion]([Proveedor], [ProviderReference])
            WHERE [ProviderReference] IS NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_Proveedor_ProviderTransactionId')
    BEGIN
        CREATE UNIQUE INDEX [IX_PagosSuscripcion_Proveedor_ProviderTransactionId]
            ON [PagosSuscripcion]([Proveedor], [ProviderTransactionId])
            WHERE [ProviderTransactionId] IS NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[PagosSuscripcion]')
          AND name = N'IX_PagosSuscripcion_Proveedor_ProviderCheckoutId')
    BEGIN
        CREATE UNIQUE INDEX [IX_PagosSuscripcion_Proveedor_ProviderCheckoutId]
            ON [PagosSuscripcion]([Proveedor], [ProviderCheckoutId])
            WHERE [ProviderCheckoutId] IS NOT NULL;
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN

    IF OBJECT_ID(N'[Facturas]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Facturas](
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [SuscripcionId] uniqueidentifier NULL,
            [PagoSuscripcionId] uniqueidentifier NULL,
            [Proveedor] int NOT NULL,
            [ProviderInvoiceId] nvarchar(100) NULL,
            [ProviderTransactionId] nvarchar(100) NULL,
            [ProviderReference] nvarchar(100) NULL,
            [Monto] decimal(18,2) NULL,
            [Moneda] nvarchar(10) NOT NULL,
            [Estado] nvarchar(50) NOT NULL,
            [Fecha] datetime2 NULL,
            CONSTRAINT [PK_Facturas] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Facturas_PagosSuscripcion_PagoSuscripcionId] FOREIGN KEY ([PagoSuscripcionId]) REFERENCES [PagosSuscripcion]([Id]),
            CONSTRAINT [FK_Facturas_Suscripciones_SuscripcionId] FOREIGN KEY ([SuscripcionId]) REFERENCES [Suscripciones]([Id]),
            CONSTRAINT [FK_Facturas_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE
        );
    END
    ELSE
    BEGIN
        IF COL_LENGTH(N'[Facturas]', 'StripeInvoiceId') IS NOT NULL AND COL_LENGTH(N'[Facturas]', 'ProviderInvoiceId') IS NULL
            EXEC sp_rename N'[Facturas].[StripeInvoiceId]', N'ProviderInvoiceId', N'COLUMN';

        IF COL_LENGTH(N'[Facturas]', 'SuscripcionId') IS NULL
            ALTER TABLE [Facturas] ADD [SuscripcionId] uniqueidentifier NULL;

        IF COL_LENGTH(N'[Facturas]', 'PagoSuscripcionId') IS NULL
            ALTER TABLE [Facturas] ADD [PagoSuscripcionId] uniqueidentifier NULL;

        IF COL_LENGTH(N'[Facturas]', 'Proveedor') IS NULL
            ALTER TABLE [Facturas] ADD [Proveedor] int NOT NULL CONSTRAINT [DF_Facturas_Proveedor] DEFAULT (1);

        IF COL_LENGTH(N'[Facturas]', 'ProviderInvoiceId') IS NULL
            ALTER TABLE [Facturas] ADD [ProviderInvoiceId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Facturas]', 'ProviderTransactionId') IS NULL
            ALTER TABLE [Facturas] ADD [ProviderTransactionId] nvarchar(100) NULL;

        IF COL_LENGTH(N'[Facturas]', 'ProviderReference') IS NULL
            ALTER TABLE [Facturas] ADD [ProviderReference] nvarchar(100) NULL;

        UPDATE [Facturas] SET [Moneda] = 'CRC' WHERE [Moneda] IS NULL;
        UPDATE [Facturas] SET [Estado] = 'Desconocido' WHERE [Estado] IS NULL;
        IF COL_LENGTH(N'[Facturas]', 'Proveedor') IS NOT NULL
            EXEC(N'UPDATE [Facturas] SET [Proveedor] = 1 WHERE [Proveedor] = 0;');

        ALTER TABLE [Facturas] ALTER COLUMN [Moneda] nvarchar(10) NOT NULL;
        ALTER TABLE [Facturas] ALTER COLUMN [Estado] nvarchar(50) NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Facturas]')
          AND name = N'IX_Facturas_TenantId')
    BEGIN
        CREATE INDEX [IX_Facturas_TenantId] ON [Facturas]([TenantId]);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Facturas]')
          AND name = N'IX_Facturas_SuscripcionId')
    BEGIN
        EXEC(N'CREATE INDEX [IX_Facturas_SuscripcionId] ON [Facturas]([SuscripcionId]);');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Facturas]')
          AND name = N'IX_Facturas_PagoSuscripcionId')
    BEGIN
        EXEC(N'CREATE INDEX [IX_Facturas_PagoSuscripcionId] ON [Facturas]([PagoSuscripcionId]);');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Facturas]')
          AND name = N'IX_Facturas_Proveedor_ProviderTransactionId')
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX [IX_Facturas_Proveedor_ProviderTransactionId]
                ON [Facturas]([Proveedor], [ProviderTransactionId])
                WHERE [ProviderTransactionId] IS NOT NULL;');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Facturas]')
          AND name = N'IX_Facturas_Proveedor_ProviderReference_Estado')
    BEGIN
        EXEC(N'CREATE INDEX [IX_Facturas_Proveedor_ProviderReference_Estado]
                ON [Facturas]([Proveedor], [ProviderReference], [Estado])
                WHERE [ProviderReference] IS NOT NULL;');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_Facturas_Suscripciones_SuscripcionId')
    BEGIN
        EXEC(N'ALTER TABLE [Facturas]
               ADD CONSTRAINT [FK_Facturas_Suscripciones_SuscripcionId]
                   FOREIGN KEY ([SuscripcionId]) REFERENCES [Suscripciones]([Id]);');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_Facturas_PagosSuscripcion_PagoSuscripcionId')
    BEGIN
        EXEC(N'ALTER TABLE [Facturas]
               ADD CONSTRAINT [FK_Facturas_PagosSuscripcion_PagoSuscripcionId]
                   FOREIGN KEY ([PagoSuscripcionId]) REFERENCES [PagosSuscripcion]([Id]);');
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410011807_saasPaymentsTilopay'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260410011807_saasPaymentsTilopay', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410015617_funcionarioPuestoRelationshipFix'
)
BEGIN

    IF OBJECT_ID(N'[Funcionarios]', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.foreign_keys
            WHERE name = N'FK_Funcionarios_Puestos_PuestoIdPuesto')
        BEGIN
            ALTER TABLE [Funcionarios] DROP CONSTRAINT [FK_Funcionarios_Puestos_PuestoIdPuesto];
        END;

        IF EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[Funcionarios]')
              AND name = N'IX_Funcionarios_PuestoIdPuesto')
        BEGIN
            DROP INDEX [IX_Funcionarios_PuestoIdPuesto] ON [Funcionarios];
        END;

        IF COL_LENGTH(N'[Funcionarios]', 'PuestoIdPuesto') IS NOT NULL
        BEGIN
            ALTER TABLE [Funcionarios] DROP COLUMN [PuestoIdPuesto];
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[Funcionarios]')
              AND name = N'IX_Funcionarios_IdPuesto')
        BEGIN
            CREATE INDEX [IX_Funcionarios_IdPuesto] ON [Funcionarios]([IdPuesto]);
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys
            WHERE name = N'FK_Funcionarios_Puestos_IdPuesto')
        BEGIN
            ALTER TABLE [Funcionarios]
            ADD CONSTRAINT [FK_Funcionarios_Puestos_IdPuesto]
                FOREIGN KEY ([IdPuesto]) REFERENCES [Puestos]([IdPuesto]) ON DELETE CASCADE;
        END;
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410015617_funcionarioPuestoRelationshipFix'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260410015617_funcionarioPuestoRelationshipFix', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410233059_addValidationPlan'
)
BEGIN
    ALTER TABLE [Planes] ADD [EsPlanValidacion] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410233059_addValidationPlan'
)
BEGIN
    DECLARE @ValidationPlanId uniqueidentifier = 'B3D5C5F0-41AE-4A64-9D04-3A70B6D4F001';
    DECLARE @ValidationPlanName nvarchar(50) = N'Prueba Tilopay';

    IF EXISTS (SELECT 1 FROM [Planes] WHERE [Id] = @ValidationPlanId)
    BEGIN
        UPDATE [Planes]
        SET [Nombre] = @ValidationPlanName,
            [Moneda] = N'CRC',
            [PrecioMensual] = CAST(1000.00 AS decimal(18,2)),
            [Activo] = CAST(1 AS bit),
            [EsPlanValidacion] = CAST(1 AS bit),
            [MaxFuncionarios] = 1
        WHERE [Id] = @ValidationPlanId;
    END
    ELSE IF EXISTS (SELECT 1 FROM [Planes] WHERE [Nombre] = @ValidationPlanName)
    BEGIN
        UPDATE [Planes]
        SET [Moneda] = N'CRC',
            [PrecioMensual] = CAST(1000.00 AS decimal(18,2)),
            [Activo] = CAST(1 AS bit),
            [EsPlanValidacion] = CAST(1 AS bit),
            [MaxFuncionarios] = 1
        WHERE [Nombre] = @ValidationPlanName;
    END
    ELSE
    BEGIN
        INSERT INTO [Planes] (
            [Id],
            [Nombre],
            [ProviderProductId],
            [ProviderPriceId],
            [Moneda],
            [PrecioMensual],
            [Activo],
            [EsPlanValidacion],
            [MaxFuncionarios]
        )
        VALUES (
            @ValidationPlanId,
            @ValidationPlanName,
            NULL,
            NULL,
            N'CRC',
            CAST(1000.00 AS decimal(18,2)),
            CAST(1 AS bit),
            CAST(1 AS bit),
            1
        );
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260410233059_addValidationPlan'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260410233059_addValidationPlan', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CommercialAccessMode] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CommercialNotes] nvarchar(250) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CommercialUpdatedByUserId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CommercialUpdatedUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ForcedPlanId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [IsPlatformSuperAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE TABLE [PromotionalCodes] (
        [Id] uniqueidentifier NOT NULL,
        [Codigo] nvarchar(100) NOT NULL,
        [Activo] bit NOT NULL,
        [TipoBeneficio] int NOT NULL,
        [DiasGratis] int NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [MaxUsos] int NULL,
        [UsosActuales] int NOT NULL,
        [FechaExpiracionUtc] datetime2 NULL,
        [SoloPrimerRegistro] bit NOT NULL,
        [EmailObjetivo] nvarchar(256) NULL,
        [CreadoPorUserId] nvarchar(450) NULL,
        [NotasInternas] nvarchar(2000) NULL,
        [FechaCreacionUtc] datetime2 NOT NULL,
        [FechaActualizacionUtc] datetime2 NULL,
        CONSTRAINT [PK_PromotionalCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionalCodes_Planes_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Planes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE TABLE [TenantCommercialAccessGrants] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Source] int NOT NULL,
        [Activo] bit NOT NULL,
        [RequiresBilling] bit NOT NULL,
        [FechaInicioUtc] datetime2 NOT NULL,
        [FechaFinUtc] datetime2 NOT NULL,
        [PromotionalCodeId] uniqueidentifier NULL,
        [CreadoPorUserId] nvarchar(450) NULL,
        [NotasInternas] nvarchar(2000) NULL,
        CONSTRAINT [PK_TenantCommercialAccessGrants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantCommercialAccessGrants_Planes_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Planes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenantCommercialAccessGrants_PromotionalCodes_PromotionalCodeId] FOREIGN KEY ([PromotionalCodeId]) REFERENCES [PromotionalCodes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenantCommercialAccessGrants_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE TABLE [PromotionalCodeRedemptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PromotionalCodeId] uniqueidentifier NOT NULL,
        [TenantCommercialAccessGrantId] uniqueidentifier NULL,
        [ConsumidoPorUserId] nvarchar(450) NULL,
        [EmailConsumidor] nvarchar(256) NOT NULL,
        [FechaConsumoUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PromotionalCodeRedemptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionalCodeRedemptions_PromotionalCodes_PromotionalCodeId] FOREIGN KEY ([PromotionalCodeId]) REFERENCES [PromotionalCodes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PromotionalCodeRedemptions_TenantCommercialAccessGrants_TenantCommercialAccessGrantId] FOREIGN KEY ([TenantCommercialAccessGrantId]) REFERENCES [TenantCommercialAccessGrants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_Tenants_ForcedPlanId] ON [Tenants] ([ForcedPlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PromotionalCodeRedemptions_PromotionalCodeId_TenantId] ON [PromotionalCodeRedemptions] ([PromotionalCodeId], [TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_PromotionalCodeRedemptions_TenantCommercialAccessGrantId] ON [PromotionalCodeRedemptions] ([TenantCommercialAccessGrantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_PromotionalCodeRedemptions_TenantId] ON [PromotionalCodeRedemptions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PromotionalCodes_Codigo] ON [PromotionalCodes] ([Codigo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_PromotionalCodes_PlanId] ON [PromotionalCodes] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_TenantCommercialAccessGrants_PlanId] ON [TenantCommercialAccessGrants] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_TenantCommercialAccessGrants_PromotionalCodeId] ON [TenantCommercialAccessGrants] ([PromotionalCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_TenantCommercialAccessGrants_TenantId] ON [TenantCommercialAccessGrants] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    CREATE INDEX [IX_TenantCommercialAccessGrants_TenantId_Activo_FechaInicioUtc_FechaFinUtc] ON [TenantCommercialAccessGrants] ([TenantId], [Activo], [FechaInicioUtc], [FechaFinUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    ALTER TABLE [Tenants] ADD CONSTRAINT [FK_Tenants_Planes_ForcedPlanId] FOREIGN KEY ([ForcedPlanId]) REFERENCES [Planes] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260413022711_platformCommercialAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260413022711_platformCommercialAccess', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.security_policies
        WHERE name = N'TenantSecurityPolicy'
          AND schema_id = SCHEMA_ID(N'dbo')
    )
    BEGIN
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy] WITH (STATE = OFF);
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Egresos_Categorias_CategoriaId')
    BEGIN
        ALTER TABLE [dbo].[Egresos] DROP CONSTRAINT [FK_Egresos_Categorias_CategoriaId];
    END

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Egresos_Categorias')
    BEGIN
        ALTER TABLE [dbo].[Egresos] DROP CONSTRAINT [FK_Egresos_Categorias];
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Funcionarios_Puestos_IdPuesto')
    BEGIN
        ALTER TABLE [dbo].[Funcionarios] DROP CONSTRAINT [FK_Funcionarios_Puestos_IdPuesto];
    END

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Funcionarios_Puestos')
    BEGIN
        ALTER TABLE [dbo].[Funcionarios] DROP CONSTRAINT [FK_Funcionarios_Puestos];
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @DropSql nvarchar(max) = N'';

    SELECT @DropSql = STRING_AGG(
        N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) +
        N' DROP CONSTRAINT ' + QUOTENAME(name) + N';',
        N' ')
    FROM sys.foreign_keys
    WHERE referenced_object_id = OBJECT_ID(N'[dbo].[Clientes]');

    IF @DropSql IS NOT NULL AND LEN(@DropSql) > 0
    BEGIN
        EXEC sp_executesql @DropSql;
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [Clientes]
    ADD [Id] int IDENTITY(1,1) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [ClienteVisitas] ADD [ClienteId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [ClienteImagenes] ADD [ClienteId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    UPDATE [Clientes]
    SET [CorreoElectronico] = NULL
    WHERE LTRIM(RTRIM(ISNULL([CorreoElectronico], N''))) = N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    UPDATE cv
    SET cv.[ClienteId] = c.[Id]
    FROM [ClienteVisitas] cv
    INNER JOIN [Clientes] c
        ON LTRIM(RTRIM(c.[NumeroTelefono])) = LTRIM(RTRIM(cv.[NumeroTelefono]))
       AND c.[TenantId] = cv.[TenantId];

    UPDATE ci
    SET ci.[ClienteId] = c.[Id]
    FROM [ClienteImagenes] ci
    INNER JOIN [Clientes] c
        ON LTRIM(RTRIM(c.[NumeroTelefono])) = LTRIM(RTRIM(ci.[NumeroTelefono]))
       AND c.[TenantId] = ci.[TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @PkName nvarchar(200);

    SELECT @PkName = kc.name
    FROM sys.key_constraints kc
    WHERE kc.parent_object_id = OBJECT_ID(N'[dbo].[Clientes]')
      AND kc.[type] = 'PK';

    IF @PkName IS NOT NULL
    BEGIN
        EXEC(N'ALTER TABLE [dbo].[Clientes] DROP CONSTRAINT [' + @PkName + ']');
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClienteVisitas]') AND [c].[name] = N'NumeroTelefono');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [ClienteVisitas] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [ClienteVisitas] ALTER COLUMN [NumeroTelefono] nvarchar(50) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Clientes]') AND [c].[name] = N'Nombre');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Clientes] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Clientes] ALTER COLUMN [Nombre] nvarchar(150) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Clientes]') AND [c].[name] = N'CorreoElectronico');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Clientes] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Clientes] ALTER COLUMN [CorreoElectronico] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Clientes]') AND [c].[name] = N'NumeroTelefono');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Clientes] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [Clientes] ALTER COLUMN [NumeroTelefono] nvarchar(50) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClienteImagenes]') AND [c].[name] = N'NumeroTelefono');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [ClienteImagenes] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [ClienteImagenes] ALTER COLUMN [NumeroTelefono] nvarchar(50) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DELETE cv
    FROM ClienteVisitas cv
    LEFT JOIN Clientes c
        ON LTRIM(RTRIM(c.NumeroTelefono)) = LTRIM(RTRIM(cv.NumeroTelefono))
       AND c.TenantId = cv.TenantId
    WHERE c.Id IS NULL;

    DELETE ci
    FROM ClienteImagenes ci
    LEFT JOIN Clientes c
        ON LTRIM(RTRIM(c.NumeroTelefono)) = LTRIM(RTRIM(ci.NumeroTelefono))
       AND c.TenantId = ci.TenantId
    WHERE c.Id IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    IF EXISTS (SELECT 1 FROM [ClienteVisitas] WHERE [ClienteId] IS NULL)
    BEGIN
        THROW 50000, 'No se pudo vincular una o más visitas con su cliente durante la migración.', 1;
    END

    IF EXISTS (SELECT 1 FROM [ClienteImagenes] WHERE [ClienteId] IS NULL)
    BEGIN
        THROW 50001, 'No se pudo vincular una o más imágenes con su cliente durante la migración.', 1;
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClienteVisitas]') AND [c].[name] = N'ClienteId');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [ClienteVisitas] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [ClienteVisitas] ALTER COLUMN [ClienteId] int NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClienteImagenes]') AND [c].[name] = N'ClienteId');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [ClienteImagenes] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [ClienteImagenes] ALTER COLUMN [ClienteId] int NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [Clientes] ADD CONSTRAINT [PK_Clientes] PRIMARY KEY ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    CREATE INDEX [IX_ClienteVisitas_ClienteId] ON [ClienteVisitas] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    CREATE INDEX [IX_ClienteVisitas_TenantId_ClienteId_FechaVisita] ON [ClienteVisitas] ([TenantId], [ClienteId], [FechaVisita]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clientes_TenantId_NumeroTelefono] ON [Clientes] ([TenantId], [NumeroTelefono]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    CREATE INDEX [IX_ClienteImagenes_ClienteId] ON [ClienteImagenes] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    CREATE INDEX [IX_ClienteImagenes_TenantId_ClienteId] ON [ClienteImagenes] ([TenantId], [ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [ClienteImagenes] ADD CONSTRAINT [FK_ClienteImagenes_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [ClienteVisitas] ADD CONSTRAINT [FK_ClienteVisitas_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [Egresos] ADD CONSTRAINT [FK_Egresos_Categorias_CategoriaId] FOREIGN KEY ([CategoriaId]) REFERENCES [Categorias] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    ALTER TABLE [Funcionarios] ADD CONSTRAINT [FK_Funcionarios_Puestos_IdPuesto] FOREIGN KEY ([IdPuesto]) REFERENCES [Puestos] ([IdPuesto]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.security_policies
        WHERE name = N'TenantSecurityPolicy'
          AND schema_id = SCHEMA_ID(N'dbo')
    )
    BEGIN
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy] WITH (STATE = ON);
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260414033014_clientFuncionarioManagementFixes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260414033014_clientFuncionarioManagementFixes', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    UPDATE [Categorias]
    SET [Nombre] = LEFT(LTRIM(RTRIM(ISNULL([Nombre], N''))), 150)
    WHERE [Nombre] IS NULL OR LEN([Nombre]) > 150 OR [Nombre] <> LTRIM(RTRIM([Nombre]));

    UPDATE [Categorias]
    SET [Detalle] = LEFT(COALESCE(NULLIF(LTRIM(RTRIM([Detalle])), N''), N'Sin detalle'), 500)
    WHERE [Detalle] IS NULL OR LEN([Detalle]) > 500 OR [Detalle] <> LTRIM(RTRIM([Detalle]));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Categorias]') AND [c].[name] = N'Nombre');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [Categorias] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [Categorias] ALTER COLUMN [Nombre] nvarchar(150) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Categorias]') AND [c].[name] = N'Detalle');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [Categorias] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [Categorias] ALTER COLUMN [Detalle] nvarchar(500) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE TABLE [LiquidacionesSemanales] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [SemanaInicio] datetime2 NOT NULL,
        [SemanaFin] datetime2 NOT NULL,
        [FechaPago] datetime2 NOT NULL,
        [MontoTotal] decimal(18,2) NOT NULL,
        [Estado] nvarchar(30) NOT NULL,
        [Observacion] nvarchar(500) NULL,
        [CreadoPor] nvarchar(450) NULL,
        [FechaCreacion] datetime2 NOT NULL,
        [EgresoId] int NULL,
        CONSTRAINT [PK_LiquidacionesSemanales] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LiquidacionesSemanales_Egresos_EgresoId] FOREIGN KEY ([EgresoId]) REFERENCES [Egresos] ([IdEgreso]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE TABLE [LiquidacionesSemanalesDetalle] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [LiquidacionSemanalId] int NOT NULL,
        [FuncionarioId] int NOT NULL,
        [MontoServicios] decimal(18,2) NOT NULL,
        [MontoProductos] decimal(18,2) NOT NULL,
        [Impuestos] decimal(18,2) NOT NULL,
        [MontoNeto] decimal(18,2) NOT NULL,
        [MontoPagado] decimal(18,2) NOT NULL,
        [Pendiente] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_LiquidacionesSemanalesDetalle] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LiquidacionesSemanalesDetalle_Funcionarios_FuncionarioId] FOREIGN KEY ([FuncionarioId]) REFERENCES [Funcionarios] ([IdFuncionario]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LiquidacionesSemanalesDetalle_LiquidacionesSemanales_LiquidacionSemanalId] FOREIGN KEY ([LiquidacionSemanalId]) REFERENCES [LiquidacionesSemanales] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE TABLE [LiquidacionesSemanalesDistribucionMensual] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [LiquidacionSemanalId] int NOT NULL,
        [Anio] int NOT NULL,
        [Mes] int NOT NULL,
        [MontoAsignado] decimal(18,2) NOT NULL,
        [DiasAplicados] int NOT NULL,
        CONSTRAINT [PK_LiquidacionesSemanalesDistribucionMensual] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LiquidacionesSemanalesDistribucionMensual_LiquidacionesSemanales_LiquidacionSemanalId] FOREIGN KEY ([LiquidacionSemanalId]) REFERENCES [LiquidacionesSemanales] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_Categorias_TenantId_Nombre] ON [Categorias] ([TenantId], [Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LiquidacionesSemanales_EgresoId] ON [LiquidacionesSemanales] ([EgresoId]) WHERE [EgresoId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanales_TenantId] ON [LiquidacionesSemanales] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanales_TenantId_Semana] ON [LiquidacionesSemanales] ([TenantId], [SemanaInicio], [SemanaFin], [FechaPago]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDetalle_FuncionarioId] ON [LiquidacionesSemanalesDetalle] ([FuncionarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDetalle_LiquidacionSemanalId] ON [LiquidacionesSemanalesDetalle] ([LiquidacionSemanalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDetalle_TenantId] ON [LiquidacionesSemanalesDetalle] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LiquidacionesSemanalesDetalle_TenantId_LiquidacionSemanalId_FuncionarioId] ON [LiquidacionesSemanalesDetalle] ([TenantId], [LiquidacionSemanalId], [FuncionarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDistribucionMensual_LiquidacionSemanalId] ON [LiquidacionesSemanalesDistribucionMensual] ([LiquidacionSemanalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDistribucionMensual_TenantId] ON [LiquidacionesSemanalesDistribucionMensual] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE INDEX [IX_LiquidacionesSemanalesDistribucionMensual_TenantId_Anio_Mes] ON [LiquidacionesSemanalesDistribucionMensual] ([TenantId], [Anio], [Mes]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LiquidacionesSemanalesDistribucionMensual_TenantId_LiquidacionSemanalId_Anio_Mes] ON [LiquidacionesSemanalesDistribucionMensual] ([TenantId], [LiquidacionSemanalId], [Anio], [Mes]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    ;WITH CategoriasDuplicadas AS
    (
        SELECT
            [Id],
            ROW_NUMBER() OVER (PARTITION BY [TenantId], [Nombre] ORDER BY [Id]) AS [RowNumber]
        FROM [Categorias]
        WHERE [Nombre] = N'Pago Funcionarios'
    )
    UPDATE c
    SET [Nombre] = LEFT(CONCAT(N'Pago Funcionarios legado ', c.[Id]), 150)
    FROM [Categorias] c
    INNER JOIN CategoriasDuplicadas d
        ON d.[Id] = c.[Id]
    WHERE d.[RowNumber] > 1;

    CREATE UNIQUE INDEX [UX_Categorias_TenantId_Nombre_PagoFuncionarios]
    ON [Categorias] ([TenantId], [Nombre])
    WHERE [Nombre] = N'Pago Funcionarios';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    ALTER TABLE [LiquidacionesSemanales]
    ADD CONSTRAINT [CK_LiquidacionesSemanales_SemanaValida]
    CHECK ([SemanaFin] >= [SemanaInicio]);

    ALTER TABLE [LiquidacionesSemanales]
    ADD CONSTRAINT [CK_LiquidacionesSemanales_MontoTotal]
    CHECK ([MontoTotal] >= 0);

    ALTER TABLE [LiquidacionesSemanalesDetalle]
    ADD CONSTRAINT [CK_LiquidacionesSemanalesDetalle_Montos]
    CHECK (
        [MontoServicios] >= 0 AND
        [MontoProductos] >= 0 AND
        [Impuestos] >= 0 AND
        [MontoNeto] >= 0 AND
        [MontoPagado] > 0
    );

    ALTER TABLE [LiquidacionesSemanalesDistribucionMensual]
    ADD CONSTRAINT [CK_LiquidacionesSemanalesDistribucionMensual_Valores]
    CHECK (
        [MontoAsignado] >= 0 AND
        [DiasAplicados] > 0 AND
        [Mes] BETWEEN 1 AND 12
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417030449_weeklyEmployeeLiquidations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417030449_weeklyEmployeeLiquidations', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    CREATE TABLE [ContractDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [VersionNumber] nvarchar(50) NOT NULL,
        [ContentHtml] nvarchar(max) NOT NULL,
        [ContentHash] nvarchar(64) NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFromUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ContractDocuments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    CREATE TABLE [ContractAcceptanceRecords] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [ContractDocumentId] uniqueidentifier NOT NULL,
        [ContractVersion] nvarchar(50) NOT NULL,
        [AcceptedContentHash] nvarchar(64) NOT NULL,
        [AcceptanceSource] nvarchar(40) NOT NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(2048) NULL,
        [AcceptedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ContractAcceptanceRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContractAcceptanceRecords_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ContractAcceptanceRecords_ContractDocuments_ContractDocumentId] FOREIGN KEY ([ContractDocumentId]) REFERENCES [ContractDocuments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ContentHash', N'ContentHtml', N'CreatedAtUtc', N'EffectiveFromUtc', N'IsActive', N'Title', N'UpdatedAtUtc', N'VersionNumber') AND [object_id] = OBJECT_ID(N'[ContractDocuments]'))
        SET IDENTITY_INSERT [ContractDocuments] ON;
    EXEC(N'INSERT INTO [ContractDocuments] ([Id], [ContentHash], [ContentHtml], [CreatedAtUtc], [EffectiveFromUtc], [IsActive], [Title], [UpdatedAtUtc], [VersionNumber])
    VALUES (''9d0d8c0b-4e22-44d1-b7d9-7ba6e95c52b1'', N''2E1530410832503E401491FB3CC70DD700E6B9B24C2549BC348C4A45F7A683AD'', CONCAT(CAST(N''<section class="contract-section">'' AS nvarchar(max)), nchar(10), N''    <h2>1. Terminos y Condiciones</h2>'', nchar(10), N''    <p>Este documento corresponde a una version inicial editable del contrato de uso de LuxuryApp. Antes de salir a produccion debes reemplazar este texto por la version final aprobada por asesoria legal.</p>'', nchar(10), N''    <p>LuxuryApp presta un servicio SaaS para negocios de belleza, barberia, salon y operaciones relacionadas. El uso del servicio implica aceptar las reglas operativas, tecnicas y comerciales definidas en este contrato.</p>'', nchar(10), N''    <p>El cliente se compromete a utilizar la plataforma conforme a la ley aplicable, a no compartir accesos de manera indebida y a custodiar sus credenciales, usuarios y configuraciones internas.</p>'', nchar(10), N''    <p>LuxuryApp puede actualizar funciones, seguridad y procesos operativos para mejorar la disponibilidad, estabilidad y cumplimiento del servicio.</p>'', nchar(10), N''</section>'', nchar(10), N''<section class="contract-section">'', nchar(10), N''    <h2>2. Politica de Privacidad</h2>'', nchar(10), N''    <p>LuxuryApp trata la informacion necesaria para operar la cuenta, autenticar usuarios, administrar tenants, procesar pagos y mantener el funcionamiento del servicio.</p>'', nchar(10), N''    <p>El cliente declara que cuenta con la base legal necesaria para cargar datos de sus propios clientes, funcionarios y operaciones en la plataforma.</p>'', nchar(10), N''    <p>Debes reemplazar esta seccion por la politica de privacidad definitiva, incluyendo finalidades, base juridica, plazos de conservacion, medidas de seguridad, transferencias y canales de ejercicio de derechos.</p>'', nchar(10), N''</section>'', nchar(10), N''<section class="contract-section">'', nchar(10), N''    <h2>3. Politica de pagos, cancelaciones y reembolsos</h2>'', nchar(10), N''    <p>El acceso comercial a LuxuryApp depende del plan contratado, sus condiciones de cobro, renovacion, suspension y reactivacion.</p>'', nchar(10), N''    <p>Debes completar esta seccion con las condiciones finales de facturacion, fechas de corte, reglas de cancelacion, periodos de aviso, politica de mora y escenarios de reembolso permitidos o no permitidos.</p>'', nchar(10), N''    <p>Mientras esta version placeholder siga vigente, ninguna clausula aqui incluida debe considerarse texto legal final para produccion.</p>'', nchar(10), N''</section>'', nchar(10), N''<section class="contract-section">'', nchar(10), N''    <h2>4. Consentimiento de tratamiento de datos</h2>'', nchar(10), N''    <p>Al aceptar este contrato el usuario declara que ha leido el alcance del tratamiento de datos relacionado con la operacion de la cuenta, la seguridad del servicio y el soporte tecnico.</p>'', nchar(10), N''    <p>Debes sustituir este apartado por el consentimiento final aprobado, incluyendo categorias de datos, finalidad, responsables, encargados, revocatoria y demas extremos regulatorios aplicables.</p>'', nchar(10), N''    <p>La aceptacion registrada por el sistema conserva fecha, direccion IP, agente de usuario, version del documento y hash del contenido aceptado para fines de trazabilidad y cumplimiento.</p>'', nchar(10), N''</section>''), ''2026-04-21T00:00:00.0000000Z'', ''2026-04-21T00:00:00.0000000Z'', CAST(1 AS bit), N''Contrato de Uso del Servicio LuxuryApp'', ''2026-04-21T00:00:00.0000000Z'', N''1.0.0'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ContentHash', N'ContentHtml', N'CreatedAtUtc', N'EffectiveFromUtc', N'IsActive', N'Title', N'UpdatedAtUtc', N'VersionNumber') AND [object_id] = OBJECT_ID(N'[ContractDocuments]'))
        SET IDENTITY_INSERT [ContractDocuments] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    CREATE INDEX [IX_ContractAcceptanceRecords_ContractDocumentId] ON [ContractAcceptanceRecords] ([ContractDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    CREATE INDEX [IX_ContractAcceptanceRecords_UserId_ContractDocumentId_AcceptedAtUtc] ON [ContractAcceptanceRecords] ([UserId], [ContractDocumentId], [AcceptedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ContractDocuments_IsActive] ON [ContractDocuments] ([IsActive]) WHERE IsActive = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ContractDocuments_VersionNumber] ON [ContractDocuments] ([VersionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421234226_contractAcceptanceVersioning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260421234226_contractAcceptanceVersioning', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN

    BEGIN TRY
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy]
        DROP FILTER PREDICATE ON [dbo].[ClienteImagenes];
    END TRY
    BEGIN CATCH
    END CATCH

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN

    BEGIN TRY
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy]
        DROP BLOCK PREDICATE ON [dbo].[ClienteImagenes] AFTER INSERT;
    END TRY
    BEGIN CATCH
    END CATCH

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN

    BEGIN TRY
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy]
        DROP BLOCK PREDICATE ON [dbo].[ClienteImagenes] AFTER UPDATE;
    END TRY
    BEGIN CATCH
    END CATCH

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN
    DROP TABLE [ClienteImagenes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN
    DROP INDEX [IX_ClienteVisitas_TenantId_ClienteId_FechaVisita] ON [ClienteVisitas];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN
    CREATE INDEX [IX_ClienteVisitas_TenantId_ClienteId_FechaVisita] ON [ClienteVisitas] ([TenantId], [ClienteId], [FechaVisita] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN
    CREATE INDEX [IX_Clientes_TenantId_Nombre] ON [Clientes] ([TenantId], [Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422215037_optimizeClientesModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260422215037_optimizeClientesModule', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423043350_optimizeFuncionariosModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Puestos_TenantId_NombrePuesto] ON [Puestos] ([TenantId], [NombrePuesto]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423043350_optimizeFuncionariosModule'
)
BEGIN
    CREATE INDEX [IX_PagosFuncionarios_TenantId_Semana_Funcionario] ON [PagosFuncionarios] ([TenantId], [InicioSemana], [FinSemana], [FuncionarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423043350_optimizeFuncionariosModule'
)
BEGIN
    CREATE INDEX [IX_Funcionarios_TenantId_Nombre] ON [Funcionarios] ([TenantId], [Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423043350_optimizeFuncionariosModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423043350_optimizeFuncionariosModule', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Servicios]') AND [c].[name] = N'Nombre');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [Servicios] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [Servicios] ALTER COLUMN [Nombre] nvarchar(450) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Servicios_TenantId_Nombre] ON [Servicios] ([TenantId], [Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    CREATE INDEX [IX_DetalleCobroProductos_TenantId_CobroId] ON [DetalleCobroProductos] ([TenantId], [CobroId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    CREATE INDEX [IX_Cobros_TenantId_FechaCobro] ON [Cobros] ([TenantId], [FechaCobro]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    CREATE INDEX [IX_Cobros_TenantId_FuncionarioId_FechaCobro] ON [Cobros] ([TenantId], [FuncionarioId], [FechaCobro]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423174550_optimizeCobrosModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423174550_optimizeCobrosModule', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423191843_optimizeEgresosModule'
)
BEGIN
    DROP INDEX [IX_Categorias_TenantId_Nombre] ON [Categorias];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423191843_optimizeEgresosModule'
)
BEGIN
    CREATE INDEX [IX_Egresos_TenantId_CategoriaId_FechaEgreso] ON [Egresos] ([TenantId], [CategoriaId], [FechaEgreso]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423191843_optimizeEgresosModule'
)
BEGIN
    CREATE INDEX [IX_Egresos_TenantId_FechaEgreso] ON [Egresos] ([TenantId], [FechaEgreso]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423191843_optimizeEgresosModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categorias_TenantId_Nombre] ON [Categorias] ([TenantId], [Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423191843_optimizeEgresosModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423191843_optimizeEgresosModule', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423200303_optimizeDashboardFinancieroQueries'
)
BEGIN
    CREATE INDEX [IX_Citas_TenantId_FechaHoraCita] ON [Citas] ([TenantId], [FechaHoraCita]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423200303_optimizeDashboardFinancieroQueries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423200303_optimizeDashboardFinancieroQueries', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    ALTER TABLE [MovimientosInventario] DROP CONSTRAINT [FK_MovimientosInventario_Productos];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Productos]') AND [c].[name] = N'NombreProducto');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [Productos] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [Productos] ALTER COLUMN [NombreProducto] nvarchar(150) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    CREATE INDEX [IX_Productos_TenantId_Activo_NombreProducto] ON [Productos] ([TenantId], [Activo], [NombreProducto]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Productos_TenantId_NombreProducto] ON [Productos] ([TenantId], [NombreProducto]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    CREATE INDEX [IX_MovimientosInventario_TenantId_ProductoId_FechaMovimiento] ON [MovimientosInventario] ([TenantId], [ProductoId], [FechaMovimiento]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    ALTER TABLE [MovimientosInventario] ADD CONSTRAINT [FK_MovimientosInventario_Productos] FOREIGN KEY ([ProductoId]) REFERENCES [Productos] ([IdProducto]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424000552_optimizeProductosModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424000552_optimizeProductosModule', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424181416_optimizeCalendarModule'
)
BEGIN
    CREATE INDEX [IX_Citas_TenantId_FuncionarioId_FechaHoraCita] ON [Citas] ([TenantId], [FuncionarioId], [FechaHoraCita]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424181416_optimizeCalendarModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424181416_optimizeCalendarModule', N'10.0.2');
END;

COMMIT;
GO

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

