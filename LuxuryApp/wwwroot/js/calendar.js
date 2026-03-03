//Declaramos que el calendario cargue en el dia actual y en la vista mensual
let currentDate = new Date();
let currentView = "month";

//Carga lo inicial apenas entramos al calendario
document.addEventListener("DOMContentLoaded", () => {
    renderCalendar(currentDate);
    loadUpcomingAppointments();
    loadFuncionariosFiltro();
});
document.addEventListener("DOMContentLoaded", () => {
    const oculto = localStorage.getItem("tareasOcultas") === "true";
    const container = document.getElementById("tareasContainer");
    const btn = document.getElementById("toggleTareasBtn");

    if (oculto) {
        container.style.display = "none";
        btn.textContent = "Mostrar";
    }
});
//Configuraciones en botones de diario y mensual
document.getElementById("viewMonthBtn").onclick = () => {
    currentView = "month";
    document.getElementById("viewMonthBtn").classList.add("active"); // para resaltar la opcion elegida
    document.getElementById("viewDayBtn").classList.remove("active");
    document.getElementById("dayPicker").classList.add("d-none");
    renderCalendar(currentDate);
};

document.getElementById("viewDayBtn").onclick = () => {
    currentView = "day";
    document.getElementById("viewDayBtn").classList.add("active");
    document.getElementById("viewMonthBtn").classList.remove("active");
    document.getElementById("dayPicker").classList.remove("d-none");

    const picker = document.getElementById("dayPicker");
    picker.value =
        `${currentDate.getFullYear()}-` +
        `${String(currentDate.getMonth() + 1).padStart(2, "0")}-` +
        `${String(currentDate.getDate()).padStart(2, "0")}`;
    renderDayView(currentDate);
};

document.getElementById("funcionarioFiltro")
    .addEventListener("change", e => {
        loadUpcomingAppointments(e.target.value);
    });

document.getElementById("dayPicker").addEventListener("change", e => {
    const [y, m, d] = e.target.value.split("-").map(Number);
    currentDate = new Date(y, m - 1, d, 12, 0, 0);
    renderDayView(currentDate);
});

function generarSlots(inicio = 6, fin = 21, intervalo = 30) {
    const slots = [];
    for (let h = inicio; h < fin; h++) {
        for (let m = 0; m < 60; m += intervalo) {
            slots.push({ hour: h, minute: m });
        }
    }
    return slots;
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

    const inicio = 6;
    const fin = 20;
    const intervalo = 15;
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

        const inicioCita = new Date(cita.fechaHoraCita);

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

        bloque.onclick = (e) => {
            e.stopPropagation();
            editarCita(cita.id);
        };

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

    cargarServicios();

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

    const url = funcionarioId
        ? `/Calendar/GetUpcomingAppointments?funcionarioId=${funcionarioId}`
        : `/Calendar/GetUpcomingAppointments`;

    const response = await fetch(url);
    const citas = await response.json();

    const lista = document.getElementById("listaTareas");
    lista.innerHTML = "";

    if (citas.length === 0) {
        lista.innerHTML = `
            <li class="list-group-item text-muted">
                No hay citas
            </li>`;
        return;
    }

    citas.forEach(cita => {

        const fecha = new Date(cita.fechaHoraCita);

        const funcionarios = cita.funcionarios
            .map(b => b.nombre)
            .join(", ");

        const li = document.createElement("li");
        li.className = "side-cita-card";

        li.innerHTML = `
    <strong>Cliente:</strong> ${cita.nombreCliente ?? "—"}<br>
    <small>
        <strong>Teléfono:</strong> ${cita.telefonoCliente ?? "—"}<br>
        <strong>Servicio:</strong> ${cita.servicio ?? "—"}<br>
        <strong>Fecha:</strong>
        ${fecha.toLocaleDateString("es-CR")}
        ${fecha.toLocaleTimeString("es-CR", { hour: '2-digit', minute: '2-digit' })}<br>
        <strong>Funcionario:</strong> ${funcionarios || "—"}
    </small>

     <div class="mt-2 d-flex gap-2">
        <button class="btn btn-sm btn-outline-primary">
            ✏️ Editar
        </button>
        <button class="btn btn-sm btn-outline-danger">
            ❌ Cancelar
        </button>
    </div>

`;

        const editBtn = li.querySelector(".btn-outline-primary");
        const deleteBtn = li.querySelector(".btn-outline-danger");

        editBtn.onclick = () => editarCita(cita.id);
        deleteBtn.onclick = () => cancelarCita(cita.id);

        lista.appendChild(li);
    });
}

async function cargarServicios() {

    const select = document.getElementById("servicio");
    select.innerHTML = "<option value=''>Seleccione un servicio</option>";

    const res = await fetch("/Calendar/GetServiciosActivos");
    const servicios = await res.json();

    servicios.forEach(s => {
        const option = document.createElement("option");
        option.value = s.id;
        option.textContent = `${s.nombre} (${s.duracionMinutos || 30} min)`;
        select.appendChild(option);
    });
}

async function guardarCita() {

    const funcionario = document.getElementById("funcionarioId");
    const servicio = document.getElementById("servicio");
    const fechaInput = document.getElementById("appointmentDate");

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
        funcionarioId: parseInt(funcionario.value)
    };
    

    const res = await fetch("/Calendar/Create", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {

        limpiarModalCita()

        bootstrap.Modal
            .getInstance(document.getElementById("createCitaModal"))
            .hide();

        loadUpcomingAppointments();

        if (currentView === "day") {

            const nuevaCita = await res.json();

            // Vista principal
            agregarCitaVisual(nuevaCita, document);

        } else if (document.getElementById("dayModal").classList.contains("show")) {

            const nuevaCita = await res.json();

            // Vista dentro del modal
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

function agregarCitaVisual(cita, container = document) {

    const inicio = 6;
    const intervalo = 15;
    const altoSlot = 30;

    const inicioCita = new Date(cita.fechaHoraCita);

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

    bloque.onclick = (e) => {
        e.stopPropagation();
        editarCita(cita.id);
    };

    col.appendChild(bloque);
}

function limpiarModalCita() {

    document.getElementById("nombreCliente").value = "";
    document.getElementById("telefonoCliente").value = "";
    document.getElementById("servicio").value = "";
    document.getElementById("funcionarioId").value = "";
    document.getElementById("appointmentDate").value = "";
    document.getElementById("duracionMinutos").value = "30";
}


function formatHourAMPM(hour, minute = 0) {
    const h = hour % 12 || 12;
    const ampm = hour < 12 ? "AM" : "PM";
    return `${h}:${minute.toString().padStart(2, "0")} ${ampm}`;
}

async function cancelarCita(id) {

    if (!confirm("¿Seguro que deseas cancelar esta cita?"))
        return;

    const res = await fetch(`/Calendar/Delete/${id}`, {
        method: "DELETE"
    });

    if (res.ok) {
        alert("Cita cancelada");
        loadUpcomingAppointments();
    } else {
        alert("Error al cancelar la cita");
    }
}

async function editarCita(id) {

    const res = await fetch(`/Calendar/GetById/${id}`);
    const cita = await res.json();

    document.getElementById("editCitaId").value = cita.id;
    document.getElementById("editNombreCliente").value = cita.nombreCliente;
    document.getElementById("editTelefonoCliente").value = cita.telefonoCliente;
    document.getElementById("editServicio").value = cita.servicio;

    document.getElementById("editFechaHora").value =
        cita.fechaHoraCita.substring(0, 16);

    await loadFuncionariosEdit(cita.funcionarioId);

    new bootstrap.Modal(
        document.getElementById("editCitaModal")
    ).show();
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

function openManualCitaModal() {

    // limpiar campos
    document.getElementById("manualFecha").value = "";
    document.getElementById("manualHora").value = "";
    document.getElementById("manualNombre").value = "";
    document.getElementById("manualTelefono").value = "";
    document.getElementById("manualServicio").value = "";

    // cargar funcionarios
    loadFuncionariosForManual();

    const modal = new bootstrap.Modal(
        document.getElementById("manualCitaModal")
    );
    modal.show();
}

async function guardarCitaManual() {

    const fecha = document.getElementById("manualFecha").value;
    const hora = document.getElementById("manualHora").value;
    const funcionarioId = document.getElementById("manualFuncionarioId").value;

    if (!fecha || !hora || !funcionarioId) {
        alert("Complete fecha, hora y funcionario");
        return;
    }

    // construir fecha + hora
    const [y, m, d] = fecha.split("-").map(Number);
    const [hh, mm] = hora.split(":").map(Number);

    const fechaHora = new Date(y, m - 1, d, hh, mm, 0);

    const data = {
        nombreCliente: document.getElementById("manualNombre").value,
        telefonoCliente: document.getElementById("manualTelefono").value,
        servicio: document.getElementById("manualServicio").value,
        fechaHoraCita: fechaHora.toLocaleString("sv-SE").replace(" ", "T"),
        funcionarioId: parseInt(funcionarioId)
    };

    const res = await fetch("/Calendar/Create", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {
        bootstrap.Modal
            .getInstance(document.getElementById("manualCitaModal"))
            .hide();

        alert("Cita creada correctamente");

        loadUpcomingAppointments();
        renderCalendar(currentDate);

        if (currentView === "day") {
            renderDayView(currentDate);
        }
    } else {
        const txt = await res.text();
        console.error(txt);
        alert("Error al crear la cita");
    }
}

async function guardarEdicion() {

    const id = document.getElementById("editCitaId").value;

    const data = {
        nombreCliente: document.getElementById("editNombreCliente").value,
        telefonoCliente: document.getElementById("editTelefonoCliente").value,
        servicio: document.getElementById("editServicio").value,
        fechaHoraCita: document.getElementById("editFechaHora").value,
        funcionarioId: parseInt(document.getElementById("editFuncionarioId").value)
    };

    const res = await fetch(`/Calendar/Edit/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });

    if (res.ok) {
        alert("Cita actualizada");
        bootstrap.Modal.getInstance(
            document.getElementById("editCitaModal")
        ).hide();
        loadUpcomingAppointments();
    } else {
        alert("Error al actualizar");
    }
}


