using LuxuryApp.Models.Fiscal;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Configuracion
{
    [Authorize(Roles = "Administrador")]
    public class ConfiguracionFiscalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<ConfiguracionFiscalController> _logger;

        public ConfiguracionFiscalController(
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            ILogger<ConfiguracionFiscalController> logger)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var tenantId = _tenantProvider.GetTenantId();

            var vm = await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new ConfiguracionFiscalViewModel
                {
                    PreciosIncluyenIva = t.PreciosIncluyenIva,
                    TarifaIvaPorDefecto = t.TarifaIvaPorDefecto
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? new ConfiguracionFiscalViewModel();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
            ConfiguracionFiscalViewModel model,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var tenantId = _tenantProvider.GetTenantId();
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

            if (tenant is null)
            {
                return NotFound();
            }

            tenant.PreciosIncluyenIva = model.PreciosIncluyenIva;
            tenant.TarifaIvaPorDefecto = Math.Round(model.TarifaIvaPorDefecto, 2, MidpointRounding.AwayFromZero);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                TempData["Mensaje"] = "Configuración fiscal actualizada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al guardar la configuración fiscal del tenant {TenantId}.", tenantId);
                ModelState.AddModelError(string.Empty, "No fue posible guardar la configuración fiscal.");
                return View(model);
            }
        }
    }
}
