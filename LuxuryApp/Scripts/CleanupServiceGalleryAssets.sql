-- =============================================================================
-- CleanupServiceGalleryAssets - Retiro de la galeria/"trabajos" por servicio
-- =============================================================================
-- El producto ahora usa una sola imagen principal por servicio. La galeria por
-- servicio (TenantPublicAssetType.ServiceGallery = 5) fue eliminada de la UI y del
-- pipeline. Este script hace soft-delete de las imagenes de galeria por servicio que
-- ya existan, para dejar de mostrarlas y LIBERAR su cuota de almacenamiento publico
-- (el calculo de cuota solo considera assets con IsActive = 1 y DeletedAtUtc IS NULL).
--
-- SEGURO PARA PRODUCCION:
--   * Idempotente (se puede correr varias veces; solo afecta filas aun activas).
--   * NO borra imagenes principales (ServiceMain = 4), logo, portada, ubicacion ni galeria del negocio.
--   * NO cambia esquema; solo hace soft-delete de datos.
--   * Multi-tenant: aplica a TODOS los tenants a la vez (columna global TenantPublicAssets).
--
-- NOTA: los archivos fisicos en el storage/S3 correspondientes quedan huerfanos e
-- inofensivos (no cuentan para la cuota). Si se desea, pueden limpiarse aparte.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @Now datetime2(7) = SYSUTCDATETIME();

UPDATE [TenantPublicAssets]
   SET [IsActive]     = 0,
       [DeletedAtUtc] = @Now,
       [UpdatedAtUtc] = @Now
 WHERE [AssetType]    = 5          -- ServiceGallery
   AND [IsActive]     = 1
   AND [DeletedAtUtc] IS NULL;

DECLARE @Afectadas int = @@ROWCOUNT;
PRINT CONCAT('Imagenes de galeria por servicio desactivadas: ', @Afectadas);
