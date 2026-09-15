/*
    FinancialPostDeployVerification.sql
    -----------------------------------
    Verificación financiera DESPUÉS de aplicar AddFinancialHardeningSystemCategoryAndIdempotency.

    SOLO LECTURA. No hay UPDATE, DELETE, INSERT, MERGE ni DDL en este archivo.

    Cómo leerlo: un result set VACÍO es la respuesta buena, salvo en los bloques marcados
    como INVENTARIO, que son informativos.

    Nota sobre RLS: ejecutarlo con una cuenta de administración; si TenantSecurityPolicy filtra
    la sesión, los conteos saldrán en cero y el script no estará viendo nada.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

/*  0. PUERTA DE ENTRADA — NO QUITAR. Ver la explicación en FinancialPreDeployAudit.sql:
    bajo RLS sin TenantId en sesión, este script reportaría "todo limpio" sin mirar nada. */
PRINT '=== 0. Puerta: ¿esta sesión puede ver los datos? ===';

DECLARE @rlsActivo bit =
    CASE WHEN EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantSecurityPolicy' AND is_enabled = 1)
         THEN 1 ELSE 0 END;
DECLARE @tenantEnSesion uniqueidentifier = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);

SELECT @rlsActivo AS TenantSecurityPolicyActiva,
       @tenantEnSesion AS TenantIdDeLaSesion,
       (SELECT COUNT(*) FROM dbo.Categorias) AS CategoriasVisibles,
       (SELECT COUNT(*) FROM dbo.Egresos)    AS EgresosVisibles;

IF @rlsActivo = 1 AND @tenantEnSesion IS NULL
BEGIN
    RAISERROR (N'ABORTADO: la política RLS está activa y la sesión no tiene TenantId. Los resultados serían falsos.', 16, 1) WITH NOWAIT;
END
GO

PRINT '=== 1. La migración aplicó estructura (columnas e índices esperados) ===';
SELECT
    CASE WHEN COL_LENGTH('dbo.Categorias', 'SystemCode') IS NULL
         THEN 'FALTA Categorias.SystemCode' ELSE 'OK Categorias.SystemCode' END AS Columna_SystemCode,
    CASE WHEN COL_LENGTH('dbo.LiquidacionesSemanales', 'IdempotencyKey') IS NULL
         THEN 'FALTA LiquidacionesSemanales.IdempotencyKey' ELSE 'OK IdempotencyKey' END AS Columna_IdempotencyKey,
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Categorias_TenantId_SystemCode')
         THEN 'OK' ELSE 'FALTA' END AS UX_Categorias_TenantId_SystemCode,
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LiquidacionesSemanales_TenantId_IdempotencyKey')
         THEN 'OK' ELSE 'FALTA' END AS UX_Liquidaciones_IdempotencyKey;
GO

PRINT '=== 2. INVENTARIO: categorías del sistema por tenant ===';
SELECT c.TenantId, c.SystemCode, c.Id, c.Nombre, c.Activo
FROM dbo.Categorias AS c
WHERE c.SystemCode IS NOT NULL
ORDER BY c.TenantId, c.SystemCode;
GO

PRINT '=== 3. SystemCode DUPLICADO por tenant (el índice único debería hacerlo imposible) ===';
SELECT c.TenantId, c.SystemCode, COUNT(*) AS Cantidad
FROM dbo.Categorias AS c
WHERE c.SystemCode IS NOT NULL
GROUP BY c.TenantId, c.SystemCode
HAVING COUNT(*) > 1;
GO

PRINT '=== 4. SystemCode con un valor NO reconocido por el código ===';
SELECT c.TenantId, c.Id, c.Nombre, c.SystemCode
FROM dbo.Categorias AS c
WHERE c.SystemCode IS NOT NULL
  AND c.SystemCode NOT IN (N'EmployeeSettlement', N'InvestorDistribution', N'ExtraordinaryLaborCost');
GO

PRINT '=== 5. Categorías que TODAVÍA dependen del nombre (backfill no las pudo resolver) ===';
PRINT '    Estas siguen funcionando igual que antes gracias al criterio histórico por nombre,';
PRINT '    PERO renombrarlas sí cambiaría la ganancia. Unificarlas y asignarles código manualmente.';
SELECT c.TenantId, c.Id, c.Nombre, c.Activo
FROM dbo.Categorias AS c
WHERE c.SystemCode IS NULL
  AND c.Nombre IN (N'Pago Funcionarios', N'Distribución a inversionistas', N'Costo laboral extraordinario')
ORDER BY c.TenantId, c.Nombre;
GO

PRINT '=== 6. Tenants con liquidaciones y SIN categoría EmployeeSettlement identificada ===';
SELECT l.TenantId, COUNT(*) AS Liquidaciones
FROM dbo.LiquidacionesSemanales AS l
WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Categorias AS c
        WHERE c.TenantId = l.TenantId AND c.SystemCode = N'EmployeeSettlement')
GROUP BY l.TenantId;
GO

PRINT '=== 7. Integridad Egreso <-> Liquidación ===';
SELECT 'EgresoInexistente' AS Problema, l.TenantId, l.Id AS LiquidacionId, l.EgresoId, NULL AS Diferencia
FROM dbo.LiquidacionesSemanales AS l
WHERE l.EgresoId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Egresos AS e WHERE e.IdEgreso = l.EgresoId)
UNION ALL
SELECT 'MontoDistinto', l.TenantId, l.Id, l.EgresoId, e.Monto - l.MontoTotal
FROM dbo.LiquidacionesSemanales AS l
INNER JOIN dbo.Egresos AS e ON e.IdEgreso = l.EgresoId
WHERE e.Monto <> l.MontoTotal
UNION ALL
SELECT 'LiquidacionSinEgreso', l.TenantId, l.Id, NULL, NULL
FROM dbo.LiquidacionesSemanales AS l
WHERE l.EgresoId IS NULL;
GO

PRINT '=== 8. Egreso de categoría EmployeeSettlement HUÉRFANO (sin liquidación viva) ===';
PRINT '    Una reversión bien hecha borra los dos juntos: acá no debería quedar nada nuevo.';
PRINT '    Lo que aparezca es histórico previo al hardening (pagos legacy).';
SELECT e.TenantId, e.IdEgreso, e.FechaEgreso, e.Monto, e.Detalle
FROM dbo.Egresos AS e
INNER JOIN dbo.Categorias AS c ON c.Id = e.CategoriaId
WHERE c.SystemCode = N'EmployeeSettlement'
  AND NOT EXISTS (SELECT 1 FROM dbo.LiquidacionesSemanales AS l WHERE l.EgresoId = e.IdEgreso)
ORDER BY e.FechaEgreso DESC;
GO

PRINT '=== 9. Claves de idempotencia repetidas dentro de un tenant (imposible con el índice) ===';
SELECT l.TenantId, l.IdempotencyKey, COUNT(*) AS Cantidad
FROM dbo.LiquidacionesSemanales AS l
WHERE l.IdempotencyKey IS NOT NULL
GROUP BY l.TenantId, l.IdempotencyKey
HAVING COUNT(*) > 1;
GO

PRINT '=== 10. INVENTARIO: mismo GUID de idempotencia usado por tenants distintos (es válido) ===';
SELECT l.IdempotencyKey, COUNT(DISTINCT l.TenantId) AS Tenants
FROM dbo.LiquidacionesSemanales AS l
WHERE l.IdempotencyKey IS NOT NULL
GROUP BY l.IdempotencyKey
HAVING COUNT(DISTINCT l.TenantId) > 1;
GO

PRINT '=== 11. Invariantes de importe ===';
SELECT 'MontoTotalDistintoDeDetalles' AS Problema, l.TenantId, l.Id AS LiquidacionId,
       l.MontoTotal - SUM(d.MontoPagado) AS Diferencia
FROM dbo.LiquidacionesSemanales AS l
INNER JOIN dbo.LiquidacionesSemanalesDetalle AS d ON d.LiquidacionSemanalId = l.Id
GROUP BY l.TenantId, l.Id, l.MontoTotal
HAVING l.MontoTotal <> SUM(d.MontoPagado);
GO

SELECT 'PendienteNegativo' AS Problema, l.TenantId, d.Id AS DetalleId, d.Pendiente
FROM dbo.LiquidacionesSemanalesDetalle AS d
INNER JOIN dbo.LiquidacionesSemanales AS l ON l.Id = d.LiquidacionSemanalId
WHERE d.Pendiente < 0;
GO

PRINT '=== 12. Registros huérfanos de liquidación (deberían morir con la reversión) ===';
SELECT 'DetalleHuerfano' AS Problema, d.TenantId, d.Id AS RegistroId
FROM dbo.LiquidacionesSemanalesDetalle AS d
WHERE NOT EXISTS (SELECT 1 FROM dbo.LiquidacionesSemanales AS l WHERE l.Id = d.LiquidacionSemanalId)
UNION ALL
SELECT 'DistribucionHuerfana', dm.TenantId, dm.Id
FROM dbo.LiquidacionesSemanalesDistribucionMensual AS dm
WHERE NOT EXISTS (SELECT 1 FROM dbo.LiquidacionesSemanales AS l WHERE l.Id = dm.LiquidacionSemanalId);
GO

PRINT '=== 13. INVENTARIO: reversiones de pago registradas en la bitácora ===';
SELECT TOP (200)
    a.CreatedAtUtc, a.TenantId, a.EntityId AS LiquidacionId, a.ActorEmail, a.Reason
FROM dbo.PlatformAuditLogs AS a
WHERE a.Action = N'EmployeeSettlementPaymentReverted'
ORDER BY a.CreatedAtUtc DESC;
GO

PRINT '=== 14. Reversión registrada pero la liquidación sigue viva (inconsistencia grave) ===';
SELECT a.TenantId, a.EntityId AS LiquidacionId, a.CreatedAtUtc
FROM dbo.PlatformAuditLogs AS a
WHERE a.Action = N'EmployeeSettlementPaymentReverted'
  AND EXISTS (
        SELECT 1 FROM dbo.LiquidacionesSemanales AS l
        WHERE CAST(l.Id AS nvarchar(450)) = a.EntityId
          AND l.TenantId = a.TenantId);
GO

PRINT '=== 15. FASE 4: estructura del snapshot aplicada ===';
SELECT
    CASE WHEN COL_LENGTH('dbo.Cobros','AplicaIvaSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS AplicaIvaSnapshot,
    CASE WHEN COL_LENGTH('dbo.Cobros','TarifaIvaSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS TarifaIvaSnapshot,
    CASE WHEN COL_LENGTH('dbo.Cobros','PrecioIncluyeIvaSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS PrecioIncluyeIvaSnapshot,
    CASE WHEN COL_LENGTH('dbo.Cobros','DetalleSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS DetalleSnapshot,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Cobros_SnapshotFiscal')
         THEN 'OK' ELSE 'FALTA' END AS CK_Cobros_SnapshotFiscal,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints
                      WHERE name = 'CK_Cobros_SnapshotFiscal' AND is_disabled = 0 AND is_not_trusted = 0)
         THEN 'OK' ELSE 'REVISAR (deshabilitado o no confiable)' END AS CK_Estado;
GO

PRINT '=== 16. FASE 4: RLS activo sobre las tres tablas de liquidaciones ===';
PRINT '    Las tres deben mostrar Filter=1, BlockInsert=1, BlockUpdate=1. Cualquier 0 es un hueco.';
SELECT
    t.name AS Tabla,
    SUM(CASE WHEN p.predicate_type = 0 THEN 1 ELSE 0 END) AS Filter,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 1 THEN 1 ELSE 0 END) AS BlockInsert,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 2 THEN 1 ELSE 0 END) AS BlockUpdate
FROM sys.tables AS t
LEFT JOIN sys.security_predicates AS p ON p.target_object_id = t.object_id
WHERE t.name IN (N'LiquidacionesSemanales', N'LiquidacionesSemanalesDetalle', N'LiquidacionesSemanalesDistribucionMensual')
GROUP BY t.name
ORDER BY t.name;
GO

PRINT '=== 17. FASE 4: la política quedó ENCENDIDA ===';
SELECT name, is_enabled
FROM sys.security_policies
WHERE name = N'TenantSecurityPolicy';
GO

PRINT '=== 18. FASE 4: tablas con TenantId que siguen SIN ningún predicado ===';
PRINT '    Después del deploy no debería aparecer ninguna tabla financiera.';
SELECT t.name AS Tabla
FROM sys.tables AS t
INNER JOIN sys.columns AS c ON c.object_id = t.object_id AND c.name = 'TenantId'
WHERE NOT EXISTS (SELECT 1 FROM sys.security_predicates AS p WHERE p.target_object_id = t.object_id)
ORDER BY t.name;
GO

PRINT '=== 19. FASE 4: snapshot PARCIAL (el CHECK debería hacerlo imposible) ===';
SELECT co.TenantId, co.IdCobro, co.FechaCobro,
       co.AplicaIvaSnapshot, co.TarifaIvaSnapshot, co.PrecioIncluyeIvaSnapshot
FROM dbo.Cobros AS co
WHERE (co.AplicaIvaSnapshot IS NULL OR co.TarifaIvaSnapshot IS NULL OR co.PrecioIncluyeIvaSnapshot IS NULL)
  AND NOT (co.AplicaIvaSnapshot IS NULL AND co.TarifaIvaSnapshot IS NULL AND co.PrecioIncluyeIvaSnapshot IS NULL);
GO

PRINT '=== 20. FASE 4: cobros NUEVOS sin snapshot (¡la app no los estaría congelando!) ===';
PRINT '    Cómo leerlo: reemplazá la fecha por el momento REAL del arranque de la versión nueva.';
PRINT '    Cualquier cobro posterior a ese instante DEBE traer snapshot. Si aparecen filas acá,';
PRINT '    hay un camino de escritura que no pasa por CobroService y hay que cerrarlo.';
DECLARE @arranqueVersionNueva datetime2(0) = CAST(CAST(SYSDATETIME() AS date) AS datetime2(0));

SELECT co.TenantId, co.IdCobro, co.FechaCobro, co.Monto, co.ServicioId, co.ProductoId
FROM dbo.Cobros AS co
WHERE co.AplicaIvaSnapshot IS NULL
  AND co.FechaCobro >= @arranqueVersionNueva
ORDER BY co.FechaCobro DESC;
GO

PRINT '=== 21. INVENTARIO: avance del snapshot por tenant ===';
SELECT
    co.TenantId,
    COUNT(*) AS CobrosTotales,
    SUM(CASE WHEN co.AplicaIvaSnapshot IS NOT NULL THEN 1 ELSE 0 END) AS ConSnapshot,
    SUM(CASE WHEN co.AplicaIvaSnapshot IS NULL THEN 1 ELSE 0 END) AS Legacy,
    MAX(CASE WHEN co.AplicaIvaSnapshot IS NOT NULL THEN co.FechaCobro END) AS UltimoConSnapshot
FROM dbo.Cobros AS co
GROUP BY co.TenantId
ORDER BY co.TenantId;
GO

PRINT '=== 22. FASE 5: estructura del snapshot de remuneración aplicada ===';
SELECT
    CASE WHEN COL_LENGTH('dbo.Cobros','PorcentajeServicioSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS PorcentajeServicio,
    CASE WHEN COL_LENGTH('dbo.Cobros','PorcentajeProductoSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS PorcentajeProducto,
    CASE WHEN COL_LENGTH('dbo.Cobros','ComisionCalculadaSobreSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS ComisionSobre,
    CASE WHEN COL_LENGTH('dbo.Cobros','TipoRelacionColaboradorSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS TipoRelacion,
    CASE WHEN COL_LENGTH('dbo.Cobros','ModalidadIvaColaboradorSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS ModalidadIva,
    CASE WHEN COL_LENGTH('dbo.Cobros','TarifaIvaColaboradorSnapshot') IS NULL THEN 'FALTA' ELSE 'OK' END AS TarifaColaborador;
GO

PRINT '=== 23. FASE 5: constraints financieras habilitadas y CONFIABLES ===';
PRINT '    is_disabled=0 e is_not_trusted=0. Un CHECK creado con NOCHECK no valida las filas viejas';
PRINT '    y el motor tampoco lo usa para optimizar: sería una garantía de papel.';
SELECT name AS Constraint_, is_disabled, is_not_trusted
FROM sys.check_constraints
WHERE name IN (N'CK_Cobros_SnapshotFiscal', N'CK_Cobros_SnapshotRemuneracion')
ORDER BY name;
GO

PRINT '=== 24. FASE 5: snapshot de remuneración PARCIAL ===';
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
       AND co.ModalidadIvaColaboradorSnapshot IS NULL AND co.TarifaIvaColaboradorSnapshot IS NULL);
GO

PRINT '=== 25. FASE 5: cobros NUEVOS sin snapshot COMPLETO (fiscal + remuneración) ===';
PRINT '    Reemplazá la fecha por el instante REAL del arranque de la versión nueva. Cualquier';
PRINT '    cobro posterior DEBE traer los dos snapshots; si aparece alguno, hay un bypass abierto.';
DECLARE @arranqueFase5 datetime2(0) = CAST(CAST(SYSDATETIME() AS date) AS datetime2(0));

SELECT co.TenantId, co.IdCobro, co.FechaCobro, co.Monto,
       CASE WHEN co.AplicaIvaSnapshot IS NULL THEN 'FALTA FISCAL' ELSE 'ok' END AS Fiscal,
       CASE WHEN co.PorcentajeServicioSnapshot IS NULL THEN 'FALTA REMUNERACION' ELSE 'ok' END AS Remuneracion
FROM dbo.Cobros AS co
WHERE co.FechaCobro >= @arranqueFase5
  AND (co.AplicaIvaSnapshot IS NULL OR co.PorcentajeServicioSnapshot IS NULL)
ORDER BY co.FechaCobro DESC;
GO

PRINT '=== 26. FASE 5: RLS de comprobantes (BLOCK sí, FILTER no — es deliberado) ===';
SELECT
    t.name AS Tabla,
    SUM(CASE WHEN p.predicate_type = 0 THEN 1 ELSE 0 END) AS Filter_EsperadoCero,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 1 THEN 1 ELSE 0 END) AS BlockInsert,
    SUM(CASE WHEN p.predicate_type = 1 AND p.operation = 2 THEN 1 ELSE 0 END) AS BlockUpdate
FROM sys.tables AS t
LEFT JOIN sys.security_predicates AS p ON p.target_object_id = t.object_id
WHERE t.name IN (N'ComprobantesCobro', N'ComprobanteCobroLineas')
GROUP BY t.name
ORDER BY t.name;
GO

PRINT '=== 27. INVENTARIO: avance de los DOS snapshots por tenant ===';
SELECT
    co.TenantId,
    COUNT(*) AS CobrosTotales,
    SUM(CASE WHEN co.AplicaIvaSnapshot IS NOT NULL THEN 1 ELSE 0 END) AS ConSnapshotFiscal,
    SUM(CASE WHEN co.PorcentajeServicioSnapshot IS NOT NULL THEN 1 ELSE 0 END) AS ConSnapshotRemuneracion,
    SUM(CASE WHEN co.AplicaIvaSnapshot IS NULL AND co.PorcentajeServicioSnapshot IS NULL THEN 1 ELSE 0 END) AS LegacyCompleto
FROM dbo.Cobros AS co
GROUP BY co.TenantId
ORDER BY co.TenantId;
GO

PRINT '=== FIN. Todo vacío (salvo los INVENTARIO) = deploy verificado. ===';
GO
