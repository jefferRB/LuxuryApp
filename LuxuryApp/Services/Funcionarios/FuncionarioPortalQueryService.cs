using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services.BusinessTime;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Funcionarios
{
    public sealed class FuncionarioPortalQueryService : IFuncionarioPortalQueryService
    {
        private const int PagosPageSize = 20;
        private const int ProximasCitasLimit = 10;

        private readonly ApplicationDbContext _context;
        private readonly ILiquidacionSemanalService _liquidacionSemanalService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public FuncionarioPortalQueryService(
            ApplicationDbContext context,
            ILiquidacionSemanalService liquidacionSemanalService,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _liquidacionSemanalService = liquidacionSemanalService;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<PortalFuncionario?> ResolverFuncionarioAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            // Tenant-safe por el global query filter: solo devuelve funcionarios del tenant actual.
            return await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.IdFuncionario == funcionarioId)
                .Select(f => new PortalFuncionario
                {
                    IdFuncionario = f.IdFuncionario,
                    Nombre = f.Nombre,
                    Activo = f.Activo,
                    ColorCalendario = f.ColorCalendario
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<MiPanelViewModel> ObtenerPanelAsync(
            int funcionarioId,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default)
        {
            var hoy = _businessDateTimeProvider.Today();
            var inicioManana = hoy.AddDays(1);

            // Solo enriquecemos estado de cobro cuando hay permiso (evita query innecesaria).
            var citasHoy = await ConsultarCitasAsync(
                funcionarioId, hoy, inicioManana, null, puedeRegistrarCobros, cancellationToken);

            var proximasCitas = await ConsultarCitasAsync(
                funcionarioId, inicioManana, null, ProximasCitasLimit, false, cancellationToken);

            // Reutiliza la fórmula canónica de liquidaciones (respeta RebajarImpuestosAntesDeComision).
            var resumenSemana = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(hoy, cancellationToken);
            var meSemana = resumenSemana.Funcionarios.FirstOrDefault(f => f.FuncionarioId == funcionarioId);

            // "Hoy" se calcula pidiendo el resumen para un rango de un solo día.
            var resumenHoy = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(hoy, hoy, cancellationToken);
            var meHoy = resumenHoy.Funcionarios.FirstOrDefault(f => f.FuncionarioId == funcionarioId);

            var nombre = meSemana?.Nombre
                ?? meHoy?.Nombre
                ?? (await ResolverFuncionarioAsync(funcionarioId, cancellationToken))?.Nombre
                ?? string.Empty;

            return new MiPanelViewModel
            {
                Nombre = nombre,
                Hoy = hoy,
                CitasHoy = citasHoy,
                ProximasCitas = proximasCitas,
                ResumenHoy = MapResumen(meHoy),
                ProduccionSemana = meSemana?.TotalServicios + meSemana?.TotalProductos ?? 0m,
                ComisionSemana = meSemana?.PagoFinal ?? 0m,
                PagadoSemana = meSemana?.MontoPagado ?? 0m,
                PendienteSemana = meSemana?.MontoPendiente ?? 0m,
                InicioSemana = resumenSemana.InicioSemana,
                FinSemana = resumenSemana.FinSemana,
                PuedeRegistrarCobros = puedeRegistrarCobros
            };
        }

        public async Task<MisGananciasViewModel> ObtenerGananciasAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var hoy = _businessDateTimeProvider.Today();
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var finMes = inicioMes.AddMonths(1).AddDays(-1);

            var resumenHoy = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(hoy, hoy, cancellationToken);
            var resumenSemana = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(hoy, cancellationToken);
            var resumenMes = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(inicioMes, finMes, cancellationToken);

            var meHoy = resumenHoy.Funcionarios.FirstOrDefault(f => f.FuncionarioId == funcionarioId);
            var meSemana = resumenSemana.Funcionarios.FirstOrDefault(f => f.FuncionarioId == funcionarioId);
            var meMes = resumenMes.Funcionarios.FirstOrDefault(f => f.FuncionarioId == funcionarioId);

            var nombre = meSemana?.Nombre
                ?? (await ResolverFuncionarioAsync(funcionarioId, cancellationToken))?.Nombre
                ?? string.Empty;

            return new MisGananciasViewModel
            {
                Nombre = nombre,
                Hoy = MapResumen(meHoy),
                Semana = MapResumen(meSemana),
                Mes = MapResumen(meMes),
                InicioSemana = resumenSemana.InicioSemana,
                FinSemana = resumenSemana.FinSemana,
                InicioMes = inicioMes,
                PagadoSemana = meSemana?.MontoPagado ?? 0m,
                PendienteSemana = meSemana?.MontoPendiente ?? 0m,
                DetalleDiasSemana = meSemana?.DetalleDias ?? new List<DetalleDiaVM>()
            };
        }

        public async Task<MisPagosViewModel> ObtenerPagosAsync(
            int funcionarioId,
            int pagina,
            CancellationToken cancellationToken = default)
        {
            if (pagina < 1)
            {
                pagina = 1;
            }

            // Fuente moderna: detalle de liquidaciones semanales.
            var liquidaciones = await _context.LiquidacionesSemanalesDetalle
                .AsNoTracking()
                .Where(d => d.FuncionarioId == funcionarioId && d.LiquidacionSemanal != null)
                .Select(d => new PortalPagoItem
                {
                    FechaPago = d.LiquidacionSemanal!.FechaPago,
                    Monto = d.MontoPagado,
                    MetodoPago = d.LiquidacionSemanal.Egreso != null
                        ? d.LiquidacionSemanal.Egreso.MetodoPago
                        : null,
                    InicioSemana = d.LiquidacionSemanal.SemanaInicio,
                    FinSemana = d.LiquidacionSemanal.SemanaFin,
                    Observacion = d.LiquidacionSemanal.Observacion
                })
                .ToListAsync(cancellationToken);

            // Fuente legacy: pagos antiguos.
            var legacy = await _context.PagosFuncionarios
                .AsNoTracking()
                .Where(p => p.FuncionarioId == funcionarioId)
                .Select(p => new PortalPagoItem
                {
                    FechaPago = p.FechaPago,
                    Monto = p.MontoPagado,
                    MetodoPago = null,
                    InicioSemana = p.InicioSemana,
                    FinSemana = p.FinSemana,
                    Observacion = p.Observacion
                })
                .ToListAsync(cancellationToken);

            var todos = liquidaciones
                .Concat(legacy)
                .OrderByDescending(p => p.FechaPago)
                .ToList();

            var totalRegistros = todos.Count;
            var totalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)PagosPageSize));
            if (pagina > totalPaginas)
            {
                pagina = totalPaginas;
            }

            var pagos = todos
                .Skip((pagina - 1) * PagosPageSize)
                .Take(PagosPageSize)
                .ToList();

            var nombre = (await ResolverFuncionarioAsync(funcionarioId, cancellationToken))?.Nombre ?? string.Empty;

            return new MisPagosViewModel
            {
                Nombre = nombre,
                Pagos = pagos,
                TotalPagadoHistorico = todos.Sum(p => p.Monto),
                Pagina = pagina,
                TotalPaginas = totalPaginas
            };
        }

        public async Task<MiCalendarioViewModel> ObtenerCalendarioAsync(
            int funcionarioId,
            DateTime fecha,
            bool puedeCrearCitas,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default)
        {
            var dia = fecha.Date;
            var siguiente = dia.AddDays(1);

            var citas = await ConsultarCitasAsync(
                funcionarioId, dia, siguiente, null, puedeRegistrarCobros, cancellationToken);

            var funcionario = await ResolverFuncionarioAsync(funcionarioId, cancellationToken);

            var servicios = puedeCrearCitas
                ? await LoadServiciosActivosAsync(cancellationToken)
                : Array.Empty<CalendarServiceOptionResponse>();

            return new MiCalendarioViewModel
            {
                Nombre = funcionario?.Nombre ?? string.Empty,
                ColorCalendario = funcionario?.ColorCalendario ?? "#111111",
                Fecha = dia,
                EsHoy = dia == _businessDateTimeProvider.Today(),
                Citas = citas,
                PuedeCrearCitas = puedeCrearCitas,
                PuedeRegistrarCobros = puedeRegistrarCobros,
                Servicios = servicios
            };
        }

        public async Task<MisCobrosViewModel> ObtenerCobrosAsync(
            int funcionarioId,
            int pagina,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default)
        {
            if (pagina < 1)
            {
                pagina = 1;
            }

            var hoy = _businessDateTimeProvider.Today();
            var inicioManana = hoy.AddDays(1);
            var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
            var inicioSemana = hoy.AddDays(-diff).Date;
            var finSemanaExclusivo = inicioSemana.AddDays(7);

            // Solo cobros del funcionario autenticado (tenant-safe + filtro FuncionarioId).
            var baseQuery = _context.Cobros
                .AsNoTracking()
                .Where(c => c.FuncionarioId == funcionarioId);

            var totalHoy = await baseQuery
                .Where(c => c.FechaCobro >= hoy && c.FechaCobro < inicioManana)
                .SumAsync(c => (decimal?)c.Monto, cancellationToken) ?? 0m;

            var totalSemana = await baseQuery
                .Where(c => c.FechaCobro >= inicioSemana && c.FechaCobro < finSemanaExclusivo)
                .SumAsync(c => (decimal?)c.Monto, cancellationToken) ?? 0m;

            var totalRegistros = await baseQuery.CountAsync(cancellationToken);
            var totalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)PagosPageSize));
            if (pagina > totalPaginas)
            {
                pagina = totalPaginas;
            }

            var cobros = await baseQuery
                .OrderByDescending(c => c.FechaCobro)
                .ThenByDescending(c => c.IdCobro)
                .Skip((pagina - 1) * PagosPageSize)
                .Take(PagosPageSize)
                .Select(c => new PortalCobroItem
                {
                    IdCobro = c.IdCobro,
                    FechaCobro = c.FechaCobro,
                    Cliente = c.Cliente != null ? c.Cliente.Nombre : c.NombreCliente,
                    Servicio = c.Servicio != null
                        ? c.Servicio.Nombre
                        : (c.Producto != null ? c.Producto.NombreProducto : "Servicio"),
                    Monto = c.Monto,
                    MetodoPago = c.MetodoPago,
                    DesdeCita = c.CitaId != null
                })
                .ToListAsync(cancellationToken);

            var nombre = (await ResolverFuncionarioAsync(funcionarioId, cancellationToken))?.Nombre ?? string.Empty;

            return new MisCobrosViewModel
            {
                Nombre = nombre,
                PuedeRegistrarCobros = puedeRegistrarCobros,
                TotalHoy = totalHoy,
                TotalSemana = totalSemana,
                Cobros = cobros,
                Pagina = pagina,
                TotalPaginas = totalPaginas
            };
        }

        public async Task<PortalCitaItem?> ObtenerCitaCobrableAsync(
            int funcionarioId,
            int citaId,
            CancellationToken cancellationToken = default)
        {
            // Tenant-safe (global filter) + filtro explícito por funcionario.
            var cita = await _context.Citas
                .AsNoTracking()
                .Where(c => c.Id == citaId && c.FuncionarioId == funcionarioId)
                .Select(c => new
                {
                    c.Id,
                    c.FechaHoraCita,
                    Cliente = c.Cliente != null ? c.Cliente.Nombre : (c.NombreCliente ?? "Cliente"),
                    Servicio = c.Servicio != null ? c.Servicio.Nombre : c.ServicioNombrePersonalizado,
                    c.Tipo,
                    c.ServicioId,
                    PrecioServicio = c.Servicio != null ? (decimal?)c.Servicio.Precio : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (cita is null || !string.Equals(cita.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var yaCobrada = await _context.Cobros
                .AsNoTracking()
                .AnyAsync(co => co.CitaId == citaId, cancellationToken);

            return new PortalCitaItem
            {
                Id = cita.Id,
                FechaHora = cita.FechaHoraCita,
                Cliente = cita.Cliente,
                Servicio = cita.Servicio ?? "Servicio",
                Tipo = cita.Tipo,
                ServicioId = cita.ServicioId,
                PrecioServicio = cita.PrecioServicio,
                YaCobrada = yaCobrada
            };
        }

        private async Task<IReadOnlyList<CalendarServiceOptionResponse>> LoadServiciosActivosAsync(
            CancellationToken cancellationToken)
        {
            return await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Activo)
                .OrderBy(s => s.Nombre)
                .Select(s => new CalendarServiceOptionResponse
                {
                    Id = s.Id,
                    Nombre = s.Nombre,
                    DuracionMinutos = s.DuracionMinutos ?? 30
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<PortalCitaItem>> ConsultarCitasAsync(
            int funcionarioId,
            DateTime desde,
            DateTime? hasta,
            int? limite,
            bool incluirEstadoCobro,
            CancellationToken cancellationToken)
        {
            // Tenant-safe + filtro explícito por funcionario.
            var query = _context.Citas
                .AsNoTracking()
                .Where(c => c.FuncionarioId == funcionarioId && c.FechaHoraCita >= desde);

            if (hasta.HasValue)
            {
                var limiteSuperior = hasta.Value;
                query = query.Where(c => c.FechaHoraCita < limiteSuperior);
            }

            query = query.OrderBy(c => c.FechaHoraCita);

            if (limite.HasValue)
            {
                query = query.Take(limite.Value);
            }

            var citas = await query
                .Select(c => new
                {
                    c.Id,
                    c.FechaHoraCita,
                    Cliente = c.Cliente != null ? c.Cliente.Nombre : (c.NombreCliente ?? "Cliente"),
                    Servicio = c.Servicio != null
                        ? c.Servicio.Nombre
                        : (c.ServicioNombrePersonalizado ?? (c.Tipo == "CITA" ? "Servicio" : c.Tipo)),
                    c.Tipo,
                    Telefono = c.Cliente != null ? c.Cliente.NumeroTelefono : c.TelefonoCliente,
                    c.DuracionMinutos,
                    c.ServicioId,
                    PrecioServicio = c.Servicio != null ? (decimal?)c.Servicio.Precio : null
                })
                .ToListAsync(cancellationToken);

            // Estado de cobro: una sola consulta para todas las citas con servicio de catálogo.
            var cobradas = new HashSet<int>();
            if (incluirEstadoCobro)
            {
                var idsConServicio = citas
                    .Where(c => c.ServicioId.HasValue)
                    .Select(c => c.Id)
                    .ToList();

                if (idsConServicio.Count > 0)
                {
                    cobradas = (await _context.Cobros
                            .AsNoTracking()
                            .Where(co => co.CitaId != null && idsConServicio.Contains(co.CitaId.Value))
                            .Select(co => co.CitaId!.Value)
                            .ToListAsync(cancellationToken))
                        .ToHashSet();
                }
            }

            return citas
                .Select(c => new PortalCitaItem
                {
                    Id = c.Id,
                    FechaHora = c.FechaHoraCita,
                    Cliente = c.Cliente,
                    Servicio = c.Servicio,
                    Tipo = c.Tipo,
                    Telefono = c.Telefono,
                    DuracionMinutos = c.DuracionMinutos,
                    ServicioId = c.ServicioId,
                    PrecioServicio = c.PrecioServicio,
                    YaCobrada = cobradas.Contains(c.Id)
                })
                .ToList();
        }

        private static PortalResumenProduccion MapResumen(PagoFuncionarioVM? me)
        {
            if (me is null)
            {
                return new PortalResumenProduccion();
            }

            // Comisión de productos: suma de la ganancia ya calculada por producto
            // (misma base/impuestos del servicio canónico). El resto de PagoFinal es servicios.
            var comisionProductos = me.ProductosVendidos.Sum(p => p.GananciaFuncionario);
            var comisionServicios = me.PagoFinal - comisionProductos;
            if (comisionServicios < 0)
            {
                comisionServicios = 0;
            }

            return new PortalResumenProduccion
            {
                ProduccionServicios = me.TotalServicios,
                ProduccionProductos = me.TotalProductos,
                ComisionProductos = comisionProductos,
                ComisionServicios = comisionServicios,
                TotalEstimado = me.PagoFinal
            };
        }
    }
}
