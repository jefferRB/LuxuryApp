using LuxuryApp.Models.Calendar;
using Microsoft.AspNetCore.Mvc;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Calendar
{
    [ApiController]
    [Route("[controller]")]
    public class BarberosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BarberosController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetAll")]
        public IActionResult GetAll()
        {
            var barberos = _context.Barberos
                .Where(b => b.Activo)
                .OrderBy(b => b.Nombre)
                .ToList();

            return Ok(barberos);
        }

        [HttpPost("Create")]
        public IActionResult Create([FromBody] BarberoCreateVM vm)
        {
            if (string.IsNullOrWhiteSpace(vm.Nombre))
                return BadRequest("Nombre inválido");

            var barbero = new Barbero { Nombre = vm.Nombre };
            _context.Barberos.Add(barbero);
            _context.SaveChanges();

            return Ok(barbero);
        }

        [HttpDelete("Delete/{id}")]
        public IActionResult Delete(int id)
        {
            var barbero = _context.Barberos.Find(id);
            if (barbero == null) return NotFound();

            barbero.Activo = false; // borrado lógico
            _context.SaveChanges();

            return Ok();
        }
    }
}
