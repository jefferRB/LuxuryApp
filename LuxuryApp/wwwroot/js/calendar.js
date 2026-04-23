/* VARIABLES GLOBALES */

let currentDate = new Date();
let currentView = "month";
let fechasOcupadas = [];
let calendarConfig = {
    inicio: 6,
    fin: 22,
    intervalo: 15
};
const CALENDAR_DEBUG_ENABLED = true;

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

function formatDateOnly(date) {

    const pad = value => String(value).padStart(2, "0");

    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
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

}

/* CALENDARIO DUPLICAR CITAS */

function initCalendar() {

    flatpickr("#fechasDuplicadas", {

        mode: "multiple",
        dateFormat: "Y-m-d",

        onOpen: async function (selectedDates, dateStr, instance) {

            await cargarFechasOcupadas();
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

    const nombreInput = document.getElementById("nombreCliente");
    const telefonoInput = document.getElementById("telefonoCliente");
    const sugerenciasDiv = document.getElementById("sugerenciasClientes");
    const minAutocompleteLength = 2;

    if (!nombreInput) return;

    nombreInput.addEventListener("input", async function () {

        const term = this.value.trim();

        if (term.length < minAutocompleteLength) {
            sugerenciasDiv.innerHTML = "";
            return;
        }

        const response = await fetch(`/Clientes/Autocompletado?term=${encodeURIComponent(term)}`);
        const clientes = await response.json();

        sugerenciasDiv.innerHTML = "";

        clientes.forEach(cliente => {

            const item = document.createElement("a");
            item.classList.add("list-group-item", "list-group-item-action");
            item.textContent = `${cliente.nombre} - ${cliente.telefono}`;

            item.addEventListener("click", function () {

                nombreInput.value = cliente.nombre;
                telefonoInput.value = cliente.telefono;
                sugerenciasDiv.innerHTML = "";

            });

            sugerenciasDiv.appendChild(item);

        });

    });

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
    //limpia el calendario
    const calendar = document.getElementById("calendar");
    calendar.innerHTML = "";
    //Año y mes actual
    const year = date.getFullYear();
    const month = date.getMonth();

    // conteo de citas del mes
    const res = await fetch(
        `/Calendar/GetCitasCountByMonth?year=${year}&month=${month + 1}`//se llama al controller y trae las citas
    );
    const counts = await res.json();

    // convertir a mapa { day: count }, mas rapido para encontrar que en un arraylist 
    const citasPorDia = {};
    counts.forEach(c => citasPorDia[c.day] = c.count);

    const firstDayOfMonth = new Date(year, month, 1);
    const lastDayOfMonth = new Date(year, month + 1, 0); // el 0 es dia 0 del siguiente mes = ultimo dia de mes actual

    // ================= HEADER =================
    const header = document.createElement("div");
    header.className = "calendar-header d-flex align-items-center gap-2";

    const prevBtn = document.createElement("button");
    prevBtn.className = "btn btn-sm btn-outline-secondary";
    prevBtn.textContent = "◀";
    prevBtn.onclick = () => { // se configura como volver atras entre meses con -1 mes facil, llamamos el currentDate y le restamos 1 
        currentDate.setMonth(currentDate.getMonth() - 1);
        renderCalendar(currentDate);
    };

    const nextBtn = document.createElement("button");
    nextBtn.className = "btn btn-sm btn-outline-secondary";
    nextBtn.textContent = "▶" // lo mismo que botón para retroceder pero ahora +1
    nextBtn.onclick = () => {
        currentDate.setMonth(currentDate.getMonth() + 1);
        renderCalendar(currentDate);
    };

    const title = document.createElement("h4");
    title.className = "m-0 flex-grow-1 text-center";
    title.textContent = date.toLocaleString("es-CR", { //Para mostrar el nombre del mes y año actual
        month: "long",
        year: "numeric"
    });

    // 🔽 SELECT MES
    const monthSelect = document.createElement("select");
    monthSelect.className = "form-select form-select-sm w-auto";

    for (let m = 0; m < 12; m++) { // se crea de enero a diciembre
        const opt = document.createElement("option");
        opt.value = m;
        opt.textContent = new Date(2024, m, 1)
            .toLocaleString("es-CR", { month: "long" });
        if (m === month) opt.selected = true;
        monthSelect.appendChild(opt);
    }

    // 🔽 SELECT AÑO
    const yearSelect = document.createElement("select");
    yearSelect.className = "form-select form-select-sm w-auto";

    for (let y = year - 5; y <= year + 5; y++) { // mostrar 5 años atás y 5 años siguientes para no saturar
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

    header.appendChild(prevBtn); //meter el boton al header
    header.appendChild(title);
    header.appendChild(monthSelect);
    header.appendChild(yearSelect);
    header.appendChild(nextBtn);
    calendar.appendChild(header); //agregar todo al heades y despues agregar el header al calendar

    // ================= GRID =================
    const grid = document.createElement("div");
    grid.className = "calendar-grid";

    const days = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];
    days.forEach(d => {
        const dayName = document.createElement("div");
        dayName.className = "calendar-day-name";
        dayName.textContent = d;
        grid.appendChild(dayName);
    });

    let startDay = firstDayOfMonth.getDay(); //js empieza la semana domingo y yo la ajuste para empezar lunes
    startDay = startDay === 0 ? 6 : startDay - 1;

    for (let i = 0; i < startDay; i++) {
        grid.appendChild(document.createElement("div"));
    }

    // ================= DÍAS =================
    for (let day = 1; day <= lastDayOfMonth.getDate(); day++) { // recorrer de primer a last day

        const dayDiv = document.createElement("div");
        dayDiv.className = "calendar-day";

        const count = citasPorDia[day] || 0; // mostrar citas por dia o si no hay mostrar 0

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

    container.innerHTML = "";

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

    // ===== OBTENER DATOS =====

    const [citasRes, funcRes] = await Promise.all([
        fetch(`/Calendar/GetCitasByDay?date=${dateStr}`),
        fetch(`/Funcionarios/GetActivos`)
    ]);

    const citas = await citasRes.json();
    const funcionarios = await funcRes.json();

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

        const bloque = document.createElement("div");
        bloque.className = "cita-bloque";
        bloque.dataset.id = cita.id;

        // 🔥 DRAG
        bloque.draggable = true;

        bloque.addEventListener("dragstart", e => {
            e.dataTransfer.setData("citaId", cita.id);
        });

        bloque.style.top = top + "px";
        bloque.style.height = altura + "px";

        if (cita.tipo === "DESCANSO") {
            bloque.style.backgroundColor = "#6c757d";
        } else {
            bloque.style.backgroundColor = cita.colorCalendario || "#004445";
        }

        if (cita.tipo === "DESCANSO") {

            bloque.innerHTML = `
                <div style="font-weight:600">☕ DESCANSO</div>
                <div style="font-size:11px">
                    ${cita.duracionMinutos} min
                </div>
            `;

        } else {

            bloque.innerHTML = `
                <div style="font-weight:600">${cita.nombreCliente}</div>
                <div style="font-size:11px; opacity:0.9">
                    ${cita.servicioNombre}
                </div>
            `;
        }

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

    try {

        const dateStr =
            `${currentDate.getFullYear()}-${String(currentDate.getMonth() + 1).padStart(2, "0")}-${String(currentDate.getDate()).padStart(2, "0")}`;


        const url = funcionarioId
            ? `/Calendar/GetUpcomingAppointments?date=${dateStr}&funcionarioId=${funcionarioId}`
            : `/Calendar/GetUpcomingAppointments?date=${dateStr}`;

        const response = await fetch(url);

        if (!response.ok)
            throw new Error("Error cargando citas");

        const citas = await response.json();

        const lista = document.getElementById("listaTareas");
        lista.innerHTML = "";

        if (!citas.length) {
            lista.innerHTML = `
                <li class="list-group-item text-muted">
                    No hay citas
                </li>`;
            return;
        }

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

            const li = document.createElement("li");
            li.className = "side-cita-card";

            li.innerHTML = `
                <strong>Cliente:</strong> ${cita.nombreCliente ?? "—"}<br>
                <small>
                    <strong>Teléfono:</strong> ${cita.telefonoCliente ?? "—"}<br>
                    <strong>Servicio:</strong> ${cita.servicioNombre ?? "—"}<br>
                    <strong>Fecha:</strong>
                    ${inicioCita.toLocaleDateString("es-CR")}
                    ${inicioCita.toLocaleTimeString("es-CR", { hour: '2-digit', minute: '2-digit' })}<br>
                    <strong>Funcionario:</strong> ${cita.funcionarioNombre ?? "—"}
                </small>

                <div class="mt-2 d-flex gap-2">
                    <button class="btn btn-sm btn-outline-primary edit-btn">
                        ✏️ Editar
                    </button>
                    <button class="btn btn-sm btn-outline-danger delete-btn">
                        ❌ Cancelar
                    </button>
                </div>
            `;

            li.querySelector(".edit-btn")
                .addEventListener("click", () => editarCita(cita.id));

            li.querySelector(".delete-btn")
                .addEventListener("click", () => cancelarCita(cita.id));

            lista.appendChild(li);
        });

    } catch (error) {

        console.error(error);

        document.getElementById("listaTareas").innerHTML = `
            <li class="list-group-item text-danger">
                Error cargando citas
            </li>`;
    }
}

async function cargarServicios() {

    const select = document.getElementById("servicio");

    select.innerHTML =
        "<option value=''>Seleccione un servicio</option>";

    const res = await fetch("/Calendar/GetServiciosActivos");

    const servicios = await res.json();

    servicios.forEach(s => {

        const option = document.createElement("option");

        option.value = s.id;

        option.textContent =
            `${s.nombre} (${s.duracionMinutos || 30} min)`;

        option.dataset.duracion =
            s.duracionMinutos || 30;

        select.appendChild(option);

    });
}

async function guardarCita() {

    const funcionario = document.getElementById("funcionarioId");
    const servicio = document.getElementById("servicio");
    const fechaInput = document.getElementById("appointmentDate");
    const duplicar = document.getElementById("duplicarCita").checked;
    const fechasDuplicadas = document.getElementById("fechasDuplicadas").value?.split(",").filter(f => f);
   

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

    const data = {
        nombreCliente: esDescanso ? null : document.getElementById("nombreCliente").value,
        telefonoCliente: esDescanso ? null : document.getElementById("telefonoCliente").value,

        servicioId: esDescanso
            ? null
            : parseInt(servicio.value),

        fechaHoraCita: fechaInput.value,
        funcionarioId: funcionarioId,

        tipo: esDescanso ? "DESCANSO" : "CITA",

        duracionMinutos: esDescanso
            ? parseInt(document.getElementById("duracionDescanso").value)
            : null,

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

    const res = await fetch("/Calendar/Create", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {

        const nuevaCita = await res.json();   // 🔥 SOLO UNA VEZ

        limpiarModalCita()

        bootstrap.Modal
            .getInstance(document.getElementById("createCitaModal"))
            .hide();

        loadUpcomingAppointments();

        if (currentView === "day") {

            agregarCitaVisual(nuevaCita, document);

        } else if (document.getElementById("dayModal").classList.contains("show")) {

            const hoursContainer = document.getElementById("hoursContainer");
            agregarCitaVisual(nuevaCita, hoursContainer);

        } else {

            renderCalendar(currentDate);

        }
    

    } else {

        const error = await res.text();
        alert(error);
    }
}

async function moverCita(citaId, nuevaFechaHora, funcionarioId) {

    try {

        const res = await fetch(`/Calendar/Move/${citaId}`, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                fechaHoraCita: nuevaFechaHora,
                funcionarioId: funcionarioId
            })
        });

        if (!res.ok)
            throw new Error("No se pudo mover la cita");

        await refreshCalendarView();
        await loadUpcomingAppointments();

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

    const bloque = document.createElement("div");
    bloque.className = "cita-bloque";
    bloque.dataset.id = cita.id;

    bloque.style.top = top + "px";
    bloque.style.height = altura + "px";
    if (cita.tipo === "DESCANSO") {

        bloque.style.backgroundColor = "#6c757d";

    } else {

        bloque.style.backgroundColor = cita.colorCalendario || "#004445";

    }

    if (cita.tipo === "DESCANSO") {

        bloque.innerHTML = `
        <div style="font-weight:600">☕ DESCANSO</div>
        <div style="font-size:11px">
            ${cita.duracionMinutos} min
        </div>
    `;

    } else {

        bloque.innerHTML = `
        <div style="font-weight:600">${cita.nombreCliente}</div>
        <div style="font-size:11px; opacity:0.9">
            ${cita.servicioNombre}
        </div>
    `;

    }

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

    document.getElementById("nombreCliente").value = "";
    document.getElementById("telefonoCliente").value = "";
    document.getElementById("servicio").value = "";
    document.getElementById("funcionarioId").value = "";
    document.getElementById("appointmentDate").value = "";
    document.getElementById("duracionMinutos").value = "30";

    document.getElementById("editCamposDescanso").checked = false;
    document.getElementById("duplicarCita").checked = false;
    document.getElementById("duplicarConfig").classList.add("d-none");

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

    const tipo = document.getElementById("editTipo")?.value;

const mensaje =
    tipo === "DESCANSO"
        ? "¿Seguro que deseas eliminar este descanso?"
        : "¿Seguro que deseas cancelar esta cita?";

if (!confirm(mensaje))
    return;

    try {

        const res = await fetch(`/Calendar/Delete/${id}`, {
            method: "DELETE"
        });

        if (!res.ok)
            throw new Error("Error eliminando la cita");

        const modalEl = document.getElementById("editCitaModal");
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();

        await loadUpcomingAppointments();
        await refreshCalendarView();

    } catch (err) {

        alert(err.message);
    }
}

async function editarCita(id) {

    const res = await fetch(`/Calendar/GetById/${id}`);
    const cita = await res.json();

    document.getElementById("editCitaId").value = cita.id;
    document.getElementById("editTipo").value = cita.tipo;

    document.getElementById("editFechaHora").value =
        cita.fechaHoraCita.substring(0, 16);

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

        document.getElementById("editNombreCliente").value =
            cita.nombreCliente;

        document.getElementById("editTelefonoCliente").value =
            cita.telefonoCliente;

    }

    const modal = new bootstrap.Modal(
        document.getElementById("editCitaModal")
    );

    modal.show();

    setTimeout(async () => {

        await loadFuncionariosEdit(cita.funcionarioId);

        if (cita.tipo !== "DESCANSO") {
            await loadServiciosEdit(cita.servicioId);
        }

    }, 50);
}

async function loadFuncionariosForCita(selectedId = null, selectedName = null) {

    const res = await fetch("/Funcionarios/GetActivos");
    if (!res.ok)
        throw new Error("No se pudieron cargar los funcionarios activos");

    const funcionarios = await res.json();

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

    const res = await fetch("/Funcionarios/GetActivos");
    const funcionarios = await res.json();

    const select = document.getElementById("editFuncionarioId");
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

    const res = await fetch("/Calendar/GetServiciosActivos");
    const servicios = await res.json();

    const select = document.getElementById("editServicioId");
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

    const res = await fetch("/Funcionarios/GetActivos");
    const funcionarios = await res.json();

    const select = document.getElementById("funcionarioFiltro");
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

    const id = document.getElementById("editCitaId").value;
    const tipo = document.getElementById("editTipo").value;

    let data = {
        fechaHoraCita: document.getElementById("editFechaHora").value,
        funcionarioId: parseInt(document.getElementById("editFuncionarioId").value),
        tipo: tipo
    };

    if (tipo === "DESCANSO") {

        data.duracionMinutos =
            parseInt(document.getElementById("editDuracionDescanso").value);

    } else {

        data.nombreCliente =
            document.getElementById("editNombreCliente").value;

        data.telefonoCliente =
            document.getElementById("editTelefonoCliente").value;

        data.servicioId =
            parseInt(document.getElementById("editServicioId").value);

    }

    const res = await fetch(`/Calendar/Edit/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {

        const citaActualizada = await res.json();

        // 🔥 eliminar bloque viejo
        document
            .querySelectorAll(".cita-bloque")
            .forEach(b => {
                if (b.dataset.id == citaActualizada.id)
                    b.remove();
            });

        // 🔥 agregar bloque nuevo
        agregarCitaVisual(citaActualizada);

        bootstrap.Modal
            .getInstance(document.getElementById("editCitaModal"))
            .hide();

        await loadUpcomingAppointments();
        await refreshCalendarView();

    } else {

        const error = await res.text();
        alert(error);
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


async function cargarFechasOcupadas() {

    const funcionarioId =
        document.getElementById("funcionarioId").value;

    if (!funcionarioId) {
        fechasOcupadas = [];
        return;
    }

    const res = await fetch(
        `/Calendar/GetFechasOcupadas?funcionarioId=${funcionarioId}`
    );

    fechasOcupadas = await res.json();

}
