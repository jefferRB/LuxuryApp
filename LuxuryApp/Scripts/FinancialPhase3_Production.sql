/*
    FinancialPhase3_Production.sql
    ------------------------------
    FASE 3 — Hardening financiero: identidad estructural de categorías + idempotencia de pagos.

    Migración ORIGEN  (estado esperado antes): 20260901175515_AddWhatsAppInboundAutoReply
    Migración DESTINO (estado esperado después): 20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency

    Generado con:
        dotnet ef migrations script 20260901175515_AddWhatsAppInboundAutoReply ^
                                    20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency ^
                                    --idempotent

    Es el PRIMERO de los tres scripts financieros. Orden obligatorio:
        FinancialPhase3_Production.sql  ← este
        FinancialPhase4_Production.sql
        FinancialPhase5_Production.sql

    ── CÓMO EJECUTARLO ────────────────────────────────────────────────────────────────
    1. SOLO después de haber corrido Scripts/FinancialPreDeployAudit.sql y revisado su salida.
    2. SOLO después del BACKUP verificado de la base.
    3. CON EL SERVICIO DETENIDO (systemctl stop luxury.service, o el nombre real de la unidad).
    4. NO ejecutar 'dotnet ef database update' además de este script: este archivo YA registra
       la migración en __EFMigrationsHistory, y correr ambos mecanismos sobre la misma migración
       es lo que produce estados inconsistentes.

        sqlcmd -S <servidor> -d <base> -U <usuario> -P <clave> -b -I -i FinancialPhase3_Production.sql

    ***  EL FLAG  -I  ES OBLIGATORIO  ***
    sqlcmd conecta con QUOTED_IDENTIFIER OFF por defecto, y -I lo pone en ON. Esta base tiene
    ÍNDICES FILTRADOS sobre Categorias (UX_Categorias_TenantId_Nombre_PagoFuncionarios) y este
    script además CREA dos índices filtrados. SQL Server exige QUOTED_IDENTIFIER ON tanto para
    hacer DML sobre una tabla con índices filtrados como para crearlos. Sin -I el script falla
    con el error 1934 y la transacción revierte entera (probado). No es opcional.

    ── QUÉ HACE ───────────────────────────────────────────────────────────────────────
    · Agrega Categorias.SystemCode (nvarchar(40), NULL) — identidad estructural de las
      categorías financieras, para que renombrar una etiqueta no cambie la ganancia.
    · Agrega LiquidacionesSemanales.IdempotencyKey (uniqueidentifier, NULL) — evita que un
      doble click o un reintento dupliquen un pago.
    · Crea dos índices únicos FILTRADOS:
          UX_Categorias_TenantId_SystemCode            WHERE SystemCode IS NOT NULL
          UX_LiquidacionesSemanales_TenantId_IdempotencyKey  WHERE IdempotencyKey IS NOT NULL
    · Backfill CONSERVADOR de SystemCode: solo etiqueta una categoría cuando el tenant tiene
      EXACTAMENTE UNA con ese nombre. Ante cualquier ambigüedad la deja en NULL a propósito y
      el motor conserva para ella el criterio histórico por nombre (cero regresión).

    ── QUÉ *NO* HACE ──────────────────────────────────────────────────────────────────
    · NO toca Cobros, pagos, liquidaciones históricas ni egresos.
    · NO hace backfill fiscal ni de remuneración (eso es Fase 4 y Fase 5).
    · NO contiene nada de Fase 4 ni de Fase 5.
    · NO contiene ningún TenantId hardcodeado.
    · NO adivina categorías ambiguas.

    ── SOBRE TenantSecurityPolicy ─────────────────────────────────────────────────────
    El backfill apaga la política momentáneamente porque la sesión de despliegue no tiene
    TenantId en SESSION_CONTEXT y el FILTER PREDICATE dejaría el UPDATE en cero filas.
    La ventana es la de un solo UPDATE sobre Categorias y está protegida por partida doble:
      a) TRY/CATCH que la vuelve a encender ante cualquier error, y después relanza;
      b) la transacción que envuelve todo el script: si el batch aborta, el ALTER SECURITY
         POLICY también se revierte (el DDL en SQL Server es transaccional).
    Es la ÚNICA de las tres fases que apaga la política. Fase 4 y Fase 5 nunca lo hacen.

    ── ROLLBACK: NO USAR EL Down DE ESTA MIGRACIÓN EN PRODUCCIÓN ──────────────────────
    El Down de Fase 3 ELIMINA la columna LiquidacionesSemanales.IdempotencyKey. Una vez que
    producción empiece a registrar pagos con clave de idempotencia, revertir el esquema por
    Down BORRA esas claves: se pierde la protección contra pagos duplicados de los pagos ya
    registrados, y esa información no se puede reconstruir.

    Estrategia de rollback correcta después del deploy:
      1. NO ejecutar el Down de Fase 3 sobre producción con tráfico financiero real.
      2. Si el esquema nuevo quedó aplicado pero la aplicación nueva falla:
         volver PRIMERO al BINARIO ANTERIOR. Las columnas nuevas son aditivas y nullable,
         así que el código viejo funciona igual contra el esquema nuevo.
      3. Conservar el esquema. No hace falta revertirlo para volver a la versión anterior.
      4. Si de verdad hay que revertir la BASE: RESTAURAR EL BACKUP PREDEPLOY.
         El Down NO es un mecanismo de recuperación de datos.

    VERIFICAR DESPUÉS:
        SELECT name, is_enabled FROM sys.security_policies;   -- is_enabled debe ser 1
        SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
*/

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    ALTER TABLE [LiquidacionesSemanales] ADD [IdempotencyKey] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    ALTER TABLE [Categorias] ADD [SystemCode] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    DECLARE @policyOff bit = 0;

    IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantSecurityPolicy' AND is_enabled = 1)
    BEGIN
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy] WITH (STATE = OFF);
        SET @policyOff = 1;
    END

    BEGIN TRY
        /*  El UPDATE va dentro de EXEC a propósito: SQL Server compila el batch COMPLETO
            antes de ejecutarlo, y en el script idempotente de despliegue este bloque
            viaja en el MISMO batch que el "ALTER TABLE Categorias ADD SystemCode". Sin
            el EXEC, el compilador todavía no conoce la columna y rechaza el batch entero
            con "Invalid column name 'SystemCode'" — es decir, el script no correría
            nunca desde el estado real de producción.

            Es exactamente la misma técnica que EF aplica por su cuenta a los CREATE
            INDEX de esta migración; acá hace falta hacerlo a mano porque el SQL es
            manual. Las comillas simples van duplicadas por estar anidadas.

            La semántica NO cambia: mismas categorías, misma guarda COUNT(*) = 1, misma
            transacción. El EXEC corre en el mismo ámbito transaccional, así que la
            atomicidad se conserva, y un error dentro del SQL dinámico lo sigue
            capturando el CATCH de abajo. */
        EXEC (N'
            /*  EXEC() arranca el batch dinámico con QUOTED_IDENTIFIER OFF, y la tabla
                Categorias tiene un índice FILTRADO
                (UX_Categorias_TenantId_Nombre_PagoFuncionarios). SQL Server exige
                QUOTED_IDENTIFIER ON para cualquier DML sobre una tabla con índices
                filtrados; sin esto el UPDATE falla con el error 1934. No cambia el
                significado de nada: QUOTED_IDENTIFIER solo afecta a las comillas
                DOBLES, y acá todos los literales usan comillas simples. */
            SET QUOTED_IDENTIFIER ON;

            ;WITH Mapa(Nombre, SystemCode) AS
            (
                SELECT * FROM (VALUES
                    (N''Pago Funcionarios'',              N''EmployeeSettlement''),
                    (N''Distribución a inversionistas'',  N''InvestorDistribution''),
                    (N''Costo laboral extraordinario'',   N''ExtraordinaryLaborCost'')
                ) AS v(Nombre, SystemCode)
            )
            UPDATE c
            SET c.SystemCode = m.SystemCode
            FROM dbo.Categorias AS c
            INNER JOIN Mapa AS m ON c.Nombre = m.Nombre
            WHERE c.SystemCode IS NULL
              AND (
                    SELECT COUNT(*)
                    FROM dbo.Categorias AS d
                    WHERE d.TenantId = c.TenantId
                      AND d.Nombre = m.Nombre
                  ) = 1;');
    END TRY
    BEGIN CATCH
        IF @policyOff = 1
            ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy] WITH (STATE = ON);
        THROW;
    END CATCH

    IF @policyOff = 1
        ALTER SECURITY POLICY [dbo].[TenantSecurityPolicy] WITH (STATE = ON);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_LiquidacionesSemanales_TenantId_IdempotencyKey] ON [LiquidacionesSemanales] ([TenantId], [IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Categorias_TenantId_SystemCode] ON [Categorias] ([TenantId], [SystemCode]) WHERE [SystemCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915040514_AddFinancialHardeningSystemCategoryAndIdempotency', N'10.0.2');
END;

COMMIT;
GO

