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
                throw new InvalidOperationException("La semana indicada no es válida.");
            }

            var cobros = await _context.Cobros
                .Include(c => c.Producto)
                .Where(c => c.FechaCobro >= inicioSemana && c.FechaCobro < finSemana.AddDays(1))
                .ToListAsync(cancellationToken);

            var pagosHistoricos = await _context.PagosFuncionarios
                .Where(p => p.InicioSemana.Date == inicioSemana && p.FinSemana.Date == finSemana)
                .ToListAsync(cancellationToken);

            var liquidacionesSemana = await _context.LiquidacionesSemanales
                .Include(l => l.Detalles)
                .Where(l => l.SemanaInicio.Date == inicioSemana && l.SemanaFin.Date == finSemana)
                .ToListAsync(cancellationToken);

            var detalleLiquidaciones = liquidacionesSemana
                .SelectMany(l => l.Detalles.Select(d => new
                {
                    LiquidacionId = l.Id,
                    l.FechaPago,
                    l.Observacion,
                    d.FuncionarioId,
                    d.MontoPagado
                }))
                .ToList();

            var funcionarioIdsInvolucrados = cobros
                .Select(c => c.FuncionarioId)
                .Concat(pagosHistoricos.Select(p => p.FuncionarioId))
                .Concat(detalleLiquidaciones.Select(d => d.FuncionarioId))
                .Distinct()
                .ToList();

            var funcionarios = await _context.Funcionarios
                .Where(f => f.Activo || funcionarioIdsInvolucrados.Contains(f.IdFuncionario))
                .OrderBy(f => f.Nombre)
                .ToListAsync(cancellationToken);

            var pagos = funcionarios.Select(f =>
            {
                var cobrosFuncionario = cobros
                    .Where(c => c.FuncionarioId == f.IdFuncionario)
                    .ToList();

                var servicios = cobrosFuncionario
                    .Where(c => c.ServicioId != null)
                    .ToList();

                var productos = cobrosFuncionario
                    .Where(c => c.ProductoId != null)
                    .ToList();

                var totalServicios = servicios.Sum(c => c.Monto);
                var totalProductos = productos.Sum(c => c.Monto);
                var total = totalServicios + totalProductos;
                var impuestos = (totalServicios + totalProductos) * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
                var netoServicios = totalServicios - (totalServicios * PagoFuncionarioDevengadoCalculator.TasaImpuesto);
                var netoProductos = totalProductos - (totalProductos * PagoFuncionarioDevengadoCalculator.TasaImpuesto);
                var pagoServicios = netoServicios * (f.PorcentajeGanancia / 100);
                var pagoProductos = netoProductos * (f.PorcentajeProducto / 100);
                var pagoFuncionario = pagoServicios + pagoProductos;
                var totalPagadoLegacy = pagosHistoricos
                    .Where(p => p.FuncionarioId == f.IdFuncionario)
                    .Sum(p => p.MontoPagado);
                var totalPagadoLiquidaciones = detalleLiquidaciones
                    .Where(d => d.FuncionarioId == f.IdFuncionario)
                    .Sum(d => d.MontoPagado);
                var totalPagado = totalPagadoLegacy + totalPagadoLiquidaciones;
                var pendiente = pagoFuncionario - totalPagado;

                var detalleDias = Enumerable.Range(0, (finSemana - inicioSemana).Days + 1)
                    .Select(i =>
                    {
                        var fechaDia = inicioSemana.AddDays(i).Date;
                        var serviciosDia = servicios
                            .Where(c => c.FechaCobro.Date == fechaDia)
                            .ToList();

                        return new DetalleDiaVM
                        {
                            Dia = fechaDia.ToString("dddd"),
                            CantidadServicios = serviciosDia.Count,
                            Monto = serviciosDia.Sum(s => s.Monto)
                        };
                    })
                    .ToList();

                var productosVendidos = productos
                    .Select(p => new ProductoVendidoVM
                    {
                        Fecha = p.FechaCobro,
                        NombreProducto = p.Producto?.NombreProducto ?? "Producto",
                        Precio = p.Monto,
                        GananciaFuncionario = (p.Monto - (p.Monto * PagoFuncionarioDevengadoCalculator.TasaImpuesto)) * (f.PorcentajeProducto / 100)
                    })
                    .OrderByDescending(p => p.Fecha)
                    .ToList();

                var historialPagos = pagosHistoricos
                    .Where(p => p.FuncionarioId == f.IdFuncionario)
                    .Select(p => new PagoFuncionario
                    {
                        IdPago = p.IdPago,
                        FuncionarioId = p.FuncionarioId,
                        MontoPagado = p.MontoPagado,
                        FechaPago = p.FechaPago,
                        InicioSemana = p.InicioSemana,
                        FinSemana = p.FinSemana,
                        Observacion = p.Observacion
                    })
                    .Concat(detalleLiquidaciones
                        .Where(d => d.FuncionarioId == f.IdFuncionario)
                        .Select(d => new PagoFuncionario
                        {
                            IdPago = d.LiquidacionId,
                            FuncionarioId = d.FuncionarioId,
                            MontoPagado = d.MontoPagado,
                            FechaPago = d.FechaPago,
                            InicioSemana = inicioSemana,
                            FinSemana = finSemana,
                            Observacion = d.Observacion
                        }))
                    .OrderByDescending(p => p.FechaPago)
                    .ToList();

                return new PagoFuncionarioVM
                {
                    FuncionarioId = f.IdFuncionario,
                    Nombre = f.Nombre,
                    TotalGenerado = total,
                    Impuestos = impuestos,
                    TotalNeto = total - impuestos,
                    Porcentaje = f.PorcentajeGanancia,
                    PorcentajeProducto = f.PorcentajeProducto,
                    PagoFinal = pagoFuncionario,
                    MontoPagado = totalPagado,
                    MontoPendiente = pendiente,
                    DetalleDias = detalleDias,
                    ProductosVendidos = productosVendidos,
                    HistorialPagos = historialPagos
                };
            }).ToList();

            var totalGeneradoServicios = cobros
                .Where(c => c.ServicioId != null)
                .Sum(c => c.Monto);

            var totalGeneradoProductos = cobros
                .Where(c => c.ProductoId != null)
                .Sum(c => c.Monto);

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
                throw new InvalidOperationException("La liquidación solicitada no es válida.");
            }

            command.SemanaInicio = command.SemanaInicio.Date;
            command.SemanaFin = command.SemanaFin.Date;

            if (command.SemanaFin < command.SemanaInicio)
            {
                throw new InvalidOperationException("La semana indicada no es válida.");
            }

            command.MetodoPago = NormalizeMetodoPago(command.MetodoPago);
            command.Observacion = NormalizeOptionalText(command.Observacion, 500);
            command.CreadoPor = NormalizeOptionalText(command.CreadoPor, 450);

            var detallesSolicitados = command.Detalles
                .Where(d => d.MontoPagado > 0)
                .GroupBy(d => d.FuncionarioId)
                .Select(g => new RegistrarLiquidacionSemanalDetalleCommand
                {
                    FuncionarioId = g.Key,
                    MontoPagado = g.Sum(x => x.MontoPagado)
                })
                .ToList();

            if (detallesSolicitados.Count == 0)
            {
                throw new InvalidOperationException("No hay montos válidos para registrar en la liquidación.");
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
                var fechaPago = NormalizeFechaPago(command.FechaPago);
                var montoTotal = detallesValidados.Sum(x => x.MontoPagado);

                var egreso = new Egreso
                {
                    FechaEgreso = fechaPago,
                    CategoriaId = categoria.Id,
                    Monto = montoTotal,
                    MetodoPago = command.MetodoPago,
                    Detalle = BuildDetalleEgreso(
                        detallesValidados.Select(x => x.Resumen.Nombre).ToList(),
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
                    FechaCreacion = DateTime.Now,
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
                        MontoServicios = detalle.Resumen.TotalGenerado - detalle.Resumen.ProductosVendidos.Sum(p => p.Precio),
                        MontoProductos = detalle.Resumen.ProductosVendidos.Sum(p => p.Precio),
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
                    "Liquidación semanal {LiquidacionId} registrada. Semana {SemanaInicio:yyyy-MM-dd} a {SemanaFin:yyyy-MM-dd}. Monto {MontoTotal}.",
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
                Detalle = "Categoría generada automáticamente para registrar pagos reales a funcionarios.",
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
                _logger.LogWarning(ex, "La categoría de Pago Funcionarios fue creada concurrentemente por otra solicitud.");
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
                .Where(f => funcionarioIds.Contains(f.IdFuncionario))
                .ToDictionaryAsync(f => f.IdFuncionario, cancellationToken);

            var cobrosSemana = await _context.Cobros
                .Where(c => funcionarioIds.Contains(c.FuncionarioId) &&
                            c.FechaCobro >= semanaInicio.Date &&
                            c.FechaCobro < semanaFin.Date.AddDays(1))
                .ToListAsync(cancellationToken);

            var distribuciones = new List<LiquidacionSemanalDistribucionMensual>();
            var diasPorMes = new Dictionary<(int Anio, int Mes), HashSet<DateTime>>();
            foreach (var detalle in detallesValidados)
            {
                if (!funcionarios.TryGetValue(detalle.Resumen.FuncionarioId, out var funcionario))
                {
                    throw new InvalidOperationException(
                        $"No se pudo resolver el funcionario {detalle.Resumen.FuncionarioId} para construir la distribución analítica.");
                }

                var cobrosFuncionario = cobrosSemana
                    .Where(c => c.FuncionarioId == detalle.Resumen.FuncionarioId)
                    .ToList();

                var distribucionFuncionario = PagoFuncionarioDevengadoCalculator.DistribuirMontoPagadoPorMes(
                    cobrosFuncionario,
                    funcionario,
                    detalle.MontoPagado);

                if (distribucionFuncionario.Count == 0 && detalle.MontoPagado > 0)
                {
                    throw new InvalidOperationException(
                        $"No se encontró producción devengada para distribuir analíticamente el pago del funcionario {funcionario.Nombre} en la semana seleccionada.");
                }

                foreach (var distribucionMes in distribucionFuncionario)
                {
                    var claveMes = (distribucionMes.Anio, distribucionMes.Mes);
                    if (!diasPorMes.TryGetValue(claveMes, out var fechasMes))
                    {
                        fechasMes = cobrosFuncionario
                            .Where(c => c.FechaCobro.Year == distribucionMes.Anio &&
                                        c.FechaCobro.Month == distribucionMes.Mes)
                            .Select(c => c.FechaCobro.Date)
                            .ToHashSet();

                        diasPorMes[claveMes] = fechasMes;
                    }
                    else
                    {
                        foreach (var fecha in cobrosFuncionario
                                     .Where(c => c.FechaCobro.Year == distribucionMes.Anio &&
                                                 c.FechaCobro.Month == distribucionMes.Mes)
                                     .Select(c => c.FechaCobro.Date))
                        {
                            fechasMes.Add(fecha);
                        }
                    }

                    var existente = distribuciones.FirstOrDefault(d =>
                        d.Anio == distribucionMes.Anio &&
                        d.Mes == distribucionMes.Mes);

                    if (existente == null)
                    {
                        distribuciones.Add(new LiquidacionSemanalDistribucionMensual
                        {
                            LiquidacionSemanalId = liquidacionId,
                            Anio = distribucionMes.Anio,
                            Mes = distribucionMes.Mes,
                            MontoAsignado = distribucionMes.MontoAsignado,
                            DiasAplicados = fechasMes.Count
                        });
                        continue;
                    }

                    existente.MontoAsignado += distribucionMes.MontoAsignado;
                    existente.DiasAplicados = fechasMes.Count;
                }
            }

            AjustarRedondeoDistribucion(distribuciones, detallesValidados.Sum(d => d.MontoPagado));
            return distribuciones
                .OrderBy(d => d.Anio)
                .ThenBy(d => d.Mes)
                .ToList();
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

        private static DateTime NormalizeFechaPago(DateTime? fechaPago)
        {
            var baseDate = fechaPago ?? DateTime.Now;
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
                throw new InvalidOperationException("El método de pago indicado no es válido.");
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
            string detalle = nombresFuncionarios.Count == 1
                ? $"Pago a {nombresFuncionarios.Single()} - Semana {semanaInicio:dd/MM} al {semanaFin:dd/MM}"
                : $"Liquidación semanal de funcionarios - Semana {semanaInicio:dd/MM} al {semanaFin:dd/MM}";

            return detalle.Length <= 200
                ? detalle
                : detalle[..200];
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("PagoFuncionarios", StringComparison.OrdinalIgnoreCase);
        }
    }
}
