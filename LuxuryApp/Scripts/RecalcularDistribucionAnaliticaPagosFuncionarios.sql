/*
    Recalcula LiquidacionesSemanalesDistribucionMensual usando cobros reales
    en lugar de prorrateo por días calendario.

    Importante:
    - Respeta la caja real: no toca Egresos ni LiquidacionesSemanales.
    - Recalcula solo la capa analítica mensual.
    - Usa los porcentajes actuales de Funcionarios. Si ya hubo cambios de porcentaje
      después del cobro, el resultado será consistente con la lógica actual del módulo
      de Cobros, pero no congela histórico.
    - Revisa primero en backup o staging.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#DistribucionBase') IS NOT NULL DROP TABLE #DistribucionBase;
IF OBJECT_ID('tempdb..#DistribucionRedondeada') IS NOT NULL DROP TABLE #DistribucionRedondeada;
IF OBJECT_ID('tempdb..#LiquidacionesSinBase') IS NOT NULL DROP TABLE #LiquidacionesSinBase;

;WITH CobrosLiquidacion AS
(
    SELECT
        l.Id AS LiquidacionId,
        d.FuncionarioId,
        CAST(c.FechaCobro AS date) AS FechaCobroFecha,
        YEAR(c.FechaCobro) AS Anio,
        MONTH(c.FechaCobro) AS Mes,
        CAST(
            (
                c.Monto - (c.Monto * 0.13)
            ) * (
                CASE
                    WHEN c.ProductoId IS NOT NULL THEN f.PorcentajeProducto
                    ELSE f.PorcentajeGanancia
                END / 100.0
            ) AS decimal(18,6)
        ) AS MontoDevengadoCobro,
        CAST(d.MontoPagado AS decimal(18,6)) AS MontoPagadoDetalle
    FROM LiquidacionesSemanales l
    INNER JOIN LiquidacionesSemanalesDetalle d
        ON d.LiquidacionSemanalId = l.Id
    INNER JOIN Funcionarios f
        ON f.IdFuncionario = d.FuncionarioId
       AND f.TenantId = d.TenantId
    INNER JOIN Cobros c
        ON c.FuncionarioId = d.FuncionarioId
       AND c.TenantId = d.TenantId
       AND c.FechaCobro >= CAST(l.SemanaInicio AS date)
       AND c.FechaCobro < DATEADD(day, 1, CAST(l.SemanaFin AS date))
),
BaseFuncionarioMes AS
(
    SELECT
        LiquidacionId,
        FuncionarioId,
        Anio,
        Mes,
        SUM(MontoDevengadoCobro) AS MontoDevengadoMes
    FROM CobrosLiquidacion
    WHERE MontoDevengadoCobro > 0
    GROUP BY LiquidacionId, FuncionarioId, Anio, Mes
),
DiasLiquidacionMes AS
(
    SELECT
        LiquidacionId,
        Anio,
        Mes,
        COUNT(DISTINCT FechaCobroFecha) AS DiasAplicados
    FROM CobrosLiquidacion
    WHERE MontoDevengadoCobro > 0
    GROUP BY LiquidacionId, Anio, Mes
),
BaseFuncionarioTotal AS
(
    SELECT
        LiquidacionId,
        FuncionarioId,
        SUM(MontoDevengadoMes) AS MontoDevengadoSemana
    FROM BaseFuncionarioMes
    GROUP BY LiquidacionId, FuncionarioId
),
DistribucionFuncionarioMes AS
(
    SELECT
        b.LiquidacionId,
        b.Anio,
        b.Mes,
        CAST(
            CASE
                WHEN t.MontoDevengadoSemana <= 0 THEN 0
                ELSE d.MontoPagado * b.MontoDevengadoMes / t.MontoDevengadoSemana
            END
            AS decimal(18,6)
        ) AS MontoAsignadoRaw,
        b.DiasAplicados
    FROM BaseFuncionarioMes b
    INNER JOIN BaseFuncionarioTotal t
        ON t.LiquidacionId = b.LiquidacionId
       AND t.FuncionarioId = b.FuncionarioId
    INNER JOIN LiquidacionesSemanalesDetalle d
        ON d.LiquidacionSemanalId = b.LiquidacionId
       AND d.FuncionarioId = b.FuncionarioId
),
DistribucionLiquidacionMes AS
(
    SELECT
        dfm.LiquidacionId,
        dfm.Anio,
        dfm.Mes,
        SUM(dfm.MontoAsignadoRaw) AS MontoAsignadoRaw,
        dlm.DiasAplicados
    FROM DistribucionFuncionarioMes dfm
    INNER JOIN DiasLiquidacionMes dlm
        ON dlm.LiquidacionId = dfm.LiquidacionId
       AND dlm.Anio = dfm.Anio
       AND dlm.Mes = dfm.Mes
    GROUP BY dfm.LiquidacionId, dfm.Anio, dfm.Mes, dlm.DiasAplicados
)
SELECT
    dlm.LiquidacionId,
    dlm.Anio,
    dlm.Mes,
    dlm.MontoAsignadoRaw,
    dlm.DiasAplicados,
    ROW_NUMBER() OVER (PARTITION BY dlm.LiquidacionId ORDER BY dlm.Anio DESC, dlm.Mes DESC) AS RowNumDesc,
    SUM(CAST(ROUND(dlm.MontoAsignadoRaw, 2) AS decimal(18,2))) OVER (PARTITION BY dlm.LiquidacionId) AS TotalRedondeado
INTO #DistribucionBase
FROM DistribucionLiquidacionMes dlm;

SELECT
    l.Id AS LiquidacionId
INTO #LiquidacionesSinBase
FROM LiquidacionesSemanales l
WHERE NOT EXISTS
(
    SELECT 1
    FROM #DistribucionBase db
    WHERE db.LiquidacionId = l.Id
);

SELECT
    db.LiquidacionId,
    db.Anio,
    db.Mes,
    CAST(
        CASE
            WHEN db.RowNumDesc = 1
                THEN l.MontoTotal - (db.TotalRedondeado - CAST(ROUND(db.MontoAsignadoRaw, 2) AS decimal(18,2)))
            ELSE CAST(ROUND(db.MontoAsignadoRaw, 2) AS decimal(18,2))
        END
        AS decimal(18,2)
    ) AS MontoAsignado,
    db.DiasAplicados
INTO #DistribucionRedondeada
FROM #DistribucionBase db
INNER JOIN LiquidacionesSemanales l
    ON l.Id = db.LiquidacionId;

DELETE dm
FROM LiquidacionesSemanalesDistribucionMensual dm
WHERE EXISTS
(
    SELECT 1
    FROM #DistribucionRedondeada dr
    WHERE dr.LiquidacionId = dm.LiquidacionSemanalId
);

INSERT INTO LiquidacionesSemanalesDistribucionMensual
(
    TenantId,
    LiquidacionSemanalId,
    Anio,
    Mes,
    MontoAsignado,
    DiasAplicados
)
SELECT
    l.TenantId,
    dr.LiquidacionId,
    dr.Anio,
    dr.Mes,
    dr.MontoAsignado,
    dr.DiasAplicados
FROM #DistribucionRedondeada dr
INNER JOIN LiquidacionesSemanales l
    ON l.Id = dr.LiquidacionId;

COMMIT TRANSACTION;

SELECT
    'Liquidaciones sin base de cobros para recalcular' AS Mensaje,
    ls.LiquidacionId
FROM #LiquidacionesSinBase ls
ORDER BY ls.LiquidacionId;
