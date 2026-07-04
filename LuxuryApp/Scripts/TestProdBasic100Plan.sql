-- =============================================================================
-- TEST_PROD_BASIC_100 - Plan de prueba controlada de TiloPay Repeat en PRODUCCION
-- =============================================================================
-- Plan publico de CRC 100.00 mapeado al plan recurrente TiloPay produccion 6106.
-- Objetivo: validar suscriptor + repeat_registration + repeat_payment_success +
-- activacion automatica con una compra real de bajo monto.
--
-- SEGURO PARA PRODUCCION:
--   * Idempotente (se puede correr varias veces).
--   * NO toca el plan BASIC ni ningun plan real.
--   * NO modifica suscripciones ni pagos existentes.
--   * Solo cambio de DATOS (una fila en Planes), sin cambio de esquema.
--
-- El mapping recurrente (TilopayPlanId 6106, monto, hosted link) vive en
-- appsettings.Production.json -> TilopayRepeat:TestProdBasic100. La fila de Planes
-- se enlaza por Codigo = 'TEST_PROD_BASIC_100'.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @PlanId uniqueidentifier = 'A1C2E3F4-6106-4A64-9D04-3A70B6D4F100';
DECLARE @Codigo nvarchar(50)     = N'TEST_PROD_BASIC_100';
DECLARE @Nombre nvarchar(50)     = N'LuxuryCloud Test Producción';

IF EXISTS (SELECT 1 FROM [Planes] WHERE [Codigo] = @Codigo)
BEGIN
    UPDATE [Planes]
       SET [Nombre]           = @Nombre,
           [Moneda]           = N'CRC',
           [PrecioMensual]    = CAST(100.00 AS decimal(18,2)),
           [Activo]           = CAST(1 AS bit),
           [EsPlanValidacion] = CAST(0 AS bit),
           [MaxFuncionarios]  = 1
     WHERE [Codigo] = @Codigo;

    PRINT 'TEST_PROD_BASIC_100: plan actualizado.';
END
ELSE
BEGIN
    INSERT INTO [Planes]
        ([Id], [Codigo], [Nombre], [ProviderProductId], [ProviderPriceId],
         [Moneda], [PrecioMensual], [Activo], [EsPlanValidacion], [MaxFuncionarios])
    VALUES
        (@PlanId, @Codigo, @Nombre, NULL, NULL,
         N'CRC', CAST(100.00 AS decimal(18,2)), CAST(1 AS bit), CAST(0 AS bit), 1);

    PRINT 'TEST_PROD_BASIC_100: plan creado.';
END;

-- Verificacion inmediata
SELECT [Id], [Codigo], [Nombre], [PrecioMensual], [Moneda],
       [MaxFuncionarios], [Activo], [EsPlanValidacion]
FROM [Planes]
WHERE [Codigo] = @Codigo;
GO

-- =============================================================================
-- TEARDOWN (ejecutar DESPUES de la prueba) - desactivar sin tocar datos reales
-- =============================================================================
-- Desactiva el plan: desaparece de /Billing/Planes (la query filtra Activo=1).
-- No borra historia de pagos/suscripciones de la prueba.
--
--   UPDATE [Planes] SET [Activo] = CAST(0 AS bit)
--   WHERE [Codigo] = N'TEST_PROD_BASIC_100';
--
-- Borrado opcional SOLO si no quedo ninguna suscripcion/pago asociado:
--
--   DELETE FROM [Planes]
--   WHERE [Codigo] = N'TEST_PROD_BASIC_100'
--     AND NOT EXISTS (SELECT 1 FROM [Suscripciones] s
--                     JOIN [Planes] p ON p.[Id] = s.[PlanId]
--                     WHERE p.[Codigo] = N'TEST_PROD_BASIC_100')
--     AND NOT EXISTS (SELECT 1 FROM [PagosSuscripcion] pg
--                     JOIN [Planes] p ON p.[Id] = pg.[PlanId]
--                     WHERE p.[Codigo] = N'TEST_PROD_BASIC_100');
-- =============================================================================
