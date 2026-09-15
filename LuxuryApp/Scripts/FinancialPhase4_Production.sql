/*
    FinancialPhase4_Production.sql
    ------------------------------
    Corresponde EXACTAMENTE a la migración EF:

        20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls

    Generado con:
        dotnet ef migrations script 20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency ^
                                    20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls ^
                                    --idempotent

    QUÉ HACE
    --------
    1. Agrega a dbo.Cobros el snapshot financiero de la transacción, todo NULLABLE:
           AplicaIvaSnapshot        bit
           TarifaIvaSnapshot        decimal(5,2)
           PrecioIncluyeIvaSnapshot bit
           DetalleSnapshot          nvarchar(200)

    2. Agrega CK_Cobros_SnapshotFiscal: o los tres campos fiscales son NULL (cobro LEGACY)
       o los tres tienen valor (historia completa). No existe el snapshot parcial.

    3. Agrega a la política RLS existente las tres tablas de liquidaciones, que hasta hoy
       NO tenían ningún predicado:
           LiquidacionesSemanales
           LiquidacionesSemanalesDetalle
           LiquidacionesSemanalesDistribucionMensual
       Con el patrón ya usado en el resto del sistema:
           FILTER PREDICATE            fnTenantAccess(TenantId)
           BLOCK PREDICATE AFTER INSERT
           BLOCK PREDICATE AFTER UPDATE

    QUÉ **NO** HACE
    ---------------
    · NO hay backfill. Ningún cobro existente recibe snapshot: quedan en NULL = LEGACY y
      se siguen interpretando con el catálogo actual, igual que antes de esta versión.
      No podemos demostrar qué configuración regía cuando se registraron y no la inventamos.
    · NO apaga la política de seguridad en ningún momento. SQL Server permite agregar
      predicados con la política encendida, así que no hay ventana sin aislamiento.
    · NO borra, modifica ni reclasifica ninguna fila existente.
    · NO contiene datos de ningún tenant.

    ORDEN DE DESPLIEGUE
    -------------------
    Las columnas son nullable y el CHECK acepta las filas viejas (todas NULL), así que este
    script se puede aplicar ANTES de subir el código nuevo. La aplicación vieja las ignora.
    Al arrancar la versión nueva, todo cobro registrado empieza a guardar su snapshot.

    Recomendado:
        1. Scripts/FinancialPreDeployAudit.sql   (solo lectura)
        2. backup
        3. detener el servicio
        4. este script
        5. deploy del código
        6. arrancar el servicio
        7. Scripts/FinancialPostDeployVerification.sql

    EJECUCIÓN
    ---------
        sqlcmd -S <servidor> -d <base> -U <usuario> -P <clave> -b -i FinancialPhase4_Production.sql

    El script es idempotente (cada bloque revisa __EFMigrationsHistory) y corre dentro de una
    transacción: si algo falla, no queda aplicado a medias.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [AplicaIvaSnapshot] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [DetalleSnapshot] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [PrecioIncluyeIvaSnapshot] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [TarifaIvaSnapshot] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    EXEC(N'ALTER TABLE [Cobros] ADD CONSTRAINT [CK_Cobros_SnapshotFiscal] CHECK (([AplicaIvaSnapshot] IS NULL AND [TarifaIvaSnapshot] IS NULL AND [PrecioIncluyeIvaSnapshot] IS NULL) OR ([AplicaIvaSnapshot] IS NOT NULL AND [TarifaIvaSnapshot] IS NOT NULL AND [PrecioIncluyeIvaSnapshot] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    DECLARE @tablas TABLE (Nombre sysname NOT NULL);

    INSERT INTO @tablas (Nombre)
    VALUES
        (N'LiquidacionesSemanales'), (N'LiquidacionesSemanalesDetalle'), (N'LiquidacionesSemanalesDistribucionMensual');

    DECLARE @policySchema sysname;
    DECLARE @policyName sysname;
    DECLARE @qualifiedPolicy nvarchar(300);
    DECLARE @sql nvarchar(max);
    DECLARE @tabla sysname;
    DECLARE @targetObjectId int;

    /*  La política se descubre por una tabla que YA está protegida, igual que en las
        migraciones anteriores: así no se hardcodea el nombre y funciona en cualquier base. */
    SELECT TOP (1)
        @policySchema = SCHEMA_NAME(policy.schema_id),
        @policyName   = policy.name
    FROM sys.security_policies AS policy
    INNER JOIN sys.security_predicates AS predicate
        ON predicate.object_id = policy.object_id
    WHERE predicate.target_object_id = OBJECT_ID(N'[dbo].[Cobros]', N'U')
      AND predicate.predicate_definition LIKE N'%fnTenantAccess%'
    ORDER BY policy.name;

    /*  Sin RLS en la base (por ejemplo un entorno de desarrollo recién creado) no hay
        nada que extender, y la migración NO debe fallar por eso. Se usa IF/ELSE y no
        RETURN a propósito: el script idempotente de EF inserta este bloque dentro de un
        batch más grande, y un RETURN abortaría también el registro en
        __EFMigrationsHistory dejando la migración "aplicada pero no anotada". */
    IF @policyName IS NOT NULL AND OBJECT_ID(N'[dbo].[fnTenantAccess]') IS NOT NULL
    BEGIN
        SET @qualifiedPolicy = QUOTENAME(@policySchema) + N'.' + QUOTENAME(@policyName);

        DECLARE tablas_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT Nombre FROM @tablas;

        OPEN tablas_cursor;
        FETCH NEXT FROM tablas_cursor INTO @tabla;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @targetObjectId = OBJECT_ID(N'[dbo].' + QUOTENAME(@tabla), N'U');

            IF @targetObjectId IS NOT NULL
            BEGIN
            /* FILTER: nadie LEE filas de otro tenant. */
            IF NOT EXISTS (
                SELECT 1 FROM sys.security_predicates
                WHERE object_id = OBJECT_ID(@qualifiedPolicy)
                  AND target_object_id = @targetObjectId
                  AND predicate_type = 0)
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy
                         + N' ADD FILTER PREDICATE [dbo].[fnTenantAccess]([TenantId]) ON [dbo].'
                         + QUOTENAME(@tabla) + N';';
                EXEC sys.sp_executesql @sql;
            END;

            /* BLOCK AFTER INSERT: nadie ESCRIBE una fila a nombre de otro tenant. */
            IF NOT EXISTS (
                SELECT 1 FROM sys.security_predicates
                WHERE object_id = OBJECT_ID(@qualifiedPolicy)
                  AND target_object_id = @targetObjectId
                  AND predicate_type = 1
                  AND operation = 1)
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy
                         + N' ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId]) ON [dbo].'
                         + QUOTENAME(@tabla) + N' AFTER INSERT;';
                EXEC sys.sp_executesql @sql;
            END;

            /* BLOCK AFTER UPDATE: nadie MUEVE una fila hacia otro tenant. */
            IF NOT EXISTS (
                SELECT 1 FROM sys.security_predicates
                WHERE object_id = OBJECT_ID(@qualifiedPolicy)
                  AND target_object_id = @targetObjectId
                  AND predicate_type = 1
                  AND operation = 2)
            BEGIN
                SET @sql = N'ALTER SECURITY POLICY ' + @qualifiedPolicy
                         + N' ADD BLOCK PREDICATE [dbo].[fnTenantAccess]([TenantId]) ON [dbo].'
                         + QUOTENAME(@tabla) + N' AFTER UPDATE;';
                EXEC sys.sp_executesql @sql;
            END;
            END;

            FETCH NEXT FROM tablas_cursor INTO @tabla;
        END;

        CLOSE tablas_cursor;
        DEALLOCATE tablas_cursor;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls', N'10.0.2');
END;

COMMIT;
GO

