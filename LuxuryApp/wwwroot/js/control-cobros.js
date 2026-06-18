/* ===========================================================================
   Control de citas y cobros — interacción de la vista admin.
   - Filtros (Día/Semana/Mes, fecha, funcionario, estado, búsqueda) por AJAX.
   - Cobro rápido con modal -> POST /Calendar/CobrarCita (reusa CobroService).
   - Cancelar cita -> DELETE /Calendar/Delete/{id}.
   Actualiza KPIs y tabla sin recargar toda la página.
   =========================================================================== */
(function () {
    "use strict";

    var resultsEl = document.getElementById("cc-results");
    if (!resultsEl) return;

    function token() {
        var el = document.querySelector("#cc-antiforgery input[name='__RequestVerificationToken']");
        return el ? el.value : "";
    }

    var segWrap = document.getElementById("cc-rango");
    var fechaEl = document.getElementById("cc-fecha");
    var funcEl = document.getElementById("cc-funcionario");
    var estadoEl = document.getElementById("cc-estado");
    var buscarEl = document.getElementById("cc-buscar");

    function rangoActivo() {
        var b = segWrap ? segWrap.querySelector(".cc-seg-btn.active") : null;
        return b ? b.dataset.rango : "dia";
    }

    function buildParams() {
        var p = new URLSearchParams();
        p.set("rango", rangoActivo());
        if (fechaEl && fechaEl.value) p.set("fecha", fechaEl.value);
        if (funcEl && funcEl.value) p.set("funcionarioId", funcEl.value);
        if (estadoEl && estadoEl.value) p.set("estado", estadoEl.value);
        if (buscarEl && buscarEl.value.trim()) p.set("buscar", buscarEl.value.trim());
        return p;
    }

    var cargando = false;
    function loadResults() {
        if (cargando) return;
        cargando = true;
        resultsEl.classList.add("cc-loading");
        fetch("/Calendar/ControlCobrosData?" + buildParams().toString(), {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            credentials: "same-origin"
        })
            .then(function (r) {
                if (!r.ok) throw new Error("No se pudieron cargar los datos.");
                return r.text();
            })
            .then(function (html) { resultsEl.innerHTML = html; })
            .catch(function () { toast("No se pudieron cargar los datos. Intenta de nuevo.", true); })
            .finally(function () {
                cargando = false;
                resultsEl.classList.remove("cc-loading");
            });
    }

    // ── Filtros ────────────────────────────────────────────────────────────
    if (segWrap) {
        segWrap.addEventListener("click", function (e) {
            var btn = e.target.closest(".cc-seg-btn");
            if (!btn || btn.classList.contains("active")) return;
            segWrap.querySelectorAll(".cc-seg-btn").forEach(function (b) { b.classList.remove("active"); });
            btn.classList.add("active");
            loadResults();
        });
    }
    if (fechaEl) fechaEl.addEventListener("change", loadResults);
    if (funcEl) funcEl.addEventListener("change", loadResults);
    if (estadoEl) estadoEl.addEventListener("change", loadResults);
    if (buscarEl) {
        var t = null;
        buscarEl.addEventListener("input", function () {
            clearTimeout(t);
            t = setTimeout(loadResults, 350);
        });
    }

    // ── Modal de cobro ─────────────────────────────────────────────────────
    var modalEl = document.getElementById("ccCobroModal");
    var modal = (modalEl && window.bootstrap) ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;

    function money(v) {
        return "₡" + Number(v).toLocaleString("es-CR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest(".js-cc-cobrar");
        if (!btn || !modal) return;
        document.getElementById("ccCobroCitaId").value = btn.dataset.citaId || "";
        document.getElementById("ccCobroCliente").textContent = btn.dataset.cliente || "Cliente";
        document.getElementById("ccCobroServicio").textContent = btn.dataset.servicio || "Servicio";
        var precio = btn.dataset.precio || "";
        document.getElementById("ccCobroMonto").value = precio;
        document.getElementById("ccCobroEsperado").textContent = (precio && Number(precio) > 0) ? money(precio) : "—";
        document.getElementById("ccCobroMetodo").value = "EFECTIVO";
        document.getElementById("ccCobroObs").value = "";

        // Comprobante: reset + prefill del correo del cliente si existe.
        var enviarChk = document.getElementById("ccCobroEnviar");
        var emailWrap = document.getElementById("ccCobroEmailWrap");
        var emailInp = document.getElementById("ccCobroEmail");
        var guardarWrap = document.getElementById("ccCobroGuardarWrap");
        var guardarChk = document.getElementById("ccCobroGuardarEmail");
        if (enviarChk) enviarChk.checked = false;
        if (emailWrap) emailWrap.style.display = "none";
        if (emailInp) emailInp.value = btn.dataset.email || "";
        if (guardarWrap) guardarWrap.style.display = "none";
        if (guardarChk) guardarChk.checked = false;
        emailWrap && (emailWrap.dataset.clienteId = btn.dataset.clienteId || "");

        var err = document.getElementById("ccCobroError");
        err.classList.add("d-none"); err.textContent = "";
        modal.show();
    });

    // Toggle del bloque de comprobante.
    var enviarChkGlobal = document.getElementById("ccCobroEnviar");
    if (enviarChkGlobal) {
        enviarChkGlobal.addEventListener("change", function () {
            var emailWrap = document.getElementById("ccCobroEmailWrap");
            var guardarWrap = document.getElementById("ccCobroGuardarWrap");
            if (emailWrap) emailWrap.style.display = this.checked ? "" : "none";
            // Solo ofrecemos "guardar en cliente" si la cita tiene un cliente registrado.
            var tieneCliente = emailWrap && emailWrap.dataset.clienteId;
            if (guardarWrap) guardarWrap.style.display = (this.checked && tieneCliente) ? "flex" : "none";
        });
    }

    var submitBtn = document.getElementById("ccCobroSubmit");
    if (submitBtn) {
        submitBtn.addEventListener("click", function () {
            var citaId = document.getElementById("ccCobroCitaId").value;
            var monto = document.getElementById("ccCobroMonto").value;
            var metodo = document.getElementById("ccCobroMetodo").value;
            var obs = document.getElementById("ccCobroObs").value;
            var err = document.getElementById("ccCobroError");

            var enviar = document.getElementById("ccCobroEnviar");
            var emailInp = document.getElementById("ccCobroEmail");
            var guardarChk = document.getElementById("ccCobroGuardarEmail");
            var enviarComprobante = enviar && enviar.checked;
            var email = emailInp ? emailInp.value.trim() : "";

            if (!monto || Number(monto) <= 0) {
                err.textContent = "Indica un monto mayor a cero.";
                err.classList.remove("d-none");
                return;
            }

            if (enviarComprobante && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
                err.textContent = "Indica un correo válido para enviar el comprobante.";
                err.classList.remove("d-none");
                return;
            }

            submitBtn.disabled = true;
            var body = new URLSearchParams();
            body.set("citaId", citaId);
            body.set("monto", monto);
            body.set("metodoPago", metodo);
            body.set("observacion", obs);
            body.set("enviarComprobante", enviarComprobante ? "true" : "false");
            body.set("emailComprobante", email);
            body.set("guardarEmailEnCliente", (guardarChk && guardarChk.checked) ? "true" : "false");

            fetch("/Calendar/CobrarCita", {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded",
                    "RequestVerificationToken": token(),
                    "X-Requested-With": "XMLHttpRequest"
                },
                credentials: "same-origin",
                body: body.toString()
            })
                .then(function (r) { return r.json().then(function (d) { return { ok: r.ok, d: d }; }); })
                .then(function (res) {
                    if (!res.ok) {
                        err.textContent = (res.d && res.d.error) || "No fue posible registrar el cobro.";
                        err.classList.remove("d-none");
                        return;
                    }
                    if (modal) modal.hide();
                    toast((res.d && res.d.message) || "Cobro registrado correctamente.", false);
                    loadResults();
                })
                .catch(function () {
                    err.textContent = "Error de conexión. Intenta de nuevo.";
                    err.classList.remove("d-none");
                })
                .finally(function () { submitBtn.disabled = false; });
        });
    }

    // ── Cancelar cita ──────────────────────────────────────────────────────
    document.addEventListener("click", function (e) {
        var btn = e.target.closest(".js-cc-cancelar");
        if (!btn) return;
        var citaId = btn.dataset.citaId;
        var cliente = btn.dataset.cliente || "esta cita";
        if (!citaId) return;
        if (!window.confirm("¿Cancelar la cita de " + cliente + "? Esta acción no se puede deshacer.")) return;

        btn.disabled = true;
        fetch("/Calendar/Delete/" + encodeURIComponent(citaId), {
            method: "DELETE",
            headers: {
                "RequestVerificationToken": token(),
                "X-Requested-With": "XMLHttpRequest"
            },
            credentials: "same-origin"
        })
            .then(function (r) {
                if (!r.ok) throw new Error();
                toast("Cita cancelada correctamente.", false);
                loadResults();
            })
            .catch(function () {
                btn.disabled = false;
                toast("No fue posible cancelar la cita.", true);
            });
    });

    // ── Toast simple ───────────────────────────────────────────────────────
    function toast(msg, isError) {
        var t = document.createElement("div");
        t.textContent = msg;
        t.style.cssText = "position:fixed;bottom:1.25rem;left:50%;transform:translateX(-50%);z-index:1080;" +
            "padding:.7rem 1.1rem;border-radius:.7rem;font-weight:700;font-size:.88rem;color:#fff;" +
            "box-shadow:0 10px 30px rgba(2,6,23,.3);max-width:90vw;text-align:center;" +
            "background:" + (isError ? "#dc2626" : "#059669") + ";";
        document.body.appendChild(t);
        setTimeout(function () { t.style.transition = "opacity .3s"; t.style.opacity = "0"; }, 2600);
        setTimeout(function () { t.remove(); }, 3000);
    }
})();
