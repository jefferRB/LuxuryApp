using System.Data;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Funcionarios
{
    public class LiquidacionSemanalService : ILiquidacionSemanalService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LiquidacionSemanalService> _logger;

        public LiquidacionSemanalService(
            ApplicationDbContext context,
            ILogger<LiquidacionSemanalService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<PagosSemanaResumen> ObtenerResumenSemanaAsync(
            DateTime fechaReferencia,
            CancellationToken cancellationToken = default)
        {
            var hoy = fechaReferencia.Date;
            var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
            var inicioSemana = hoy.AddDays(-diff).Date;
            var finSemana = inicioSemana.AddDays(6).Date;

            return ObtenerResumenSemanaAsync(inicioSemana, finSemana, cancellationToken);
        }

        public async Task<PagosSemanaResumen> ObtenerResumenSemanaAsync(
            DateTime inicioSemana,
            DateTime finSemana,
            CancellationToken cancellationToken = default)
        {
            inicioSemana = inicioSemana.Date;
            finSemana = finSemana.Date;

            if (finSemana < inicioSemana)
            {
                throw new InvalidOperationException("La semana indicada no es valida.");
            }

            var finSemanaExclusive = finSemana.AddDays(1);

            var serviciosPorDia = await LoadServiceDayAggregatesAsync(
                inicioSemana,
                finSemanaExclusive,
                cancellationToken);

            var productosVendidos = await LoadProductSalesAsync(
                inicioSemana,
                finSemanaExclusive,
                cancellationToken);

            var pagosLegacySemana = await LoadLegacyPaymentHistoryAsync(
                inicioSemana,
                finSemana,
                cancellationToken);

            var pagosLiquidacionSemana = await LoadLiquidationPaymentHistoryAsync(
                inicioSemana,
                finSemana,
                cancellationToken);

            var funcionarioIdsInvolucrados = serviciosPorDia
                .Select(s => s.FuncionarioId)
                .Concat(productosVendidos.Select(p => p.FuncionarioId))
                .Concat(pagosLegacySemana.Select(p => p.FuncionarioId))
                .Concat(pagosLiquidacionSemana.Select(p => p.FuncionarioId))
                .Distinct()
                .ToList();

            var funcionarios = await LoadFuncionariosSemanaAsync(
                funcionarioIdsInvolucrados,
                cancellationToken);

            var serviciosPorFuncionario = serviciosPorDia
                .GroupBy(s => s.FuncionarioId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Monto));

            var detalleServiciosPorFuncionario = serviciosPorDia
                .GroupBy(s => s.FuncionarioId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyDictionary<DateTime, ServicioDiaAggregate>)group.ToDictionary(
                        item => item.Fecha,
                        item => item));

            var productosPorFuncionario = productosVendidos
                .GroupBy(p => p.FuncionarioId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.Fecha).ToList());

            var totalProductosPorFuncionario = productosPorFuncionario
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Sum(item => item.Precio));

            var historialPagosPorFuncionario = pagosLegacySemana
                .Concat(pagosLiquidacionSemana)
                .GroupBy(p => p.FuncionarioId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item => item.FechaPago)
                        .ThenByDescending(item => item.ReferenciaId)
                        .ToList());

            var totalPagadoPorFuncionario = historialPagosPorFuncionario
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Sum(item => item.MontoPagado));

            var pagos = new List<PagoFuncionarioVM>(funcionarios.Count);
            foreach (var funcionario in funcionarios)
            {
                var totalServicios = GetAmount(serviciosPorFuncionario, funcionario.IdFuncionario);
                var totalProductos = GetAmount(totalProductosPorFuncionario, funcionario.IdFuncionario);
                var totalGenerado = totalServicios + totalProductos;
                var impuestos = totalGenerado * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
                var totalNeto = totalGenerado - impuestos;

                var netoServicios = totalServicios - (totalServicios * PagoFuncionarioDevengadoCalculator.TasaImpuesto);
                var netoProductos = totalProductos - (totalProductos * PagoFuncionarioDevengadoCalculator.TasaImpuesto);
                var pagoServicios = netoServicios * (funcionario.PorcentajeGanancia / 100m);
                var pagoProductos = netoProductos * (funcionario.PorcentajeProducto / 100m);
                var pagoFinal = pagoServicios + pagoProductos;
                var montoPagado = GetAmount(totalPagadoPorFuncionario, funcionario.IdFuncionario);
                var montoPendiente = pagoFinal - montoPagado;

                detalleServiciosPorFuncionario.TryGetValue(funcionario.IdFuncionario, out var serviciosDiarios);
                productosPorFuncionario.TryGetValue(funcionario.IdFuncionario, out var productosFuncionarioRows);
                historialPagosPorFuncionario.TryGetValue(funcionario.IdFuncionario, out var historialPagos);

                var productosFuncionario = (productosFuncionarioRows ?? [])
                    .Select(producto => new ProductoVendidoVM
                    {
                        Fecha = producto.Fecha,
                        NombreProducto = producto.NombreProducto,
                        Precio = producto.Precio,
                        GananciaFuncionario =
                            (producto.Precio - (producto.Precio * PagoFuncionarioDevengadoCalculator.TasaImpuesto)) *
                            (funcionario.PorcentajeProducto / 100m)
                    })
                    .ToList();

                pagos.Add(new PagoFuncionarioVM
                {
                    FuncionarioId = funcionario.IdFuncionario,
                    Nombre = funcionario.Nombre,
                    TotalGenerado = totalGenerado,
                    Impuestos = impuestos,
                    TotalNeto = totalNeto,
                    Porcentaje = funcionario.PorcentajeGanancia,
                    PorcentajeProducto = funcionario.PorcentajeProducto,
                    PagoFinal = pagoFinal,
                    MontoPagado = montoPagado,
                    MontoPendiente = montoPendiente,
                    TotalServicios = totalServicios,
                    TotalProductos = totalProductos,
                    DetalleDias = BuildDetalleDias(inicioSemana, finSemana, serviciosDiarios),
                    ProductosVendidos = productosFuncionario,
                    HistorialPagos = historialPagos ?? new List<HistorialPagoFuncionarioViewModel>()
                });
            }

            var totalGeneradoServicios = serviciosPorFuncionario.Values.Sum();
            var totalGeneradoProductos = totalProductosPorFuncionario.Values.Sum();
            var totalGeneradoGeneral = totalGeneradoServicios + totalGeneradoProductos;
            var totalImpuestosGeneral = totalGeneradoGeneral * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
            var totalSinImpuestosGeneral = totalGeneradoGeneral - totalImpuestosGeneral;
            var totalPagadoGeneral = pagos.Sum(p => p.MontoPagado);
            var totalPendienteGeneral = pagos.Sum(p => p.MontoPendiente);
            var gananciaNegocio = totalSinImpuestosGeneral - pagos.Sum(p => p.PagoFinal);

            return new PagosSemanaResumen
            {
                InicioSemana = inicioSemana,
                FinSemana = finSemana,
                Funcionarios = pagos,
                TotalGeneradoServicios = totalGeneradoServicios,
                TotalGeneradoProductos = totalGeneradoProductos,
                TotalGeneradoGeneral = totalGeneradoGeneral,
                TotalImpuestosGeneral = totalImpuestosGeneral,
                TotalSinImpuestosGeneral = totalSinImpuestosGeneral,
                TotalPagadoGeneral = totalPagadoGeneral,
                TotalPendienteGeneral = totalPendienteGeneral,
                GananciaNegocio = gananciaNegocio
            };
        }

        public async Task<int> RegistrarPagoAsync(
            RegistrarLiquidacionSemanalCommand command,
            CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                throw new InvalidOperationException("La liquidacion solicitada no es valida.");
            }

            command.SemanaInicio = command.SemanaInicio.Date;
            command.SemanaFin = command.SemanaFin.Date;

            if (command.SemanaFin < command.SemanaInicio)
            {
                throw new InvalidOperationException("La semana indicada no es valida.");
            }

            command.MetodoPago = NormalizeMetodoPago(command.MetodoPago);
            command.Observacion = NormalizeOptionalText(command.Observacion, 500);
            command.CreadoPor = NormalizeOptionalText(command.CreadoPor, 450);

            var detallesSolicitados = command.Detalles
                .Where(d => d.MontoPagado > 0)
                .GroupBy(d => d.FuncionarioId)
                .Select(group => new RegistrarLiquidacionSemanalDetalleCommand
                {
                    FuncionarioId = group.Key,
                    MontoPagado = group.Sum(item => item.MontoPagado)
                })
                .ToList();

            if (detallesSolicitados.Count == 0)
            {
                throw new InvalidOperationException("No hay montos validos para registrar en la liquidacion.");
            }

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                var resumen = await ObtenerResumenSemanaAsync(
                    command.SemanaInicio,
                    command.SemanaFin,
                    cancellationToken);

                var detallesValidados = new List<(PagoFuncionarioVM Resumen, decimal MontoPagado)>();
                foreach (var detalle in detallesSolicitados)
                {
                    var resumenFuncionario = resumen.Funcionarios
                        .FirstOrDefault(f => f.FuncionarioId == detalle.FuncionarioId);

                    if (resumenFuncionario == null)
                    {
                        throw new InvalidOperationException("Uno de los funcionarios indicados no existe o no pertenece al tenant actual.");
                    }

                    if (detalle.MontoPagado > resumenFuncionario.MontoPendiente)
                    {
                        throw new InvalidOperationException(
                            $"El monto a pagar de {resumenFuncionario.Nombre} excede el pendiente disponible para la semana seleccionada.");
                    }

                    detallesValidados.Add((resumenFuncionario, detalle.MontoPagado));
                }

                var categoria = await EnsureCategoriaPagoFuncionariosAsync(cancellationToken);
                var now = DateTime.Now;
                var fechaPago = NormalizeFechaPago(command.FechaPago, now);
                var montoTotal = detallesValidados.Sum(item => item.MontoPagado);

                var egreso = new Egreso
                {
                    FechaEgreso = fechaPago,
                    CategoriaId = categoria.Id,
                    Monto = montoTotal,
                    MetodoPago = command.MetodoPago,
                    Detalle = BuildDetalleEgreso(
                        detallesValidados.Select(item => item.Resumen.Nombre).ToList(),
                        command.SemanaInicio,
                        command.SemanaFin)
                };

                _context.Egresos.Add(egreso);
                await _context.SaveChangesAsync(cancellationToken);

                var liquidacion = new LiquidacionSemanal
                {
                    SemanaInicio = command.SemanaInicio,
                    SemanaFin = command.SemanaFin,
                    FechaPago = fechaPago,
                    MontoTotal = montoTotal,
                    Estado = LiquidacionSemanalDefaults.EstadoPagada,
                    Observacion = command.Observacion,
                    CreadoPor = command.CreadoPor,
                    FechaCreacion = now,
                    EgresoId = egreso.IdEgreso
                };

                _context.LiquidacionesSemanales.Add(liquidacion);
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var detalle in detallesValidados)
                {
                    _context.LiquidacionesSemanalesDetalle.Add(new LiquidacionSemanalDetalle
                    {
                        LiquidacionSemanalId = liquidacion.Id,
                        FuncionarioId = detalle.Resumen.FuncionarioId,
                        MontoServicios = detalle.Resumen.TotalServicios,
                        MontoProductos = detalle.Resumen.TotalProductos,
                        Impuestos = detalle.Resumen.Impuestos,
                        MontoNeto = detalle.Resumen.TotalNeto,
                        MontoPagado = detalle.MontoPagado,
                        Pendiente = detalle.Resumen.MontoPendiente - detalle.MontoPagado
                    });
                }

                foreach (var distribucion in await BuildDistribucionMensualDesdeCobrosAsync(
                    liquidacion.Id,
                    command.SemanaInicio,
                    command.SemanaFin,
                    detallesValidados,
                    cancellationToken))
                {
                    _context.LiquidacionesSemanalesDistribucionMensual.Add(distribucion);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Liquidacion semanal {LiquidacionId} registrada. Semana {SemanaInicio:yyyy-MM-dd} a {SemanaFin:yyyy-MM-dd}. Monto {MontoTotal}.",
                    liquidacion.Id,
                    command.SemanaInicio,
                    command.SemanaFin,
                    montoTotal);

                return liquidacion.Id;
            });
        }

        private async Task<Categoria> EnsureCategoriaPagoFuncionariosAsync(CancellationToken cancellationToken)
        {
            var categoria = await _context.Categorias
                .FirstOrDefaultAsync(c => c.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios, cancellationToken);

            if (categoria != null)
            {
                if (!categoria.Activo)
                {
                    categoria.Activo = true;
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return categoria;
            }

            categoria = new Categoria
            {
                Nombre = LiquidacionSemanalDefaults.CategoriaPagoFuncionarios,
                Detalle = "Categoria generada automaticamente para registrar pagos reales a funcionarios.",
                Activo = true
            };

            _context.Categorias.Add(categoria);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return categoria;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(ex, "La categoria de Pago Funcionarios fue creada concurrentemente por otra solicitud.");
                _context.Entry(categoria).State = EntityState.Detached;

                var existente = await _context.Categorias
                    .FirstOrDefaultAsync(c => c.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios, cancellationToken);

                if (existente != null)
                {
                    if (!existente.Activo)
                    {
                        existente.Activo = true;
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    return existente;
                }

                throw;
            }
        }

        private async Task<List<LiquidacionSemanalDistribucionMensual>> BuildDistribucionMensualDesdeCobrosAsync(
            int liquidacionId,
            DateTime semanaInicio,
            DateTime semanaFin,
            IReadOnlyCollection<(PagoFuncionarioVM Resumen, decimal MontoPagado)> detallesValidados,
            CancellationToken cancellationToken)
        {
            var funcionarioIds = detallesValidados
                .Select(d => d.Resumen.FuncionarioId)
                .Distinct()
                .ToList();

            if (funcionarioIds.Count == 0)
            {
                return new List<LiquidacionSemanalDistribucionMensual>();
            }

            var funcionarios = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => funcionarioIds.Contains(f.IdFuncionario))
                .Select(f => new Funcionario
                {
                    IdFuncionario = f.IdFuncionario,
                    Nombre = f.Nombre,
                    PorcentajeGanancia = f.PorcentajeGanancia,
                    PorcentajeProducto = f.PorcentajeProducto
                })
                .ToDictionaryAsync(f => f.IdFuncionario, cancellationToken);

            var cobrosSemana = await _context.Cobros
                .AsNoTracking()
                .Where(c => funcionarioIds.Contains(c.FuncionarioId) &&
                            c.FechaCobro >= semanaInicio.Date &&
                            c.FechaCobro < semanaFin.Date.AddDays(1))
                .Select(c => new Cobro
                {
                    FuncionarioId = c.FuncionarioId,
                    FechaCobro = c.FechaCobro,
                    Monto = c.Monto,
                    ProductoId = c.ProductoId
                })
                .ToListAsync(cancellationToken);

            var cobrosPorFuncionario = cobrosSemana
                .GroupBy(c => c.FuncionarioId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var diasPorMes = cobrosSemana
                .GroupBy(c => (c.FechaCobro.Year, c.FechaCobro.Month))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(c => c.FechaCobro.Date)
                        .Distinct()
                        .Count());

            var distribucionesPorMes = new Dictionary<(int Anio, int Mes), LiquidacionSemanalDistribucionMensual>();
            foreach (var detalle in detallesValidados)
            {
                if (!funcionarios.TryGetValue(detalle.Resumen.FuncionarioId, out var funcionario))
                {
                    throw new InvalidOperationException(
                        $"No se pudo resolver el funcionario {detalle.Resumen.FuncionarioId} para construir la distribucion analitica.");
                }

                var cobrosFuncionario = cobrosPorFuncionario.TryGetValue(detalle.Resumen.FuncionarioId, out var listaCobros)
                    ? listaCobros
                    : new List<Cobro>();

                var distribucionFuncionario = PagoFuncionarioDevengadoCalculator.DistribuirMontoPagadoPorMes(
                    cobrosFuncionario,
                    funcionario,
                    detalle.MontoPagado);

                if (distribucionFuncionario.Count == 0 && detalle.MontoPagado > 0)
                {
                    throw new InvalidOperationException(
                        $"No se encontro produccion devengada para distribuir analiticamente el pago del funcionario {funcionario.Nombre} en la semana seleccionada.");
                }

                foreach (var distribucionMes in distribucionFuncionario)
                {
                    var claveMes = (distribucionMes.Anio, distribucionMes.Mes);
                    if (!distribucionesPorMes.TryGetValue(claveMes, out var acumulado))
                    {
                        distribucionesPorMes[claveMes] = new LiquidacionSemanalDistribucionMensual
                        {
                            LiquidacionSemanalId = liquidacionId,
                            Anio = distribucionMes.Anio,
                            Mes = distribucionMes.Mes,
                            MontoAsignado = distribucionMes.MontoAsignado,
                            DiasAplicados = diasPorMes.GetValueOrDefault(claveMes)
                        };
                        continue;
                    }

                    acumulado.MontoAsignado += distribucionMes.MontoAsignado;
                }
            }

            var distribuciones = distribucionesPorMes.Values
                .OrderBy(d => d.Anio)
                .ThenBy(d => d.Mes)
                .ToList();

            AjustarRedondeoDistribucion(distribuciones, detallesValidados.Sum(d => d.MontoPagado));
            return distribuciones;
        }

        private static List<DetalleDiaVM> BuildDetalleDias(
            DateTime inicioSemana,
            DateTime finSemana,
            IReadOnlyDictionary<DateTime, ServicioDiaAggregate>? detalleServicios)
        {
            var totalDias = (finSemana - inicioSemana).Days + 1;
            var detalle = new List<DetalleDiaVM>(totalDias);

            for (var index = 0; index < totalDias; index++)
            {
                var fechaDia = inicioSemana.AddDays(index).Date;
                ServicioDiaAggregate? resumenDia = null;
                if (detalleServicios != null)
                {
                    detalleServicios.TryGetValue(fechaDia, out resumenDia);
                }

                detalle.Add(new DetalleDiaVM
                {
                    Dia = fechaDia.ToString("dddd"),
                    CantidadServicios = resumenDia?.CantidadServicios ?? 0,
                    Monto = resumenDia?.Monto ?? 0m
                });
            }

            return detalle;
        }

        private async Task<List<ServicioDiaAggregate>> LoadServiceDayAggregatesAsync(
            DateTime inicioSemana,
            DateTime finSemanaExclusive,
            CancellationToken cancellationToken)
        {
            return await _context.Cobros
                .AsNoTracking()
                .Where(c => c.ServicioId != null &&
                            c.FechaCobro >= inicioSemana &&
                            c.FechaCobro < finSemanaExclusive)
                .GroupBy(c => new { c.FuncionarioId, Fecha = c.FechaCobro.Date })
                .Select(group => new ServicioDiaAggregate
                {
                    FuncionarioId = group.Key.FuncionarioId,
                    Fecha = group.Key.Fecha,
                    CantidadServicios = group.Count(),
                    Monto = group.Sum(item => item.Monto)
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<List<ProductoVentaAggregate>> LoadProductSalesAsync(
            DateTime inicioSemana,
            DateTime finSemanaExclusive,
            CancellationToken cancellationToken)
        {
            return await _context.Cobros
                .AsNoTracking()
                .Where(c => c.ProductoId != null &&
                            c.FechaCobro >= inicioSemana &&
                            c.FechaCobro < finSemanaExclusive)
                .OrderByDescending(c => c.FechaCobro)
                .Select(c => new ProductoVentaAggregate
                {
                    FuncionarioId = c.FuncionarioId,
                    Fecha = c.FechaCobro,
                    NombreProducto = c.Producto != null ? c.Producto.NombreProducto : "Producto",
                    Precio = c.Monto
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<List<HistorialPagoFuncionarioViewModel>> LoadLegacyPaymentHistoryAsync(
            DateTime inicioSemana,
            DateTime finSemana,
            CancellationToken cancellationToken)
        {
            var inicioSemanaExclusive = inicioSemana.AddDays(1);
            var finSemanaExclusive = finSemana.AddDays(1);

            return await _context.PagosFuncionarios
                .AsNoTracking()
                .Where(p => p.InicioSemana >= inicioSemana &&
                            p.InicioSemana < inicioSemanaExclusive &&
                            p.FinSemana >= finSemana &&
                            p.FinSemana < finSemanaExclusive)
                .Select(p => new HistorialPagoFuncionarioViewModel
                {
                    ReferenciaId = p.IdPago,
                    FuncionarioId = p.FuncionarioId,
                    MontoPagado = p.MontoPagado,
                    FechaPago = p.FechaPago,
                    InicioSemana = p.InicioSemana,
                    FinSemana = p.FinSemana,
                    Observacion = p.Observacion,
                    OrigenRegistro = "LEGACY"
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<List<HistorialPagoFuncionarioViewModel>> LoadLiquidationPaymentHistoryAsync(
            DateTime inicioSemana,
            DateTime finSemana,
            CancellationToken cancellationToken)
        {
            var inicioSemanaExclusive = inicioSemana.AddDays(1);
            var finSemanaExclusive = finSemana.AddDays(1);

            return await _context.LiquidacionesSemanalesDetalle
                .AsNoTracking()
                .Where(d => d.LiquidacionSemanal != null &&
                            d.LiquidacionSemanal.SemanaInicio >= inicioSemana &&
                            d.LiquidacionSemanal.SemanaInicio < inicioSemanaExclusive &&
                            d.LiquidacionSemanal.SemanaFin >= finSemana &&
                            d.LiquidacionSemanal.SemanaFin < finSemanaExclusive)
                .Select(d => new HistorialPagoFuncionarioViewModel
                {
                    ReferenciaId = d.LiquidacionSemanalId,
                    FuncionarioId = d.FuncionarioId,
                    MontoPagado = d.MontoPagado,
                    FechaPago = d.LiquidacionSemanal!.FechaPago,
                    InicioSemana = d.LiquidacionSemanal.SemanaInicio,
                    FinSemana = d.LiquidacionSemanal.SemanaFin,
                    Observacion = d.LiquidacionSemanal.Observacion,
                    OrigenRegistro = "LIQUIDACION"
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<List<FuncionarioResumenData>> LoadFuncionariosSemanaAsync(
            IReadOnlyCollection<int> funcionarioIdsInvolucrados,
            CancellationToken cancellationToken)
        {
            return await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.Activo || funcionarioIdsInvolucrados.Contains(f.IdFuncionario))
                .OrderBy(f => f.Nombre)
                .Select(f => new FuncionarioResumenData
                {
                    IdFuncionario = f.IdFuncionario,
                    Nombre = f.Nombre,
                    PorcentajeGanancia = f.PorcentajeGanancia,
                    PorcentajeProducto = f.PorcentajeProducto
                })
                .ToListAsync(cancellationToken);
        }

        private static void AjustarRedondeoDistribucion(
            List<LiquidacionSemanalDistribucionMensual> distribuciones,
            decimal montoTotalEsperado)
        {
            if (distribuciones.Count == 0)
            {
                return;
            }

            var montoActual = distribuciones.Sum(d => d.MontoAsignado);
            var diferencia = montoTotalEsperado - montoActual;
            distribuciones[^1].MontoAsignado += diferencia;
        }

        private static DateTime NormalizeFechaPago(DateTime? fechaPago, DateTime defaultNow)
        {
            var baseDate = fechaPago ?? defaultNow;
            return new DateTime(
                baseDate.Year,
                baseDate.Month,
                baseDate.Day,
                baseDate.Hour,
                baseDate.Minute,
                0);
        }

        private static string NormalizeMetodoPago(string? metodoPago)
        {
            var normalized = string.IsNullOrWhiteSpace(metodoPago)
                ? "EFECTIVO"
                : metodoPago.Trim().ToUpperInvariant();

            if (!LiquidacionSemanalDefaults.MetodosPagoPermitidos.Contains(normalized))
            {
                throw new InvalidOperationException("El metodo de pago indicado no es valido.");
            }

            return normalized;
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..maxLength];
        }

        private static string BuildDetalleEgreso(
            IReadOnlyCollection<string> nombresFuncionarios,
            DateTime semanaInicio,
            DateTime semanaFin)
        {
            var detalle = nombresFuncionarios.Count == 1
                ? $"Pago a {nombresFuncionarios.Single()} - Semana {semanaInicio:dd/MM} al {semanaFin:dd/MM}"
                : $"Liquidacion semanal de funcionarios - Semana {semanaInicio:dd/MM} al {semanaFin:dd/MM}";

            return detalle.Length <= 200
                ? detalle
                : detalle[..200];
        }

        private static decimal GetAmount(IReadOnlyDictionary<int, decimal> amounts, int funcionarioId)
        {
            return amounts.TryGetValue(funcionarioId, out var amount)
                ? amount
                : 0m;
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("PagoFuncionarios", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class FuncionarioResumenData
        {
            public int IdFuncionario { get; init; }
            public string Nombre { get; init; } = string.Empty;
            public decimal PorcentajeGanancia { get; init; }
            public decimal PorcentajeProducto { get; init; }
        }

        private sealed class ServicioDiaAggregate
        {
            public int FuncionarioId { get; init; }
            public DateTime Fecha { get; init; }
            public int CantidadServicios { get; init; }
            public decimal Monto { get; init; }
        }

        private sealed class ProductoVentaAggregate
        {
            public int FuncionarioId { get; init; }
            public DateTime Fecha { get; init; }
            public string NombreProducto { get; init; } = string.Empty;
            public decimal Precio { get; init; }
        }
    }
}
