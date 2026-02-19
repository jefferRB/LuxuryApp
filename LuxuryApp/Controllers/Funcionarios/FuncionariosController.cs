using LuxuryApp.Models.Funcionarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Funcionarios
{
    [Authorize(Roles = "Administrador")]
    public class FuncionariosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FuncionariosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ============================
        // LISTADO
        // ============================
        public async Task<IActionResult> Index()
        {
            var funcionarios = await _context.Funcionarios
                .OrderBy(f => f.Nombre)
                .ToListAsync();

            return View(funcionarios);
        }

        // ============================
        // CREAR
        // ============================

        [HttpGet]
        public IActionResult Create()
        {
            var funcionario = new Funcionario
            {
                FechaIngreso = DateTime.Today,
                Activo = true
            };

            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Funcionario funcionario)
        {
            bool nombreExiste = await _context.Funcionarios
                .AnyAsync(f => f.Nombre == funcionario.Nombre);

            if (nombreExiste)
            {
                ModelState.AddModelError("Nombre", "Ya existe un funcionario con ese nombre.");
            }

            if (!ModelState.IsValid)
                return View(funcionario);

            _context.Funcionarios.Add(funcionario);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario creado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ============================
        // EDITAR
        // ============================

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Funcionario funcionario)
        {
            if (!ModelState.IsValid)
                return View(funcionario);

            var funcionarioDB = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionario.IdFuncionario);

            if (funcionarioDB == null)
                return NotFound();

            funcionarioDB.Nombre = funcionario.Nombre;
            funcionarioDB.Telefono = funcionario.Telefono;
            funcionarioDB.IdPuesto = funcionario.IdPuesto;
            funcionarioDB.ColorCalendario = funcionario.ColorCalendario;
            funcionarioDB.PorcentajeGanancia = funcionario.PorcentajeGanancia;
            funcionarioDB.FechaIngreso = funcionario.FechaIngreso;
            funcionarioDB.Activo = funcionario.Activo;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ============================
        // ELIMINAR (Soft delete recomendado)
        // ============================

        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            funcionario.Activo = false;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario desactivado.";

            return RedirectToAction(nameof(Index));
        }

        // ============================
        // ACTIVAR
        // ============================

        [HttpPost]
        public async Task<IActionResult> Activar(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            funcionario.Activo = true;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario activado.";

            return RedirectToAction(nameof(Index));
        }
    }
}
