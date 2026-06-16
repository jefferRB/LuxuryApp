/* VARIABLES GLOBALES */

let currentDate = new Date();
let currentView = "month";
let fechasOcupadas = [];
let calendarConfig = {
    inicio: 6,
    fin: 22,
    intervalo: 15
};
const CALENDAR_DEBUG_ENABLED = window.CALENDAR_DEBUG_ENABLED === true;
const calendarCache = {
    servicios: null,
    serviciosPromise: null,
    funcionarios: null,
    funcionariosPromise: null
};
const calendarRequestState = {
    sequence: Object.create(null),
    controllers: Object.create(null)
};
const CLIENTE_AUTOCOMPLETE_MIN_LENGTH = 3;
const CLIENTE_AUTOCOMPLETE_DEBOUNCE_MS = 300;
const TENANT_WHATSAPP_ENABLED = window.LUXURY_CALENDAR_CONFIG?.tenantWhatsAppEnabled === true;
const BUSINESS_TODAY_ISO = window.LUXURY_CALENDAR_CONFIG?.businessToday || null;

/* ───────────────────────────────────────────────────────────
   MANEJO CENTRALIZADO DE MODALES (Bootstrap)
   Evita instancias duplicadas y capas negras huérfanas.
   ─────────────────────────────────────────────────────────── */

function getCalendarModalInstance(id) {
    const el = document.getElementById(id);
    if (!el || typeof bootstrap === "undefined" || !bootstrap.Modal) {
        return null;
    }
    // getOrCreateInstance reutiliza la instancia existente en lugar de crear
    // una nueva cada vez (causa raíz de backdrops duplicados / pantalla bloqueada).
    return bootstrap.Modal.getOrCreateInstance(el);
}

function showCalendarModal(id) {
    const instance = getCalendarModalInstance(id);
    instance?.show();
    return instance;
}

function hideCalendarModal(id) {
    const el = document.getElementById(id);
    if (!el || typeof bootstrap === "undefined" || !bootstrap.Modal) {
        return;
    }
    bootstrap.Modal.getInstance(el)?.hide();
}

// Red de seguridad: cuando se cierra cualquier modal y NO queda ninguno abierto,
// elimina backdrops huérfanos y restaura el scroll del body. Solo actúa cuando
// realmente no hay modales visibles, por lo que no rompe modales apilados.
function initCalendarModalSafetyNet() {
    document.addEventListener("hidden.bs.modal", () => {
        if (document.querySelector(".modal.show")) {
            return; // Aún hay un modal abierto (apilado): no tocar nada.
        }

        document.querySelectorAll(".modal-backdrop").forEach(backdrop => backdrop.remove());
        document.body.classList.remove("modal-open");
        document.body.style.removeProperty("overflow");
        document.body.style.removeProperty("padding-right");
    });
}

function getBusinessTodayDate() {
    if (BUSINESS_TODAY_ISO && /^\d{4}-\d{2}-\d{2}$/.test(BUSINESS_TODAY_ISO)) {
        const [y, m, d] = BUSINESS_TODAY_ISO.split("-").map(Number);
        return new Date(y, m - 1, d, 12, 0, 0);
    }
    return new Date();
}

function isBusinessToday(date) {
    const today = getBusinessTodayDate();
    return date.getFullYear() === today.getFullYear() &&
        date.getMonth() === today.getMonth() &&
        date.getDate() === today.getDate();
}

function calendarDebugLog(message, payload = null) {

    if (!CALENDAR_DEBUG_ENABLED) return;

    if (payload === null) {
        console.debug(`[CalendarDebug] ${message}`);
        return;
    }

    console.debug(`[CalendarDebug] ${message}`, payload);
}

function isTenantWhatsAppEnabled() {
    return TENANT_WHATSAPP_ENABLED;
}

function describeElement(element) {

    if (!element) return null;

    return {
        tagName: element.tagName,
        id: element.id || null,
        className: element.className || null,
        dataset: { ...element.dataset }
    };
}

function normalizeFuncionarioId(value) {

    if (value === null || value === undefined || value === "")
        return null;

    const parsed = Number.parseInt(value, 10);

    return Number.isInteger(parsed) && parsed > 0
        ? String(parsed)
        : null;
}

function getAntiForgeryToken() {
    return document.querySelector("#calendar-antiforgery input[name='__RequestVerificationToken']")
        ?.value
        || document.querySelector("input[name='__RequestVerificationToken']")
            ?.value
        || "";
}

function parsePositiveInt(value) {
    if (value === null || value === undefined) {
        return null;
    }

    const normalized = String(value).trim();
    if (normalized === "") {
        return null;
    }

    const parsed = Number.parseInt(normalized, 10);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}

function normalizeDateTimeLocalValue(value) {
    const parsed = parseLocalDateTime(value);
    return parsed ? formatLocalDateTime(parsed) : null;
}

function splitSelectedDates(value) {
    if (!value) {
        return [];
    }

    return value
        .split(",")
        .map(item => item.trim())
        .filter(item => item !== "");
}

function beginRequest(key) {
    const nextSequence = (calendarRequestState.sequence[key] || 0) + 1;
    calendarRequestState.sequence[key] = nextSequence;

    if (calendarRequestState.controllers[key]) {
        calendarRequestState.controllers[key].abort();
    }

    const controller = typeof AbortController !== "undefined"
        ? new AbortController()
        : null;

    calendarRequestState.controllers[key] = controller;

    return {
        requestId: nextSequence,
        signal: controller?.signal
    };
}

function cancelRequest(key) {
    calendarRequestState.sequence[key] = (calendarRequestState.sequence[key] || 0) + 1;

    if (calendarRequestState.controllers[key]) {
        calendarRequestState.controllers[key].abort();
        delete calendarRequestState.controllers[key];
    }
}

function isLatestRequest(key, requestId) {
    return calendarRequestState.sequence[key] === requestId;
}

async function readResponsePayload(response) {
    if (response.status === 204) {
        return null;
    }

    const contentType = response.headers.get("content-type") || "";

    if (contentType.includes("application/json")) {
        return await response.json();
    }

    return await response.text();
}

function extractErrorMessage(payload) {
    if (!payload) {
        return "No fue posible completar la operación.";
    }

    if (typeof payload === "string") {
        const trimmed = payload.trim();

        if (trimmed === "") {
            return "No fue posible completar la operación.";
        }

        if (trimmed.includes("RequestVerificationToken") ||
            trimmed.includes("antiforgery") ||
            trimmed.includes("AntiForgery")) {
            return "La solicitud fue rechazada por seguridad anti-forgery. Recarga la agenda e intenta de nuevo.";
        }

        if (trimmed.startsWith("<!DOCTYPE") || trimmed.startsWith("<html")) {
            return "El servidor rechazo la solicitud con HTTP 400. Revisa la consola para el detalle tecnico.";
        }

        return trimmed;
    }

    if (typeof payload.message === "string" && payload.message.trim() !== "") {
        return payload.message;
    }

    if (typeof payload.error === "string" && payload.error.trim() !== "") {
        return payload.error;
    }

    return "No fue posible completar la operación.";
}

async function apiFetch(url, options = {}) {
    const finalOptions = { ...options };
    const headers = new Headers(finalOptions.headers || {});
    const method = (finalOptions.method || "GET").toUpperCase();

    if (!headers.has("X-Requested-With")) {
        headers.set("X-Requested-With", "XMLHttpRequest");
    }

    if (["POST", "PUT", "PATCH", "DELETE"].includes(method)) {
        const antiForgeryToken = getAntiForgeryToken();
        if (!antiForgeryToken) {
            throw new Error("No se encontro el token anti-forgery de la agenda. Recarga la pagina e intenta de nuevo.");
        }

        if (!headers.has("RequestVerificationToken")) {
            headers.set("RequestVerificationToken", antiForgeryToken);
        }
    }

    if (!finalOptions.credentials) {
        finalOptions.credentials = "same-origin";
    }

    finalOptions.headers = headers;

    return fetch(url, finalOptions);
}

async function apiFetchJson(url, options = {}) {
    const response = await apiFetch(url, options);
    const payload = await readResponsePayload(response);

    if (!response.ok) {
        console.error("Calendar API error", {
            url,
            method: (options.method || "GET").toUpperCase(),
            status: response.status,
            payload
        });

        const error = new Error(extractErrorMessage(payload));
        error.status = response.status;
        error.payload = payload;
        throw error;
    }

    return payload;
}

function safeText(value, fallback = "") {
    if (value === null || value === undefined) {
        return fallback;
    }

    const normalized = String(value);
    return normalized.trim() === "" ? fallback : normalized;
}

function createTextNode(value, fallback = "") {
    return document.createTextNode(safeText(value, fallback));
}

function clearElement(element) {
    if (element) {
        element.replaceChildren();
    }
}

function appendLabeledText(container, label, value, addBreak = true) {
    const strong = document.createElement("strong");
    strong.textContent = label;
    container.appendChild(strong);
    container.appendChild(createTextNode(` ${safeText(value, "—")}`));

    if (addBreak) {
        container.appendChild(document.createElement("br"));
    }
}

function formatWhatsAppState(value) {
    const normalized = safeText(value, "Pendiente").toLowerCase();

    if (normalized === "confirmada") return "Confirmada";
    if (normalized === "cancelada") return "Cancelada";
    if (normalized === "errorenvio") return "Error de envío";
    if (normalized === "noenviada") return "No enviada";

    return "Pendiente";
}

function formatSentState(value) {
    return value ? "Sí" : "No";
}

function getConsentUIConfig(mode) {
    const isEdit = mode === "edit";

    return {
        mode,
        nombreInput: document.getElementById(isEdit ? "editNombreCliente" : "nombreCliente"),
        telefonoInput: document.getElementById(isEdit ? "editTelefonoCliente" : "telefonoCliente"),
        clienteIdInput: document.getElementById(isEdit ? "editNombreClienteClienteId" : "nombreClienteClienteId"),
        consentInfo: document.getElementById(isEdit ? "editClienteConsentInfo" : "clienteConsentInfo"),
        manualContainer: document.getElementById(isEdit ? "editManualConsentContainer" : "manualConsentContainer"),
        manualCheckbox: document.getElementById(isEdit ? "editWhatsAppConsentAtCreation" : "whatsAppConsentAtCreation"),
        capturedAtInput: document.getElementById(isEdit ? "editWhatsAppConsentCapturedAtUtc" : "whatsAppConsentCapturedAtUtc")
    };
}

function clearSelectedClienteState(nombreInput, clienteIdInput) {
    if (!nombreInput) {
        return;
    }

    nombreInput.dataset.selectedClienteId = "";
    nombreInput.dataset.selectedClienteName = "";
    nombreInput.dataset.selectedClientePhone = "";
    nombreInput.dataset.selectedClienteWhatsAppOptIn = "";

    if (clienteIdInput) {
        clienteIdInput.value = "";
    }
}

function resetManualConsentState(config) {
    if (!config?.manualCheckbox) {
        return;
    }

    config.manualCheckbox.checked = false;

    if (config.capturedAtInput) {
        config.capturedAtInput.value = "";
    }
}

function transitionSelectedClienteToManual(config, options = {}) {
    if (!config?.nombreInput || !hasSelectedCliente(config)) {
        return false;
    }

    const selectedPhone = safeText(config.nombreInput.dataset.selectedClientePhone);

    clearSelectedClienteState(config.nombreInput, config.clienteIdInput);
    resetManualConsentState(config);

    if (options.clearPhone && config.telefonoInput) {
        const currentPhone = safeText(config.telefonoInput.value);
        if (currentPhone === selectedPhone) {
            config.telefonoInput.value = "";
        }
    }

    return true;
}

function applySelectedClienteState(nombreInput, telefonoInput, clienteIdInput, cliente) {
    if (!nombreInput) {
        return;
    }

    const clienteId = cliente?.id === null || cliente?.id === undefined
        ? ""
        : String(cliente.id);
    const clienteNombre = safeText(cliente?.nombre);
    const clienteTelefono = safeText(cliente?.telefono);
    const aceptaMensajesWhatsApp = cliente?.aceptaMensajesWhatsApp === true;

    nombreInput.value = clienteNombre;
    nombreInput.dataset.selectedClienteId = clienteId;
    nombreInput.dataset.selectedClienteName = clienteNombre;
    nombreInput.dataset.selectedClientePhone = clienteTelefono;
    nombreInput.dataset.selectedClienteWhatsAppOptIn = aceptaMensajesWhatsApp ? "true" : "false";

    if (telefonoInput) {
        telefonoInput.value = clienteTelefono;
    }

    if (clienteIdInput) {
        clienteIdInput.value = clienteId;
    }
}

function hasSelectedCliente(config) {
    return Boolean(config?.clienteIdInput?.value);
}

function ensureConsentCapturedAt(checkbox, capturedAtInput) {
    if (!checkbox || !capturedAtInput) {
        return null;
    }

    if (!checkbox.checked) {
        capturedAtInput.value = "";
        return null;
    }

    if (!capturedAtInput.value) {
        capturedAtInput.value = new Date().toISOString();
    }

    return capturedAtInput.value;
}

function updateConsentInfoPanel(panel, text, tone) {
    if (!panel) {
        return;
    }

    clearElement(panel);

    if (!text) {
        panel.classList.add("d-none");
        panel.classList.remove("text-success", "text-warning", "text-muted");
        return;
    }

    panel.textContent = text;
    panel.classList.remove("d-none", "text-success", "text-warning", "text-muted");
    panel.classList.add(tone || "text-muted");
}

function isBreakMode(config) {
    if (!config) {
        return false;
    }

    if (config.mode === "edit") {
        return safeText(document.getElementById("editTipo")?.value).toUpperCase() === "DESCANSO";
    }

    return document.getElementById("esDescanso")?.checked === true;
}

function hasManualAppointmentData(config) {
    if (!config) {
        return false;
    }

    return safeText(config.nombreInput?.value) !== "" ||
        safeText(config.telefonoInput?.value) !== "";
}

function renderWhatsAppConsentUI(mode) {
    const config = getConsentUIConfig(mode);

    if (!config.nombreInput || !config.telefonoInput || !config.manualContainer || !config.manualCheckbox) {
        return;
    }

    if (!isTenantWhatsAppEnabled()) {
        resetManualConsentState(config);
        config.manualContainer.classList.add("d-none");
        updateConsentInfoPanel(config.consentInfo, "", "");
        return;
    }

    const hasClienteId = hasSelectedCliente(config);
    const isBreak = isBreakMode(config);
    const hasManualData = hasManualAppointmentData(config);

    if (isBreak) {
        resetManualConsentState(config);
        config.manualContainer.classList.add("d-none");
        updateConsentInfoPanel(config.consentInfo, "", "");
        return;
    }

    if (hasClienteId) {
        const consentGranted = config.nombreInput.dataset.selectedClienteWhatsAppOptIn === "true";

        resetManualConsentState(config);
        config.manualContainer.classList.add("d-none");
        updateConsentInfoPanel(
            config.consentInfo,
            consentGranted
                ? "WhatsApp autorizado para este cliente."
                : "WhatsApp no autorizado para este cliente. No se enviarán confirmaciones ni recordatorios.",
            consentGranted ? "text-success" : "text-warning");
        return;
    }

    if (!hasManualData) {
        resetManualConsentState(config);
        config.manualContainer.classList.add("d-none");
        updateConsentInfoPanel(config.consentInfo, "", "");
        return;
    }

    updateConsentInfoPanel(config.consentInfo, "", "");
    config.manualContainer.classList.remove("d-none");

    if (config.manualCheckbox.checked) {
        ensureConsentCapturedAt(config.manualCheckbox, config.capturedAtInput);
    } else if (config.capturedAtInput) {
        config.capturedAtInput.value = "";
    }
}

function syncCreateConsentUI() {
    renderWhatsAppConsentUI("create");
}

function syncEditConsentUI() {
    renderWhatsAppConsentUI("edit");
}

function invalidateSelectedClienteIfNeeded(mode) {
    const config = getConsentUIConfig(mode);

    if (!config.nombreInput || !config.telefonoInput || !hasSelectedCliente(config)) {
        return;
    }

    const currentName = safeText(config.nombreInput.value);
    const currentPhone = safeText(config.telefonoInput.value);
    const selectedName = safeText(config.nombreInput.dataset.selectedClienteName);
    const selectedPhone = safeText(config.nombreInput.dataset.selectedClientePhone);

    if (currentName === selectedName && currentPhone === selectedPhone) {
        return;
    }

    transitionSelectedClienteToManual(config, {
        clearPhone: currentName === ""
    });

    if (mode === "edit") {
        syncEditConsentUI();
        return;
    }

    syncCreateConsentUI();
}

function updateWhatsAppStatusPanel(cita) {
    const panel = document.getElementById("editWhatsAppStatus");
    if (!panel) return;

    clearElement(panel);

    if (!isTenantWhatsAppEnabled() || !cita || cita.tipo === "DESCANSO") {
        panel.classList.add("d-none");
        return;
    }

    appendLabeledText(panel, "WhatsApp:", cita.whatsAppStatusDisplay || formatWhatsAppState(cita.estadoConfirmacionWhatsApp));

    if (cita.whatsAppConsentDisplay) {
        appendLabeledText(panel, "Consentimiento:", cita.whatsAppConsentDisplay);
    }

    appendLabeledText(panel, "Estado de respuesta:", formatWhatsAppState(cita.estadoConfirmacionWhatsApp));
    appendLabeledText(panel, "Confirmación enviada:", formatSentState(cita.confirmacionWhatsAppEnviadaUtc));
    appendLabeledText(panel, "Recordatorio 3h enviado:", formatSentState(cita.recordatorioWhatsAppTresHorasEnviadoUtc), false);
    panel.classList.remove("d-none");
}

function formatLocalDate(date) {

    const pad = value => String(value).padStart(2, "0");

    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function formatDateOnly(date) {
    return formatLocalDate(date);
}

function getFuncionarioColumn(element) {
    return element?.closest?.(".funcionario-column[data-funcionario-id]") ?? null;
}

function getCalendarSlot(element) {
    return element?.closest?.(".calendar-slot[data-funcionario-id]") ?? null;
}

function buildSlotContext(slot) {

    if (!slot) return null;

    return {
        date: slot.dataset.date || null,
        slotIndex: Number.parseInt(slot.dataset.slotIndex || "0", 10),
        hour: Number.parseInt(slot.dataset.hour || "0", 10),
        minute: Number.parseInt(slot.dataset.minute || "0", 10),
        funcionarioId: normalizeFuncionarioId(slot.dataset.funcionarioId),
        funcionarioNombre: slot.dataset.funcionarioNombre || "",
        funcionarioColor: slot.dataset.funcionarioColor || "",
        clickedElement: describeElement(slot)
    };
}

function getSelectionState(container) {

    if (!container._calendarSelectionState) {
        container._calendarSelectionState = {
            active: false,
            pointerId: null,
            startSlot: null,
            currentSlot: null,
            column: null,
            previewBlock: null
        };
    }

    return container._calendarSelectionState;
}

function removeSelectionPreview(state) {

    if (state.previewBlock?.parentElement) {
        state.previewBlock.parentElement.removeChild(state.previewBlock);
    }

    state.previewBlock = null;
}

function resetSlotSelection(container) {

    const state = getSelectionState(container);

    removeSelectionPreview(state);

    state.active = false;
    state.pointerId = null;
    state.startSlot = null;
    state.currentSlot = null;
    state.column = null;
}

function updateSelectionPreview(state, altoSlot) {

    if (!state.previewBlock || !state.startSlot)
        return;

    const startIndex = Number.parseInt(state.startSlot.dataset.slotIndex || "0", 10);
    const endIndex = Math.max(
        startIndex,
        Number.parseInt((state.currentSlot || state.startSlot).dataset.slotIndex || `${startIndex}`, 10)
    );

    state.previewBlock.style.top = `${startIndex * altoSlot}px`;
    state.previewBlock.style.height = `${(endIndex - startIndex + 1) * altoSlot}px`;
}

function resolveSlotFromPoint(clientX, clientY) {
    return getCalendarSlot(document.elementFromPoint(clientX, clientY));
}

function buildAppointmentBlock(cita, top, altura) {
    const bloque = document.createElement("div");
    bloque.className = "cita-bloque";
    bloque.dataset.id = String(cita.id);
    bloque.style.top = `${top}px`;
    bloque.style.height = `${altura}px`;
    bloque.style.backgroundColor = cita.tipo === "DESCANSO"
        ? "#6c757d"
        : (cita.colorCalendario || "#004445");

    const title = document.createElement("div");
    title.style.fontWeight = "600";

    const detail = document.createElement("div");
    detail.style.fontSize = "11px";

    if (cita.tipo === "DESCANSO") {
        title.textContent = "☕ DESCANSO";
        detail.textContent = `${cita.duracionMinutos || 30} min`;
    } else {
        title.textContent = safeText(cita.nombreCliente, "Sin cliente");
        detail.style.opacity = "0.9";
        detail.textContent = safeText(cita.servicioNombre, "Sin servicio");
    }

    bloque.appendChild(title);
    bloque.appendChild(detail);

    const resizeHandle = document.createElement("div");
    resizeHandle.className = "cita-resize-handle";
    bloque.appendChild(resizeHandle);

    return bloque;
}

function buildUpcomingAppointmentItem(cita) {
    const li = document.createElement("li");
    li.className = "side-cita-card";

    const inicioCita = parseLocalDateTime(cita.fechaHoraCita);

    appendLabeledText(li, "Cliente:", cita.nombreCliente);

    const small = document.createElement("small");
    appendLabeledText(small, "Teléfono:", cita.telefonoCliente);
    appendLabeledText(small, "Servicio:", cita.servicioNombre);
    appendLabeledText(
        small,
        "Fecha:",
        inicioCita
            ? `${inicioCita.toLocaleDateString("es-CR")} ${inicioCita.toLocaleTimeString("es-CR", { hour: "2-digit", minute: "2-digit" })}`
            : "—");
    appendLabeledText(small, "Funcionario:", cita.funcionarioNombre);

    if (isTenantWhatsAppEnabled()) {
        appendLabeledText(
            small,
            "WhatsApp:",
            cita.whatsAppStatusDisplay || formatWhatsAppState(cita.estadoConfirmacionWhatsApp),
            false);
    }

    li.appendChild(small);

    const actions = document.createElement("div");
    actions.className = "mt-2 d-flex gap-2";

    const editButton = document.createElement("button");
    editButton.className = "btn btn-sm btn-outline-primary edit-btn";
    editButton.textContent = "✏️ Editar";
    editButton.addEventListener("click", () => editarCita(cita.id));

    const deleteButton = document.createElement("button");
    deleteButton.className = "btn btn-sm btn-outline-danger delete-btn";
    deleteButton.textContent = "❌ Cancelar";
    deleteButton.addEventListener("click", () => cancelarCita(cita.id));

    actions.appendChild(editButton);
    actions.appendChild(deleteButton);
    li.appendChild(actions);

    return li;
}


/* INICIALIZACIÓN */



document.addEventListener("DOMContentLoaded", initApp);

function initApp() {

    initCalendarModalSafetyNet();
    initCalendar();
    initAutocomplete();
    initUIState();
    initEvents();

    renderCalendar(currentDate);
    loadUpcomingAppointments();
    loadFuncionariosFiltro();
    getFuncionariosActivos().catch(() => { });
    getServiciosActivos().catch(() => { });

}

async function getFuncionariosActivos(forceRefresh = false) {
    if (!forceRefresh && Array.isArray(calendarCache.funcionarios)) {
        return calendarCache.funcionarios;
    }

    if (!forceRefresh && calendarCache.funcionariosPromise) {
        return calendarCache.funcionariosPromise;
    }

    const promise = apiFetchJson("/Funcionarios/GetActivos")
        .then(funcionarios => {
            calendarCache.funcionarios = Array.isArray(funcionarios) ? funcionarios : [];
            return calendarCache.funcionarios;
        })
        .finally(() => {
            calendarCache.funcionariosPromise = null;
        });

    calendarCache.funcionariosPromise = promise;
    return promise;
}

async function getServiciosActivos(forceRefresh = false) {
    if (!forceRefresh && Array.isArray(calendarCache.servicios)) {
        return calendarCache.servicios;
    }

    if (!forceRefresh && calendarCache.serviciosPromise) {
        return calendarCache.serviciosPromise;
    }

    const promise = apiFetchJson("/Calendar/GetServiciosActivos")
        .then(servicios => {
            calendarCache.servicios = Array.isArray(servicios) ? servicios : [];
            return calendarCache.servicios;
        })
        .finally(() => {
            calendarCache.serviciosPromise = null;
        });

    calendarCache.serviciosPromise = promise;
    return promise;
}

/* CALENDARIO DUPLICAR CITAS */

function initCalendar() {

    flatpickr("#fechasDuplicadas", {

        mode: "multiple",
        dateFormat: "Y-m-d",

        onOpen: async function (selectedDates, dateStr, instance) {

            await cargarFechasOcupadas(instance);
            instance.redraw();

        },

        onDayCreate: function (dObj, dStr, fp, dayElem) {

            const fecha =
                dayElem.dateObj.getFullYear() + "-" +
                String(dayElem.dateObj.getMonth() + 1).padStart(2, "0") + "-" +
                String(dayElem.dateObj.getDate()).padStart(2, "0");

            const fechaHoraSeleccionada =
                document.getElementById("appointmentDate").value;

            if (!fechaHoraSeleccionada) return;

            const funcionarioId =
                parseInt(document.getElementById("funcionarioId").value);

            if (!funcionarioId) return;

            const servicioSelect =
                document.getElementById("servicio");

            if (!servicioSelect.value) return;

            const duracionServicio =
                parseInt(
                    servicioSelect.selectedOptions[0]?.dataset.duracion || 30
                );

            const partes = fechaHoraSeleccionada.split("T");

            if (partes.length < 2) return;

            const hora = partes[1].slice(0, 5);

            const [year, month, day] = fecha.split("-").map(Number);
            const [hour, minute] = hora.split(":").map(Number);

            const inicioNueva =
                new Date(year, month - 1, day, hour, minute);

            const finNueva =
                new Date(inicioNueva.getTime() + duracionServicio * 60000);

            const ocupado = fechasOcupadas.some(f => {

                if (f.fecha !== fecha || f.funcionarioId != funcionarioId)
                    return false;

                const [y, m, d] = f.fecha.split("-").map(Number);
                const [h, min] = f.hora.split(":").map(Number);

                const inicioExistente =
                    new Date(y, m - 1, d, h, min);

                const finExistente =
                    new Date(
                        inicioExistente.getTime() +
                        (f.duracion || 30) * 60000
                    );

                return (
                    inicioNueva < finExistente &&
                    finNueva > inicioExistente
                );

            });

            if (ocupado) {

                dayElem.classList.add("dia-ocupado");

                dayElem.style.backgroundColor = "#ffb3b3";
                dayElem.style.borderRadius = "8px";
                dayElem.style.pointerEvents = "none";
                dayElem.style.opacity = "0.5";

                dayElem.title =
                    "Ya existe una cita en este horario";

            }

        }

    });

}

/*  AUTOCOMPLETADO CLIENTES */

function initAutocomplete() {

    initClienteAutocomplete({
        inputId: "nombreCliente",
        telefonoInputId: "telefonoCliente",
        dropdownId: "sugerenciasClientes",
        modalId: "createCitaModal",
        requestKey: "clientesAutocompleteCreate"
    });

    initClienteAutocomplete({
        inputId: "editNombreCliente",
        telefonoInputId: "editTelefonoCliente",
        dropdownId: "editSugerenciasClientes",
        modalId: "editCitaModal",
        requestKey: "clientesAutocompleteEdit"
    });
}

function initClienteAutocomplete(config) {

    const nombreInput = document.getElementById(config.inputId);
    const telefonoInput = document.getElementById(config.telefonoInputId);
    const dropdown = document.getElementById(config.dropdownId);
    const modalElement = document.getElementById(config.modalId);
    const wrapper = nombreInput?.closest(".cliente-autocomplete-wrapper");
    const clienteIdInput = resolveClienteIdInput(nombreInput, wrapper);

    if (!nombreInput || !dropdown || !wrapper) return;

    let debounceTimer = null;

    const closeDropdown = () => {
        hideClienteAutocomplete(dropdown, nombreInput);
    };

    const mode = config.modalId === "editCitaModal" ? "edit" : "create";

    const syncConsent = () => {
        if (mode === "edit") {
            syncEditConsentUI();
            return;
        }

        syncCreateConsentUI();
    };

    const clearSelectedCliente = options => {
        const consentConfig = getConsentUIConfig(mode);
        transitionSelectedClienteToManual(consentConfig, options);
        syncConsent();
    };

    const scheduleSearch = () => {
        const term = nombreInput.value.trim();
        clearSelectedCliente({ clearPhone: term === "" });

        window.clearTimeout(debounceTimer);

        if (!isAutocompleteModalOpen(modalElement) ||
            term.length < CLIENTE_AUTOCOMPLETE_MIN_LENGTH) {
            cancelRequest(config.requestKey);
            closeDropdown();
            return;
        }

        debounceTimer = window.setTimeout(
            () => searchClientes(config, nombreInput, telefonoInput, dropdown, clienteIdInput, term),
            CLIENTE_AUTOCOMPLETE_DEBOUNCE_MS);
    };

    nombreInput.addEventListener("input", scheduleSearch);
    telefonoInput?.addEventListener("input", () => {
        invalidateSelectedClienteIfNeeded(mode);
        syncConsent();
    });

    const manualCheckbox = document.getElementById(
        config.modalId === "editCitaModal"
            ? "editWhatsAppConsentAtCreation"
            : "whatsAppConsentAtCreation");
    const capturedAtInput = document.getElementById(
        config.modalId === "editCitaModal"
            ? "editWhatsAppConsentCapturedAtUtc"
            : "whatsAppConsentCapturedAtUtc");

    manualCheckbox?.addEventListener("change", () => {
        ensureConsentCapturedAt(manualCheckbox, capturedAtInput);
    });

    nombreInput.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            closeDropdown();
        }
    });

    dropdown.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            closeDropdown();
            nombreInput.focus();
        }
    });

    document.addEventListener("mousedown", event => {
        if (!wrapper.contains(event.target)) {
            closeDropdown();
        }
    });

    modalElement?.addEventListener("hidden.bs.modal", () => {
        window.clearTimeout(debounceTimer);
        cancelRequest(config.requestKey);
        closeDropdown();
    });

    syncConsent();
}

async function searchClientes(config, nombreInput, telefonoInput, dropdown, clienteIdInput, term) {

    if (nombreInput.value.trim() !== term) {
        return;
    }

    const request = beginRequest(config.requestKey);

    try {
        const clientes = await apiFetchJson(
            `/Clientes/Autocompletado?term=${encodeURIComponent(term)}`,
            { signal: request.signal });

        if (!isLatestRequest(config.requestKey, request.requestId) ||
            nombreInput.value.trim() !== term) {
            return;
        }

        renderClienteAutocompleteResults(
            Array.isArray(clientes) ? clientes : [],
            nombreInput,
            telefonoInput,
            dropdown,
            clienteIdInput);
    } catch (error) {
        if (error.name === "AbortError") {
            return;
        }

        hideClienteAutocomplete(dropdown, nombreInput);
        console.error("Error cargando autocompletado de clientes", error);
    }
}

function renderClienteAutocompleteResults(clientes, nombreInput, telefonoInput, dropdown, clienteIdInput) {

    clearElement(dropdown);

    if (clientes.length === 0) {
        const empty = document.createElement("div");
        empty.className = "cliente-autocomplete-empty";
        empty.textContent = "Sin coincidencias";
        dropdown.appendChild(empty);
    }

    clientes.forEach(cliente => {
        dropdown.appendChild(buildClienteAutocompleteItem(
            cliente,
            nombreInput,
            telefonoInput,
            dropdown,
            clienteIdInput));
    });

    dropdown.appendChild(buildClienteAutocompleteManualAction(dropdown, nombreInput, clienteIdInput));
    showClienteAutocomplete(dropdown, nombreInput);
}

function buildClienteAutocompleteItem(cliente, nombreInput, telefonoInput, dropdown, clienteIdInput) {

    const item = document.createElement("button");
    item.type = "button";
    item.className = "cliente-autocomplete-item";
    item.setAttribute("role", "option");

    const name = document.createElement("span");
    name.className = "cliente-autocomplete-name";
    name.textContent = safeText(cliente.nombre, "Cliente");

    const phone = document.createElement("span");
    phone.className = "cliente-autocomplete-phone";
    phone.textContent = safeText(cliente.telefono, "Sin telefono");

    item.appendChild(name);
    item.appendChild(phone);

    item.addEventListener("mousedown", event => {
        event.preventDefault();
    });

    item.addEventListener("click", () => {
        resetManualConsentState(getConsentUIConfig(nombreInput.id === "editNombreCliente" ? "edit" : "create"));
        applySelectedClienteState(nombreInput, telefonoInput, clienteIdInput, cliente);
        if (nombreInput.id === "editNombreCliente") {
            syncEditConsentUI();
        } else {
            syncCreateConsentUI();
        }

        hideClienteAutocomplete(dropdown, nombreInput);
    });

    return item;
}

function buildClienteAutocompleteManualAction(dropdown, nombreInput, clienteIdInput) {

    const manual = document.createElement("button");
    manual.type = "button";
    manual.className = "cliente-autocomplete-manual";
    manual.textContent = "Escribir manualmente / ocultar sugerencias";

    manual.addEventListener("mousedown", event => {
        event.preventDefault();
    });

    manual.addEventListener("click", () => {
        const consentConfig = getConsentUIConfig(nombreInput.id === "editNombreCliente" ? "edit" : "create");
        transitionSelectedClienteToManual(consentConfig);
        if (nombreInput.id === "editNombreCliente") {
            syncEditConsentUI();
        } else {
            syncCreateConsentUI();
        }

        hideClienteAutocomplete(dropdown, nombreInput);
        nombreInput.focus();
    });

    return manual;
}

function resolveClienteIdInput(nombreInput, wrapper) {

    if (!nombreInput) {
        return null;
    }

    return document.getElementById(`${nombreInput.id}ClienteId`) ||
        wrapper?.querySelector("input[type='hidden'][name$='ClienteId']") ||
        null;
}

function isAutocompleteModalOpen(modalElement) {
    return !modalElement || modalElement.classList.contains("show");
}

function showClienteAutocomplete(dropdown, input) {
    dropdown.classList.remove("d-none");
    input?.setAttribute("aria-expanded", "true");
}

function hideClienteAutocomplete(dropdown, input) {
    clearElement(dropdown);
    dropdown?.classList.add("d-none");
    input?.setAttribute("aria-expanded", "false");
}

/* ESTADO DE LA UI */

function initUIState() {

    const oculto = localStorage.getItem("tareasOcultas") === "true";
    const container = document.getElementById("tareasContainer");
    const btn = document.getElementById("toggleTareasBtn");

    if (oculto && container) {
        container.style.display = "none";
        btn.textContent = "Mostrar";
    }

}

/* EVENTOS */

function initEvents() {

    document.getElementById("servicio")
        ?.addEventListener("change", redrawDuplicarCalendar);

    document.getElementById("funcionarioId")
        ?.addEventListener("change", redrawDuplicarCalendar);

    document.getElementById("funcionarioFiltro")
        ?.addEventListener("change", e =>
            loadUpcomingAppointments(e.target.value)
        );

    document.getElementById("dayPicker")
        ?.addEventListener("change", changeDay);

    document
        .getElementById("aplicarHorario")
        ?.addEventListener("click", actualizarHorario);

    // WhatsApp consent UI - modo cita manual
    document.getElementById("nombreCliente")?.addEventListener("input", () => {
        invalidateSelectedClienteIfNeeded("create");
        renderWhatsAppConsentUI("create");
    });

    document.getElementById("telefonoCliente")?.addEventListener("input", () => {
        invalidateSelectedClienteIfNeeded("create");
        renderWhatsAppConsentUI("create");
    });

    document.getElementById("esDescanso").addEventListener("change", function () {

        const esDescanso = this.checked;

        const camposCita = document.getElementById("camposCita");
        const duracionDescanso = document.getElementById("duracionDescansoContainer");

        if (esDescanso) {

            camposCita.classList.add("d-none");
            duracionDescanso.classList.remove("d-none");

        } else {

            camposCita.classList.remove("d-none");
            duracionDescanso.classList.add("d-none");

        }

        syncCreateConsentUI();

    });

    document.getElementById("createCitaModal")
        ?.addEventListener("hidden.bs.modal", limpiarModalCita);

    /* CARGAR CONFIGURACIÓN GUARDADA */

    const savedConfig = localStorage.getItem("calendarConfig");

    if (savedConfig) {
        calendarConfig = JSON.parse(savedConfig);
    }

    /* REFLEJAR CONFIG EN INPUTS */

    const horaInicio = document.getElementById("horaInicio");
    const horaFin = document.getElementById("horaFin");
    const intervalo = document.getElementById("intervaloCitas");

    if (horaInicio) horaInicio.value = calendarConfig.inicio;
    if (horaFin) horaFin.value = calendarConfig.fin;
    if (intervalo) intervalo.value = calendarConfig.intervalo;

    initViewButtons();
    initDuplicarToggle();
    initServicioToggles();
    initDayModalNavigation();

}

/* NAVEGACIÓN DE DÍA DENTRO DEL MODAL (listeners enlazados una sola vez) */

function initDayModalNavigation() {
    document.getElementById("dayModalPrev")
        ?.addEventListener("click", () => navigateDayModal(-1));

    document.getElementById("dayModalNext")
        ?.addEventListener("click", () => navigateDayModal(1));

    document.getElementById("dayModalToday")
        ?.addEventListener("click", () => navigateDayModal("today"));
}

function redrawDuplicarCalendar() {

    const fp = document.querySelector("#fechasDuplicadas")?._flatpickr;
    if (fp) fp.redraw();

}

function changeDay(e) {

    const [y, m, d] = e.target.value.split("-").map(Number);
    currentDate = new Date(y, m - 1, d, 12, 0, 0);

    renderDayView(currentDate);

}

/* BOTONES DE VISTA */

function initViewButtons() {

    document.getElementById("viewMonthBtn").onclick = () => {

        currentView = "month";

        document.getElementById("viewMonthBtn").classList.add("active");
        document.getElementById("viewDayBtn").classList.remove("active");
        document.getElementById("dayPicker").classList.add("d-none");

        renderCalendar(currentDate);

    };

    document.getElementById("viewDayBtn").onclick = () => {

        currentView = "day";

        document.getElementById("viewDayBtn").classList.add("active");
        document.getElementById("viewMonthBtn").classList.remove("active");
        document.getElementById("dayPicker").classList.remove("d-none");

        currentDate = new Date();

        const picker = document.getElementById("dayPicker");

        picker.value =
            `${currentDate.getFullYear()}-` +
            `${String(currentDate.getMonth() + 1).padStart(2, "0")}-` +
            `${String(currentDate.getDate()).padStart(2, "0")}`;

        renderDayView(currentDate);

    };

}

/* DUPLICAR CITAS */

function initDuplicarToggle() {

    const duplicarCheckbox = document.getElementById("duplicarCita");

    if (!duplicarCheckbox) return;

    duplicarCheckbox.addEventListener("change", function () {

        const config = document.getElementById("duplicarConfig");

        if (this.checked)
            config.classList.remove("d-none");
        else
            config.classList.add("d-none");

    });

}

/* SERVICIO: CATÁLOGO vs PERSONALIZADO (segmented control) */

function getServicioToggleEls(mode) {
    if (mode === "edit") {
        return {
            catalogoBtn: document.getElementById("editServicioModoCatalogo"),
            customBtn: document.getElementById("editServicioModoPersonalizado"),
            catalogoContainer: document.getElementById("editServicioCatalogoContainer"),
            customContainer: document.getElementById("editServicioPersonalizadoContainer"),
            nombreInput: document.getElementById("editServicioNombrePersonalizado"),
            duracionInput: document.getElementById("editServicioDuracionPersonalizada"),
            select: document.getElementById("editServicioId")
        };
    }

    return {
        catalogoBtn: document.getElementById("servicioModoCatalogo"),
        customBtn: document.getElementById("servicioModoPersonalizado"),
        catalogoContainer: document.getElementById("servicioCatalogoContainer"),
        customContainer: document.getElementById("servicioPersonalizadoContainer"),
        nombreInput: document.getElementById("servicioNombrePersonalizado"),
        duracionInput: document.getElementById("servicioDuracionPersonalizada"),
        select: document.getElementById("servicio")
    };
}

function setServicioModo(mode, modo) {
    const els = getServicioToggleEls(mode);
    if (!els.catalogoBtn || !els.customBtn) {
        return;
    }

    const isCustom = modo === "personalizado";

    els.catalogoBtn.classList.toggle("active", !isCustom);
    els.catalogoBtn.setAttribute("aria-pressed", String(!isCustom));
    els.customBtn.classList.toggle("active", isCustom);
    els.customBtn.setAttribute("aria-pressed", String(isCustom));

    els.catalogoContainer?.classList.toggle("d-none", isCustom);
    els.customContainer?.classList.toggle("d-none", !isCustom);
}

function getServicioModo(mode) {
    const els = getServicioToggleEls(mode);
    return els.customBtn?.classList.contains("active") ? "personalizado" : "catalogo";
}

function initServicioToggles() {
    ["create", "edit"].forEach(mode => {
        const els = getServicioToggleEls(mode);
        els.catalogoBtn?.addEventListener("click", () => setServicioModo(mode, "catalogo"));
        els.customBtn?.addEventListener("click", () => setServicioModo(mode, "personalizado"));
    });
}

function generarSlots() {

    const slots = [];
    const { inicio, fin, intervalo } = calendarConfig;

    for (let h = inicio; h < fin; h++) {

        for (let m = 0; m < 60; m += intervalo) {

            slots.push({
                hour: h,
                minute: m
            });

        }

    }

    return slots;

}

function actualizarHorario() {

    const inicio =
        parseInt(document.getElementById("horaInicio").value);

    const fin =
        parseInt(document.getElementById("horaFin").value);

    const intervalo =
        parseInt(document.getElementById("intervaloCitas").value);

    if (inicio >= fin) {

        alert("La hora de inicio debe ser menor que la hora final");
        return;

    }

    if (![5, 10, 15, 30].includes(intervalo)) {

        alert("Intervalo inválido");
        return;

    }

    calendarConfig.inicio = inicio;
    calendarConfig.fin = fin;
    calendarConfig.intervalo = intervalo;

    localStorage.setItem(
        "calendarConfig",
        JSON.stringify(calendarConfig)
    );

    if (currentView === "day") {

        renderDayView(currentDate);

    } else {

        renderCalendar(currentDate);

    }

}

async function renderCalendar(date) {
    const calendar = document.getElementById("calendar");
    if (!calendar) {
        return;
    }

    const request = beginRequest("monthCalendar");

    clearElement(calendar);

    const year = date.getFullYear();
    const month = date.getMonth();

    let counts = [];
    try {
        counts = await apiFetchJson(
            `/Calendar/GetCitasCountByMonth?year=${year}&month=${month + 1}`,
            { signal: request.signal });
    } catch (error) {
        if (error.name === "AbortError") {
            return;
        }

        console.error("Error cargando conteo de citas por mes", error);
        counts = [];
    }

    if (!isLatestRequest("monthCalendar", request.requestId)) {
        return;
    }

    const citasPorDia = {};
    counts.forEach(c => citasPorDia[c.day] = c.count);

    const firstDayOfMonth = new Date(year, month, 1);
    const lastDayOfMonth = new Date(year, month + 1, 0);

    const header = document.createElement("div");
    header.className = "calendar-header d-flex align-items-center gap-2";

    const prevBtn = document.createElement("button");
    prevBtn.className = "btn btn-sm btn-outline-secondary";
    prevBtn.textContent = "◀";
    prevBtn.onclick = () => {
        currentDate = new Date(currentDate.getFullYear(), currentDate.getMonth() - 1, 1, 12, 0, 0);
        renderCalendar(currentDate);
    };

    const nextBtn = document.createElement("button");
    nextBtn.className = "btn btn-sm btn-outline-secondary";
    nextBtn.textContent = "▶";
    nextBtn.onclick = () => {
        currentDate = new Date(currentDate.getFullYear(), currentDate.getMonth() + 1, 1, 12, 0, 0);
        renderCalendar(currentDate);
    };

    const title = document.createElement("h4");
    title.className = "m-0 flex-grow-1 text-center";
    title.textContent = date.toLocaleString("es-CR", {
        month: "long",
        year: "numeric"
    });

    const monthSelect = document.createElement("select");
    monthSelect.className = "form-select form-select-sm w-auto";

    for (let m = 0; m < 12; m++) {
        const opt = document.createElement("option");
        opt.value = m;
        opt.textContent = new Date(2024, m, 1)
            .toLocaleString("es-CR", { month: "long" });
        if (m === month) opt.selected = true;
        monthSelect.appendChild(opt);
    }

    const yearSelect = document.createElement("select");
    yearSelect.className = "form-select form-select-sm w-auto";

    for (let y = year - 5; y <= year + 5; y++) {
        const opt = document.createElement("option");
        opt.value = y;
        opt.textContent = y;
        if (y === year) opt.selected = true;
        yearSelect.appendChild(opt);
    }

    monthSelect.onchange = yearSelect.onchange = () => {
        currentDate = new Date(
            parseInt(yearSelect.value),
            parseInt(monthSelect.value),
            1
        );
        renderCalendar(currentDate);
    };

    header.appendChild(prevBtn);
    header.appendChild(title);
    header.appendChild(monthSelect);
    header.appendChild(yearSelect);
    header.appendChild(nextBtn);
    calendar.appendChild(header);

    const grid = document.createElement("div");
    grid.className = "calendar-grid";

    const days = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];
    days.forEach(d => {
        const dayName = document.createElement("div");
        dayName.className = "calendar-day-name";
        dayName.textContent = d;
        grid.appendChild(dayName);
    });

    let startDay = firstDayOfMonth.getDay();
    startDay = startDay === 0 ? 6 : startDay - 1;

    for (let i = 0; i < startDay; i++) {
        grid.appendChild(document.createElement("div"));
    }

    for (let day = 1; day <= lastDayOfMonth.getDate(); day++) {

        const dayDiv = document.createElement("div");
        dayDiv.className = "calendar-day";

        const count = citasPorDia[day] || 0;

        dayDiv.innerHTML = ` 
            <strong>${day}</strong>
            <div class="text-muted small">
                Citas: ${count}
            </div>
        `;

        const today = new Date();
        if (
            day === today.getDate() &&
            month === today.getMonth() &&
            year === today.getFullYear()
        ) {
            dayDiv.classList.add("today");
        }

        dayDiv.onclick = () => onDayClick(year, month, day);

        grid.appendChild(dayDiv);
    }

    calendar.appendChild(grid);
}

async function buildDayGrid(container, date, funcionarioFilter = null) {

    if (!container) {
        return;
    }

    const request = beginRequest("dayGrid");

    clearElement(container);

    const grid = document.createElement("div");
    grid.className = "day-grid";

    const timeColumn = document.createElement("div");
    timeColumn.className = "time-column";

    const funcionariosContainer = document.createElement("div");
    funcionariosContainer.className = "funcionarios-container";

    const headerStrip = document.createElement("div");
    headerStrip.className = "day-func-header-strip";
    const headerTimePad = document.createElement("div");
    headerTimePad.className = "day-func-header-time-pad";
    headerTimePad.textContent = "HORA";
    headerStrip.appendChild(headerTimePad);
    const headerCols = document.createElement("div");
    headerCols.className = "day-func-header-cols";
    headerStrip.appendChild(headerCols);

    grid.appendChild(timeColumn);
    grid.appendChild(funcionariosContainer);
    container.appendChild(headerStrip);
    container.appendChild(grid);

    const inicio = calendarConfig.inicio;
    const fin = calendarConfig.fin;
    const intervalo = calendarConfig.intervalo;
    const altoSlot = 30;
    const totalSlots = ((fin - inicio) * 60) / intervalo;
    const dateStr = formatDateOnly(date);

    // ===== GENERAR HORAS =====

    for (let i = 0; i < totalSlots; i++) {

        const hour = inicio + Math.floor((i * intervalo) / 60);
        const minute = (i * intervalo) % 60;

        const slot = document.createElement("div");
        slot.className = "time-slot";
        slot.textContent = formatHourAMPM(hour, minute);

        slot.addEventListener("click", () => {
            abrirModalConDuracion(date, hour, minute, 30);
        });

        timeColumn.appendChild(slot);
    }

    let citas = [];
    let funcionarios = [];

    try {
        [citas, funcionarios] = await Promise.all([
            apiFetchJson(`/Calendar/GetCitasByDay?date=${encodeURIComponent(dateStr)}`, { signal: request.signal }),
            getFuncionariosActivos()
        ]);
    } catch (error) {
        if (error.name === "AbortError") {
            return;
        }

        console.error("Error cargando la grilla diaria del calendario", error);
        return;
    }

    if (!isLatestRequest("dayGrid", request.requestId)) {
        return;
    }

    // ===== FUNCIONARIOS =====

    const funcionariosFiltrados = funcionarioFilter
        ? funcionarios.filter(f => String(f.id) === String(funcionarioFilter))
        : funcionarios;

    funcionariosFiltrados.forEach(func => {

        const headerCell = document.createElement("div");
        headerCell.className = "day-func-header-cell";
        const hAvatar = document.createElement("div");
        hAvatar.className = "day-func-avatar";
        hAvatar.style.background = func.colorCalendario || "#6366f1";
        hAvatar.textContent = (func.nombre || "?").substring(0, 2).toUpperCase();
        const hName = document.createElement("span");
        hName.className = "day-func-name";
        hName.textContent = func.nombre || "—";
        headerCell.appendChild(hAvatar);
        headerCell.appendChild(hName);
        headerCols.appendChild(headerCell);

        const col = document.createElement("div");
        col.className = "funcionario-column";
        col.dataset.id = String(func.id);
        col.dataset.color = func.colorCalendario || "";
        col.dataset.funcionarioId = String(func.id);
        col.dataset.funcionarioNombre = func.nombre || "";
        col.dataset.funcionarioColor = func.colorCalendario || "";

        calendarDebugLog("Construyendo columna dinámica", {
            funcionarioId: func.id,
            funcionarioNombre: func.nombre || ""
        });

        // Drag & drop desactivado — solo resize de duración

        // líneas base
        for (let i = 0; i < totalSlots; i++) {
            const line = document.createElement("div");
            const hour = inicio + Math.floor((i * intervalo) / 60);
            const minute = (i * intervalo) % 60;

            line.className = "slot-line calendar-slot";
            line.dataset.date = dateStr;
            line.dataset.slotIndex = String(i);
            line.dataset.hour = String(hour);
            line.dataset.minute = String(minute);
            line.dataset.funcionarioId = String(func.id);
            line.dataset.funcionarioNombre = func.nombre || "";
            line.dataset.funcionarioColor = func.colorCalendario || "";
            col.appendChild(line);
        }

        funcionariosContainer.appendChild(col);
    });

    bindCalendarSlotDelegation(funcionariosContainer, date, intervalo, altoSlot);

    // ===== POSICIONAR CITAS =====

    citas.forEach(cita => {

        const partes = cita.fechaHoraCita.split(/[-T:]/);

        const inicioCita = new Date(
            partes[0],
            partes[1] - 1,
            partes[2],
            partes[3],
            partes[4],
            partes[5] || 0
        );

        const minutosDesdeInicio =
            (inicioCita.getHours() * 60 + inicioCita.getMinutes()) - (inicio * 60);

        if (minutosDesdeInicio < 0) return;

        const duracion = cita.duracionMinutos || 30;

        const top = (minutosDesdeInicio / intervalo) * altoSlot;
        const altura = (duracion / intervalo) * altoSlot;

        const col = funcionariosContainer.querySelector(
            `.funcionario-column[data-id="${cita.funcionarioId}"]`
        );

        if (!col) return;

        const bloque = buildAppointmentBlock(cita, top, altura);

        bloque.addEventListener("mousedown", (e) => {
            if (e.target.classList.contains("cita-resize-handle")) return;
            e.stopPropagation();
        });

        bloque.addEventListener("mouseup", (e) => {
            e.stopPropagation();
        });

        bloque.addEventListener("click", (e) => {
            if (bloque._resizeDidHappen) { bloque._resizeDidHappen = false; return; }
            e.stopPropagation();
            editarCita(cita.id);
        });

        // ── RESIZE DE DURACIÓN ──────────────────────────────
        const resizeHandle = bloque.querySelector(".cita-resize-handle");
        if (resizeHandle) {
            const originalAltura = altura;
            let startY = 0;
            let duracionActual = cita.duracionMinutos || 30;

            resizeHandle.addEventListener("pointerdown", (e) => {
                e.preventDefault();
                e.stopPropagation();
                startY = e.clientY;
                duracionActual = cita.duracionMinutos || 30;
                resizeHandle.setPointerCapture(e.pointerId);
            });

            resizeHandle.addEventListener("pointermove", (e) => {
                if (!resizeHandle.hasPointerCapture(e.pointerId)) return;
                bloque._resizeDidHappen = true;

                const dy = e.clientY - startY;
                const rawHeight = Math.max(altoSlot, originalAltura + dy);
                const snappedSlots = Math.max(1, Math.round(rawHeight / altoSlot));
                const snappedHeight = snappedSlots * altoSlot;
                duracionActual = snappedSlots * intervalo;

                bloque.style.height = `${snappedHeight}px`;
                const detailEl = bloque.querySelector("div:nth-child(2)");
                if (detailEl) detailEl.textContent = `${duracionActual} min`;
            });

            resizeHandle.addEventListener("pointerup", async (e) => {
                if (!resizeHandle.hasPointerCapture(e.pointerId)) return;
                resizeHandle.releasePointerCapture(e.pointerId);

                const originalDuracion = cita.duracionMinutos || 30;
                if (!bloque._resizeDidHappen || duracionActual === originalDuracion) {
                    setTimeout(() => { bloque._resizeDidHappen = false; }, 100);
                    return;
                }

                const minDuracion = Math.max(intervalo, 5);
                const maxDuracion = (fin - inicio) * 60;
                if (duracionActual < minDuracion) duracionActual = minDuracion;
                if (duracionActual > maxDuracion) duracionActual = maxDuracion;

                try {
                    await apiFetchJson(`/Calendar/ResizeDuration/${cita.id}`, {
                        method: "PUT",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ duracionMinutos: duracionActual })
                    });
                    cita.duracionMinutos = duracionActual;
                    await refreshCalendarView();
                } catch (err) {
                    bloque.style.height = `${originalAltura}px`;
                    const detailEl = bloque.querySelector("div:nth-child(2)");
                    if (detailEl) {
                        if (cita.tipo === "DESCANSO") {
                            detailEl.textContent = `${originalDuracion} min`;
                        } else {
                            detailEl.textContent = safeText(cita.servicioNombre, "Sin servicio");
                        }
                    }
                    showCalendarToast(
                        "Horario no disponible",
                        err.message || "No se pudo ajustar la duración de la cita."
                    );
                }

                setTimeout(() => { bloque._resizeDidHappen = false; }, 100);
            });
        }

        col.appendChild(bloque);
    });
}

function bindCalendarSlotDelegation(funcionariosContainer, date, intervalo, altoSlot) {

    if (!funcionariosContainer || funcionariosContainer.dataset.slotDelegationBound === "true")
        return;

    funcionariosContainer.dataset.slotDelegationBound = "true";

    funcionariosContainer.addEventListener("pointerdown", function (event) {

        if (event.button !== 0)
            return;

        if (event.target.closest(".cita-bloque"))
            return;

        const slot = getCalendarSlot(event.target);

        calendarDebugLog("PointerDown recibido en calendario", {
            clickedElement: describeElement(event.target),
            slotEncontrado: buildSlotContext(slot)
        });

        if (!slot)
            return;

        const column = getFuncionarioColumn(slot);
        if (!column)
            return;

        const state = getSelectionState(funcionariosContainer);

        resetSlotSelection(funcionariosContainer);

        state.active = true;
        state.pointerId = event.pointerId;
        state.startSlot = slot;
        state.currentSlot = slot;
        state.column = column;
        state.previewBlock = document.createElement("div");
        state.previewBlock.className = "cita-preview";
        state.previewBlock.style.backgroundColor =
            slot.dataset.funcionarioColor || column.dataset.funcionarioColor || "#004445";
        state.previewBlock.style.opacity = "0.5";

        column.appendChild(state.previewBlock);
        updateSelectionPreview(state, altoSlot);

        funcionariosContainer.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });

    funcionariosContainer.addEventListener("pointermove", function (event) {

        const state = getSelectionState(funcionariosContainer);

        if (!state.active || state.pointerId !== event.pointerId)
            return;

        const slot = resolveSlotFromPoint(event.clientX, event.clientY);

        if (!slot || getFuncionarioColumn(slot) !== state.column)
            return;

        state.currentSlot = slot;
        updateSelectionPreview(state, altoSlot);
    });

    funcionariosContainer.addEventListener("pointerup", function (event) {

        const state = getSelectionState(funcionariosContainer);

        if (!state.active || state.pointerId !== event.pointerId)
            return;

        const slotFromPoint = resolveSlotFromPoint(event.clientX, event.clientY);
        if (slotFromPoint && getFuncionarioColumn(slotFromPoint) === state.column) {
            state.currentSlot = slotFromPoint;
        }

        const startContext = buildSlotContext(state.startSlot);
        const endContext = buildSlotContext(state.currentSlot || state.startSlot);
        const endIndex = Math.max(startContext?.slotIndex ?? 0, endContext?.slotIndex ?? 0);
        const duration = ((endIndex - (startContext?.slotIndex ?? 0)) + 1) * intervalo;
        const slotElement = state.startSlot;

        calendarDebugLog("Slot resuelto antes de abrir modal", {
            clickedElement: describeElement(event.target),
            slotEncontrado: startContext,
            funcionarioResuelto: startContext
                ? {
                    funcionarioId: startContext.funcionarioId,
                    funcionarioNombre: startContext.funcionarioNombre
                }
                : null,
            valoresModal: startContext
                ? {
                    fecha: startContext.date,
                    hora: startContext.hour,
                    minuto: startContext.minute,
                    duracion: duration
                }
                : null
        });

        funcionariosContainer.releasePointerCapture?.(event.pointerId);
        resetSlotSelection(funcionariosContainer);

        if (!startContext || !startContext.funcionarioId)
            return;

        abrirModalConDuracion(
            date,
            startContext.hour,
            startContext.minute,
            duration,
            startContext.funcionarioId,
            startContext.funcionarioNombre,
            slotElement
        );
    });

    funcionariosContainer.addEventListener("pointercancel", function () {
        resetSlotSelection(funcionariosContainer);
    });
}

async function renderDayView(date) {

    const calendar = document.getElementById("calendar");

    const wrapper = document.createElement("div");
    wrapper.className = "day-view-container";

    const header = document.createElement("h4");
    header.className = "day-view-title";
    header.textContent = date.toLocaleDateString("es-CR", {
        weekday: "long",
        year: "numeric",
        month: "long",
        day: "numeric"
    });

    wrapper.appendChild(header);
    calendar.innerHTML = "";
    calendar.appendChild(wrapper);

    await buildDayGrid(wrapper, date);
}

function formatDayTitle(date) {
    return date.toLocaleDateString("es-CR", {
        weekday: "long",
        year: "numeric",
        month: "long",
        day: "numeric"
    });
}

function getDayModalFilterValue() {
    const filtro = document.getElementById("dayModalFuncionarioFiltro");
    return filtro && filtro.value ? filtro.value : null;
}

function updateDayTodayButton() {
    const btn = document.getElementById("dayModalToday");
    if (!btn) {
        return;
    }
    // El botón "Hoy" solo aparece cuando el día mostrado NO es el día actual del negocio.
    btn.classList.toggle("d-none", isBusinessToday(currentDate));
}

// Reconstruye el contenido del modal de día para la fecha indicada, conservando
// el filtro de funcionario actual. Reutilizada por onDayClick y la navegación.
async function renderDayModalContent(date) {
    currentDate = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 12, 0, 0);

    const modalTitle = document.getElementById("modalTitle");
    if (modalTitle) {
        modalTitle.textContent = formatDayTitle(currentDate);
    }

    updateDayTodayButton();

    const hoursContainer = document.getElementById("hoursContainer");
    await buildDayGrid(hoursContainer, currentDate, getDayModalFilterValue());
}

let dayModalNavInFlight = false;

// Navega entre días dentro del modal. delta = -1/+1; "today" vuelve al día del negocio.
// Un guard evita solapar navegaciones por doble click / clicks rápidos; buildDayGrid
// ya cancela respuestas AJAX viejas mediante beginRequest("dayGrid").
async function navigateDayModal(delta) {
    if (dayModalNavInFlight) {
        return;
    }
    dayModalNavInFlight = true;

    try {
        let target;
        if (delta === "today") {
            target = getBusinessTodayDate();
        } else {
            target = new Date(
                currentDate.getFullYear(),
                currentDate.getMonth(),
                currentDate.getDate() + delta,
                12, 0, 0);
        }

        await renderDayModalContent(target);
    } finally {
        dayModalNavInFlight = false;
    }
}

async function onDayClick(year, month, day) {

    const selectedDate = new Date(year, month, day, 12, 0, 0);

    const hoursContainer = document.getElementById("hoursContainer");
    const modalFiltro = document.getElementById("dayModalFuncionarioFiltro");
    if (modalFiltro) {
        if (!modalFiltro.dataset.bound) {
            const funcs = await getFuncionariosActivos();
            modalFiltro.innerHTML = '<option value="">Todos los funcionarios</option>';
            funcs.forEach(f => {
                const opt = document.createElement("option");
                opt.value = f.id;
                opt.textContent = f.nombre;
                modalFiltro.appendChild(opt);
            });
            modalFiltro.dataset.bound = "true";
            // El listener se enlaza una sola vez; rebuild conservando la fecha actual.
            modalFiltro.addEventListener("change", async () => {
                await buildDayGrid(hoursContainer, currentDate, modalFiltro.value || null);
            });
        } else {
            modalFiltro.value = "";
        }
    }

    await renderDayModalContent(selectedDate);

    showCalendarModal("dayModal");
}

function onHourClick(date, hour, minute = 0) {

    hideCalendarModal("dayModal");

    abrirModalConDuracion(date, hour, minute, 30);
}

async function abrirModalConDuracion(
    date,
    hour,
    minute,
    duracion,
    funcionarioId = null,
    funcionarioNombre = null,
    clickedElement = null) {

    const fullDate = new Date(
        date.getFullYear(),
        date.getMonth(),
        date.getDate(),
        hour,
        minute,
        0
    );

    limpiarModalCita();

    const formatted = formatLocalDateTime(fullDate);

    const inputFecha = document.getElementById("appointmentDate");
    if (inputFecha) {
        inputFecha.value = formatted;
    }

    const inputDuracion = document.getElementById("duracionMinutos");
    if (inputDuracion) {
        inputDuracion.value = duracion;
    }

    const modalElement = document.getElementById("createCitaModal");
    const normalizedFuncionarioId = normalizeFuncionarioId(funcionarioId);

    if (modalElement) {
        modalElement.dataset.funcionarioId = normalizedFuncionarioId || "";
        modalElement.dataset.funcionarioNombre = funcionarioNombre || "";
    }

    calendarDebugLog("Abriendo modal de nueva cita", {
        clickedElement: describeElement(clickedElement),
        slotEncontrado: clickedElement ? buildSlotContext(getCalendarSlot(clickedElement)) : null,
        funcionarioResuelto: {
            funcionarioId: normalizedFuncionarioId,
            funcionarioNombre: funcionarioNombre || ""
        },
        valoresModal: {
            appointmentDate: inputFecha?.value || null,
            duracion
        }
    });

    try {
        await Promise.all([
            cargarServicios(),
            loadFuncionariosForCita(normalizedFuncionarioId, funcionarioNombre)
        ]);
    } catch (error) {
        console.error("Error preparando el modal de cita", error);
    }

    calendarDebugLog("Valores cargados en modal", {
        funcionarioSelectValue: document.getElementById("funcionarioId")?.value || null,
        appointmentDate: document.getElementById("appointmentDate")?.value || null,
        duracionMinutos: document.getElementById("duracionMinutos")?.value || null
    });

    syncCreateConsentUI();

    showCalendarModal("createCitaModal");
}

function formatLocalDateTime(date) {
    const pad = n => n.toString().padStart(2, '0');

    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
        + `T${pad(date.getHours())}:${pad(date.getMinutes())}:00`;
}

async function loadUpcomingAppointments(funcionarioId = "") {

    const lista = document.getElementById("listaTareas");
    if (!lista) {
        return;
    }

    const request = beginRequest("upcomingAppointments");

    try {

        const dateStr = formatLocalDate(currentDate);

        const url = funcionarioId
            ? `/Calendar/GetUpcomingAppointments?date=${dateStr}&funcionarioId=${funcionarioId}`
            : `/Calendar/GetUpcomingAppointments?date=${dateStr}`;

        const citas = await apiFetchJson(url, { signal: request.signal });

        if (!isLatestRequest("upcomingAppointments", request.requestId)) {
            return;
        }

        clearElement(lista);

        if (!citas.length) {
            const emptyItem = document.createElement("li");
            emptyItem.className = "list-group-item text-muted";
            emptyItem.textContent = "No hay citas";
            lista.appendChild(emptyItem);
            return;
        }

        citas.forEach(cita => {
            lista.appendChild(buildUpcomingAppointmentItem(cita));
        });

    } catch (error) {
        if (error.name === "AbortError") {
            return;
        }

        console.error(error);

        clearElement(lista);
        const errorItem = document.createElement("li");
        errorItem.className = "list-group-item text-danger";
        errorItem.textContent = "Error cargando citas";
        lista.appendChild(errorItem);
    }
}

async function cargarServicios() {

    const select = document.getElementById("servicio");
    if (!select) {
        return [];
    }

    select.innerHTML =
        "<option value=''>Seleccione un servicio</option>";

    const servicios = await getServiciosActivos();

    servicios.forEach(s => {

        const option = document.createElement("option");

        option.value = s.id;

        option.textContent =
            `${s.nombre} (${s.duracionMinutos || 30} min)`;

        option.dataset.duracion =
            s.duracionMinutos || 30;

        select.appendChild(option);

    });

    return servicios;
}

async function guardarCita() {

    const funcionario = document.getElementById("funcionarioId");
    const servicio = document.getElementById("servicio");
    const fechaInput = document.getElementById("appointmentDate");
    const duplicar = document.getElementById("duplicarCita").checked;
    const fechasDuplicadas = splitSelectedDates(document.getElementById("fechasDuplicadas").value);
    const consentConfig = getConsentUIConfig("create");
   

    if (!funcionario || !servicio || !fechaInput) {
        console.error("Elementos del modal no encontrados");
        return;
    }

    if (!funcionario.value) {
        alert("Debe seleccionar un funcionario");
        return;
    }

    const funcionarioId = Number.parseInt(funcionario.value, 10);
    if (!Number.isInteger(funcionarioId) || funcionarioId <= 0) {
        alert("Debe seleccionar un funcionario válido");
        return;
    }

    const esDescanso = document.getElementById("esDescanso").checked;
    const fechaHoraCita = normalizeDateTimeLocalValue(fechaInput.value);

    if (!fechaHoraCita) {
        alert("Debe indicar una fecha y hora válidas");
        return;
    }

    const esPersonalizado = !esDescanso && getServicioModo("create") === "personalizado";

    let servicioId = null;
    let servicioNombrePersonalizado = null;
    let duracionMinutos = null;

    if (esDescanso) {
        duracionMinutos = parsePositiveInt(document.getElementById("duracionDescanso").value);
        if (!duracionMinutos) {
            alert("Debe indicar una duración válida para el descanso");
            return;
        }
    } else if (esPersonalizado) {
        servicioNombrePersonalizado = (document.getElementById("servicioNombrePersonalizado").value || "").trim();
        if (!servicioNombrePersonalizado) {
            alert("Debe indicar el nombre del servicio personalizado");
            return;
        }

        duracionMinutos = parsePositiveInt(document.getElementById("servicioDuracionPersonalizada").value);
        if (!duracionMinutos || duracionMinutos < 5 || duracionMinutos > 480) {
            alert("La duración del servicio personalizado debe estar entre 5 y 480 minutos");
            return;
        }
    } else {
        servicioId = parsePositiveInt(servicio.value);
        if (!servicioId) {
            alert("Debe seleccionar un servicio válido");
            return;
        }
    }

    const clienteId = esDescanso
        ? null
        : parsePositiveInt(consentConfig.clienteIdInput?.value);
    const manualConsent = isTenantWhatsAppEnabled() &&
        !esDescanso &&
        !clienteId &&
        consentConfig.manualCheckbox?.checked === true;
    const consentCapturedAtUtc = manualConsent
        ? ensureConsentCapturedAt(consentConfig.manualCheckbox, consentConfig.capturedAtInput)
        : null;

    const data = {
        nombreCliente: esDescanso ? null : document.getElementById("nombreCliente").value,
        telefonoCliente: esDescanso ? null : document.getElementById("telefonoCliente").value,
        clienteId: clienteId,
        servicioId: servicioId,
        esServicioPersonalizado: esPersonalizado,
        servicioNombrePersonalizado: servicioNombrePersonalizado,
        fechaHoraCita: fechaHoraCita,
        funcionarioId: funcionarioId,

        tipo: esDescanso ? "DESCANSO" : "CITA",

        duracionMinutos: duracionMinutos,
        whatsAppConsentAtCreation: !isTenantWhatsAppEnabled() || esDescanso || clienteId ? false : manualConsent,
        whatsAppConsentSource: !isTenantWhatsAppEnabled() || esDescanso || clienteId
            ? null
            : (manualConsent ? "CitaManual" : "SinConsentimiento"),
        whatsAppConsentCapturedAtUtc: !isTenantWhatsAppEnabled() || esDescanso || clienteId ? null : consentCapturedAtUtc,

        duplicar: duplicar,
        fechasDuplicadas: duplicar ? fechasDuplicadas : []
    };

    calendarDebugLog("Payload enviado al guardar cita", {
        funcionarioId: data.funcionarioId,
        funcionarioNombre: funcionario.selectedOptions[0]?.textContent || "",
        fechaHoraCita: data.fechaHoraCita,
        tipo: data.tipo,
        servicioId: data.servicioId
    });

    try {
        await apiFetchJson("/Calendar/Create", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(data)
        });

        limpiarModalCita();

        hideCalendarModal("createCitaModal");

        await Promise.all([
            refreshCalendarView(),
            loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || "")
        ]);
    } catch (error) {
        showCalendarToast("Horario no disponible", error.message || "No se pudo guardar la cita.");
    }
}

async function moverCita(citaId, nuevaFechaHora, funcionarioId) {
    const citaIdNormalizado = parsePositiveInt(citaId);
    const funcionarioIdNormalizado = parsePositiveInt(funcionarioId);
    const fechaHoraNormalizada = normalizeDateTimeLocalValue(nuevaFechaHora);

    if (!citaIdNormalizado || !funcionarioIdNormalizado || !fechaHoraNormalizada) {
        alert("No fue posible mover la cita por datos inválidos.");
        return;
    }

    try {

        await apiFetchJson(`/Calendar/Move/${citaIdNormalizado}`, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                fechaHoraCita: fechaHoraNormalizada,
                funcionarioId: funcionarioIdNormalizado
            })
        });

        await refreshCalendarView();
        await loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || "");

    } catch (err) {

        alert(err.message);
    }
}
function parseLocalDateTime(dateStr) {

    if (!dateStr) return null;

    if (dateStr instanceof Date)
        return dateStr;

    dateStr = dateStr.toString();

    let fecha, hora;

    if (dateStr.includes("T")) {
        [fecha, hora] = dateStr.split("T");
    } else {
        [fecha, hora] = dateStr.split(" ");
    }

    const [y, m, d] = fecha.split("-").map(Number);
    const [h, min, s] = hora.split(":").map(Number);

    return new Date(y, m - 1, d, h, min, s || 0);
}

function agregarCitaVisual(cita, container = document) {

    const inicio = calendarConfig.inicio;
    const intervalo = calendarConfig.intervalo;
    const altoSlot = 30;

    const inicioCita = parseLocalDateTime(cita.fechaHoraCita);

    const minutosDesdeInicio =
        (inicioCita.getHours() * 60 + inicioCita.getMinutes()) - (inicio * 60);

    if (minutosDesdeInicio < 0) return;

    const duracion = cita.duracionMinutos || 30;

    const top = (minutosDesdeInicio / intervalo) * altoSlot;
    const altura = (duracion / intervalo) * altoSlot;

    const col = container.querySelector(
        `.funcionario-column[data-id="${cita.funcionarioId}"]`
    );

    if (!col) return;

    const bloque = buildAppointmentBlock(cita, top, altura);

    bloque.addEventListener("mousedown", (e) => {
        e.stopPropagation();
    });

    bloque.addEventListener("mouseup", (e) => {
        e.stopPropagation();
    });

    bloque.addEventListener("click", (e) => {
        e.stopPropagation();
        editarCita(cita.id);
    });

    col.appendChild(bloque);
}

function limpiarModalCita() {

    const nombreClienteInput = document.getElementById("nombreCliente");
    nombreClienteInput.value = "";
    clearSelectedClienteState(nombreClienteInput, document.getElementById("nombreClienteClienteId"));
    document.getElementById("telefonoCliente").value = "";
    document.getElementById("servicio").value = "";
    setServicioModo("create", "catalogo");
    const servicioNombrePersonalizadoInput = document.getElementById("servicioNombrePersonalizado");
    if (servicioNombrePersonalizadoInput) servicioNombrePersonalizadoInput.value = "";
    const servicioDuracionPersonalizadaInput = document.getElementById("servicioDuracionPersonalizada");
    if (servicioDuracionPersonalizadaInput) servicioDuracionPersonalizadaInput.value = "30";
    document.getElementById("funcionarioId").value = "";
    document.getElementById("appointmentDate").value = "";
    document.getElementById("duracionMinutos").value = "30";
    document.getElementById("duracionDescanso").value = "30";
    document.getElementById("whatsAppConsentAtCreation").checked = false;
    document.getElementById("whatsAppConsentCapturedAtUtc").value = "";

    document.getElementById("esDescanso").checked = false;
    document.getElementById("camposCita").classList.remove("d-none");
    document.getElementById("duracionDescansoContainer").classList.add("d-none");
    document.getElementById("duplicarCita").checked = false;
    document.getElementById("duplicarConfig").classList.add("d-none");
    hideClienteAutocomplete(document.getElementById("sugerenciasClientes"), nombreClienteInput);

    const fechasInput = document.getElementById("fechasDuplicadas");

    if (fechasInput && fechasInput._flatpickr) {
        fechasInput._flatpickr.clear(); // 🔥 limpia selección del calendario
    }

    syncCreateConsentUI();
}

function showCalendarToast(title, message, durationMs = 5000) {
    const existing = document.getElementById("calendarConflictToast");
    if (existing) existing.remove();

    const toast = document.createElement("div");
    toast.id = "calendarConflictToast";
    toast.className = "cal-toast cal-toast--error";
    toast.setAttribute("role", "alert");
    toast.setAttribute("aria-live", "assertive");
    toast.innerHTML = `
        <div class="cal-toast-icon"><i class="bi bi-exclamation-triangle-fill"></i></div>
        <div class="cal-toast-body">
            <strong class="cal-toast-title">${escapeHtml(title)}</strong>
            <div class="cal-toast-msg">${escapeHtml(message)}</div>
        </div>
        <button class="cal-toast-close" onclick="this.closest('.cal-toast').remove()" aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>
    `;

    document.body.appendChild(toast);
    requestAnimationFrame(() => {
        requestAnimationFrame(() => toast.classList.add("cal-toast--visible"));
    });

    const timer = setTimeout(() => {
        toast.classList.remove("cal-toast--visible");
        setTimeout(() => toast.remove(), 350);
    }, durationMs);

    toast.querySelector(".cal-toast-close").addEventListener("click", () => clearTimeout(timer));
}

function formatHourAMPM(hour, minute = 0) {
    const h = hour % 12 || 12;
    const ampm = hour < 12 ? "AM" : "PM";
    return `${h}:${minute.toString().padStart(2, "0")} ${ampm}`;
}

async function cancelarCita(id = null) {

    if (!id) {
        id = document.getElementById("editCitaId").value;
    }

    const citaId = parsePositiveInt(id);
    if (!citaId) {
        alert("No fue posible identificar la cita a eliminar.");
        return;
    }

    await abrirModalCancelarCita(citaId);
}

function escapeHtml(str) {
    const div = document.createElement("div");
    div.appendChild(document.createTextNode(String(str)));
    return div.innerHTML;
}

async function abrirModalCancelarCita(citaId) {
    const modalEl = document.getElementById("cancelCitaModal");
    if (!modalEl) return;

    const citaIdInput = document.getElementById("cancelCitaId");
    if (citaIdInput) citaIdInput.value = citaId;

    const summaryEl = document.getElementById("cancelCitaSummary");
    if (summaryEl) {
        summaryEl.innerHTML = '<div style="color:var(--private-muted-text);font-size:.84rem;padding:.5rem 0">Cargando…</div>';
    }

    const btn = document.getElementById("btnConfirmarCancelarCita");
    if (btn) {
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-x-circle-fill"></i> Cancelar cita';
    }

    showCalendarModal("cancelCitaModal");

    try {
        const cita = await apiFetchJson(`/Calendar/GetById/${citaId}`);
        if (!summaryEl) return;

        const tipo = safeText(cita.tipo, "CITA").toUpperCase();
        const esDescanso = tipo === "DESCANSO";
        const fechaCita = parseLocalDateTime(cita.fechaHoraCita);
        const fechaStr = fechaCita
            ? fechaCita.toLocaleDateString("es-CR", { weekday: "long", year: "numeric", month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" })
            : "—";

        const titleEl = document.getElementById("cancelCitaModalTitle");
        const descEl = document.getElementById("cancelCitaDesc");
        if (titleEl) titleEl.textContent = esDescanso ? "Eliminar descanso" : "Cancelar cita";
        if (descEl) descEl.textContent = esDescanso
            ? "¿Seguro que deseas eliminar este bloque de descanso?"
            : "¿Seguro que deseas cancelar esta cita?";

        clearElement(summaryEl);
        const rows = [];
        if (!esDescanso) {
            rows.push({ icon: "bi-person-fill", label: "Cliente", value: safeText(cita.nombreCliente, "—") });
            rows.push({ icon: "bi-scissors", label: "Servicio", value: safeText(cita.servicioNombre, "—") });
        }
        rows.push({ icon: "bi-person-badge-fill", label: "Funcionario", value: safeText(cita.funcionarioNombre, "—") });
        rows.push({ icon: "bi-calendar3", label: "Fecha y hora", value: fechaStr });

        rows.forEach(r => {
            const row = document.createElement("div");
            row.className = "cal-cancel-summary-row";
            row.innerHTML = `<div class="cal-cancel-summary-icon"><i class="bi ${escapeHtml(r.icon)}"></i></div><div class="cal-cancel-summary-info"><span class="cal-cancel-summary-label">${escapeHtml(r.label)}</span><span class="cal-cancel-summary-value">${escapeHtml(r.value)}</span></div>`;
            summaryEl.appendChild(row);
        });
    } catch (error) {
        if (error.name === "AbortError") return;
        if (summaryEl) summaryEl.innerHTML = `<div class="text-danger small py-2">Error al cargar el detalle de la cita.</div>`;
        console.error("Error cargando detalle para cancelar cita", error);
    }
}

async function confirmarCancelarCita() {
    const citaId = parsePositiveInt(document.getElementById("cancelCitaId")?.value);
    if (!citaId) return;

    const btn = document.getElementById("btnConfirmarCancelarCita");
    if (btn) { btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Cancelando…'; }

    try {
        await apiFetchJson(`/Calendar/Delete/${citaId}`, { method: "DELETE" });

        hideCalendarModal("cancelCitaModal");
        hideCalendarModal("editCitaModal");

        await loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || "");
        await refreshCalendarView();
    } catch (err) {
        if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-x-circle-fill"></i> Cancelar cita'; }
        const warningEl = document.getElementById("cancelCitaWarningBox");
        if (warningEl) {
            const span = warningEl.querySelector("span");
            if (span) span.textContent = err.message;
        }
    }
}

async function editarCita(id) {
    try {
        const cita = await apiFetchJson(`/Calendar/GetById/${id}`);

        document.getElementById("editCitaId").value = cita.id;
        document.getElementById("editTipo").value = cita.tipo;
        updateWhatsAppStatusPanel(cita);

        const fechaCita = parseLocalDateTime(cita.fechaHoraCita);
        document.getElementById("editFechaHora").value =
            fechaCita ? formatLocalDateTime(fechaCita).substring(0, 16) : "";

        const camposCita = document.getElementById("editCamposCita");
        const camposDescanso = document.getElementById("editCamposDescanso");

        if (cita.tipo === "DESCANSO") {

            camposCita.classList.add("d-none");
            camposDescanso.classList.remove("d-none");

            document.getElementById("editDuracionDescanso").value =
                cita.duracionMinutos || 30;

        } else {

            camposCita.classList.remove("d-none");
            camposDescanso.classList.add("d-none");

            const editNombreInput = document.getElementById("editNombreCliente");
            const editTelefonoInput = document.getElementById("editTelefonoCliente");
            const editClienteIdInput = document.getElementById("editNombreClienteClienteId");
            const editConsentCheckbox = document.getElementById("editWhatsAppConsentAtCreation");
            const editConsentCapturedAtInput = document.getElementById("editWhatsAppConsentCapturedAtUtc");

            editNombreInput.value =
                safeText(cita.nombreCliente);
            editTelefonoInput.value =
                safeText(cita.telefonoCliente);

            if (cita.clienteId) {
                applySelectedClienteState(editNombreInput, editTelefonoInput, editClienteIdInput, {
                    id: cita.clienteId,
                    nombre: cita.nombreCliente,
                    telefono: cita.telefonoCliente,
                    aceptaMensajesWhatsApp: cita.clienteAceptaMensajesWhatsApp === true
                });
                editConsentCheckbox.checked = false;
                editConsentCapturedAtInput.value = "";
            } else {
                clearSelectedClienteState(editNombreInput, editClienteIdInput);
                editConsentCheckbox.checked = cita.whatsAppConsentAtCreation === true;
                editConsentCapturedAtInput.value = cita.whatsAppConsentCapturedAtUtc || "";
            }

            hideClienteAutocomplete(document.getElementById("editSugerenciasClientes"), editNombreInput);
            syncEditConsentUI();

        }

        if (cita.tipo === "DESCANSO") {
            clearSelectedClienteState(
                document.getElementById("editNombreCliente"),
                document.getElementById("editNombreClienteClienteId"));
            document.getElementById("editWhatsAppConsentAtCreation").checked = false;
            document.getElementById("editWhatsAppConsentCapturedAtUtc").value = "";
            syncEditConsentUI();
        }

        await loadFuncionariosEdit(cita.funcionarioId);

        if (cita.tipo !== "DESCANSO") {
            await loadServiciosEdit(cita.servicioId);

            const editNombrePersonalizado = document.getElementById("editServicioNombrePersonalizado");
            const editDuracionPersonalizada = document.getElementById("editServicioDuracionPersonalizada");

            if (cita.esServicioPersonalizado) {
                setServicioModo("edit", "personalizado");
                if (editNombrePersonalizado) editNombrePersonalizado.value = safeText(cita.servicioNombre);
                if (editDuracionPersonalizada) editDuracionPersonalizada.value = cita.duracionMinutos || 30;
            } else {
                setServicioModo("edit", "catalogo");
                if (editNombrePersonalizado) editNombrePersonalizado.value = "";
                if (editDuracionPersonalizada) editDuracionPersonalizada.value = "30";
            }
        }

        showCalendarModal("editCitaModal");
    } catch (error) {
        showCalendarToast(
            "Error al cargar cita",
            error.message || "No fue posible cargar los datos de la cita."
        );
    }
}

async function loadFuncionariosForCita(selectedId = null, selectedName = null) {

    const funcionarios = await getFuncionariosActivos();

    const select = document.getElementById("funcionarioId");
    if (!select)
        return [];

    select.innerHTML = `<option value="">Seleccione funcionario</option>`;

    const normalizedSelectedId = normalizeFuncionarioId(selectedId);

    funcionarios.forEach(f => {
        const opt = document.createElement("option");
        opt.value = f.id;
        opt.textContent = f.nombre;
        opt.dataset.funcionarioNombre = f.nombre || "";
        opt.dataset.funcionarioColor = f.colorCalendario || "";

        if (normalizedSelectedId === String(f.id)) {
            opt.selected = true;
        }

        select.appendChild(opt);
    });

    if (normalizedSelectedId) {
        select.value = normalizedSelectedId;
    }

    calendarDebugLog("Funcionarios cargados en modal", {
        selectedRequested: normalizedSelectedId,
        selectedRequestedName: selectedName || "",
        selectedApplied: select.value || null,
        totalFuncionarios: funcionarios.length
    });

    return funcionarios;
}

async function loadFuncionariosEdit(selectedId) {

    const funcionarios = await getFuncionariosActivos();

    const select = document.getElementById("editFuncionarioId");
    if (!select) {
        return;
    }
    select.innerHTML = "";

    funcionarios.forEach(f => {
        const opt = document.createElement("option");
        opt.value = f.id;
        opt.textContent = f.nombre;
        if (f.id === selectedId) opt.selected = true;
        select.appendChild(opt);
    });
}

async function loadServiciosEdit(selectedId) {

    const servicios = await getServiciosActivos();

    const select = document.getElementById("editServicioId");
    if (!select) {
        return;
    }
    select.innerHTML = "<option value=''>Seleccione un servicio</option>";

    servicios.forEach(s => {
        const option = document.createElement("option");
        option.value = s.id; // 👈 el usuario NO ve esto
        option.textContent = `${s.nombre} (${s.duracionMinutos || 30} min)`;

        if (s.id === selectedId) {
            option.selected = true;
        }

        select.appendChild(option);
    });
}

async function loadFuncionariosFiltro() {

    const funcionarios = await getFuncionariosActivos();

    const select = document.getElementById("funcionarioFiltro");
    if (!select) {
        return;
    }
    select.innerHTML = `<option value="">Todos</option>`;

    funcionarios.forEach(f => {
        const opt = document.createElement("option");
        opt.value = f.id;
        opt.textContent = f.nombre;
        select.appendChild(opt);
    });
}

function toggleTareas() {
    const container = document.getElementById("tareasContainer");
    const btn = document.getElementById("toggleTareasBtn");

    const ocultar = container.style.display !== "none";

    container.style.display = ocultar ? "none" : "block";
    btn.textContent = ocultar ? "Mostrar" : "Ocultar";

    localStorage.setItem("tareasOcultas", ocultar);
}

async function guardarEdicion() {

    const id = parsePositiveInt(document.getElementById("editCitaId").value);
    const tipo = safeText(document.getElementById("editTipo").value).toUpperCase();
    const fechaHoraCita = normalizeDateTimeLocalValue(document.getElementById("editFechaHora").value);
    const funcionarioId = parsePositiveInt(document.getElementById("editFuncionarioId").value);
    const consentConfig = getConsentUIConfig("edit");

    if (!id) {
        alert("No fue posible identificar la cita a editar.");
        return;
    }

    if (!fechaHoraCita) {
        alert("Debe indicar una fecha y hora válidas.");
        return;
    }

    if (!funcionarioId) {
        alert("Debe seleccionar un funcionario válido.");
        return;
    }

    let data = {
        fechaHoraCita: fechaHoraCita,
        funcionarioId: funcionarioId,
        tipo: tipo
    };

    if (tipo === "DESCANSO") {
        const duracionDescanso = parsePositiveInt(document.getElementById("editDuracionDescanso").value);
        if (!duracionDescanso) {
            alert("Debe indicar una duración válida para el descanso.");
            return;
        }

        data.duracionMinutos =
            duracionDescanso;
        data.servicioId = null;
        data.nombreCliente = null;
        data.telefonoCliente = null;
        data.clienteId = null;
        data.whatsAppConsentAtCreation = false;
        data.whatsAppConsentSource = null;
        data.whatsAppConsentCapturedAtUtc = null;

    } else {
        const esPersonalizado = getServicioModo("edit") === "personalizado";

        let servicioId = null;
        let servicioNombrePersonalizado = null;
        let duracionPersonalizada = null;

        if (esPersonalizado) {
            servicioNombrePersonalizado = (document.getElementById("editServicioNombrePersonalizado").value || "").trim();
            if (!servicioNombrePersonalizado) {
                alert("Debe indicar el nombre del servicio personalizado.");
                return;
            }

            duracionPersonalizada = parsePositiveInt(document.getElementById("editServicioDuracionPersonalizada").value);
            if (!duracionPersonalizada || duracionPersonalizada < 5 || duracionPersonalizada > 480) {
                alert("La duración del servicio personalizado debe estar entre 5 y 480 minutos.");
                return;
            }
        } else {
            servicioId = parsePositiveInt(document.getElementById("editServicioId").value);
            if (!servicioId) {
                alert("Debe seleccionar un servicio válido.");
                return;
            }
        }

        const clienteId = parsePositiveInt(consentConfig.clienteIdInput?.value);
        const manualConsent = isTenantWhatsAppEnabled() &&
            !clienteId &&
            consentConfig.manualCheckbox?.checked === true;
        const consentCapturedAtUtc = manualConsent
            ? ensureConsentCapturedAt(consentConfig.manualCheckbox, consentConfig.capturedAtInput)
            : null;

        data.nombreCliente =
            document.getElementById("editNombreCliente").value;

        data.telefonoCliente =
            document.getElementById("editTelefonoCliente").value;

        data.clienteId =
            clienteId;
        data.servicioId =
            servicioId;
        data.esServicioPersonalizado = esPersonalizado;
        data.servicioNombrePersonalizado = servicioNombrePersonalizado;
        data.duracionMinutos = duracionPersonalizada;
        data.whatsAppConsentAtCreation = !isTenantWhatsAppEnabled() || clienteId ? false : manualConsent;
        data.whatsAppConsentSource = !isTenantWhatsAppEnabled() || clienteId
            ? null
            : (manualConsent ? "CitaManual" : "SinConsentimiento");
        data.whatsAppConsentCapturedAtUtc = !isTenantWhatsAppEnabled() || clienteId ? null : consentCapturedAtUtc;

    }

    try {
        await apiFetchJson(`/Calendar/Edit/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(data)
        });

        hideCalendarModal("editCitaModal");

        await Promise.all([
            loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || ""),
            refreshCalendarView()
        ]);
    } catch (error) {
        showCalendarToast(
            "Error al guardar",
            error.message || "No fue posible guardar los cambios de la cita."
        );
    }
}

async function refreshCalendarView() {



    // 🔥 Si está abierto el modal del día → reconstruirlo conservando el filtro actual
    const dayModalEl = document.getElementById("dayModal");

    if (dayModalEl && dayModalEl.classList.contains("show")) {

        const hoursContainer = document.getElementById("hoursContainer");

        await buildDayGrid(hoursContainer, currentDate, getDayModalFilterValue());
        return;
    }

    // 🔥 Si estamos en vista día principal
    if (currentView === "day") {
        await renderDayView(currentDate);
        return;
    }

    // 🔥 Si estamos en vista mes
    if (currentView === "month") {
        await renderCalendar(currentDate);
        return;
    }
}


async function cargarFechasOcupadas(flatpickrInstance = null) {

    const funcionarioId =
        document.getElementById("funcionarioId").value;

    if (!funcionarioId) {
        fechasOcupadas = [];
        return;
    }

    const request = beginRequest("occupiedDates");
    const visibleYear = flatpickrInstance?.currentYear ?? currentDate.getFullYear();
    const visibleMonth = flatpickrInstance?.currentMonth ?? currentDate.getMonth();
    const startDate = new Date(visibleYear, visibleMonth, 1);
    const endDate = new Date(visibleYear, visibleMonth + 1, 0);

    try {
        fechasOcupadas = await apiFetchJson(
            `/Calendar/GetFechasOcupadas?funcionarioId=${encodeURIComponent(funcionarioId)}&startDate=${encodeURIComponent(formatLocalDate(startDate))}&endDate=${encodeURIComponent(formatLocalDate(endDate))}`,
            { signal: request.signal });
    } catch (error) {
        if (error.name === "AbortError") {
            return;
        }

        fechasOcupadas = [];
        console.error("Error cargando fechas ocupadas", error);
    }

}
