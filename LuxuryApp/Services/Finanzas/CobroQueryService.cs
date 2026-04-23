using LuxuryApp.Models.Finanzas;
using LuxuryApp.Services.Funcionarios;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Finanzas
{
    public sealed class CobroQueryService : ICobroQueryService
    {
        private readonly ApplicationDbContext _context;

        public CobroQueryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CobroIndexViewModel> BuildIndexViewModelAsync(
            CobroFiltroViewModel filtros,
            bool includeFilterOptions = true,
            CancellationToken cancellationToken = default)
        {
            filtros ??= new CobroFiltroViewModel();

            var filteredQuery = BuildFilteredCobrosQuery(filtros);

            var aggregate = await BuildAggregateAsync(filteredQuery, cancellationToken)
                ?? new CobroAggregateProjection();

            var rows = await filteredQuery
                .OrderByDescending(c => c.FechaCobro)
                .ThenByDescending(c => c.IdCobro)
                .Select(c => new CobroIndexItemViewModel
                {
                    IdCobro = c.IdCobro,
                    FechaCobro = c.FechaCobro,
                    NombreCliente = c.NombreCliente,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    Detalle = c.ServicioId != null
                        ? (c.Servicio != null ? c.Servicio.Nombre : "Sin detalle")
                        : (c.Producto != null ? c.Producto.NombreProducto : "Sin detalle"),
                    Monto = c.Monto,
                    MetodoPago = c.MetodoPago,
                    EsServicio = c.ServicioId != null
                })
                .ToListAsync(cancellationToken);

            var totalImpuestos = aggregate.TotalGenerado * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
            var totalSinImpuestos = aggregate.TotalGenerado - totalImpuestos;

            var viewModel = new CobroIndexViewModel
            {
                Cobros = rows,
                Filtros = filtros,
                TotalCobrado = aggregate.TotalGenerado,
                CantidadServicios = rows.Count(c => c.EsServicio),
                TotalServicios = aggregate.TotalServicios,
                TotalProductos = aggregate.TotalProductos,
                TotalGenerado = aggregate.TotalGenerado,
                TotalImpuestos = totalImpuestos,
                TotalSinImpuestos = totalSinImpuestos,
                PagoColaboradores = aggregate.PagoColaboradores,
                GananciaNegocio = totalSinImpuestos - aggregate.PagoColaboradores,
                GananciaEfectivo = aggregate.GananciaEfectivo,
                GananciaTarjeta = aggregate.GananciaTarjeta,
                GananciaSinpe = aggregate.GananciaSinpe
            };

            if (includeFilterOptions)
            {
                viewModel.Funcionarios = await GetFuncionariosFiltroAsync(cancellationToken);
                viewModel.MetodosPago = GetMetodosPago();
            }

            return viewModel;
        }

        public async Task<CobroViewModel> BuildCreateViewModelAsync(
            Cobro? cobro = null,
            CancellationToken cancellationToken = default)
        {
            var currentCobro = cobro ?? new Cobro
            {
                FechaCobro = NormalizeCobroDateTime(DateTime.Now)
            };

            if (currentCobro.FechaCobro == default)
            {
                currentCobro.FechaCobro = NormalizeCobroDateTime(DateTime.Now);
            }

            return new CobroViewModel
            {
                Cobro = currentCobro,
                Funcionarios = await _context.Funcionarios
                    .AsNoTracking()
                    .Where(f => f.Activo)
                    .OrderBy(f => f.Nombre)
                    .Select(f => new SelectListItem
                    {
                        Value = f.IdFuncionario.ToString(),
                        Text = f.Nombre
                    })
                    .ToListAsync(cancellationToken),
                Servicios = await _context.Servicios
                    .AsNoTracking()
                    .Where(s => s.Activo)
                    .OrderBy(s => s.Nombre)
                    .Select(s => new SelectListItem
                    {
                        Value = s.Id.ToString(),
                        Text = s.Nombre
                    })
                    .ToListAsync(cancellationToken),
                Productos = await _context.Productos
                    .AsNoTracking()
                    .Where(p => p.Activo && p.CantidadProducto > 0)
                    .OrderBy(p => p.NombreProducto)
                    .Select(p => new SelectListItem
                    {
                        Value = p.IdProducto.ToString(),
                        Text = p.NombreProducto
                    })
                    .ToListAsync(cancellationToken),
                MetodosPago = GetMetodosPago()
            };
        }

        public Task<decimal?> ObtenerPrecioServicioAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Servicios
                .AsNoTracking()
                .Where(s => s.Id == id && s.Activo)
                .Select(s => (decimal?)s.Precio)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<decimal?> ObtenerPrecioProductoAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Productos
                .AsNoTracking()
                .Where(p => p.IdProducto == id && p.Activo)
                .Select(p => (decimal?)p.PrecioProducto)
                .SingleOrDefaultAsync(cancellationToken);

        private IQueryable<Cobro> BuildFilteredCobrosQuery(CobroFiltroViewModel filtros)
        {
            var query = _context.Cobros
                .AsNoTracking()
                .AsQueryable();

            if (filtros.FuncionarioId.HasValue)
            {
                query = query.Where(c => c.FuncionarioId == filtros.FuncionarioId.Value);
            }

            var metodoPago = NormalizeMetodoPagoFilter(filtros.MetodoPago);
            if (!string.IsNullOrEmpty(metodoPago))
            {
                query = query.Where(c => c.MetodoPago == metodoPago);
            }

            var (fechaInicio, fechaFinExclusiva) = ResolveDateRange(filtros);
            if (fechaInicio.HasValue)
            {
                query = query.Where(c => c.FechaCobro >= fechaInicio.Value);
            }

            if (fechaFinExclusiva.HasValue)
            {
                query = query.Where(c => c.FechaCobro < fechaFinExclusiva.Value);
            }

            if (filtros.MostrarServicios && !filtros.MostrarProductos)
            {
                query = query.Where(c => c.ServicioId != null);
            }
            else if (!filtros.MostrarServicios && filtros.MostrarProductos)
            {
                query = query.Where(c => c.ProductoId != null);
            }
            else if (!filtros.MostrarServicios && !filtros.MostrarProductos)
            {
                query = query.Where(c => false);
            }

            return query;
        }

        private Task<CobroAggregateProjection?> BuildAggregateAsync(
            IQueryable<Cobro> filteredQuery,
            CancellationToken cancellationToken) =>
            filteredQuery
                .Select(c => new
                {
                    c.Monto,
                    c.ServicioId,
                    c.ProductoId,
                    c.MetodoPago,
                    PagoColaborador = (c.Monto - (c.Monto * PagoFuncionarioDevengadoCalculator.TasaImpuesto))
                        * ((c.ProductoId != null
                            ? c.Funcionario!.PorcentajeProducto
                            : c.Funcionario!.PorcentajeGanancia) / 100m)
                })
                .GroupBy(_ => 1)
                .Select(group => new CobroAggregateProjection
                {
                    TotalServicios = group.Sum(x => x.ServicioId != null ? x.Monto : 0m),
                    TotalProductos = group.Sum(x => x.ProductoId != null ? x.Monto : 0m),
                    TotalGenerado = group.Sum(x => x.Monto),
                    PagoColaboradores = group.Sum(x => x.PagoColaborador),
                    GananciaEfectivo = group.Sum(x => x.MetodoPago == "EFECTIVO" ? x.Monto : 0m),
                    GananciaTarjeta = group.Sum(x => x.MetodoPago == "TARJETA" ? x.Monto : 0m),
                    GananciaSinpe = group.Sum(x => x.MetodoPago == "SINPE" ? x.Monto : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);

        private Task<List<SelectListItem>> GetFuncionariosFiltroAsync(CancellationToken cancellationToken) =>
            _context.Funcionarios
                .AsNoTracking()
                .OrderBy(f => f.Nombre)
                .Select(f => new SelectListItem
                {
                    Value = f.IdFuncionario.ToString(),
                    Text = f.Nombre
                })
                .ToListAsync(cancellationToken);

        private static List<SelectListItem> GetMetodosPago() =>
            new()
            {
                new SelectListItem { Value = "EFECTIVO", Text = "Efectivo" },
                new SelectListItem { Value = "TARJETA", Text = "Tarjeta" },
                new SelectListItem { Value = "SINPE", Text = "Sinpe" }
            };

        private static (DateTime? FechaInicio, DateTime? FechaFinExclusiva) ResolveDateRange(CobroFiltroViewModel filtros)
        {
            var vistaTiempo = string.IsNullOrWhiteSpace(filtros.VistaTiempo)
                ? "dia"
                : filtros.VistaTiempo.Trim().ToLowerInvariant();

            var today = DateTime.Today;

            return vistaTiempo switch
            {
                "todo" => (null, null),
                "dia" => (today, today.AddDays(1)),
                "semana" => ResolveWeekRange(today),
                "mes" => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1)),
                "anio" => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 1, 1).AddYears(1)),
                "fechas" => (
                    filtros.FechaInicio?.Date,
                    filtros.FechaFin?.Date.AddDays(1)),
                _ => (today, today.AddDays(1))
            };
        }

        private static (DateTime FechaInicio, DateTime FechaFinExclusiva) ResolveWeekRange(DateTime today)
        {
            var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var inicioSemana = today.AddDays(-diff).Date;
            return (inicioSemana, inicioSemana.AddDays(7));
        }

        private static string NormalizeMetodoPagoFilter(string? metodoPago) =>
            string.IsNullOrWhiteSpace(metodoPago)
                ? string.Empty
                : metodoPago.Trim().ToUpperInvariant();

        private static DateTime NormalizeCobroDateTime(DateTime value) =>
            new(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0);

        private sealed class CobroAggregateProjection
        {
            public decimal TotalServicios { get; init; }
            public decimal TotalProductos { get; init; }
            public decimal TotalGenerado { get; init; }
            public decimal PagoColaboradores { get; init; }
            public decimal GananciaEfectivo { get; init; }
            public decimal GananciaTarjeta { get; init; }
            public decimal GananciaSinpe { get; init; }
        }
    }
}
