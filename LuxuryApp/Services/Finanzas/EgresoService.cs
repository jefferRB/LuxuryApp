using LuxuryApp.Models.Finanzas;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Finanzas
{
    public sealed class EgresoService : IEgresoService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EgresoService> _logger;

        public EgresoService(ApplicationDbContext context, ILogger<EgresoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task RegistrarAsync(EgresoCreateRequest request, CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeRequest(request);
            ValidateRequest(normalizedRequest);

            try
            {
                await EnsureCategoriaActivaAsync(normalizedRequest.CategoriaId, cancellationToken);

                _context.Egresos.Add(new Egreso
                {
                    FechaEgreso = normalizedRequest.FechaEgreso,
                    Detalle = normalizedRequest.Detalle,
                    Monto = normalizedRequest.Monto,
                    MetodoPago = normalizedRequest.MetodoPago,
                    CategoriaId = normalizedRequest.CategoriaId
                });

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (EgresoValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al registrar egreso para categoria {CategoriaId}.", normalizedRequest.CategoriaId);
                throw new InvalidOperationException("No fue posible registrar el egreso.");
            }
            catch (InvalidOperationException ex) when (ex is not EgresoValidationException)
            {
                _logger.LogError(ex, "Operacion invalida al registrar egreso para categoria {CategoriaId}.", normalizedRequest.CategoriaId);
                throw;
            }
        }

        private async Task EnsureCategoriaActivaAsync(int categoriaId, CancellationToken cancellationToken)
        {
            var exists = await _context.Categorias
                .AsNoTracking()
                .AnyAsync(c => c.Id == categoriaId && c.Activo, cancellationToken);

            if (!exists)
            {
                throw new EgresoValidationException(
                    "La categoria seleccionada no existe, no esta activa o no pertenece al tenant actual.",
                    "Egreso.CategoriaId");
            }
        }

        private static EgresoCreateRequest NormalizeRequest(EgresoCreateRequest request) =>
            new()
            {
                FechaEgreso = NormalizeEgresoDateTime(request.FechaEgreso),
                Detalle = CollapseWhitespace(request.Detalle),
                Monto = Math.Round(request.Monto, 2, MidpointRounding.AwayFromZero),
                MetodoPago = string.IsNullOrWhiteSpace(request.MetodoPago)
                    ? string.Empty
                    : request.MetodoPago.Trim().ToUpperInvariant(),
                CategoriaId = request.CategoriaId
            };

        private static void ValidateRequest(EgresoCreateRequest request)
        {
            if (request.CategoriaId <= 0)
            {
                throw new EgresoValidationException("Debe seleccionar una categoria valida.", "Egreso.CategoriaId");
            }

            if (string.IsNullOrWhiteSpace(request.Detalle))
            {
                throw new EgresoValidationException("Debe indicar el detalle del egreso.", "Egreso.Detalle");
            }

            if (request.Detalle.Length > 200)
            {
                throw new EgresoValidationException("El detalle del egreso no puede exceder 200 caracteres.", "Egreso.Detalle");
            }

            if (request.Monto <= 0 || request.Monto > 999999)
            {
                throw new EgresoValidationException("Debe indicar un monto mayor a cero y dentro del rango permitido.", "Egreso.Monto");
            }

            if (!EgresoPaymentCatalog.IsAllowed(request.MetodoPago))
            {
                throw new EgresoValidationException("El metodo de pago seleccionado no es valido.", "Egreso.MetodoPago");
            }
        }

        private static DateTime NormalizeEgresoDateTime(DateTime value)
        {
            var source = value == default ? DateTime.Now : value;
            return new DateTime(
                source.Year,
                source.Month,
                source.Day,
                source.Hour,
                source.Minute,
                0);
        }

        private static string CollapseWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(
                ' ',
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
}
