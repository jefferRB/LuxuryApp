/* ============================================================
   WhatsApp Inbox — whatsapp-inbox.js
   Enriquece el panel derecho del calendario (Bandeja WhatsApp),
   los KPIs de la bandeja y la agenda del día, reutilizando la
   infraestructura de calendar.js (apiFetchJson, currentDate,
   editarCita, cancelarCita). NO modifica calendar.js.
   ============================================================ */
(function () {
    "use strict";

    let lastItems = [];
    let inboxSequence = 0;

    function waEnabled() {
        return window.LUXURY_CALENDAR_CONFIG?.tenantWhatsAppEnabled === true;
    }

    function fetchJson(url, options) {
        // Reutiliza el helper de calendar.js (añade antiforgery + X-Requested-With).
        if (typeof apiFetchJson === "function") {
            return apiFetchJson(url, options);
        }
        return fetch(url, options).then(r => r.json());
    }

    function selectedDateStr() {
        const base = (typeof currentDate !== "undefined" && currentDate) ? currentDate : new Date();
        if (typeof formatLocalDate === "function") {
            return formatLocalDate(base);
        }
        const pad = n => String(n).padStart(2, "0");
        return `${base.getFullYear()}-${pad(base.getMonth() + 1)}-${pad(base.getDate())}`;
    }

    function selectedFuncionario() {
        return document.getElementById("funcionarioFiltro")?.value || "";
    }

    /* ── OVERRIDE del cargador de calendar.js ─────────────────── */
    window.loadUpcomingAppointments = async function (funcionarioId = "") {
        const requestId = ++inboxSequence;

        try {
            const dateStr = selectedDateStr();
            const url = funcionarioId
                ? `/WhatsAppInbox/Inbox?date=${encodeURIComponent(dateStr)}&funcionarioId=${encodeURIComponent(funcionarioId)}`
                : `/WhatsAppInbox/Inbox?date=${encodeURIComponent(dateStr)}`;

            const data = await fetchJson(url);

            // Evita renders fuera de orden por navegación rápida.
            if (requestId !== inboxSequence) {
                return;
            }

            lastItems = Array.isArray(data.items) ? data.items : [];
            renderStats(data.stats || {});
            renderThreadList(applyStatusFilter(lastItems), data.whatsAppEnabled === true);
            renderDayAgenda(lastItems);
        } catch (error) {
            if (error && error.name === "AbortError") {
                return;
            }
            console.error("Error cargando la bandeja WhatsApp", error);
            renderThreadError();
        }
    };

    /* ── KPIs de la bandeja ───────────────────────────────────── */
    function renderStats(stats) {
        setText("waInboxEnviados", stats.enviados ?? 0);
        setText("waInboxConfirmados", stats.confirmados ?? 0);
        setText("waInboxPendientes", stats.pendientes ?? 0);
    }

    /* ── Filtro de estados (cliente) ──────────────────────────── */
    function applyStatusFilter(items) {
        const filter = document.getElementById("statusFiltro")?.value || "";
        if (!filter) {
            return items;
        }

        const groups = {
            enviados: ["sent", "reminder"],
            confirmados: ["confirmed"],
            pendientes: ["pending", "not_sent"],
            fallidos: ["failed"],
            cancelados: ["cancelled"]
        };

        const keys = groups[filter];
        return keys ? items.filter(item => keys.includes(item.waStatusKey)) : items;
    }

    /* ── Lista de conversaciones (panel derecho) ──────────────── */
    function renderThreadList(items, enabled) {
        const lista = document.getElementById("listaTareas");
        if (!lista) {
            return;
        }

        lista.innerHTML = "";

        if (!items.length) {
            lista.appendChild(buildInboxEmpty());
            return;
        }

        items.forEach(item => lista.appendChild(buildThreadCard(item, enabled)));
    }

    function renderThreadError() {
        const lista = document.getElementById("listaTareas");
        if (!lista) {
            return;
        }
        lista.innerHTML = "";
        const li = document.createElement("li");
        li.className = "whatsapp-thread-card";
        li.textContent = "No fue posible cargar la bandeja.";
        lista.appendChild(li);
    }

    function buildInboxEmpty() {
        const li = document.createElement("li");
        li.className = "whatsapp-inbox-empty";

        const icon = document.createElement("div");
        icon.className = "whatsapp-inbox-empty-icon";
        icon.innerHTML = '<i class="bi bi-whatsapp"></i>';

        const title = document.createElement("div");
        title.style.fontWeight = "700";
        title.style.color = "var(--private-surface-text)";
        title.textContent = "Sin conversaciones";

        const sub = document.createElement("div");
        sub.style.fontSize = "0.82rem";
        sub.textContent = "No hay citas para el día seleccionado.";

        li.appendChild(icon);
        li.appendChild(title);
        li.appendChild(sub);
        return li;
    }

    function buildThreadCard(item, enabled) {
        const li = document.createElement("li");
        li.className = "whatsapp-thread-card";

        const top = document.createElement("div");
        top.className = "whatsapp-thread-top";

        const avatar = document.createElement("div");
        avatar.className = "whatsapp-avatar";
        avatar.textContent = item.iniciales || "?";

        const main = document.createElement("div");
        main.className = "whatsapp-thread-main";

        const name = document.createElement("div");
        name.className = "whatsapp-thread-name";
        name.textContent = item.nombreCliente || "Cliente";

        const meta = document.createElement("div");
        meta.className = "whatsapp-thread-meta";
        meta.textContent = [item.horaLocal, item.servicioNombre].filter(Boolean).join(" • ");

        main.appendChild(name);
        main.appendChild(meta);

        const badge = buildStatusBadge(item.waStatusKey, item.waStatusLabel);

        top.appendChild(avatar);
        top.appendChild(main);
        top.appendChild(badge);
        li.appendChild(top);

        if (item.waSubText) {
            const sub = document.createElement("div");
            sub.className = "whatsapp-thread-sub";
            sub.textContent = item.waSubText;
            li.appendChild(sub);
        }

        const actions = document.createElement("div");
        actions.className = "whatsapp-thread-actions";

        if (enabled && item.puedeEnviar) {
            actions.appendChild(buildSendButton(item, "Enviar ahora", "action-icon-button-success", "bi-send"));
        }
        if (enabled && item.puedeReenviar) {
            actions.appendChild(buildSendButton(item, "Reenviar", "action-icon-button-primary", "bi-arrow-repeat"));
        }

        actions.appendChild(buildActionButton("Ver chat", "bi-chat-dots", "", () => viewChat(item.citaId, item.nombreCliente)));
        actions.appendChild(buildActionButton("Editar", "bi-pencil", "", () => {
            if (typeof editarCita === "function") editarCita(item.citaId);
        }));
        actions.appendChild(buildActionButton("Cancelar", "bi-x-circle", "action-icon-button-danger", () => {
            if (typeof cancelarCita === "function") cancelarCita(item.citaId);
        }));

        li.appendChild(actions);
        return li;
    }

    function buildStatusBadge(key, label) {
        const span = document.createElement("span");
        const map = {
            confirmed: "status-badge-confirmed",
            sent: "status-badge-sent",
            reminder: "status-badge-reminder",
            pending: "status-badge-pending",
            not_sent: "status-badge-pending",
            failed: "status-badge-failed",
            cancelled: "status-badge-cancelled",
            no_phone: "status-badge-neutral",
            no_consent: "status-badge-neutral"
        };
        span.className = "status-badge " + (map[key] || "status-badge-neutral");
        span.textContent = label || "—";
        return span;
    }

    function buildSendButton(item, label, variant, icon) {
        return buildActionButton(label, icon, variant, async (btn) => {
            const confirmed = window.confirm(
                `¿Enviar mensaje de WhatsApp a ${item.nombreCliente || "el cliente"}? Esta acción puede generar un costo.`);
            if (!confirmed) {
                return;
            }
            await sendConfirmation(item.citaId, btn);
        });
    }

    function buildActionButton(label, icon, variant, handler) {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "action-icon-button" + (variant ? " " + variant : "");
        btn.innerHTML = `<i class="bi ${icon}"></i><span>${label}</span>`;
        btn.addEventListener("click", () => handler(btn));
        return btn;
    }

    /* ── Acción: enviar / reenviar ────────────────────────────── */
    async function sendConfirmation(citaId, btn) {
        if (btn) {
            btn.disabled = true;
            btn.dataset.originalHtml = btn.innerHTML;
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i><span>Enviando…</span>';
        }

        try {
            await fetchJson(`/WhatsAppInbox/Send/${citaId}`, { method: "POST" });
            waToast("Mensaje de WhatsApp en proceso de envío.", "success");
            await window.loadUpcomingAppointments(selectedFuncionario());
        } catch (error) {
            console.error("Error enviando WhatsApp", error);
            waToast(extractError(error) || "No fue posible enviar el mensaje.", "error");
            if (btn) {
                btn.disabled = false;
                if (btn.dataset.originalHtml) {
                    btn.innerHTML = btn.dataset.originalHtml;
                }
            }
        }
    }

    /* ── Acción: ver chat / log ───────────────────────────────── */
    async function viewChat(citaId, nombre) {
        const titleEl = document.getElementById("waChatModalTitle");
        const bodyEl = document.getElementById("waChatModalBody");
        if (!bodyEl) {
            return;
        }

        if (titleEl) {
            titleEl.textContent = nombre ? `Historial — ${nombre}` : "Historial de WhatsApp";
        }
        bodyEl.innerHTML = '<div class="text-muted small">Cargando…</div>';

        showChatModal();

        try {
            const logs = await fetchJson(`/WhatsAppInbox/Chat/${citaId}`);
            renderChatLogs(bodyEl, logs);
        } catch (error) {
            console.error("Error cargando historial WhatsApp", error);
            bodyEl.innerHTML = '<div class="text-danger small">No fue posible cargar el historial.</div>';
        }
    }

    function renderChatLogs(bodyEl, logs) {
        bodyEl.innerHTML = "";

        if (!Array.isArray(logs) || !logs.length) {
            bodyEl.innerHTML = '<div class="text-muted small">No hay mensajes registrados para esta cita.</div>';
            return;
        }

        logs.forEach(log => {
            const row = document.createElement("div");
            row.className = "wa-chat-log-row";

            const head = document.createElement("div");
            head.className = "wa-chat-log-head";

            const dir = document.createElement("span");
            dir.className = "wa-chat-log-dir";
            dir.textContent = `${log.direccion} · ${log.tipo}`;

            const time = document.createElement("span");
            time.className = "wa-chat-log-time";
            time.textContent = log.fechaHoraLocal;

            head.appendChild(dir);
            head.appendChild(time);

            const status = document.createElement("div");
            status.className = "wa-chat-log-status";
            status.textContent = "Estado: " + (log.estado || "—");

            row.appendChild(head);
            row.appendChild(status);

            if (log.error) {
                const err = document.createElement("div");
                err.className = "wa-chat-log-error";
                err.textContent = log.error;
                row.appendChild(err);
            }

            if (log.referenciaMensaje) {
                const ref = document.createElement("div");
                ref.className = "wa-chat-log-ref";
                ref.textContent = "Ref: " + log.referenciaMensaje;
                row.appendChild(ref);
            }

            bodyEl.appendChild(row);
        });
    }

    function showChatModal() {
        const modalEl = document.getElementById("waChatModal");
        if (!modalEl || typeof bootstrap === "undefined") {
            return;
        }
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    /* ── Agenda del día (abajo a la izquierda) ────────────────── */
    function renderDayAgenda(items) {
        const titleEl = document.getElementById("dayAgendaTitle");
        if (titleEl) {
            titleEl.textContent = formatAgendaTitle();
        }

        const countEl = document.getElementById("dayAgendaCount");
        if (countEl) {
            countEl.textContent = `${items.length} cita${items.length === 1 ? "" : "s"} programada${items.length === 1 ? "" : "s"}`;
        }

        const tbody = document.getElementById("dayAgendaTableBody");
        const mobile = document.getElementById("dayAgendaMobileList");
        const empty = document.getElementById("dayAgendaEmpty");

        if (tbody) tbody.innerHTML = "";
        if (mobile) mobile.innerHTML = "";

        if (!items.length) {
            if (empty) empty.classList.remove("d-none");
            return;
        }
        if (empty) empty.classList.add("d-none");

        items.forEach(item => {
            if (tbody) tbody.appendChild(buildAgendaRow(item));
            if (mobile) mobile.appendChild(buildAgendaCard(item));
        });
    }

    function buildAgendaRow(item) {
        const tr = document.createElement("tr");
        tr.appendChild(cell(item.horaLocal));
        tr.appendChild(cell(item.nombreCliente, true));
        tr.appendChild(cell(item.servicioNombre));
        tr.appendChild(cell(item.funcionarioNombre));

        const estadoTd = document.createElement("td");
        estadoTd.appendChild(buildStatusBadge(item.waStatusKey, item.estadoCitaLabel));
        tr.appendChild(estadoTd);

        const waTd = document.createElement("td");
        waTd.appendChild(buildStatusBadge(item.waStatusKey, item.waStatusLabel));
        tr.appendChild(waTd);

        return tr;
    }

    function buildAgendaCard(item) {
        const card = document.createElement("div");
        card.className = "whatsapp-thread-card";

        const top = document.createElement("div");
        top.className = "whatsapp-thread-top";

        const main = document.createElement("div");
        main.className = "whatsapp-thread-main";

        const name = document.createElement("div");
        name.className = "whatsapp-thread-name";
        name.textContent = `${item.horaLocal} · ${item.nombreCliente}`;

        const meta = document.createElement("div");
        meta.className = "whatsapp-thread-meta";
        meta.textContent = [item.servicioNombre, item.funcionarioNombre].filter(Boolean).join(" • ");

        main.appendChild(name);
        main.appendChild(meta);
        top.appendChild(main);
        top.appendChild(buildStatusBadge(item.waStatusKey, item.estadoCitaLabel));
        card.appendChild(top);

        return card;
    }

    function formatAgendaTitle() {
        const base = (typeof currentDate !== "undefined" && currentDate) ? currentDate : new Date();
        try {
            const formatted = base.toLocaleDateString("es-CR", {
                weekday: "long",
                day: "numeric",
                month: "long",
                year: "numeric"
            });
            return formatted.charAt(0).toUpperCase() + formatted.slice(1);
        } catch (e) {
            return "Agenda del día";
        }
    }

    /* ── Utilidades ───────────────────────────────────────────── */
    function cell(text, strong) {
        const td = document.createElement("td");
        if (strong) {
            td.style.fontWeight = "600";
        }
        td.textContent = text || "—";
        return td;
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) {
            el.textContent = value;
        }
    }

    function extractError(error) {
        if (!error) return null;
        if (typeof error.bodyText === "string" && error.bodyText.trim()) return error.bodyText.trim();
        if (typeof error.message === "string") return error.message;
        return null;
    }

    let toastTimer = null;
    function waToast(message, type) {
        let toast = document.getElementById("waInboxToast");
        if (!toast) {
            toast = document.createElement("div");
            toast.id = "waInboxToast";
            toast.className = "wa-inbox-toast";
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.classList.remove("wa-inbox-toast-error", "wa-inbox-toast-success");
        toast.classList.add(type === "error" ? "wa-inbox-toast-error" : "wa-inbox-toast-success");
        toast.classList.add("is-visible");

        if (toastTimer) {
            clearTimeout(toastTimer);
        }
        toastTimer = setTimeout(() => toast.classList.remove("is-visible"), 4000);
    }

    /* ── Eventos propios ──────────────────────────────────────── */
    document.addEventListener("DOMContentLoaded", function () {
        const statusFilter = document.getElementById("statusFiltro");
        if (statusFilter) {
            statusFilter.addEventListener("change", function () {
                renderThreadList(applyStatusFilter(lastItems), waEnabled());
            });
        }
    });
})();
