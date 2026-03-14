/* VARIABLES GLOBALES */

let currentDate = new Date();
let currentView = "month";
let fechasOcupadas = [];
let calendarConfig = {
    inicio: 6,
    fin: 22,
    intervalo: 15
};


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

    if (!nombreInput) return;

    nombreInput.addEventListener("input", async function () {

        const term = this.value;

        if (term.length < 2) {
            sugerenciasDiv.innerHTML = "";
            return;
        }

        const response = await fetch(`/Clientes/Autocompletado?term=${term}`);
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
    grid.className = "day-grid"; // 👈 respetamos tu clase original

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

    // ===== GENERAR HORAS =====

    for (let i = 0; i < totalSlots; i++) {

        const hour = inicio + Math.floor((i * intervalo) / 60);
        const minute = (i * intervalo) % 60;

        const slot = document.createElement("div");
        slot.className = "time-slot";
        slot.textContent = formatHourAMPM(hour, minute);

        // ✅ CLICK EN HORAS RESTAURADO
        slot.addEventListener("click", () => {
            abrirModalConDuracion(date, hour, minute, 30);
        });

        timeColumn.appendChild(slot);
    }

    // ===== OBTENER DATOS =====

    const dateStr =
        `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

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
        col.dataset.id = func.id;
        col.dataset.color = func.colorCalendario;

        // líneas base
        for (let i = 0; i < totalSlots; i++) {
            const line = document.createElement("div");
            line.className = "slot-line";
            col.appendChild(line);
        }

        enableColumnInteraction(col, date, inicio, altoSlot, intervalo);

        funcionariosContainer.appendChild(col);
    });

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

        bloque.style.top = top + "px";
        bloque.style.height = altura + "px";
        bloque.style.backgroundColor = cita.colorCalendario || "#004445";

        bloque.innerHTML = `
    <div style="font-weight:600">${cita.nombreCliente}</div>
    <div style="font-size:11px; opacity:0.9">
        ${cita.servicioNombre}
    </div>
`;

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

async function enableColumnInteraction(col, date, inicio, altoSlot, intervalo) {

    let isSelecting = false;
    let startY = 0;
    let previewBlock = null;

    col.addEventListener("mousedown", function (e) {

        if (e.target.classList.contains("cita-bloque"))
            return;

        isSelecting = true;

        const rect = col.getBoundingClientRect();
        startY = e.clientY - rect.top; // 👈 POSICIÓN REAL

        previewBlock = document.createElement("div");
        previewBlock.className = "cita-preview";

        const snappedTop =
            Math.floor(startY / altoSlot) * altoSlot;

        previewBlock.style.top = snappedTop + "px";
        previewBlock.style.height = altoSlot + "px";
        previewBlock.style.backgroundColor = col.dataset.color;
        previewBlock.style.opacity = "0.5";

        col.appendChild(previewBlock);
    });

    col.addEventListener("mousemove", function (e) {

        if (!isSelecting) return;

        const rect = col.getBoundingClientRect();
        const currentY = e.clientY - rect.top;

        const diff = currentY - startY;
        const slots = Math.max(1, Math.ceil(diff / altoSlot));

        previewBlock.style.height = (slots * altoSlot) + "px";
    });

    col.addEventListener("mouseup", function (e) {

        if (!isSelecting) return;

        isSelecting = false;

        const rect = col.getBoundingClientRect();
        const endY = e.clientY - rect.top;

        const diff = endY - startY;
        const slots = Math.max(1, Math.ceil(diff / altoSlot));

        const snappedStart =
            Math.floor(startY / altoSlot);

        const minutosDesdeInicio =
            snappedStart * intervalo;

        const hora = inicio + Math.floor(minutosDesdeInicio / 60);
        const minuto = minutosDesdeInicio % 60;
        const duracion = slots * intervalo;

        const funcionarioId = col.dataset.id;

        col.removeChild(previewBlock);

        abrirModalConDuracion(date, hora, minuto, duracion, funcionarioId);
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

    const fullDate = new Date(date);
    fullDate.setHours(hour, minute, 0, 0);

    document.getElementById("appointmentDate").value =
        formatLocalDateTime(fullDate);

    // 🔹 Cargar combos
    loadFuncionariosForCita();
    cargarServicios()

    // 🔧 Cerrar modal del día si existe
    const dayModalEl = document.getElementById("dayModal");
    const dayModal = bootstrap.Modal.getInstance(dayModalEl);
    if (dayModal) dayModal.hide();

    // 🔹 Abrir modal de crear cita
    const createModal = new bootstrap.Modal(
        document.getElementById("createCitaModal")
    );
    createModal.show();
}

function abrirModalConDuracion(date, hour, minute, duracion, funcionarioId = null) {

    cargarServicios();

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

    // 🔥 Cargar funcionarios
    loadFuncionariosForCita();

    // 🔥 Esperar a que carguen y luego seleccionar el correcto
    if (funcionarioId) {
        setTimeout(() => {
            const select = document.getElementById("funcionarioId");
            if (select) {
                select.value = funcionarioId;
            }
        }, 300); // pequeño delay para asegurar que ya cargó
    }

    

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

        const url = funcionarioId
            ? `/Calendar/GetUpcomingAppointments?funcionarioId=${funcionarioId}`
            : `/Calendar/GetUpcomingAppointments`;

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

    const data = {
        nombreCliente: document.getElementById("nombreCliente").value,
        telefonoCliente: document.getElementById("telefonoCliente").value,
        servicioId: parseInt(servicio.value),
        fechaHoraCita: fechaInput.value,
        funcionarioId: parseInt(funcionario.value),

        duplicar: duplicar,
        fechasDuplicadas: duplicar ? fechasDuplicadas : []
    };


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

    bloque.style.top = top + "px";
    bloque.style.height = altura + "px";
    bloque.style.backgroundColor = cita.colorCalendario || "#004445";

    bloque.innerHTML = `
        <div style="font-weight:600">${cita.nombreCliente}</div>
        <div style="font-size:11px; opacity:0.9">
            ${cita.servicioNombre}
        </div>
    `;

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

    if (!confirm("¿Seguro que deseas cancelar esta cita?"))
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
    document.getElementById("editNombreCliente").value = cita.nombreCliente;
    document.getElementById("editTelefonoCliente").value = cita.telefonoCliente;
    document.getElementById("editFechaHora").value =
        cita.fechaHoraCita.substring(0, 16);

    const modal = new bootstrap.Modal(
        document.getElementById("editCitaModal")
    );

    modal.show();

    setTimeout(async () => {

        await loadFuncionariosEdit(cita.funcionarioId);
        await loadServiciosEdit(cita.servicioId); 

    }, 50);
}

async function loadFuncionariosForCita() {

    const res = await fetch("/Funcionarios/GetActivos");
    const funcionarios = await res.json();

    const select = document.getElementById("funcionarioId");
    select.innerHTML = `<option value="">Seleccione funcionario</option>`;

    funcionarios.forEach(f => {
        const opt = document.createElement("option");
        opt.value = f.id;
        opt.textContent = f.nombre;
        select.appendChild(opt);
    });
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

    const data = {
        nombreCliente: document.getElementById("editNombreCliente").value,
        telefonoCliente: document.getElementById("editTelefonoCliente").value,
        servicioId: parseInt(document.getElementById("editServicioId").value),
        fechaHoraCita: document.getElementById("editFechaHora").value,
        funcionarioId: parseInt(document.getElementById("editFuncionarioId").value)
    };

    const res = await fetch(`/Calendar/Edit/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {

        bootstrap.Modal
            .getInstance(document.getElementById("editCitaModal"))
            .hide();

        await loadUpcomingAppointments();

        refreshCalendarView(); // 🔥 actualización en tiempo real

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
