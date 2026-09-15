/*
    FinancialPhase5_Production.sql
    ------------------------------
    Corresponde EXACTAMENTE a la migración EF:

        20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls

    Generado con:
        dotnet ef migrations script 20260915051321_AddFinancialPhase4CobroSnapshotAndLiquidacionRls ^
                                    20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls ^
                                    --idempotent

    Aplicar DESPUÉS de FinancialPhase4_Production.sql.

    QUÉ HACE
    --------
    1. Agrega a dbo.Cobros el snapshot de REMUNERACIÓN, todo NULLABLE:
           PorcentajeServicioSnapshot        decimal(5,2)
           PorcentajeProductoSnapshot        decimal(5,2)
           ComisionCalculadaSobreSnapshot    int
           TipoRelacionColaboradorSnapshot   int
           ModalidadIvaColaboradorSnapshot   int
           TarifaIvaColaboradorSnapshot      decimal(5,2)

       Son exactamente los seis valores que consume el motor de liquidación. Desde el deploy,
       un cobro registrado con el colaborador al 50 % se liquida al 50 % para siempre, aunque
       mañana ese colaborador pase al 55 %.

    2. Agrega CK_Cobros_SnapshotRemuneracion: o los seis son NULL (cobro LEGACY) o los seis
       tienen valor. No existe el snapshot parcial — liquidar con el porcentaje viejo y la
       modalidad de IVA nueva daría una cifra que no existió nunca.

    3. Agrega BLOCK PREDICATE (INSERT y UPDATE) a la política RLS existente sobre:
           ComprobantesCobro
           ComprobanteCobroLineas

       ATENCIÓN — se agregan BLOCK pero NO FILTER, y es deliberado:
       la ruta pública /comprobantes/{token} es anónima (el cliente abre su comprobante sin
       login) y por lo tanto no tiene TenantId en SESSION_CONTEXT. Un FILTER PREDICATE actúa
       por debajo de EF —IgnoreQueryFilters no lo esquiva— y dejaría esa consulta en cero
       filas, rompiendo TODOS los comprobantes ya enviados por correo. Los BLOCK cierran el
       riesgo de escritura sin tocar esa ruta. Ver el informe de Fase 5, sección P.

    QUÉ **NO** HACE
    ---------------
    · NO hay backfill. Ningún cobro existente recibe snapshot de remuneración: quedan en NULL
      = LEGACY y se siguen liquidando con la configuración actual del colaborador, igual que
      antes. No podemos demostrar qué porcentaje regía cuando se registraron.
    · NO toca dbo.Facturas. Es una tabla de BILLING de la plataforma y se consulta de forma
      cross-tenant con IgnoreQueryFilters para deduplicar webhooks de pago; agregarle un
      FILTER produciría facturas duplicadas en cada reintento. Ver el informe, sección P.
    · NO apaga la política de seguridad en ningún momento.
    · NO borra, modifica ni reclasifica ninguna fila existente.
    · NO contiene datos de ningún tenant.

    ORDEN DE DESPLIEGUE
    -------------------
    Las columnas son nullable y el CHECK acepta las filas viejas (todas NULL), así que este
    script se puede aplicar ANTES de subir el código nuevo. La aplicación vieja las ignora.

    Recomendado:
        1. Scripts/FinancialPreDeployAudit.sql   (solo lectura)
        2. backup
        3. detener el servicio
        4. FinancialPhase4_Production.sql
        5. FinancialPhase5_Production.sql
        6. deploy del código
        7. arrancar el servicio
        8. Scripts/FinancialPostDeployVerification.sql

    EJECUCIÓN
    ---------
        sqlcmd -S <servidor> -d <base> -U <usuario> -P <clave> -b -i FinancialPhase5_Production.sql

    Idempotente (cada bloque revisa __EFMigrationsHistory) y dentro de una transacción: si algo
    falla, no queda aplicado a medias.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [ComisionCalculadaSobreSnapshot] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [ModalidadIvaColaboradorSnapshot] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [PorcentajeProductoSnapshot] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [PorcentajeServicioSnapshot] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [TarifaIvaColaboradorSnapshot] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    ALTER TABLE [Cobros] ADD [TipoRelacionColaboradorSnapshot] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    EXEC(N'ALTER TABLE [Cobros] ADD CONSTRAINT [CK_Cobros_SnapshotRemuneracion] CHECK (([PorcentajeServicioSnapshot] IS NULL AND [PorcentajeProductoSnapshot] IS NULL AND [ComisionCalculadaSobreSnapshot] IS NULL AND [TipoRelacionColaboradorSnapshot] IS NULL AND [ModalidadIvaColaboradorSnapshot] IS NULL AND [TarifaIvaColaboradorSnapshot] IS NULL) OR ([PorcentajeServicioSnapshot] IS NOT NULL AND [PorcentajeProductoSnapshot] IS NOT NULL AND [ComisionCalculadaSobreSnapshot] IS NOT NULL AND [TipoRelacionColaboradorSnapshot] IS NOT NULL AND [ModalidadIvaColaboradorSnapshot] IS NOT NULL AND [TarifaIvaColaboradorSnapshot] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    DECLARE @tablas TABLE (Nombre sysname NOT NULL);

    INSERT INTO @tablas (Nombre)
    VALUES
        (N'ComprobantesCobro'), (N'ComprobanteCobroLineas');

    DECLARE @policySchema sysname;
    DECLARE @policyName sysname;
    DECLARE @qualifiedPolicy nvarchar(300);
    DECLARE @sql nvarchar(max);
    DECLARE @tabla sysname;
    DECLARE @targetObjectId int;

    /*  La política se descubre por una tabla que YA está protegida, igual que en las
        migraciones anteriores: no se hardcodea el nombre. */
    SELECT TOP (1)
        @policySchema = SCHEMA_NAME(policy.schema_id),
        @policyName   = policy.name
    FROM sys.security_policies AS policy
    INNER JOIN sys.security_predicates AS predicate
        ON predicate.object_id = policy.object_id
    WHERE predicate.target_object_id = OBJECT_ID(N'[dbo].[Cobros]', N'U')
      AND predicate.predicate_definition LIKE N'%fnTenantAccess%'
    ORDER BY policy.name;

    /*  Sin RLS en la base no hay nada que extender, y la migración NO debe fallar por
        eso. IF/ELSE y no RETURN: el script idempotente de EF mete este bloque dentro de
        un batch más grande y un RETURN abortaría el registro en __EFMigrationsHistory. */
    IF @policyName IS NOT NULL AND OBJECT_ID(N'[dbo].[fnTenantAccess]') IS NOT NULL
    BEGIN
        SET @qualifiedPolicy = QUOTENAME(@policySchema) + N'.' + QUOTENAME(@policyName);

        DECLARE comprobantes_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT Nombre FROM @tablas;

        OPEN comprobantes_cursor;
        FETCH NEXT FROM comprobantes_cursor INTO @tabla;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @targetObjectId = OBJECT_ID(N'[dbo].' + QUOTENAME(@tabla), N'U');

            IF @targetObjectId IS NOT NULL
            BEGIN
            /* BLOCK AFTER INSERT: nadie crea un comprobante a nombre de otro tenant. */
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

            /* BLOCK AFTER UPDATE: nadie mueve un comprobante hacia otro tenant. */
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

            FETCH NEXT FROM comprobantes_cursor INTO @tabla;
        END;

        CLOSE comprobantes_cursor;
        DEALLOCATE comprobantes_cursor;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915061159_AddFinancialPhase5CommissionSnapshotAndComprobanteRls', N'10.0.2');
END;

COMMIT;
GO

