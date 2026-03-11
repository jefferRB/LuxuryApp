using LuxuryApp.Models.Informacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

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

        public async Task<IActionResult> Index()
        {
            var vm = new InformacionViewModel();

            // MES CON MÁS CITAS
            var mesMas = await _context.Citas
                .GroupBy(c => c.FechaHoraCita.Month)
                .Select(g => new { Mes = g.Key, Total = g.Count() })
                .OrderByDescending(g => g.Total)
                .FirstOrDefaultAsync();

            if (mesMas != null)
            {
                vm.MesMasCitas = System.Globalization.CultureInfo.CurrentCulture
                    .DateTimeFormat.GetMonthName(mesMas.Mes);

                vm.TotalMesMasCitas = mesMas.Total;
            }

            // MES CON MENOS CITAS
            var mesMenos = await _context.Citas
                .GroupBy(c => c.FechaHoraCita.Month)
                .Select(g => new { Mes = g.Key, Total = g.Count() })
                .OrderBy(g => g.Total)
                .FirstOrDefaultAsync();

            if (mesMenos != null)
            {
                vm.MesMenosCitas = System.Globalization.CultureInfo.CurrentCulture
                    .DateTimeFormat.GetMonthName(mesMenos.Mes);

                vm.TotalMesMenosCitas = mesMenos.Total;
            }

            // DIA MAS OCUPADO
            var diaMas = _context.Citas
     .Select(c => c.FechaHoraCita)
     .AsEnumerable()
     .GroupBy(f => f.DayOfWeek)
     .Select(g => new { Dia = g.Key, Total = g.Count() })
     .OrderByDescending(g => g.Total)
     .FirstOrDefault();

            if (diaMas != null)
            {
                vm.DiaMasOcupado = diaMas.Dia.ToString();
                vm.TotalDiaMasOcupado = diaMas.Total;
            }

            // HORA MAS OCUPADA
            var horaMas = await _context.Citas
                .GroupBy(c => c.FechaHoraCita.Hour)
                .Select(g => new { Hora = g.Key, Total = g.Count() })
                .OrderByDescending(g => g.Total)
                .FirstOrDefaultAsync();

            if (horaMas != null)
            {
                vm.HoraMasOcupada = $"{horaMas.Hora}:00";
                vm.TotalHoraMasOcupada = horaMas.Total;
            }

            // SERVICIO MAS SOLICITADO
            var servicio = await _context.Citas
                .Include(c => c.Servicio)
                .GroupBy(c => c.Servicio.Nombre)
                .Select(g => new { Nombre = g.Key, Total = g.Count() })
                .OrderByDescending(g => g.Total)
                .FirstOrDefaultAsync();

            if (servicio != null)
            {
                vm.ServicioMasSolicitado = servicio.Nombre;
                vm.TotalServicioMasSolicitado = servicio.Total;
            }

            // FUNCIONARIO CON MAS CITAS
            var funcionario = await _context.Citas
                .Include(c => c.Funcionario)
                .GroupBy(c => c.Funcionario.Nombre)
                .Select(g => new { Nombre = g.Key, Total = g.Count() })
                .OrderByDescending(g => g.Total)
                .FirstOrDefaultAsync();

            if (funcionario != null)
            {
                vm.FuncionarioMasCitas = funcionario.Nombre;
                vm.TotalFuncionarioCitas = funcionario.Total;
            }
            //citas por barbero del mes 
            var inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var finMes = inicioMes.AddMonths(1);

            var citasBarberos = await _context.Citas
            .Where(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes)
            .Include(c => c.Funcionario)
            .GroupBy(c => c.Funcionario.Nombre)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .ToListAsync();

            vm.BarberosNombres = citasBarberos.Select(x => x.Nombre).ToList();
            vm.BarberosCitas = citasBarberos.Select(x => x.Total).ToList();

            //Servicios solicitados 

            var servicios = await _context.Citas
            .Where(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes && c.ServicioId != null)
            .Include(c => c.Servicio)
            .GroupBy(c => c.Servicio.Nombre)
            .Select(g => new
            {
                Nombre = g.Key,
                Total = g.Count()
            })
            .ToListAsync();

            vm.ServiciosNombres = servicios.Select(x => x.Nombre).ToList();
            vm.ServiciosCantidad = servicios.Select(x => x.Total).ToList();

            // TOP CLIENTES
            vm.TopClientes = await _context.ClienteVisitas
                .GroupBy(v => v.NumeroTelefono)
                .Select(g => new TopClienteVM
                {
                    Telefono = g.Key,
                    TotalVisitas = g.Count()
                })
                .OrderByDescending(x => x.TotalVisitas)
                .Take(10)
                .ToListAsync();

            return View(vm);
        }
    }
}