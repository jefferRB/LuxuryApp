using System.Security.Claims;
using LuxuryApp.Models.Inversionistas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Inversionistas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Inversionistas
{
    /// <summary>
    /// Módulo de inversionistas: acuerdos de participación, estados de cuenta, pagos y envíos.
    ///
    /// <para>
    /// Solo administradores del tenant. Un inversionista NO es usuario del sistema y en esta fase
    /// no existe portal para él: solo recibe el correo con su estado de cuenta.
    /// </para>
    ///
    /// <para>
    /// El controlador no calcula dinero: todo el cálculo vive en
    /// <see cref="IInvestorProfitCalculationService"/> y <see cref="IInvestorStatementService"/>.
    /// </para>
    /// </summary>
    [Authorize(Roles = AppRoles.Administrador)]
    public class InversionistasController : Controller
    {
        private readonly IInvestorService _investorService;
        private readonly IInvestorStatementService _statementService;
        private readonly IInvestorStatementEmailService _emailService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public InversionistasController(
            IInvestorService investorService,
            IInvestorStatementService statementService,
            IInvestorStatementEmailService emailService,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _investorService = investorService;
            _statementService = statementService;
            _emailService = emailService;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        // ─────────────── Inversionistas ───────────────

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var vm = await _investorService.BuildIndexAsync(cancellationToken);
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Crear(CancellationToken cancellationToken)
        {
            var vm = await _investorService.BuildCreateFormAsync(cancellationToken);
            return View("Form", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(InvestorFormViewModel form, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View("Form", await RehydrateAsync(form, cancellationToken));
            }

            try
            {
                await _investorService.CreateAsync(form, CurrentUserId(), cancellationToken);
                TempData["Mensaje"] = "Inversionista registrado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvestorValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }

            return View("Form", await RehydrateAsync(form, cancellationToken));
        }

        [HttpGet]
        public async Task<IActionResult> Editar(int id, CancellationToken cancellationToken)
        {
            var vm = await _investorService.BuildEditFormAsync(id, cancellationToken);
            return vm is null ? NotFound() : View("Form", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, InvestorFormViewModel form, CancellationToken cancellationToken)
        {
            form.Id = id;

            if (!ModelState.IsValid)
            {
                return View("Form", await RehydrateAsync(form, cancellationToken));
            }

            try
            {
                await _investorService.UpdateAsync(id, form, CurrentUserId(), cancellationToken);
                TempData["Mensaje"] = "Inversionista actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvestorValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }

            return View("Form", await RehydrateAsync(form, cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, bool activo, CancellationToken cancellationToken)
        {
            try
            {
                await _investorService.SetActivoAsync(id, activo, CurrentUserId(), cancellationToken);
                TempData["Mensaje"] = activo
                    ? "El inversionista quedó activo."
                    : "El inversionista quedó inactivo.";
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // ─────────────── Política de cálculo ───────────────

        [HttpGet]
        public async Task<IActionResult> Politica(CancellationToken cancellationToken)
        {
            var vm = await _investorService.BuildPolicyFormAsync(cancellationToken);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Politica(InvestorPolicyViewModel form, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                var recargada = await _investorService.BuildPolicyFormAsync(cancellationToken);
                form.Categorias = recargada.Categorias;
                return View(form);
            }

            await _investorService.SavePolicyAsync(form, CurrentUserId(), cancellationToken);
            TempData["Mensaje"] = "Configuración de la ganancia distribuible guardada.";
            return RedirectToAction(nameof(Politica));
        }

        // ─────────────── Estados de cuenta ───────────────

        [HttpGet]
        public async Task<IActionResult> Estados(
            int? inversionistaId,
            InvestorStatementStatus? estado,
            DateTime? desde,
            DateTime? hasta,
            CancellationToken cancellationToken)
        {
            var filtro = new InvestorStatementFilter
            {
                InvestorId = inversionistaId,
                Estado = estado,
                Desde = desde.HasValue ? DateOnly.FromDateTime(desde.Value.Date) : null,
                Hasta = hasta.HasValue ? DateOnly.FromDateTime(hasta.Value.Date) : null
            };

            var vm = await _statementService.BuildStatementsPageAsync(filtro, cancellationToken);
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> VistaPrevia(int id, DateTime? referencia, CancellationToken cancellationToken)
        {
            try
            {
                var vm = await _statementService.PreviewAsync(
                    id,
                    referencia.HasValue ? DateOnly.FromDateTime(referencia.Value.Date) : null,
                    cancellationToken);

                return View(vm);
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generar(int id, DateTime referencia, CancellationToken cancellationToken)
        {
            try
            {
                var statementId = await _statementService.GenerateDraftAsync(
                    id,
                    DateOnly.FromDateTime(referencia.Date),
                    CurrentUserId(),
                    cancellationToken);

                TempData["Mensaje"] = "Borrador del estado de cuenta generado.";
                return RedirectToAction(nameof(Estado), new { id = statementId });
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(VistaPrevia), new { id, referencia });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Estado(int id, CancellationToken cancellationToken)
        {
            var vm = await _statementService.BuildDetailAsync(id, cancellationToken);
            return vm is null ? NotFound() : View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Recalcular(int id, CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.RecalculateAsync(id, CurrentUserId(), cancellationToken),
                "Estado de cuenta recalculado con los datos actuales.",
                id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Finalizar(int id, CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.FinalizeAsync(id, CurrentUserId(), cancellationToken),
                "Estado de cuenta finalizado. Sus valores quedaron congelados.",
                id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Anular(int id, string motivo, CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.VoidAsync(id, motivo, CurrentUserId(), cancellationToken),
                "Estado de cuenta anulado.",
                id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reabrir(int id, string motivo, CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.ReopenAsync(id, motivo, CurrentUserId(), cancellationToken),
                "Estado de cuenta reabierto como borrador.",
                id);

        // ─────────────── Ajustes ───────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> AgregarAjuste(
            InvestorAdjustmentFormViewModel form,
            CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.AddAdjustmentAsync(form, CurrentUserId(), CurrentUserEmail(), cancellationToken),
                "Ajuste registrado.",
                form.StatementId);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuitarAjuste(int id, int statementId, CancellationToken cancellationToken)
        {
            try
            {
                await _statementService.RemoveAdjustmentAsync(id, CurrentUserId(), cancellationToken);
                TempData["Mensaje"] = "Ajuste eliminado.";
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Estado), new { id = statementId });
        }

        // ─────────────── Pagos ───────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> RegistrarPago(
            InvestorPaymentFormViewModel form,
            CancellationToken cancellationToken) =>
            ExecuteAsync(
                () => _statementService.RegisterPaymentAsync(form, CurrentUserId(), CurrentUserEmail(), cancellationToken),
                "Pago registrado.",
                form.StatementId);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevertirPago(
            int id,
            int statementId,
            string motivo,
            CancellationToken cancellationToken)
        {
            try
            {
                await _statementService.ReversePaymentAsync(
                    id,
                    motivo,
                    CurrentUserId(),
                    CurrentUserEmail(),
                    cancellationToken);

                TempData["Mensaje"] = "Pago corregido. Queda registrado el movimiento compensatorio.";
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Estado), new { id = statementId });
        }

        // ─────────────── Envíos y PDF ───────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Enviar(int id, CancellationToken cancellationToken) =>
            SendAsync(() => _emailService.SendAsync(id, CurrentUserId(), cancellationToken), id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reenviar(int id, CancellationToken cancellationToken) =>
            SendAsync(() => _emailService.ResendAsync(id, CurrentUserId(), cancellationToken), id);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> EnviarPrueba(int id, string correo, CancellationToken cancellationToken) =>
            SendAsync(() => _emailService.SendTestAsync(id, correo, CurrentUserId(), cancellationToken), id);

        [HttpGet]
        public async Task<IActionResult> DescargarPdf(int id, CancellationToken cancellationToken)
        {
            var pdf = await _emailService.BuildPdfAsync(id, cancellationToken);
            if (pdf is null)
            {
                return NotFound();
            }

            return File(pdf.Value.Content, "application/pdf", pdf.Value.FileName);
        }

        // ─────────────── Helpers ───────────────

        private async Task<IActionResult> ExecuteAsync(Func<Task> action, string mensajeExito, int statementId)
        {
            try
            {
                await action();
                TempData["Mensaje"] = mensajeExito;
            }
            catch (InvestorValidationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Estado), new { id = statementId });
        }

        private async Task<IActionResult> SendAsync(
            Func<Task<InvestorStatementSendResult>> action,
            int statementId)
        {
            var resultado = await action();

            if (resultado.Success)
            {
                TempData["Mensaje"] = resultado.Message;
            }
            else
            {
                TempData["Error"] = resultado.Message;
            }

            return RedirectToAction(nameof(Estado), new { id = statementId });
        }

        private async Task<InvestorFormViewModel> RehydrateAsync(
            InvestorFormViewModel form,
            CancellationToken cancellationToken)
        {
            // Se conserva lo que el usuario escribió y solo se recargan los datos de contexto
            // (participación de otros, próximo inicio de periodo) para que la ayuda siga siendo real.
            var contexto = form.Id.HasValue
                ? await _investorService.BuildEditFormAsync(form.Id.Value, cancellationToken)
                : await _investorService.BuildCreateFormAsync(cancellationToken);

            if (contexto is not null)
            {
                form.ParticipacionOtros = contexto.ParticipacionOtros;
                form.PorcentajeVigenteActual = contexto.PorcentajeVigenteActual;
                form.ProximoInicioPeriodo = contexto.ProximoInicioPeriodo;
            }

            return form;
        }

        private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private string? CurrentUserEmail() =>
            User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
    }
}
