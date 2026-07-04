using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Fiscal;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.BusinessTime;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Finanzas
{
    public sealed class CobroQueryService : ICobroQueryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public CobroQueryService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<CobroIndexViewModel> BuildIndexViewModelAsync(
            CobroFiltroViewModel filtros,
            bool includeFilterOptions = true,
            CancellationToken cancellationToken = default)
        {
            filtros ??= new CobroFiltroViewModel();

            var filteredQuery = BuildFilteredCobrosQuery(filtros);

            // 1) Agregados sobre TODO el filtro (no dependen de la página).
            var aggregate = await BuildAggregateAsync(filteredQuery, cancellationToken)
                ?? new CobroAggregateProjection();

            // 2) Paginación: total filtrado + normalización de tamaño/página.
            var totalRegistros = await filteredQuery.CountAsync(cancellationToken);
            var pageSize = NormalizePageSize(filtros.PageSize);
            var totalPaginas = totalRegistros == 0
                ? 1
                : (int)Math.Ceiling(totalRegistros / (double)pageSize);
            var page = Math.Clamp(filtros.Page < 1 ? 1 : filtros.Page, 1, totalPaginas);
            filtros.Page = page;
            filtros.PageSize = pageSize;

            // 3) Filas SOLO de la página actual (Skip/Take en backend, tras filtros y orden).
            var rows = await filteredQuery
                .OrderByDescending(c => c.FechaCobro)
                .ThenByDescending(c => c.IdCobro)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CobroIndexItemViewModel
                {
                    IdCobro = c.IdCobro,
                    FechaCobro = c.FechaCobro,
                    NombreCliente = c.NombreCliente,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    Detalle = c.ServicioId != null
                        ? (c.Servicio != null ? c.Servicio.Nombre : "Sin detalle")
                        : (c.ServicioNombrePersonalizado != null
                            ? c.ServicioNombrePersonalizado
                            : (c.Producto != null ? c.Producto.NombreProducto : "Sin detalle")),
                    Monto = c.Monto,
                    MetodoPago = c.MetodoPago,
                    EsServicio = c.ServicioId != null || c.ServicioNombrePersonalizado != null,
                    // Comprobante "vivo" más reciente del cobro (OUTER APPLY: una sola consulta).
                    ComprobanteId = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => (int?)cc.Id)
                        .FirstOrDefault(),
                    ComprobanteEstado = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => (Models.Comprobantes.ComprobanteEstadoEnvio?)cc.EstadoEnvio)
                        .FirstOrDefault(),
                    ComprobanteToken = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => cc.TokenPublico)
                        .FirstOrDefault(),
                    ComprobanteNumero = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => cc.NumeroInterno)
                        .FirstOrDefault(),
                    ComprobanteEmail = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => cc.EmailDestino)
                        .FirstOrDefault(),
                    ComprobanteSentAt = _context.ComprobantesCobro
                        .Where(cc => cc.CobroId == c.IdCobro && cc.EstadoEnvio != Models.Comprobantes.ComprobanteEstadoEnvio.Cancelled)
                        .OrderByDescending(cc => cc.Id)
                        .Select(cc => cc.SentAt)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var viewModel = BuildViewModelFromAggregate(aggregate, filtros);
            viewModel.Cobros = rows;
            viewModel.Page = page;
            viewModel.PageSize = pageSize;
            viewModel.TotalRegistros = totalRegistros;
            viewModel.TotalPaginas = totalPaginas;

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
            var currentCobro = cobro ?? new Cobro();

            if (currentCobro.FechaCobro == default)
            {
                currentCobro.FechaCobro = NormalizeCobroDateTime(_businessDateTimeProvider.Now());
            }

            return await BuildFormViewModelAsync(currentCobro, cancellationToken: cancellationToken);
        }

        public async Task<CobroViewModel?> BuildEditViewModelAsync(
            int id,
            Cobro? cobro = null,
            CancellationToken cancellationToken = default)
        {
            var persistedCobro = await _context.Cobros
                .AsNoTracking()
                .Where(c => c.IdCobro == id)
                .Select(c => new Cobro
                {
                    IdCobro = c.IdCobro,
                    FechaCobro = c.FechaCobro,
                    NombreCliente = c.NombreCliente,
                    ClienteId = c.ClienteId,
                    FuncionarioId = c.FuncionarioId,
                    ServicioId = c.ServicioId,
                    ProductoId = c.ProductoId,
                    Monto = c.Monto,
                    MetodoPago = c.MetodoPago,
                    Observaciones = c.Observaciones
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (persistedCobro is null)
            {
                return null;
            }

            var currentCobro = cobro ?? persistedCobro;
            currentCobro.IdCobro = persistedCobro.IdCobro;

            if (persistedCobro.ProductoId.HasValue)
            {
                currentCobro.ProductoId = persistedCobro.ProductoId;
                currentCobro.ServicioId = null;
            }

            return await BuildFormViewModelAsync(
                currentCobro,
                selectedFuncionarioId: persistedCobro.FuncionarioId,
                selectedServicioId: persistedCobro.ServicioId,
                selectedProductoId: persistedCobro.ProductoId,
                cancellationToken);
        }

        private async Task<CobroViewModel> BuildFormViewModelAsync(
            Cobro currentCobro,
            int? selectedFuncionarioId = null,
            int? selectedServicioId = null,
            int? selectedProductoId = null,
            CancellationToken cancellationToken = default)
        {
            selectedFuncionarioId ??= currentCobro.FuncionarioId > 0 ? currentCobro.FuncionarioId : null;
            selectedServicioId ??= currentCobro.ServicioId;
            selectedProductoId ??= currentCobro.ProductoId;

            return new CobroViewModel
            {
                Cobro = currentCobro,
                Funcionarios = await _context.Funcionarios
                    .AsNoTracking()
                    .Where(f => f.Activo || (selectedFuncionarioId.HasValue && f.IdFuncionario == selectedFuncionarioId.Value))
                    .OrderBy(f => f.Nombre)
                    .Select(f => new SelectListItem
                    {
                        Value = f.IdFuncionario.ToString(),
                        Text = f.Nombre
                    })
                    .ToListAsync(cancellationToken),
                Servicios = await _context.Servicios
                    .AsNoTracking()
                    .Where(s => s.Activo || (selectedServicioId.HasValue && s.Id == selectedServicioId.Value))
                    .OrderBy(s => s.Nombre)
                    .Select(s => new SelectListItem
                    {
                        Value = s.Id.ToString(),
                        Text = s.Nombre
                    })
                    .ToListAsync(cancellationToken),
                Productos = await _context.Productos
                    .AsNoTracking()
                    .Where(p =>
                        (p.Activo && p.CantidadProducto > 0) ||
                        (selectedProductoId.HasValue && p.IdProducto == selectedProductoId.Value))
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
                query = query.Where(c => c.ServicioId != null || c.ServicioNombrePersonalizado != null);
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
                    c.ServicioNombrePersonalizado,
                    c.ProductoId,
                    c.MetodoPago,
                    EsServicio = c.ServicioId != null || c.ServicioNombrePersonalizado != null,
                    // Comisión con IVA incluido: la base es Total / 1.13 cuando se calcula sobre base sin IVA
                    // (fuente de verdad: ComisionCalculadaSobre), no el viejo "Total − Total*13%".
                    PagoColaborador = (c.Funcionario!.ComisionCalculadaSobre == ComisionCalculadaSobre.BaseSinIva
                            ? c.Monto / (1m + PagoFuncionarioDevengadoCalculator.TasaImpuesto)
                            : c.Monto)
                        * ((c.ProductoId != null
                            ? c.Funcionario!.PorcentajeProducto
                            : c.Funcionario!.PorcentajeGanancia) / 100m)
                })
                .GroupBy(_ => 1)
                .Select(group => new CobroAggregateProjection
                {
                    Cantidad = group.Count(),
                    CantidadServicios = group.Sum(x => x.EsServicio ? 1 : 0),
                    TotalServicios = group.Sum(x => x.EsServicio ? x.Monto : 0m),
                    TotalProductos = group.Sum(x => x.ProductoId != null ? x.Monto : 0m),
                    TotalGenerado = group.Sum(x => x.Monto),
                    PagoColaboradores = group.Sum(x => x.PagoColaborador),
                    GananciaEfectivo = group.Sum(x => x.MetodoPago == "EFECTIVO" ? x.Monto : 0m),
                    GananciaTarjeta = group.Sum(x => x.MetodoPago == "TARJETA" ? x.Monto : 0m),
                    GananciaSinpe = group.Sum(x => x.MetodoPago == "SINPE" ? x.Monto : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<CobroExportViewModel> BuildExportAsync(
            CobroFiltroViewModel filtros,
            CancellationToken cancellationToken = default)
        {
            filtros ??= new CobroFiltroViewModel();

            var filteredQuery = BuildFilteredCobrosQuery(filtros);

            var aggregate = await BuildAggregateAsync(filteredQuery, cancellationToken)
                ?? new CobroAggregateProjection();

            var resumen = BuildViewModelFromAggregate(aggregate, filtros);
            resumen.TotalRegistros = aggregate.Cantidad;

            // TODAS las filas filtradas (sin paginar); proyección ligera (sin subconsultas de comprobante).
            var raw = await filteredQuery
                .OrderByDescending(c => c.FechaCobro)
                .ThenByDescending(c => c.IdCobro)
                .Select(c => new ExportProjection
                {
                    FechaCobro = c.FechaCobro,
                    NombreCliente = c.NombreCliente,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    EsServicio = c.ServicioId != null || c.ServicioNombrePersonalizado != null,
                    Detalle = c.ServicioId != null
                        ? (c.Servicio != null ? c.Servicio.Nombre : "Sin detalle")
                        : (c.ServicioNombrePersonalizado != null
                            ? c.ServicioNombrePersonalizado
                            : (c.Producto != null ? c.Producto.NombreProducto : "Sin detalle")),
                    MetodoPago = c.MetodoPago,
                    Monto = c.Monto,
                    ComisionSobre = c.Funcionario != null
                        ? c.Funcionario.ComisionCalculadaSobre
                        : ComisionCalculadaSobre.TotalCobrado,
                    Porcentaje = c.ProductoId != null
                        ? (c.Funcionario != null ? c.Funcionario.PorcentajeProducto : 0m)
                        : (c.Funcionario != null ? c.Funcionario.PorcentajeGanancia : 0m)
                })
                .ToListAsync(cancellationToken);

            var filas = raw
                .Select(r =>
                {
                    var baseSinIva = PagoFuncionarioDevengadoCalculator.CalcularBaseSinIvaIncluido(r.Monto);
                    var baseComision = PagoFuncionarioDevengadoCalculator.CalcularBaseComision(r.Monto, r.ComisionSobre);
                    var montoColaborador = Math.Round(baseComision * (r.Porcentaje / 100m), 2, MidpointRounding.ToEven);

                    return new CobroExportRow
                    {
                        FechaCobro = r.FechaCobro,
                        NombreCliente = r.NombreCliente,
                        FuncionarioNombre = r.FuncionarioNombre,
                        EsServicio = r.EsServicio,
                        Detalle = r.Detalle,
                        MetodoPago = r.MetodoPago,
                        Monto = r.Monto,
                        BaseSinIva = baseSinIva,
                        IvaIncluido = r.Monto - baseSinIva,
                        MontoColaborador = montoColaborador,
                        MontoNegocio = baseSinIva - montoColaborador
                    };
                })
                .ToList();

            return new CobroExportViewModel { Resumen = resumen, Filas = filas };
        }

        private static CobroIndexViewModel BuildViewModelFromAggregate(
            CobroAggregateProjection aggregate,
            CobroFiltroViewModel filtros)
        {
            // IVA incluido (CR): base = Total / 1.13; IVA = Total − base. Mismo helper que Dashboard/Liquidaciones.
            var totalSinImpuestos = PagoFuncionarioDevengadoCalculator.CalcularBaseSinIvaIncluido(aggregate.TotalGenerado);
            var totalImpuestos = aggregate.TotalGenerado - totalSinImpuestos;

            return new CobroIndexViewModel
            {
                Filtros = filtros,
                TotalCobrado = aggregate.TotalGenerado,
                CantidadServicios = aggregate.CantidadServicios,
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
        }

        private static int NormalizePageSize(int pageSize) =>
            CobroIndexViewModel.PageSizeOptions.Contains(pageSize) ? pageSize : 20;

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

        private (DateTime? FechaInicio, DateTime? FechaFinExclusiva) ResolveDateRange(CobroFiltroViewModel filtros)
        {
            var vistaTiempo = string.IsNullOrWhiteSpace(filtros.VistaTiempo)
                ? "dia"
                : filtros.VistaTiempo.Trim().ToLowerInvariant();

            var today = _businessDateTimeProvider.Today();

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

        private sealed class ExportProjection
        {
            public DateTime FechaCobro { get; init; }
            public string NombreCliente { get; init; } = string.Empty;
            public string FuncionarioNombre { get; init; } = string.Empty;
            public bool EsServicio { get; init; }
            public string Detalle { get; init; } = string.Empty;
            public string MetodoPago { get; init; } = string.Empty;
            public decimal Monto { get; init; }
            public ComisionCalculadaSobre ComisionSobre { get; init; }
            public decimal Porcentaje { get; init; }
        }

        private sealed class CobroAggregateProjection
        {
            public int Cantidad { get; init; }
            public int CantidadServicios { get; init; }
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
