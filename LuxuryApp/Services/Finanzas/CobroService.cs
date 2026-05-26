using System.Data;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Productos;
using LuxuryApp.Services.BusinessTime;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Finanzas
{
    public sealed class CobroService : ICobroService
    {
        private static readonly HashSet<string> AllowedPaymentMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "EFECTIVO",
            "TARJETA",
            "SINPE"
        };

        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ILogger<CobroService> _logger;

        public CobroService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ILogger<CobroService> logger)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
            _logger = logger;
        }

        public async Task RegistrarAsync(CobroCreateRequest request, CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeRequest(request);
            ValidateRequest(normalizedRequest);

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                await executionStrategy.ExecuteAsync(async () =>
                {
                    var isolationLevel = normalizedRequest.ProductoId.HasValue
                        ? IsolationLevel.Serializable
                        : IsolationLevel.ReadCommitted;

                    await using var transaction = await _context.Database
                        .BeginTransactionAsync(isolationLevel, cancellationToken);

                    await EnsureFuncionarioActivoAsync(
                        normalizedRequest.FuncionarioId,
                        currentFuncionarioId: null,
                        cancellationToken);

                    if (normalizedRequest.ServicioId.HasValue)
                    {
                        var servicio = await LoadServicioAsync(
                            normalizedRequest.ServicioId.Value,
                            currentServicioId: null,
                            cancellationToken);

                        var cobroServicio = BuildCobro(normalizedRequest, normalizedRequest.Monto, servicio.Id, productoId: null);

                        _context.Cobros.Add(cobroServicio);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        var producto = await ReserveProductoAsync(normalizedRequest.ProductoId!.Value, cancellationToken);
                        var cobroProducto = BuildCobro(normalizedRequest, normalizedRequest.Monto, servicioId: null, producto.IdProducto);

                        _context.Cobros.Add(cobroProducto);
                        await _context.SaveChangesAsync(cancellationToken);

                        _context.DetalleCobroProductos.Add(new DetalleCobroProducto
                        {
                            CobroId = cobroProducto.IdCobro,
                            ProductoId = producto.IdProducto,
                            Cantidad = 1,
                            PrecioUnitario = normalizedRequest.Monto,
                            Subtotal = normalizedRequest.Monto
                        });

                        _context.MovimientosInventario.Add(new MovimientoInventario
                        {
                            ProductoId = producto.IdProducto,
                            FechaMovimiento = _businessDateTimeProvider.Now(),
                            TipoMovimiento = "VENTA",
                            Cantidad = 1,
                            StockAnterior = producto.StockAnterior,
                            StockNuevo = producto.StockNuevo,
                            Observacion = $"Venta en cobro #{cobroProducto.IdCobro}"
                        });

                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                });
            }
            catch (CobroValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al registrar cobro para funcionario {FuncionarioId}.", normalizedRequest.FuncionarioId);
                throw new InvalidOperationException("No fue posible registrar el cobro.");
            }
            catch (InvalidOperationException ex) when (ex is not CobroValidationException)
            {
                _logger.LogError(ex, "Operacion invalida al registrar cobro para funcionario {FuncionarioId}.", normalizedRequest.FuncionarioId);
                throw;
            }
        }

        public async Task<bool> ActualizarAsync(
            CobroUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeRequest(request);
            ValidateRequest(normalizedRequest);

            try
            {
                var cobro = await _context.Cobros
                    .Include(c => c.ProductosVendidos)
                    .FirstOrDefaultAsync(c => c.IdCobro == normalizedRequest.IdCobro, cancellationToken);

                if (cobro is null)
                {
                    return false;
                }

                await EnsureFuncionarioActivoAsync(
                    normalizedRequest.FuncionarioId,
                    currentFuncionarioId: cobro.FuncionarioId,
                    cancellationToken);

                cobro.FechaCobro = normalizedRequest.FechaCobro;
                cobro.NombreCliente = normalizedRequest.NombreCliente;
                cobro.FuncionarioId = normalizedRequest.FuncionarioId;
                cobro.Monto = normalizedRequest.Monto;
                cobro.MetodoPago = normalizedRequest.MetodoPago;
                cobro.Observaciones = normalizedRequest.Observaciones;

                if (cobro.ProductoId.HasValue)
                {
                    cobro.ServicioId = null;

                    foreach (var detalle in cobro.ProductosVendidos)
                    {
                        detalle.PrecioUnitario = normalizedRequest.Monto;
                        detalle.Subtotal = normalizedRequest.Monto * detalle.Cantidad;
                    }
                }
                else
                {
                    if (!normalizedRequest.ServicioId.HasValue)
                    {
                        throw new CobroValidationException("Debe seleccionar un servicio.", "Cobro.ServicioId");
                    }

                    var servicio = await LoadServicioAsync(
                        normalizedRequest.ServicioId.Value,
                        cobro.ServicioId,
                        cancellationToken);

                    cobro.ServicioId = servicio.Id;
                    cobro.ProductoId = null;
                }

                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (CobroValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al actualizar cobro {CobroId}.", normalizedRequest.IdCobro);
                throw new InvalidOperationException("No fue posible actualizar el cobro.");
            }
            catch (InvalidOperationException ex) when (ex is not CobroValidationException)
            {
                _logger.LogError(ex, "Operacion invalida al actualizar cobro {CobroId}.", normalizedRequest.IdCobro);
                throw;
            }
        }

        public async Task<bool> EliminarAsync(int idCobro, CancellationToken cancellationToken = default)
        {
            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                return await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                    var cobro = await _context.Cobros
                        .Include(c => c.ProductosVendidos)
                        .FirstOrDefaultAsync(c => c.IdCobro == idCobro, cancellationToken);

                    if (cobro is null)
                    {
                        return false;
                    }

                    await ReverseProductInventoryIfNeededAsync(cobro, cancellationToken);

                    if (cobro.ProductosVendidos.Count > 0)
                    {
                        _context.DetalleCobroProductos.RemoveRange(cobro.ProductosVendidos);
                    }

                    _context.Cobros.Remove(cobro);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return true;
                });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar cobro {CobroId}.", idCobro);
                throw new InvalidOperationException("No fue posible eliminar el cobro.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operacion invalida al eliminar cobro {CobroId}.", idCobro);
                throw;
            }
        }

        private async Task EnsureFuncionarioActivoAsync(
            int funcionarioId,
            int? currentFuncionarioId,
            CancellationToken cancellationToken)
        {
            var exists = await _context.Funcionarios
                .AsNoTracking()
                .AnyAsync(
                    f => f.IdFuncionario == funcionarioId &&
                         (f.Activo || (currentFuncionarioId.HasValue && f.IdFuncionario == currentFuncionarioId.Value)),
                    cancellationToken);

            if (!exists)
            {
                throw new CobroValidationException(
                    "El funcionario seleccionado no existe o no pertenece al tenant actual.",
                    "Cobro.FuncionarioId");
            }
        }

        private async Task<ServicioSnapshot> LoadServicioAsync(
            int servicioId,
            int? currentServicioId,
            CancellationToken cancellationToken)
        {
            var servicio = await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Id == servicioId &&
                            (s.Activo || (currentServicioId.HasValue && s.Id == currentServicioId.Value)))
                .Select(s => new ServicioSnapshot
                {
                    Id = s.Id,
                    Precio = s.Precio
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (servicio is null)
            {
                throw new CobroValidationException(
                    "El servicio seleccionado no existe o no pertenece al tenant actual.",
                    "Cobro.ServicioId");
            }

            return servicio;
        }

        private async Task<ProductoVentaSnapshot> ReserveProductoAsync(int productoId, CancellationToken cancellationToken)
        {
            var affectedRows = await _context.Productos
                .Where(p => p.IdProducto == productoId && p.Activo && p.CantidadProducto > 0)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.CantidadProducto, p => p.CantidadProducto - 1),
                    cancellationToken);

            if (affectedRows == 0)
            {
                var existingProduct = await _context.Productos
                    .AsNoTracking()
                    .Where(p => p.IdProducto == productoId)
                    .Select(p => new
                    {
                        p.Activo,
                        p.CantidadProducto,
                        p.NombreProducto
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                if (existingProduct is null || !existingProduct.Activo)
                {
                    throw new CobroValidationException(
                        "El producto seleccionado no existe o no pertenece al tenant actual.",
                        "Cobro.ProductoId");
                }

                if (existingProduct.CantidadProducto <= 0)
                {
                    throw new CobroValidationException(
                        $"No hay stock disponible para {existingProduct.NombreProducto}.",
                        "Cobro.ProductoId");
                }

                throw new InvalidOperationException("No fue posible reservar inventario para el producto seleccionado.");
            }

            var producto = await _context.Productos
                .AsNoTracking()
                .Where(p => p.IdProducto == productoId && p.Activo)
                .Select(p => new ProductoVentaSnapshot
                {
                    IdProducto = p.IdProducto,
                    NombreProducto = p.NombreProducto,
                    PrecioProducto = p.PrecioProducto,
                    StockNuevo = p.CantidadProducto
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (producto is null)
            {
                throw new InvalidOperationException("No fue posible recuperar el producto reservado.");
            }

            producto.StockAnterior = producto.StockNuevo + 1;
            return producto;
        }

        private async Task ReverseProductInventoryIfNeededAsync(
            Cobro cobro,
            CancellationToken cancellationToken)
        {
            if (!cobro.ProductoId.HasValue)
            {
                return;
            }

            var cantidad = cobro.ProductosVendidos.Sum(d => d.Cantidad);
            if (cantidad <= 0)
            {
                cantidad = 1;
            }

            var affectedRows = await _context.Productos
                .Where(p => p.IdProducto == cobro.ProductoId.Value)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        p => p.CantidadProducto,
                        p => p.CantidadProducto + cantidad),
                    cancellationToken);

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("No fue posible revertir inventario para el producto del cobro.");
            }

            var stockNuevo = await _context.Productos
                .AsNoTracking()
                .Where(p => p.IdProducto == cobro.ProductoId.Value)
                .Select(p => p.CantidadProducto)
                .SingleAsync(cancellationToken);

            _context.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = cobro.ProductoId.Value,
                FechaMovimiento = _businessDateTimeProvider.Now(),
                TipoMovimiento = "ANULACION_VENTA",
                Cantidad = cantidad,
                StockAnterior = stockNuevo - cantidad,
                StockNuevo = stockNuevo,
                Observacion = $"Eliminacion de cobro #{cobro.IdCobro}"
            });
        }

        private static Cobro BuildCobro(CobroCreateRequest request, decimal monto, int? servicioId, int? productoId) =>
            new()
            {
                FechaCobro = request.FechaCobro,
                NombreCliente = request.NombreCliente,
                FuncionarioId = request.FuncionarioId,
                ServicioId = servicioId,
                ProductoId = productoId,
                Monto = monto,
                MetodoPago = request.MetodoPago,
                Observaciones = request.Observaciones
            };

        private CobroCreateRequest NormalizeRequest(CobroCreateRequest request) =>
            new()
            {
                FechaCobro = NormalizeCobroDateTime(request.FechaCobro),
                NombreCliente = CollapseWhitespace(request.NombreCliente),
                FuncionarioId = request.FuncionarioId,
                ServicioId = request.ServicioId,
                ProductoId = request.ProductoId,
                Monto = Math.Round(request.Monto, 2, MidpointRounding.AwayFromZero),
                MetodoPago = string.IsNullOrWhiteSpace(request.MetodoPago)
                    ? string.Empty
                    : request.MetodoPago.Trim().ToUpperInvariant(),
                Observaciones = string.IsNullOrWhiteSpace(request.Observaciones)
                    ? null
                    : request.Observaciones.Trim()
            };

        private CobroUpdateRequest NormalizeRequest(CobroUpdateRequest request) =>
            new()
            {
                IdCobro = request.IdCobro,
                FechaCobro = NormalizeCobroDateTime(request.FechaCobro),
                NombreCliente = CollapseWhitespace(request.NombreCliente),
                FuncionarioId = request.FuncionarioId,
                ServicioId = request.ServicioId,
                Monto = Math.Round(request.Monto, 2, MidpointRounding.AwayFromZero),
                MetodoPago = string.IsNullOrWhiteSpace(request.MetodoPago)
                    ? string.Empty
                    : request.MetodoPago.Trim().ToUpperInvariant(),
                Observaciones = string.IsNullOrWhiteSpace(request.Observaciones)
                    ? null
                    : request.Observaciones.Trim()
            };

        private static void ValidateRequest(CobroCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NombreCliente))
            {
                throw new CobroValidationException("Debe indicar el nombre del cliente.", "Cobro.NombreCliente");
            }

            var hasServicio = request.ServicioId.HasValue;
            var hasProducto = request.ProductoId.HasValue;

            if (hasServicio == hasProducto)
            {
                throw new CobroValidationException("Debe seleccionar un servicio o un producto, pero no ambos.");
            }

            if (request.FuncionarioId <= 0)
            {
                throw new CobroValidationException("Debe seleccionar un funcionario valido.", "Cobro.FuncionarioId");
            }

            if (!AllowedPaymentMethods.Contains(request.MetodoPago))
            {
                throw new CobroValidationException("El metodo de pago seleccionado no es valido.", "Cobro.MetodoPago");
            }

            if (request.Monto <= 0 || request.Monto > 999999)
            {
                throw new CobroValidationException("Debe indicar un monto mayor a cero y dentro del rango permitido.", "Cobro.Monto");
            }
        }

        private static void ValidateRequest(CobroUpdateRequest request)
        {
            if (request.IdCobro <= 0)
            {
                throw new CobroValidationException("El cobro indicado no es valido.");
            }

            if (string.IsNullOrWhiteSpace(request.NombreCliente))
            {
                throw new CobroValidationException("Debe indicar el nombre del cliente.", "Cobro.NombreCliente");
            }

            if (request.FuncionarioId <= 0)
            {
                throw new CobroValidationException("Debe seleccionar un funcionario valido.", "Cobro.FuncionarioId");
            }

            if (!AllowedPaymentMethods.Contains(request.MetodoPago))
            {
                throw new CobroValidationException("El metodo de pago seleccionado no es valido.", "Cobro.MetodoPago");
            }

            if (request.Monto <= 0 || request.Monto > 999999)
            {
                throw new CobroValidationException("Debe indicar un monto mayor a cero y dentro del rango permitido.", "Cobro.Monto");
            }
        }

        private DateTime NormalizeCobroDateTime(DateTime value)
        {
            var source = value == default ? _businessDateTimeProvider.Now() : value;
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

        private sealed class ServicioSnapshot
        {
            public int Id { get; init; }
            public decimal Precio { get; init; }
        }

        private sealed class ProductoVentaSnapshot
        {
            public int IdProducto { get; init; }
            public string NombreProducto { get; init; } = string.Empty;
            public decimal PrecioProducto { get; init; }
            public int StockAnterior { get; set; }
            public int StockNuevo { get; init; }
        }
    }
}
