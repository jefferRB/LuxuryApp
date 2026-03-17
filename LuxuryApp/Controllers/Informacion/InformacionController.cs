using LuxuryApp.Models.Informacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;
using System.Globalization;

namespace LuxuryApp.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class InformacionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public InformacionController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? mes, int? anio, int top = 10)
        {
            var hoy = DateTime.Now;

            int mesActual = mes ?? hoy.Month;
            int anioActual = anio ?? hoy.Year;

            var inicioMes = new DateTime(anioActual, mesActual, 1);
            var finMes = inicioMes.AddMonths(1);

            // semana actual (lunes a domingo)
            var inicioSemana = hoy.Date.AddDays(-(int)hoy.DayOfWeek + (int)DayOfWeek.Monday);
            var finSemana = inicioSemana.AddDays(7);

            var vm = new InformacionViewModel
            {
                MesSeleccionado = mesActual,
                AnioSeleccionado = anioActual
            };

            // SOLO CITAS (NO DESCANSOS)
            var citas = _context.Citas
                .Where(c => c.Tipo == "CITA");

            var citasMesFiltrado = citas
            .Where(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes);

            //filtrar productos por mes 
            var cobrosMes = _context.Cobros
            .Where(c => c.FechaCobro >= inicioMes && c.FechaCobro < finMes);

            // ====================================================
            // MES CON MAS / MENOS CITAS
            // ====================================================

            var citasPorMes = await citas
                .GroupBy(c => c.FechaHoraCita.Month)
                .Select(g => new
                {
                    Mes = g.Key,
                    Total = g.Count()
                })
                .ToListAsync();

            var mesMas = citasPorMes.OrderByDescending(x => x.Total).FirstOrDefault();
            var mesMenos = citasPorMes.OrderBy(x => x.Total).FirstOrDefault();

            if (mesMas != null)
            {
                vm.MesMasCitas = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mesMas.Mes);
                vm.TotalMesMasCitas = mesMas.Total;
            }

            if (mesMenos != null)
            {
                vm.MesMenosCitas = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mesMenos.Mes);
                vm.TotalMesMenosCitas = mesMenos.Total;
            }

            // ====================================================
            // DIA MAS OCUPADO / LIBRE
            // ====================================================

            var fechas = await citas
                .Select(c => c.FechaHoraCita)
                .ToListAsync();

            var dias = fechas
                .GroupBy(f => f.DayOfWeek)
                .Select(g => new
                {
                    Dia = g.Key,
                    Total = g.Count()
                })
                .ToList();

            var diaMas = dias.OrderByDescending(x => x.Total).FirstOrDefault();
            var diaMenos = dias.OrderBy(x => x.Total).FirstOrDefault();

            if (diaMas != null)
            {
                vm.DiaMasOcupado = new CultureInfo("es-ES").DateTimeFormat.GetDayName(diaMas.Dia);
                vm.TotalDiaMasOcupado = diaMas.Total;
            }

            if (diaMenos != null)
            {
                vm.DiaMasLibre = new CultureInfo("es-ES").DateTimeFormat.GetDayName(diaMenos.Dia);
                vm.TotalDiaMasLibre = diaMenos.Total;
            }

            // ====================================================
            // HORA MAS OCUPADA / LIBRE
            // ====================================================

            var horas = await citas
                .GroupBy(c => c.FechaHoraCita.Hour)
                .Select(g => new
                {
                    Hora = g.Key,
                    Total = g.Count()
                })
                .ToListAsync();

            var horaMas = horas.OrderByDescending(x => x.Total).FirstOrDefault();
            var horaMenos = horas.OrderBy(x => x.Total).FirstOrDefault();

            if (horaMas != null)
            {
                vm.HoraMasOcupada = $"{horaMas.Hora}:00";
                vm.PromedioHoraMasOcupada = horaMas.Total;
            }

            if (horaMenos != null)
            {
                vm.HoraMasLibre = $"{horaMenos.Hora}:00";
                vm.PromedioHoraMasLibre = horaMenos.Total;
            }

            // ====================================================
            // SERVICIO MAS SOLICITADO
            // ====================================================

            var servicio = await citasMesFiltrado
            .Where(c => c.ServicioId != null)
            .GroupBy(c => c.Servicio!.Nombre)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .OrderByDescending(x => x.Total)
            .FirstOrDefaultAsync();

            if (servicio != null)
            {
                vm.ServicioMasSolicitado = servicio.Nombre;
                vm.TotalServicioMasSolicitado = servicio.Total;
            }

            // ====================================================
            // PRODUCTO MAS VENDIDO
            // ====================================================

            var producto = await cobrosMes
            .Where(c => c.ProductoId != null)
            .GroupBy(c => c.Producto!.NombreProducto)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .OrderByDescending(x => x.Total)
            .FirstOrDefaultAsync();

            if (producto != null)
            {
                vm.ProductoMasVendido = producto.Nombre;
                vm.TotalProductoMasVendido = producto.Total;
            }

            //Producto menos vendido 

            var productoMenos = await cobrosMes
            .Where(c => c.ProductoId != null)
            .GroupBy(c => c.Producto!.NombreProducto)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .OrderBy(x => x.Total)
            .FirstOrDefaultAsync();

            if (productoMenos != null)
            {
                vm.ProductoMenosVendido = productoMenos.Nombre;
                vm.TotalProductoMenosVendido = productoMenos.Total;
            }

            // ====================================================
            // FUNCIONARIO CON MAS CITAS
            // ====================================================

            var funcionario = await citasMesFiltrado
            .GroupBy(c => c.Funcionario!.Nombre)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .OrderByDescending(x => x.Total)
            .FirstOrDefaultAsync();

            if (funcionario != null)
            {
                vm.FuncionarioMasCitas = funcionario.Nombre;
                vm.TotalFuncionarioCitas = funcionario.Total;
            }

            // ====================================================
            // GRAFICO CITAS POR MES
            // ====================================================

            for (int m = 1; m <= 12; m++)
            {
                var inicio = new DateTime(anioActual, m, 1);
                var fin = inicio.AddMonths(1);

                var total = await citas
                    .CountAsync(c => c.FechaHoraCita >= inicio && c.FechaHoraCita < fin);

                vm.CitasPorMes.Add(total);
            }

            // ====================================================
            // GRAFICO SEMANA ACTUAL
            // ====================================================

            var citasSemana = await citas
                .Where(c => c.FechaHoraCita >= inicioSemana && c.FechaHoraCita < finSemana)
                .Select(c => c.FechaHoraCita)
                .ToListAsync();

            var diasSemana = new[]
            {
                "Lunes","Martes","Miércoles","Jueves","Viernes","Sábado","Domingo"
            };

            foreach (var dia in diasSemana)
            {
                vm.SemanaDias.Add(dia);

                var total = citasSemana
                    .Count(c => c.ToString("dddd", new CultureInfo("es-ES"))
                    .Equals(dia, StringComparison.OrdinalIgnoreCase));

                vm.SemanaCitas.Add(total);
            }

            // ====================================================
            // GRAFICO FUNCIONARIOS
            // ====================================================

            var citasFuncionarios = await citas
                .Where(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes)
                .GroupBy(c => c.Funcionario!.Nombre)
                .Select(g => new
                {
                    Nombre = g.Key,
                    Total = g.Count()
                })
                .ToListAsync();

            vm.FuncionariosNombres = citasFuncionarios.Select(x => x.Nombre).ToList();
            vm.FuncionariosCitas = citasFuncionarios.Select(x => x.Total).ToList();

            // ====================================================
            // TOP CLIENTES
            // ====================================================

            vm.TopClientes = await citas
                .GroupBy(c => new { c.NombreCliente, c.TelefonoCliente })
                .Select(g => new TopClienteVM
                {
                    Nombre = g.Key.NombreCliente!,
                    Telefono = g.Key.TelefonoCliente!,
                    TotalVisitas = g.Count()
                })
                .OrderByDescending(x => x.TotalVisitas)
                .Take(top)
                .ToListAsync();

            vm.TopCantidad = top;

            // ====================================================
            // GRAFICO SERVICIOS POR MES
            // ====================================================

            var serviciosMes = await citas
                .Where(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes)
                .Where(c => c.ServicioId != null)
                .GroupBy(c => c.Servicio!.Nombre)
                .Select(g => new
                {
                    Nombre = g.Key,
                    Total = g.Count()
                })
                .OrderByDescending(x => x.Total)
                .ToListAsync();

            vm.ServiciosNombres = serviciosMes.Select(x => x.Nombre).ToList();
            vm.ServiciosCantidad = serviciosMes.Select(x => x.Total).ToList();

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerCitasSemana(DateTime semana)
        {
            var inicioSemana = semana.Date;
            var finSemana = inicioSemana.AddDays(7); 

            var citas = await _context.Citas
                .Where(c => c.Tipo == "CITA") 
                .Where(c => c.FechaHoraCita >= inicioSemana && c.FechaHoraCita < finSemana)
                .Select(c => c.FechaHoraCita)
                .ToListAsync();

            var diasSemana = new[] { "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom" };

            var resultado = new List<int>();

            for (int i = 0; i < 7; i++)
            {
                var diaActual = inicioSemana.AddDays(i).DayOfWeek;

                var total = citas.Count(c => c.DayOfWeek == diaActual);

                resultado.Add(total);
            }

            return Json(new
            {
                dias = diasSemana,
                citas = resultado,
                inicio = inicioSemana.ToString("dd"),
                fin = inicioSemana.AddDays(6).ToString("dd")
            });
        }

    }
}