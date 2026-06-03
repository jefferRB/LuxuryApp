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

function calendarDebugLog(message, payload = null) {

    if (!CALENDAR_DEBUG_ENABLED) return;

    if (payload === null) {
        console.debug(`[CalendarDebug] ${message}`);
        return;
    }

    console.debug(`[CalendarDebug] ${message}`, payload);
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

function updateWhatsAppStatusPanel(cita) {
    const panel = document.getElementById("editWhatsAppStatus");
    if (!panel) return;

    clearElement(panel);

    if (!cita || cita.tipo === "DESCANSO") {
        panel.classList.add("d-none");
        return;
    }

    appendLabeledText(panel, "WhatsApp:", formatWhatsAppState(cita.estadoConfirmacionWhatsApp));
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

function buildAppointmentBlock(cita, top, altura, draggable = false) {
    const bloque = document.createElement("div");
    bloque.className = "cita-bloque";
    bloque.dataset.id = String(cita.id);
    bloque.style.top = `${top}px`;
    bloque.style.height = `${altura}px`;
    bloque.style.backgroundColor = cita.tipo === "DESCANSO"
        ? "#6c757d"
        : (cita.colorCalendario || "#004445");
    bloque.draggable = draggable;

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
    appendLabeledText(small, "WhatsApp:", formatWhatsAppState(cita.estadoConfirmacionWhatsApp), false);
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

    const clearSelectedCliente = () => {
        nombreInput.dataset.selectedClienteId = "";
        nombreInput.dataset.selectedClienteName = "";

        if (clienteIdInput) {
            clienteIdInput.value = "";
        }
    };

    const scheduleSearch = () => {
        clearSelectedCliente();

        const term = nombreInput.value.trim();

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
        const clienteNombre = safeText(cliente.nombre);
        const clienteTelefono = safeText(cliente.telefono);
        const clienteId = cliente.id === null || cliente.id === undefined
            ? ""
            : String(cliente.id);

        nombreInput.value = clienteNombre;
        nombreInput.dataset.selectedClienteId = clienteId;
        nombreInput.dataset.selectedClienteName = clienteNombre;

        if (telefonoInput) {
            telefonoInput.value = clienteTelefono;
        }

        if (clienteIdInput) {
            clienteIdInput.value = clienteId;
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
        nombreInput.dataset.selectedClienteId = "";
        nombreInput.dataset.selectedClienteName = "";

        if (clienteIdInput) {
            clienteIdInput.value = "";
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

    });

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

async function buildDayGrid(container, date) {

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

    grid.appendChild(timeColumn);
    grid.appendChild(funcionariosContainer);
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

    funcionarios.forEach(func => {

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

        // 🔥 DROP ZONE
        col.addEventListener("dragover", e => {
            e.preventDefault();
        });

        col.addEventListener("drop", async e => {

            e.preventDefault();

            const citaId = e.dataTransfer.getData("citaId");

            const rect = col.getBoundingClientRect();
            const y = e.clientY - rect.top;

            // 🔥 CORRECCIÓN DE PRECISIÓN
            const slotIndex = Math.floor((y + altoSlot / 2) / altoSlot);

            const minutosDesdeInicio = slotIndex * intervalo;

            const hour = inicio + Math.floor(minutosDesdeInicio / 60);
            const minute = minutosDesdeInicio % 60;

            const nuevaFecha =
                `${dateStr}T${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}:00`;

            const funcionarioId = col.dataset.id;

            await moverCita(citaId, nuevaFecha, funcionarioId);
        });

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

        const bloque = buildAppointmentBlock(cita, top, altura, true);

        bloque.addEventListener("dragstart", e => {
            e.dataTransfer.setData("citaId", cita.id);
        });

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

async function onDayClick(year, month, day) {

    const selectedDate = new Date(year, month, day);

    currentDate = selectedDate;

    const modalTitle = document.getElementById("modalTitle");
    const hoursContainer = document.getElementById("hoursContainer");

    modalTitle.textContent = selectedDate.toLocaleDateString("es-CR", {
        weekday: "long",
        year: "numeric",
        month: "long",
        day: "numeric"
    });

    await buildDayGrid(hoursContainer, selectedDate);

    new bootstrap.Modal(document.getElementById("dayModal")).show();
}

function onHourClick(date, hour, minute = 0) {

    const dayModalEl = document.getElementById("dayModal");
    const dayModal = bootstrap.Modal.getInstance(dayModalEl);
    if (dayModal) dayModal.hide();

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

    new bootstrap.Modal(
        document.getElementById("createCitaModal")
    ).show();
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

    const servicioId = esDescanso ? null : parsePositiveInt(servicio.value);
    if (!esDescanso && !servicioId) {
        alert("Debe seleccionar un servicio válido");
        return;
    }

    const duracionMinutos = esDescanso
        ? parsePositiveInt(document.getElementById("duracionDescanso").value)
        : null;

    if (esDescanso && !duracionMinutos) {
        alert("Debe indicar una duración válida para el descanso");
        return;
    }

    const data = {
        nombreCliente: esDescanso ? null : document.getElementById("nombreCliente").value,
        telefonoCliente: esDescanso ? null : document.getElementById("telefonoCliente").value,
        servicioId: servicioId,
        fechaHoraCita: fechaHoraCita,
        funcionarioId: funcionarioId,

        tipo: esDescanso ? "DESCANSO" : "CITA",

        duracionMinutos: duracionMinutos,

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

        bootstrap.Modal
            .getInstance(document.getElementById("createCitaModal"))
            ?.hide();

        await Promise.all([
            refreshCalendarView(),
            loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || "")
        ]);
    } catch (error) {
        alert(error.message);
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

    const bloque = buildAppointmentBlock(cita, top, altura, false);

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
    nombreClienteInput.dataset.selectedClienteId = "";
    nombreClienteInput.dataset.selectedClienteName = "";
    document.getElementById("telefonoCliente").value = "";
    document.getElementById("servicio").value = "";
    document.getElementById("funcionarioId").value = "";
    document.getElementById("appointmentDate").value = "";
    document.getElementById("duracionMinutos").value = "30";
    document.getElementById("duracionDescanso").value = "30";

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

    const tipo = document.getElementById("editTipo")?.value;

const mensaje =
    tipo === "DESCANSO"
        ? "¿Seguro que deseas eliminar este descanso?"
        : "¿Seguro que deseas cancelar esta cita?";

if (!confirm(mensaje))
    return;

    try {

        await apiFetchJson(`/Calendar/Delete/${citaId}`, {
            method: "DELETE"
        });

        const modalEl = document.getElementById("editCitaModal");
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();

        await loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || "");
        await refreshCalendarView();

    } catch (err) {

        alert(err.message);
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
            editNombreInput.value =
                safeText(cita.nombreCliente);
            editNombreInput.dataset.selectedClienteId = "";
            editNombreInput.dataset.selectedClienteName = "";

            document.getElementById("editTelefonoCliente").value =
                safeText(cita.telefonoCliente);
            hideClienteAutocomplete(document.getElementById("editSugerenciasClientes"), editNombreInput);

        }

        await loadFuncionariosEdit(cita.funcionarioId);

        if (cita.tipo !== "DESCANSO") {
            await loadServiciosEdit(cita.servicioId);
        }

        const modal = new bootstrap.Modal(
            document.getElementById("editCitaModal")
        );

        modal.show();
    } catch (error) {
        alert(error.message);
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

    } else {
        const servicioId = parsePositiveInt(document.getElementById("editServicioId").value);
        if (!servicioId) {
            alert("Debe seleccionar un servicio válido.");
            return;
        }

        data.nombreCliente =
            document.getElementById("editNombreCliente").value;

        data.telefonoCliente =
            document.getElementById("editTelefonoCliente").value;

        data.servicioId =
            servicioId;
        data.duracionMinutos = null;

    }

    try {
        await apiFetchJson(`/Calendar/Edit/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(data)
        });

        bootstrap.Modal
            .getInstance(document.getElementById("editCitaModal"))
            ?.hide();

        await Promise.all([
            loadUpcomingAppointments(document.getElementById("funcionarioFiltro")?.value || ""),
            refreshCalendarView()
        ]);
    } catch (error) {
        alert(error.message);
    }
}

async function refreshCalendarView() {



    // 🔥 Si está abierto el modal del día → reconstruirlo
    const dayModalEl = document.getElementById("dayModal");

    if (dayModalEl && dayModalEl.classList.contains("show")) {

        const hoursContainer = document.getElementById("hoursContainer");
        hoursContainer.innerHTML = "";

        await buildDayGrid(hoursContainer, currentDate);
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
