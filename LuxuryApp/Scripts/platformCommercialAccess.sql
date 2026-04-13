BEGIN TRANSACTION;
ALTER TABLE [Tenants] ADD [CommercialAccessMode] int NOT NULL DEFAULT 0;

ALTER TABLE [Tenants] ADD [CommercialNotes] nvarchar(250) NULL;

ALTER TABLE [Tenants] ADD [CommercialUpdatedByUserId] nvarchar(450) NULL;

ALTER TABLE [Tenants] ADD [CommercialUpdatedUtc] datetime2 NULL;

ALTER TABLE [Tenants] ADD [ForcedPlanId] uniqueidentifier NULL;

ALTER TABLE [AspNetUsers] ADD [IsPlatformSuperAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);

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

CREATE INDEX [IX_Tenants_ForcedPlanId] ON [Tenants] ([ForcedPlanId]);

CREATE UNIQUE INDEX [IX_PromotionalCodeRedemptions_PromotionalCodeId_TenantId] ON [PromotionalCodeRedemptions] ([PromotionalCodeId], [TenantId]);

CREATE INDEX [IX_PromotionalCodeRedemptions_TenantCommercialAccessGrantId] ON [PromotionalCodeRedemptions] ([TenantCommercialAccessGrantId]);

CREATE INDEX [IX_PromotionalCodeRedemptions_TenantId] ON [PromotionalCodeRedemptions] ([TenantId]);

CREATE UNIQUE INDEX [IX_PromotionalCodes_Codigo] ON [PromotionalCodes] ([Codigo]);

CREATE INDEX [IX_PromotionalCodes_PlanId] ON [PromotionalCodes] ([PlanId]);

CREATE INDEX [IX_TenantCommercialAccessGrants_PlanId] ON [TenantCommercialAccessGrants] ([PlanId]);

CREATE INDEX [IX_TenantCommercialAccessGrants_PromotionalCodeId] ON [TenantCommercialAccessGrants] ([PromotionalCodeId]);

CREATE INDEX [IX_TenantCommercialAccessGrants_TenantId] ON [TenantCommercialAccessGrants] ([TenantId]);

CREATE INDEX [IX_TenantCommercialAccessGrants_TenantId_Activo_FechaInicioUtc_FechaFinUtc] ON [TenantCommercialAccessGrants] ([TenantId], [Activo], [FechaInicioUtc], [FechaFinUtc]);

ALTER TABLE [Tenants] ADD CONSTRAINT [FK_Tenants_Planes_ForcedPlanId] FOREIGN KEY ([ForcedPlanId]) REFERENCES [Planes] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260413022711_platformCommercialAccess', N'10.0.2');

COMMIT;
GO

