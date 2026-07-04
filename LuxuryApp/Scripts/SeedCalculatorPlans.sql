-- =============================================================================
-- Calculadora dinamica de suscripcion - filas de Planes (LC_M_01..LC_A_11)
-- =============================================================================
-- Crea/actualiza las 22 filas de [Planes] que respaldan la calculadora (1..11
-- funcionarios x Mensual/Anual). Cada fila se enlaza por [Codigo] al mapping
-- recurrente de appsettings -> TilopayRepeat:Calculator (TilopayPlanId + hosted link).
--
-- SEGURO PARA PRODUCCION:
--   * Idempotente (MERGE por Codigo; se puede correr varias veces).
--   * NO toca BASIC/PRO/BUSINESS, WhatsApp, TEST_* ni suscripciones/pagos existentes.
--   * Solo cambio de DATOS en [Planes].
--
-- DEPENDENCIA: requiere la migracion EF "AddPlanBillingCycle" YA aplicada
-- (columnas [BillingCycle] int NOT NULL default 0 y [MonthlyEquivalentAmount] decimal NULL).
--
-- BillingCycle: 0 = Monthly, 1 = Annual.
-- PrecioMensual = monto que cobra TiloPay por ciclo (anual = total anual adelantado).
-- MonthlyEquivalentAmount = equivalente mensual para mostrar (solo display).
-- =============================================================================

SET NOCOUNT ON;

DECLARE @Plans TABLE (
    Id                uniqueidentifier,
    Codigo            nvarchar(50),
    Nombre            nvarchar(50),
    PrecioMensual     decimal(18,2),
    MonthlyEquivalent decimal(18,2),
    MaxFuncionarios   int,
    BillingCycle      int
);

INSERT INTO @Plans (Id, Codigo, Nombre, PrecioMensual, MonthlyEquivalent, MaxFuncionarios, BillingCycle) VALUES
('A2C2E3F4-6100-4A64-9D04-000000006119', N'LC_M_01', N'LuxuryCloud 1 funcionario mensual',   8000.00,   8000.00,  1, 0),
('A2C2E3F4-6100-4A64-9D04-000000006126', N'LC_M_02', N'LuxuryCloud 2 funcionarios mensual', 15000.00,  15000.00,  2, 0),
('A2C2E3F4-6100-4A64-9D04-000000006127', N'LC_M_03', N'LuxuryCloud 3 funcionarios mensual', 20000.00,  20000.00,  3, 0),
('A2C2E3F4-6100-4A64-9D04-000000006128', N'LC_M_04', N'LuxuryCloud 4 funcionarios mensual', 25000.00,  25000.00,  4, 0),
('A2C2E3F4-6100-4A64-9D04-000000006129', N'LC_M_05', N'LuxuryCloud 5 funcionarios mensual', 30000.00,  30000.00,  5, 0),
('A2C2E3F4-6100-4A64-9D04-000000006130', N'LC_M_06', N'LuxuryCloud 6 funcionarios mensual', 35000.00,  35000.00,  6, 0),
('A2C2E3F4-6100-4A64-9D04-000000006131', N'LC_M_07', N'LuxuryCloud 7 funcionarios mensual', 40000.00,  40000.00,  7, 0),
('A2C2E3F4-6100-4A64-9D04-000000006132', N'LC_M_08', N'LuxuryCloud 8 funcionarios mensual', 45000.00,  45000.00,  8, 0),
('A2C2E3F4-6100-4A64-9D04-000000006133', N'LC_M_09', N'LuxuryCloud 9 funcionarios mensual', 50000.00,  50000.00,  9, 0),
('A2C2E3F4-6100-4A64-9D04-000000006134', N'LC_M_10', N'LuxuryCloud 10 funcionarios mensual',55000.00,  55000.00, 10, 0),
('A2C2E3F4-6100-4A64-9D04-000000006135', N'LC_M_11', N'LuxuryCloud 11 funcionarios mensual',60000.00,  60000.00, 11, 0),
('A2C2E3F4-6100-4A64-9D04-000000006136', N'LC_A_01', N'LuxuryCloud 1 funcionario anual',    81600.00,   6800.00,  1, 1),
('A2C2E3F4-6100-4A64-9D04-000000006137', N'LC_A_02', N'LuxuryCloud 2 funcionarios anual',   153000.00,  12750.00,  2, 1),
('A2C2E3F4-6100-4A64-9D04-000000006139', N'LC_A_03', N'LuxuryCloud 3 funcionarios anual',   204000.00,  17000.00,  3, 1),
('A2C2E3F4-6100-4A64-9D04-000000006140', N'LC_A_04', N'LuxuryCloud 4 funcionarios anual',   255000.00,  21250.00,  4, 1),
('A2C2E3F4-6100-4A64-9D04-000000006141', N'LC_A_05', N'LuxuryCloud 5 funcionarios anual',   306000.00,  25500.00,  5, 1),
('A2C2E3F4-6100-4A64-9D04-000000006142', N'LC_A_06', N'LuxuryCloud 6 funcionarios anual',   336000.00,  28000.00,  6, 1),
('A2C2E3F4-6100-4A64-9D04-000000006143', N'LC_A_07', N'LuxuryCloud 7 funcionarios anual',   360000.00,  30000.00,  7, 1),
('A2C2E3F4-6100-4A64-9D04-000000006144', N'LC_A_08', N'LuxuryCloud 8 funcionarios anual',   378000.00,  31500.00,  8, 1),
('A2C2E3F4-6100-4A64-9D04-000000006145', N'LC_A_09', N'LuxuryCloud 9 funcionarios anual',   390000.00,  32500.00,  9, 1),
('A2C2E3F4-6100-4A64-9D04-000000006146', N'LC_A_10', N'LuxuryCloud 10 funcionarios anual',  429000.00,  35750.00, 10, 1),
('A2C2E3F4-6100-4A64-9D04-000000006147', N'LC_A_11', N'LuxuryCloud 11 funcionarios anual',  468000.00,  39000.00, 11, 1);

MERGE [Planes] AS target
USING @Plans AS src
    ON target.[Codigo] = src.[Codigo]
WHEN MATCHED THEN
    UPDATE SET
        [Nombre]                  = src.[Nombre],
        [Moneda]                  = N'CRC',
        [PrecioMensual]           = src.[PrecioMensual],
        [MonthlyEquivalentAmount] = src.[MonthlyEquivalent],
        [MaxFuncionarios]         = src.[MaxFuncionarios],
        [BillingCycle]            = src.[BillingCycle],
        [Activo]                  = CAST(1 AS bit),
        [EsPlanValidacion]        = CAST(0 AS bit),
        [LimiteMensajesMensual]   = NULL
WHEN NOT MATCHED THEN
    INSERT ([Id], [Codigo], [Nombre], [ProviderProductId], [ProviderPriceId],
            [Moneda], [PrecioMensual], [MonthlyEquivalentAmount], [Activo],
            [EsPlanValidacion], [MaxFuncionarios], [LimiteMensajesMensual], [BillingCycle])
    VALUES (src.[Id], src.[Codigo], src.[Nombre], NULL, NULL,
            N'CRC', src.[PrecioMensual], src.[MonthlyEquivalent], CAST(1 AS bit),
            CAST(0 AS bit), src.[MaxFuncionarios], NULL, src.[BillingCycle]);

-- Verificacion inmediata
SELECT [Codigo], [Nombre], [PrecioMensual], [MonthlyEquivalentAmount],
       [MaxFuncionarios], [BillingCycle], [Moneda], [Activo]
FROM [Planes]
WHERE [Codigo] LIKE N'LC[_]_%'
ORDER BY [BillingCycle], [MaxFuncionarios];
GO

-- =============================================================================
-- TEARDOWN (opcional, despues de pruebas) - desactivar sin borrar historia
-- =============================================================================
--   UPDATE [Planes] SET [Activo] = CAST(0 AS bit) WHERE [Codigo] LIKE N'LC[_]_%';
-- =============================================================================
