BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    ALTER TABLE [Cobros] ADD [ClienteId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE TABLE [ClienteServiciosRealizados] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [ClienteId] int NOT NULL,
        [FuncionarioId] int NULL,
        [ServicioId] int NULL,
        [CobroId] int NULL,
        [CitaId] int NULL,
        [FechaHora] datetime2 NOT NULL,
        [Monto] decimal(18,2) NULL,
        [Notas] nvarchar(500) NULL,
        [Origen] nvarchar(30) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        CONSTRAINT [PK_ClienteServiciosRealizados] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClienteServiciosRealizados_Citas_CitaId] FOREIGN KEY ([CitaId]) REFERENCES [Citas] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ClienteServiciosRealizados_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClienteServiciosRealizados_Cobros_CobroId] FOREIGN KEY ([CobroId]) REFERENCES [Cobros] ([IdCobro]) ON DELETE SET NULL,
        CONSTRAINT [FK_ClienteServiciosRealizados_Funcionarios_FuncionarioId] FOREIGN KEY ([FuncionarioId]) REFERENCES [Funcionarios] ([IdFuncionario]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClienteServiciosRealizados_Servicios_ServicioId] FOREIGN KEY ([ServicioId]) REFERENCES [Servicios] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_Cobros_ClienteId] ON [Cobros] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Cobros_TenantId_ClienteId] ON [Cobros] ([TenantId], [ClienteId]) WHERE [ClienteId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_CitaId] ON [ClienteServiciosRealizados] ([CitaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_ClienteId] ON [ClienteServiciosRealizados] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_FuncionarioId] ON [ClienteServiciosRealizados] ([FuncionarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_ServicioId] ON [ClienteServiciosRealizados] ([ServicioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_TenantId] ON [ClienteServiciosRealizados] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    CREATE INDEX [IX_ClienteServiciosRealizados_TenantId_ClienteId_FechaHora] ON [ClienteServiciosRealizados] ([TenantId], [ClienteId], [FechaHora] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ClienteServiciosRealizados_CobroId] ON [ClienteServiciosRealizados] ([CobroId]) WHERE [CobroId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    ALTER TABLE [Cobros] ADD CONSTRAINT [FK_Cobros_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260610175944_CrmPhase1_ClienteServicioRealizado_CobroClienteId', N'10.0.2');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610200000_AddCitaClienteIndex'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Citas_TenantId_ClienteId_FechaHoraCita] ON [Citas] ([TenantId], [ClienteId], [FechaHoraCita] DESC) WHERE [ClienteId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260610200000_AddCitaClienteIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260610200000_AddCitaClienteIndex', N'10.0.2');
END;

COMMIT;
GO

