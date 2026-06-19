/* ===========================================================================
   Control de citas y cobros - interaccion de la vista admin.
   - Filtros (Dia/Semana/Mes, fecha, funcionario, estado, busqueda) por AJAX.
   - Cobro rapido con modal -> POST /Calendar/CobrarCita (reusa CobroService).
   Actualiza KPIs y tabla sin recargar toda la pagina.
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

    var modalEl = document.getElementById("ccCobroModal");
    var modal = (modalEl && window.bootstrap) ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;

    function money(v) {
        return "\u20A1" + Number(v).toLocaleString("es-CR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest(".js-cc-cobrar");
        if (!btn || !modal) return;
        document.getElementById("ccCobroCitaId").value = btn.dataset.citaId || "";
        document.getElementById("ccCobroCliente").textContent = btn.dataset.cliente || "Cliente";
        document.getElementById("ccCobroServicio").textContent = btn.dataset.servicio || "Servicio";
        var precio = btn.dataset.precio || "";
        document.getElementById("ccCobroMonto").value = precio;
        document.getElementById("ccCobroEsperado").textContent = (precio && Number(precio) > 0) ? money(precio) : "-";
        document.getElementById("ccCobroMetodo").value = "EFECTIVO";
        document.getElementById("ccCobroObs").value = "";

        var enviarChk = document.getElementById("ccCobroEnviar");
        var emailWrap = document.getElementById("ccCobroEmailWrap");
        var emailInp = document.getElementById("ccCobroEmail");
        var guardarWrap = document.getElementById("ccCobroGuardarWrap");
        var guardarChk = document.getElementById("ccCobroGuardarEmail");
        if (enviarChk) enviarChk.checked = false;
        if (emailWrap) emailWrap.classList.add("d-none");
        if (emailInp) emailInp.value = btn.dataset.email || "";
        if (guardarWrap) guardarWrap.classList.add("d-none");
        if (guardarChk) guardarChk.checked = false;
        if (emailWrap) emailWrap.dataset.clienteId = btn.dataset.clienteId || "";

        var err = document.getElementById("ccCobroError");
        err.classList.add("d-none");
        err.textContent = "";
        modal.show();
    });

    var enviarChkGlobal = document.getElementById("ccCobroEnviar");
    if (enviarChkGlobal) {
        enviarChkGlobal.addEventListener("change", function () {
            var emailWrap = document.getElementById("ccCobroEmailWrap");
            var guardarWrap = document.getElementById("ccCobroGuardarWrap");
            if (emailWrap) emailWrap.classList.toggle("d-none", !this.checked);
            var tieneCliente = emailWrap && emailWrap.dataset.clienteId;
            if (guardarWrap) guardarWrap.classList.toggle("d-none", !(this.checked && tieneCliente));
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
                err.textContent = "Indica un correo v\u00E1lido para enviar el comprobante.";
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
                    err.textContent = "Error de conexi\u00F3n. Intenta de nuevo.";
                    err.classList.remove("d-none");
                })
                .finally(function () { submitBtn.disabled = false; });
        });
    }

    function toast(msg, isError) {
        var t = document.createElement("div");
        t.textContent = msg;
        t.className = "cc-toast " + (isError ? "cc-toast-error" : "cc-toast-success");
        document.body.appendChild(t);
        setTimeout(function () { t.classList.add("is-hiding"); }, 2600);
        setTimeout(function () { t.remove(); }, 3000);
    }
})();
