/* ============================================================================
   Configuración inicial de un inversionista — PLANTILLA POST-DESPLIEGUE
   ----------------------------------------------------------------------------
   Este script NO forma parte de la migración a propósito: la migración crea el
   esquema y nada más. Ningún dato de un negocio real vive en el código fuente.

   Uso previsto: la vía normal es la interfaz (/Inversionistas → "Nuevo
   inversionista"), que aplica todas las validaciones de negocio. Este script es
   solo el camino alterno para configurar sin sesión y deja los mismos datos.

   ANTES DE EJECUTAR
   -----------------
   1. Reemplazá los valores del bloque de parámetros.
   2. Ejecutalo dentro de la ventana de mantenimiento y con respaldo reciente.
   3. Corré primero el bloque de verificación del final con la transacción
      abierta; hacé COMMIT solo si los números cuadran.

   VALIDACIONES QUE LA UI HACE Y ESTE SCRIPT REPRODUCE
   ---------------------------------------------------
   - La participación acumulada de acuerdos vigentes no puede pasar de 100 %.
   - La fecha efectiva debe ser el primer día de un periodo (día 1 para mensual).
   - No puede haber dos inversionistas con el mismo correo en el negocio.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- ─────────────── Parámetros ───────────────
DECLARE @TenantNombre    nvarchar(150) = N'REEMPLAZAR_NOMBRE_DEL_NEGOCIO';
DECLARE @Nombre          nvarchar(150) = N'REEMPLAZAR_NOMBRE_DEL_INVERSIONISTA';
DECLARE @Email           nvarchar(256) = N'reemplazar@correo.test';
DECLARE @Telefono        nvarchar(30)  = NULL;
DECLARE @Porcentaje      decimal(9,4)  = 45.0000;
DECLARE @VigenteDesde    date          = '2026-08-01';   -- día 1 para frecuencia mensual
DECLARE @Frecuencia      int           = 2;              -- 0 Semanal · 1 Quincenal · 2 Mensual
DECLARE @Perdidas        int           = 0;              -- 0 NoDistribution · 1 CarryForward
DECLARE @EnvioAutomatico bit           = 0;

DECLARE @TenantId uniqueidentifier;

SELECT @TenantId = Id
FROM dbo.Tenants
WHERE Nombre = @TenantNombre;

IF @TenantId IS NULL
BEGIN
    RAISERROR(N'No se encontró el negocio indicado. Revisá @TenantNombre.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ── Validación 1: correo no repetido dentro del negocio ──
IF EXISTS (
    SELECT 1 FROM dbo.TenantInvestors
    WHERE TenantId = @TenantId AND Email = LOWER(@Email))
BEGIN
    RAISERROR(N'Ya existe un inversionista con ese correo en este negocio.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ── Validación 2: fecha efectiva = inicio de periodo ──
IF (@Frecuencia = 2 AND DAY(@VigenteDesde) <> 1)
   OR (@Frecuencia = 1 AND DAY(@VigenteDesde) NOT IN (1, 16))
   OR (@Frecuencia = 0 AND DATEPART(WEEKDAY, @VigenteDesde) <> ((@@DATEFIRST + 0) % 7) + 1)
BEGIN
    RAISERROR(N'La fecha efectiva debe ser el primer día de un periodo de esa frecuencia.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ── Validación 3: la participación acumulada no puede pasar de 100 % ──
DECLARE @Ocupado decimal(9,4);

SELECT @Ocupado = ISNULL(SUM(acuerdo.ParticipacionPorcentaje), 0)
FROM dbo.InvestorAgreements AS acuerdo
INNER JOIN dbo.TenantInvestors AS inversionista
    ON inversionista.Id = acuerdo.InvestorId
WHERE acuerdo.TenantId = @TenantId
  AND acuerdo.Activo = 1
  AND inversionista.Activo = 1
  AND (acuerdo.EffectiveTo IS NULL OR acuerdo.EffectiveTo >= @VigenteDesde);

IF (@Ocupado + @Porcentaje) > 100
BEGIN
    RAISERROR(N'La participación acumulada superaría el 100%%. Revisá los acuerdos vigentes.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ─────────────── Inserción ───────────────
DECLARE @Ahora datetime2 = SYSUTCDATETIME();
DECLARE @InvestorId int;

INSERT INTO dbo.TenantInvestors
    (TenantId, Nombre, Email, Telefono, Activo, NotasInternas, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (@TenantId, @Nombre, LOWER(@Email), @Telefono, 1, N'Configurado por script post-despliegue.', @Ahora, @Ahora);

SET @InvestorId = SCOPE_IDENTITY();

INSERT INTO dbo.InvestorAgreements
    (TenantId, InvestorId, ParticipacionPorcentaje, EffectiveFrom, EffectiveTo,
     Frecuencia, TratamientoPerdidas, EnvioAutomatico, Activo, Notas, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (@TenantId, @InvestorId, @Porcentaje, @VigenteDesde, NULL,
     @Frecuencia, @Perdidas, @EnvioAutomatico, 1, N'Acuerdo inicial.', @Ahora, @Ahora);

-- Política de cálculo por defecto si el negocio todavía no la tiene
-- (IVA excluido, liquidaciones incluidas, todas las categorías de gasto).
IF NOT EXISTS (SELECT 1 FROM dbo.InvestorProfitPolicies WHERE TenantId = @TenantId)
BEGIN
    INSERT INTO dbo.InvestorProfitPolicies
        (TenantId, ExcluirIva, IncluirLiquidaciones, BaseLiquidaciones, ModoCategoriasGasto,
         TratamientoPerdidasPorDefecto, FrecuenciaPorDefecto, GeneracionAutomatica, EnvioAutomatico,
         DiasEsperaGeneracion, HoraGeneracion, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (@TenantId, 1, 1, 0, 0, @Perdidas, @Frecuencia, 0, 0, 1, 8, @Ahora, @Ahora);
END

-- Categoría reservada para registrar la salida de caja hacia inversionistas.
-- El motor de cálculo la excluye siempre: pagar al inversionista NO reduce la ganancia.
IF NOT EXISTS (
    SELECT 1 FROM dbo.Categorias
    WHERE TenantId = @TenantId AND Nombre = N'Distribución a inversionistas')
BEGIN
    INSERT INTO dbo.Categorias (TenantId, Nombre, Detalle, Activo)
    VALUES (@TenantId, N'Distribución a inversionistas',
            N'Salidas de dinero hacia inversionistas. Excluida del cálculo de la ganancia distribuible.', 1);
END

-- ─────────────── Verificación (revisar ANTES del COMMIT) ───────────────
SELECT
    inversionista.Id            AS InversionistaId,
    inversionista.Nombre,
    inversionista.Email,
    acuerdo.ParticipacionPorcentaje,
    acuerdo.EffectiveFrom,
    acuerdo.Frecuencia,
    acuerdo.TratamientoPerdidas,
    ParticipacionTotalVigente = @Ocupado + @Porcentaje
FROM dbo.TenantInvestors AS inversionista
INNER JOIN dbo.InvestorAgreements AS acuerdo
    ON acuerdo.InvestorId = inversionista.Id
WHERE inversionista.Id = @InvestorId;

-- Si los datos son correctos:
--   COMMIT TRANSACTION;
-- Si algo no cuadra:
--   ROLLBACK TRANSACTION;
ROLLBACK TRANSACTION;   -- ← seguro por defecto: cambiar a COMMIT tras verificar
