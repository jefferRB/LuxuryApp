BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    ALTER TABLE [Cobros] ADD [CitaId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    CREATE TABLE [FuncionarioPortalPermisos] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [FuncionarioId] int NOT NULL,
        [Permiso] nvarchar(60) NOT NULL,
        [Permitido] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FuncionarioPortalPermisos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FuncionarioPortalPermisos_Funcionarios_FuncionarioId] FOREIGN KEY ([FuncionarioId]) REFERENCES [Funcionarios] ([IdFuncionario]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    CREATE INDEX [IX_Cobros_CitaId] ON [Cobros] ([CitaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Cobros_TenantId_CitaId] ON [Cobros] ([TenantId], [CitaId]) WHERE [CitaId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    CREATE INDEX [IX_FuncionarioPortalPermisos_FuncionarioId] ON [FuncionarioPortalPermisos] ([FuncionarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    CREATE INDEX [IX_FuncionarioPortalPermisos_TenantId] ON [FuncionarioPortalPermisos] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [UX_FuncionarioPortalPermisos_Tenant_Funcionario_Permiso] ON [FuncionarioPortalPermisos] ([TenantId], [FuncionarioId], [Permiso]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    ALTER TABLE [Cobros] ADD CONSTRAINT [FK_Cobros_Citas_CitaId] FOREIGN KEY ([CitaId]) REFERENCES [Citas] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617001605_AddFuncionarioPortalPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260617001605_AddFuncionarioPortalPermissions', N'10.0.2');
END;

COMMIT;
GO

