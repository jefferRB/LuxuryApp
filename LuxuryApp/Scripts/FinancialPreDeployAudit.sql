/*
    FinancialPreDeployAudit.sql
    ---------------------------
    Auditoría financiera ANTES de aplicar la migración de hardening
    (AddFinancialHardeningSystemCategoryAndIdempotency).

    SOLO LECTURA. No hay UPDATE, DELETE, INSERT, MERGE ni DDL en este archivo.
    No arregla nada: reporta lo que una persona debe decidir.

    Objetivo: detectar los casos que harían que el backfill de SystemCode quedara incompleto
    o que dejarían inconsistencias financieras arrastradas a la versión nueva.

    Cómo leerlo: cada bloque imprime un título y devuelve un result set. Un result set VACÍO
    es la respuesta buena. Todo lo que aparezca necesita revisión manual antes del deploy.

    Nota sobre RLS: si lo ejecuta una cuenta sujeta a TenantSecurityPolicy sin TenantId en
    SESSION_CONTEXT, los conteos saldrán en cero y el script NO estará viendo nada. Ejecutarlo
    con una cuenta de administración (o con la política desactivada en una ventana controlada).
*/


/*  IMPORTANTE — asimetría de RLS.
    Las tablas LiquidacionesSemanales* NO tienen política de RLS, mientras Egresos, Categorias y
    Cobros SÍ la tienen. Con un TenantId en sesión, eso significa que se ven las liquidaciones de
    TODOS los tenants pero solo los egresos de UNO: cualquier consulta que cruce ambos daría
    falsos positivos ("el egreso no existe" cuando simplemente no es visible).

    Por eso todos los bloques que leen liquidaciones filtran explícitamente por el tenant de la
    sesión cuando hay uno. Para auditar todos los tenants de una sola pasada hay que ejecutar el
    script con una cuenta exenta del RLS.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

/*  0. PUERTA DE ENTRADA — NO QUITAR.

    fnTenantAccess compara TenantId contra SESSION_CONTEXT('TenantId'). Si la política está
    activa y la sesión NO tiene TenantId, las tablas con RLS devuelven CERO filas y este script
    reportaría "todo limpio" sin haber mirado nada. Peor: los bloques que preguntan
    "¿existe el egreso de esta liquidación?" darían FALSOS POSITIVOS, porque Egresos se ve vacía
    mientras LiquidacionesSemanales (que hoy NO tiene RLS) se ve completa.

    Por eso el script se detiene en vez de mentir. Ejecutar con `sqlcmd -b` para que aborte.
*/
PRINT '=== 0. Puerta: ¿esta sesión puede ver los datos? ===';

DECLARE @rlsActivo bit =
    CASE WHEN EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantSecurityPolicy' AND is_enabled = 1)
         THEN 1 ELSE 0 END;

DECLARE @tenantEnSesion uniqueidentifier = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);

SELECT
    @rlsActivo                                         AS TenantSecurityPolicyActiva,
    @tenantEnSesion                                    AS TenantIdDeLaSesion,
    (SELECT COUNT(*) FROM dbo.Categorias)              AS CategoriasVisibles,
    (SELECT COUNT(*) FROM dbo.Egresos)                 AS EgresosVisibles,
    (SELECT COUNT(*) FROM dbo.Cobros)                  AS CobrosVisibles,
    (SELECT COUNT(*) FROM dbo.LiquidacionesSemanales)  AS LiquidacionesVisibles;

IF @rlsActivo = 1 AND @tenantEnSesion IS NULL
BEGIN
    RAISERROR (N'ABORTADO: la política RLS está activa y la sesión no tiene TenantId. Las tablas protegidas se ven vacías y los resultados serían falsos. Ejecutá este script con una cuenta exenta del RLS, o desactivá TenantSecurityPolicy en una ventana controlada, o fijá SESSION_CONTEXT para auditar un tenant a la vez.', 16, 1) WITH NOWAIT;
END
GO

PRINT '=== 0b. Cobertura de RLS en las tablas financieras (informativo) ===';
PRINT '    Una tabla financiera SIN RLS depende únicamente del filtro de EF para el aislamiento.';
SELECT t.name AS Tabla,
       CASE WHEN EXISTS (SELECT 1 FROM sys.security_predicates p WHERE p.target_object_id = t.object_id)
            THEN 'RLS' ELSE 'SIN RLS' END AS Estado
FROM sys.tables AS t
WHERE t.name IN (N'Categorias', N'Egresos', N'Cobros', N'Funcionarios', N'Servicios', N'Productos',
                 N'PagosFuncionarios', N'LiquidacionesSemanales', N'LiquidacionesSemanalesDetalle',
                 N'LiquidacionesSemanalesDistribucionMensual')
ORDER BY Estado, t.name;
GO

PRINT '=== 1. LiquidacionSemanal cuyo EgresoId no existe (referencia rota) ===';
SELECT l.TenantId, l.Id AS LiquidacionId, l.EgresoId, l.FechaPago, l.MontoTotal
FROM dbo.LiquidacionesSemanales AS l
WHERE l.EgresoId IS NOT NULL
  AND (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
  AND NOT EXISTS (SELECT 1 FROM dbo.Egresos AS e WHERE e.IdEgreso = l.EgresoId)
ORDER BY l.TenantId, l.FechaPago;
GO

PRINT '=== 2. Egreso vinculado cuyo monto NO coincide con el de la liquidación ===';
PRINT '    (es el síntoma de un egreso editado a mano; la versión nueva ya lo impide)';
SELECT
    l.TenantId,
    l.Id            AS LiquidacionId,
    l.FechaPago,
    l.MontoTotal    AS MontoLiquidacion,
    e.IdEgreso,
    e.FechaEgreso,
    e.Monto         AS MontoEgreso,
    e.Monto - l.MontoTotal AS Diferencia
FROM dbo.LiquidacionesSemanales AS l
INNER JOIN dbo.Egresos AS e ON e.IdEgreso = l.EgresoId
WHERE e.Monto <> l.MontoTotal
  AND (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
ORDER BY ABS(e.Monto - l.MontoTotal) DESC;
GO

PRINT '=== 3. Liquidación SIN egreso asociado (pagado sin salida de caja registrada) ===';
SELECT l.TenantId, l.Id AS LiquidacionId, l.FechaPago, l.MontoTotal, l.Observacion
FROM dbo.LiquidacionesSemanales AS l
WHERE l.EgresoId IS NULL
  AND (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
ORDER BY l.TenantId, l.FechaPago;
GO

PRINT '=== 4. Categorías del sistema DUPLICADAS por tenant (bloquean el backfill determinista) ===';
PRINT '    Si aparece algo acá, el backfill dejará esas filas en NULL a propósito: hay que';
PRINT '    unificarlas manualmente ANTES del deploy y volver a correr este script.';
SELECT c.TenantId, c.Nombre, COUNT(*) AS Cantidad
FROM dbo.Categorias AS c
WHERE c.Nombre IN (N'Pago Funcionarios', N'Distribución a inversionistas', N'Costo laboral extraordinario')
GROUP BY c.TenantId, c.Nombre
HAVING COUNT(*) > 1
ORDER BY c.TenantId, c.Nombre;
GO

PRINT '=== 5. Categorías con nombres PARECIDOS a los del sistema (posible duplicado disfrazado) ===';
PRINT '    No son un error por sí mismas; se listan para decidir si deben unificarse.';
SELECT c.TenantId, c.Id, c.Nombre, c.Activo
FROM dbo.Categorias AS c
WHERE c.Nombre NOT IN (N'Pago Funcionarios', N'Distribución a inversionistas', N'Costo laboral extraordinario')
  AND (
        c.Nombre LIKE N'%funcionario%'
     OR c.Nombre LIKE N'%colaborador%'
     OR c.Nombre LIKE N'%planilla%'
     OR c.Nombre LIKE N'%inversionista%'
     OR c.Nombre LIKE N'%vacacion%'
     OR c.Nombre LIKE N'%bono%'
      )
ORDER BY c.TenantId, c.Nombre;
GO

PRINT '=== 6. Tenants con liquidaciones pero SIN categoría "Pago Funcionarios" ===';
SELECT l.TenantId, COUNT(*) AS Liquidaciones
FROM dbo.LiquidacionesSemanales AS l
WHERE (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
  AND NOT EXISTS (
        SELECT 1 FROM dbo.Categorias AS c
        WHERE c.TenantId = l.TenantId AND c.Nombre = N'Pago Funcionarios')
GROUP BY l.TenantId;
GO

PRINT '=== 7. Egresos en "Pago Funcionarios" SIN liquidación asociada ===';
PRINT '    HOY estos NO reducen la ganancia. Vacaciones, bonos o ajustes que estén acá deberían';
PRINT '    reclasificarse manualmente a "Costo laboral extraordinario". Los pagos legacy de';
PRINT '    comisiones deben QUEDARSE donde están (ya se restaron como devengado).';
SELECT e.TenantId, e.IdEgreso, e.FechaEgreso, e.Monto, e.Detalle, e.MetodoPago
FROM dbo.Egresos AS e
INNER JOIN dbo.Categorias AS c ON c.Id = e.CategoriaId
WHERE c.Nombre = N'Pago Funcionarios'
  AND NOT EXISTS (SELECT 1 FROM dbo.LiquidacionesSemanales AS l WHERE l.EgresoId = e.IdEgreso)
ORDER BY e.TenantId, e.FechaEgreso DESC;
GO

PRINT '=== 8. Posibles pagos DUPLICADOS (mismo tenant, funcionario, periodo, fecha y monto) ===';
PRINT '    Es exactamente el patrón que dejaba el doble click antes de la clave de idempotencia.';
SELECT
    l.TenantId,
    d.FuncionarioId,
    l.SemanaInicio,
    l.SemanaFin,
    CAST(l.FechaPago AS date) AS FechaPago,
    d.MontoPagado,
    COUNT(*)                          AS Veces,
    STRING_AGG(CAST(l.Id AS nvarchar(20)), ', ') AS LiquidacionIds
FROM dbo.LiquidacionesSemanalesDetalle AS d
INNER JOIN dbo.LiquidacionesSemanales  AS l ON l.Id = d.LiquidacionSemanalId
WHERE (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
GROUP BY l.TenantId, d.FuncionarioId, l.SemanaInicio, l.SemanaFin, CAST(l.FechaPago AS date), d.MontoPagado
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC, l.TenantId;
GO

PRINT '=== 9. Liquidación cuyo MontoTotal != suma de sus detalles ===';
SELECT
    l.TenantId,
    l.Id AS LiquidacionId,
    l.MontoTotal,
    SUM(d.MontoPagado) AS SumaDetalles,
    l.MontoTotal - SUM(d.MontoPagado) AS Diferencia
FROM dbo.LiquidacionesSemanales AS l
INNER JOIN dbo.LiquidacionesSemanalesDetalle AS d ON d.LiquidacionSemanalId = l.Id
WHERE (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
GROUP BY l.TenantId, l.Id, l.MontoTotal
HAVING l.MontoTotal <> SUM(d.MontoPagado)
ORDER BY ABS(l.MontoTotal - SUM(d.MontoPagado)) DESC;
GO

PRINT '=== 10. Distribución mensual cuyo total != MontoTotal de la liquidación ===';
SELECT
    l.TenantId,
    l.Id AS LiquidacionId,
    l.MontoTotal,
    SUM(dm.MontoAsignado) AS SumaDistribuido,
    l.MontoTotal - SUM(dm.MontoAsignado) AS Diferencia
FROM dbo.LiquidacionesSemanales AS l
INNER JOIN dbo.LiquidacionesSemanalesDistribucionMensual AS dm ON dm.LiquidacionSemanalId = l.Id
WHERE (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
GROUP BY l.TenantId, l.Id, l.MontoTotal
HAVING l.MontoTotal <> SUM(dm.MontoAsignado)
ORDER BY ABS(l.MontoTotal - SUM(dm.MontoAsignado)) DESC;
GO

PRINT '=== 11. Montos negativos o en cero donde no deberían existir ===';
SELECT 'Liquidacion' AS Origen, l.TenantId, l.Id AS RegistroId, l.MontoTotal AS Monto
FROM dbo.LiquidacionesSemanales AS l WHERE l.MontoTotal <= 0 AND (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier))
UNION ALL
SELECT 'Egreso', e.TenantId, e.IdEgreso, e.Monto FROM dbo.Egresos AS e WHERE e.Monto <= 0
UNION ALL
SELECT 'Cobro', co.TenantId, co.IdCobro, co.Monto FROM dbo.Cobros AS co WHERE co.Monto <= 0
UNION ALL
SELECT 'DetallePendienteNegativo', l2.TenantId, d.Id, d.Pendiente
FROM dbo.LiquidacionesSemanalesDetalle AS d
INNER JOIN dbo.LiquidacionesSemanales AS l2 ON l2.Id = d.LiquidacionSemanalId
WHERE d.Pendiente < 0
  AND (CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) IS NULL OR l2.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier));
GO

PRINT '=== 12. TenantId vacío en tablas financieras (no debería existir nunca) ===';
SELECT 'Categorias' AS Tabla, COUNT(*) AS Filas FROM dbo.Categorias WHERE TenantId = 0x0
UNION ALL SELECT 'Egresos', COUNT(*) FROM dbo.Egresos WHERE TenantId = 0x0
UNION ALL SELECT 'Cobros', COUNT(*) FROM dbo.Cobros WHERE TenantId = 0x0
UNION ALL SELECT 'LiquidacionesSemanales', COUNT(*) FROM dbo.LiquidacionesSemanales WHERE TenantId = 0x0
UNION ALL SELECT 'LiquidacionesSemanalesDetalle', COUNT(*) FROM dbo.LiquidacionesSemanalesDetalle WHERE TenantId = 0x0;
GO

PRINT '=== 13. Cobros que ninguna pantalla financiera contaría (ni servicio, ni producto) ===';
PRINT '    No deberían poder crearse desde la aplicación; si aparecen, vinieron por otra vía.';
SELECT co.TenantId, co.IdCobro, co.FechaCobro, co.Monto, co.NombreCliente
FROM dbo.Cobros AS co
WHERE co.ServicioId IS NULL
  AND co.ProductoId IS NULL
  AND (co.ServicioNombrePersonalizado IS NULL OR LTRIM(RTRIM(co.ServicioNombrePersonalizado)) = N'')
ORDER BY co.TenantId, co.FechaCobro DESC;
GO

PRINT '=== 14. COBERTURA RLS: tablas con TenantId que NO están en la política ===';
PRINT '    Antes de la Fase 4 las tres tablas de liquidaciones aparecían acá. Después de migrar';
PRINT '    la lista debería quedar vacía o contener solo tablas deliberadamente fuera de RLS';
PRINT '    (PlatformAuditLogs, reservas públicas y el auto-reply de WhatsApp lo están a propósito).';
SELECT t.name AS Tabla
FROM sys.tables AS t
INNER JOIN sys.columns AS c
    ON c.object_id = t.object_id AND c.name = 'TenantId'
WHERE NOT EXISTS (
        SELECT 1 FROM sys.security_predicates AS p
        WHERE p.target_object_id = t.object_id)
ORDER BY t.name;
GO

PRINT '=== 15. COBERTURA RLS: tablas protegidas a MEDIAS (les falta FILTER o algún BLOCK) ===';
PRINT '    El patrón completo del sistema es FILTER + BLOCK AFTER INSERT + BLOCK AFTER UPDATE.';
SELECT
    t.name AS Tabla,
    SUM(CASE WHEN p.predicate_type = 0 THEN 1 ELSE 0 END) AS Filter,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 1 THEN 1 ELSE 0 END) AS BlockInsert,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 2 THEN 1 ELSE 0 END) AS BlockUpdate
FROM sys.tables AS t
INNER JOIN sys.security_predicates AS p ON p.target_object_id = t.object_id
GROUP BY t.name
HAVING SUM(CASE WHEN p.predicate_type = 0 THEN 1 ELSE 0 END) <> 1
    OR SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 1 THEN 1 ELSE 0 END) <> 1
    OR SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 2 THEN 1 ELSE 0 END) <> 1
ORDER BY t.name;
GO

PRINT '=== 16. INVENTARIO: predicados DUPLICADOS sobre la misma tabla y operación ===';
SELECT OBJECT_NAME(p.target_object_id) AS Tabla, p.predicate_type_desc, p.operation_desc, COUNT(*) AS Cantidad
FROM sys.security_predicates AS p
GROUP BY OBJECT_NAME(p.target_object_id), p.predicate_type_desc, p.operation_desc
HAVING COUNT(*) > 1
ORDER BY Tabla;
GO

PRINT '=== 17. INVENTARIO: cuántos cobros quedarán LEGACY (sin snapshot fiscal) ===';
PRINT '    Es el universo que seguirá dependiendo del catálogo actual. NO se va a rellenar:';
PRINT '    no podemos demostrar qué configuración regía cuando se registraron.';
/*  Se ejecuta con EXEC a propósito: antes de migrar la columna no existe y SQL Server
    compila el batch COMPLETO antes de correrlo, así que un IF normal igual fallaría. */
IF COL_LENGTH('dbo.Cobros', 'AplicaIvaSnapshot') IS NULL
    SELECT 'La migración de Fase 4 todavía NO se aplicó: la columna no existe.' AS Estado;
ELSE
    EXEC (N'
        SELECT
            co.TenantId,
            COUNT(*) AS CobrosTotales,
            SUM(CASE WHEN co.AplicaIvaSnapshot IS NULL THEN 1 ELSE 0 END) AS Legacy,
            SUM(CASE WHEN co.AplicaIvaSnapshot IS NOT NULL THEN 1 ELSE 0 END) AS ConSnapshot,
            MIN(co.FechaCobro) AS Desde,
            MAX(co.FechaCobro) AS Hasta
        FROM dbo.Cobros AS co
        GROUP BY co.TenantId
        ORDER BY co.TenantId;');
GO

PRINT '=== 18. Snapshot fiscal PARCIAL (lo prohíbe CK_Cobros_SnapshotFiscal) ===';
PRINT '    Cualquier fila acá significa que el CHECK no está activo o se escribió por fuera de la app.';
IF COL_LENGTH('dbo.Cobros', 'AplicaIvaSnapshot') IS NOT NULL
    EXEC (N'
        SELECT co.TenantId, co.IdCobro, co.FechaCobro,
               co.AplicaIvaSnapshot, co.TarifaIvaSnapshot, co.PrecioIncluyeIvaSnapshot
        FROM dbo.Cobros AS co
        WHERE (co.AplicaIvaSnapshot IS NULL OR co.TarifaIvaSnapshot IS NULL OR co.PrecioIncluyeIvaSnapshot IS NULL)
          AND NOT (co.AplicaIvaSnapshot IS NULL AND co.TarifaIvaSnapshot IS NULL AND co.PrecioIncluyeIvaSnapshot IS NULL)
        ORDER BY co.FechaCobro DESC;');
GO

PRINT '=== 19. Snapshot fiscal con valores IMPOSIBLES ===';
PRINT '    Tarifa negativa, mayor a 100, o gravado con tarifa cero.';
IF COL_LENGTH('dbo.Cobros', 'AplicaIvaSnapshot') IS NOT NULL
    EXEC (N'
        SELECT co.TenantId, co.IdCobro, co.FechaCobro, co.AplicaIvaSnapshot, co.TarifaIvaSnapshot
        FROM dbo.Cobros AS co
        WHERE co.AplicaIvaSnapshot IS NOT NULL
          AND (co.TarifaIvaSnapshot < 0
               OR co.TarifaIvaSnapshot > 100
               OR (co.AplicaIvaSnapshot = 1 AND co.TarifaIvaSnapshot = 0))
        ORDER BY co.FechaCobro DESC;');
GO

PRINT '=== 20. INTEGRIDAD TenantId: filas hijas que apuntan a un padre de OTRO tenant ===';
SELECT 'Cobro->Servicio' AS Relacion, co.TenantId, co.IdCobro AS FilaId
FROM dbo.Cobros AS co
INNER JOIN dbo.Servicios AS se ON se.Id = co.ServicioId
WHERE se.TenantId <> co.TenantId
UNION ALL
SELECT 'Cobro->Funcionario', co.TenantId, co.IdCobro
FROM dbo.Cobros AS co
INNER JOIN dbo.Funcionarios AS fu ON fu.IdFuncionario = co.FuncionarioId
WHERE fu.TenantId <> co.TenantId
UNION ALL
SELECT 'Egreso->Categoria', eg.TenantId, eg.IdEgreso
FROM dbo.Egresos AS eg
INNER JOIN dbo.Categorias AS ca ON ca.Id = eg.CategoriaId
WHERE ca.TenantId <> eg.TenantId
UNION ALL
SELECT 'Detalle->Liquidacion', de.TenantId, de.Id
FROM dbo.LiquidacionesSemanalesDetalle AS de
INNER JOIN dbo.LiquidacionesSemanales AS li ON li.Id = de.LiquidacionSemanalId
WHERE li.TenantId <> de.TenantId;
GO

PRINT '=== 21. FASE 5: snapshot de REMUNERACIÓN parcial (lo prohíbe CK_Cobros_SnapshotRemuneracion) ===';
PRINT '    Una fila acá significa que el CHECK no está activo o que algo escribió por fuera de la app.';
IF COL_LENGTH('dbo.Cobros', 'PorcentajeServicioSnapshot') IS NOT NULL
    EXEC (N'
        SELECT co.TenantId, co.IdCobro, co.FechaCobro,
               co.PorcentajeServicioSnapshot, co.PorcentajeProductoSnapshot,
               co.ComisionCalculadaSobreSnapshot, co.TipoRelacionColaboradorSnapshot,
               co.ModalidadIvaColaboradorSnapshot, co.TarifaIvaColaboradorSnapshot
        FROM dbo.Cobros AS co
        WHERE (co.PorcentajeServicioSnapshot IS NULL OR co.PorcentajeProductoSnapshot IS NULL
               OR co.ComisionCalculadaSobreSnapshot IS NULL OR co.TipoRelacionColaboradorSnapshot IS NULL
               OR co.ModalidadIvaColaboradorSnapshot IS NULL OR co.TarifaIvaColaboradorSnapshot IS NULL)
          AND NOT (co.PorcentajeServicioSnapshot IS NULL AND co.PorcentajeProductoSnapshot IS NULL
               AND co.ComisionCalculadaSobreSnapshot IS NULL AND co.TipoRelacionColaboradorSnapshot IS NULL
               AND co.ModalidadIvaColaboradorSnapshot IS NULL AND co.TarifaIvaColaboradorSnapshot IS NULL)
        ORDER BY co.FechaCobro DESC;');
GO

PRINT '=== 22. FASE 5: cobros con snapshot FISCAL pero SIN snapshot de remuneración ===';
PRINT '    Debería quedar solo la ventana entre el deploy de Fase 4 y el de Fase 5. Si aparecen';
PRINT '    cobros POSTERIORES al arranque de la versión nueva, hay una ruta de escritura que no';
PRINT '    pasa por CobroService y hay que cerrarla.';
IF COL_LENGTH('dbo.Cobros', 'PorcentajeServicioSnapshot') IS NOT NULL
    EXEC (N'
        SELECT co.TenantId, COUNT(*) AS Cobros, MIN(co.FechaCobro) AS Desde, MAX(co.FechaCobro) AS Hasta
        FROM dbo.Cobros AS co
        WHERE co.AplicaIvaSnapshot IS NOT NULL
          AND co.PorcentajeServicioSnapshot IS NULL
        GROUP BY co.TenantId;');
GO

PRINT '=== 23. FASE 5: snapshot de remuneración con valores IMPOSIBLES ===';
PRINT '    Porcentajes fuera de 0-100, o tarifa de IVA del colaborador negativa / mayor a 100.';
IF COL_LENGTH('dbo.Cobros', 'PorcentajeServicioSnapshot') IS NOT NULL
    EXEC (N'
        SELECT co.TenantId, co.IdCobro, co.FechaCobro,
               co.PorcentajeServicioSnapshot, co.PorcentajeProductoSnapshot, co.TarifaIvaColaboradorSnapshot
        FROM dbo.Cobros AS co
        WHERE co.PorcentajeServicioSnapshot IS NOT NULL
          AND (co.PorcentajeServicioSnapshot < 0 OR co.PorcentajeServicioSnapshot > 100
               OR co.PorcentajeProductoSnapshot < 0 OR co.PorcentajeProductoSnapshot > 100
               OR co.TarifaIvaColaboradorSnapshot < 0 OR co.TarifaIvaColaboradorSnapshot > 100)
        ORDER BY co.FechaCobro DESC;');
GO

PRINT '=== 24. INVENTARIO: producción LEGACY por colaborador (sin snapshot de remuneración) ===';
PRINT '    Es lo que se reinterpreta si se le cambia el porcentaje. Alimenta el aviso de la UI.';
IF COL_LENGTH('dbo.Cobros', 'PorcentajeServicioSnapshot') IS NULL
    SELECT 'La migración de Fase 5 todavía NO se aplicó: la columna no existe.' AS Estado;
ELSE
    EXEC (N'
        SELECT co.TenantId, f.Nombre AS Colaborador,
               SUM(CASE WHEN co.PorcentajeServicioSnapshot IS NULL THEN 1 ELSE 0 END) AS Legacy,
               SUM(CASE WHEN co.PorcentajeServicioSnapshot IS NOT NULL THEN 1 ELSE 0 END) AS ConSnapshot
        FROM dbo.Cobros AS co
        INNER JOIN dbo.Funcionarios AS f ON f.IdFuncionario = co.FuncionarioId
        GROUP BY co.TenantId, f.Nombre
        ORDER BY co.TenantId, f.Nombre;');
GO

PRINT '=== 25. INVENTARIO: configuración de remuneración VIGENTE de cada colaborador ===';
PRINT '    Referencia para interpretar los cobros legacy, que se liquidan con estos valores.';
SELECT f.TenantId, f.IdFuncionario, f.Nombre, f.Activo,
       f.PorcentajeGanancia, f.PorcentajeProducto, f.ComisionCalculadaSobre,
       f.TipoRelacionColaborador, f.ModalidadIvaColaborador, f.TarifaIvaFacturaColaborador
FROM dbo.Funcionarios AS f
ORDER BY f.TenantId, f.Nombre;
GO

PRINT '=== 26. FASE 5: RLS de comprobantes ===';
PRINT '    Se esperan BlockInsert=1 y BlockUpdate=1. Filter=0 es CORRECTO y deliberado: la ruta';
PRINT '    pública /comprobantes/{token} es anónima y un FILTER rompería los comprobantes enviados.';
SELECT
    t.name AS Tabla,
    SUM(CASE WHEN p.predicate_type = 0 THEN 1 ELSE 0 END) AS Filter,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 1 THEN 1 ELSE 0 END) AS BlockInsert,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 2 THEN 1 ELSE 0 END) AS BlockUpdate
FROM sys.tables AS t
LEFT JOIN sys.security_predicates AS p ON p.target_object_id = t.object_id
WHERE t.name IN (N'ComprobantesCobro', N'ComprobanteCobroLineas')
GROUP BY t.name
ORDER BY t.name;
GO

PRINT '=== FIN. Todo result set vacío = listo para migrar. ===';
GO
