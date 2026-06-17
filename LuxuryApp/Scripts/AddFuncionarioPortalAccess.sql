BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616230524_AddFuncionarioPortalAccess'
)
BEGIN
    ALTER TABLE [Funcionarios] ADD [AppUsuarioId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616230524_AddFuncionarioPortalAccess'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [FuncionarioId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616230524_AddFuncionarioPortalAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Funcionarios_AppUsuarioId] ON [Funcionarios] ([AppUsuarioId]) WHERE [AppUsuarioId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616230524_AddFuncionarioPortalAccess'
)
BEGIN
    ALTER TABLE [Funcionarios] ADD CONSTRAINT [FK_Funcionarios_AspNetUsers_AppUsuarioId] FOREIGN KEY ([AppUsuarioId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616230524_AddFuncionarioPortalAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260616230524_AddFuncionarioPortalAccess', N'10.0.2');
END;

COMMIT;
GO

