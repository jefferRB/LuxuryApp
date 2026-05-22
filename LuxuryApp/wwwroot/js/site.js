// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(function () {
    const tipoItem = document.getElementById("TipoItem");
    const servicioContainer = document.getElementById("ServicioContainer");
    const productoContainer = document.getElementById("ProductoContainer");
    const servicioSelect = document.getElementById("ServicioId");
    const productoSelect = document.getElementById("ProductoId");
    const montoInput = document.getElementById("Monto");

    if (!tipoItem || !servicioContainer || !productoContainer || !servicioSelect || !productoSelect || !montoInput) {
        return;
    }

    function syncCobroTypeUi() {
        const isServicio = tipoItem.value !== "producto";

        servicioContainer.classList.toggle("d-none", !isServicio);
        productoContainer.classList.toggle("d-none", isServicio);

        servicioSelect.disabled = !isServicio;
        productoSelect.disabled = isServicio;
    }

    async function cargarPrecio(endpoint, id) {
        if (!id) {
            montoInput.value = "";
            return;
        }

        const response = await fetch(`${endpoint}?id=${encodeURIComponent(id)}`);
        const data = await response.json();

        if (data && data.precio !== undefined && data.precio !== null) {
            montoInput.value = data.precio;
        }
    }

    tipoItem.addEventListener("change", syncCobroTypeUi);
    servicioSelect.addEventListener("change", function () {
        cargarPrecio("/Cobros/ObtenerPrecioServicio", this.value);
    });
    productoSelect.addEventListener("change", function () {
        cargarPrecio("/Cobros/ObtenerPrecioProducto", this.value);
    });

    syncCobroTypeUi();
})();

function toggleProducto(id) {
    const tokenField = document.querySelector("#productos-antiforgery input[name='__RequestVerificationToken']")
        || document.querySelector("input[name='__RequestVerificationToken']");
    const headers = tokenField && tokenField.value
        ? { RequestVerificationToken: tokenField.value }
        : {};

    $.ajax({
        url: "/Productos/ToggleActivo",
        type: "POST",
        data: { id: id },
        headers: headers
    })
        .done(function () {
            location.reload();
        })
        .fail(function (xhr) {
            const message = xhr.responseJSON && xhr.responseJSON.message
                ? xhr.responseJSON.message
                : "No fue posible cambiar el estado del producto.";
            alert(message);
        });
}

(function () {
    const themeStorageKey = "luxury-theme";
    const themeClassPrefix = "theme-";
    const supportedThemes = ["marble", "futuristic"];
    const themeLabels = {
        marble: "Marmol clasico",
        futuristic: "Futurista azul/morado"
    };

    function normalizeTheme(theme) {
        return supportedThemes.includes(theme) ? theme : "marble";
    }

    function readStoredTheme() {
        try {
            return normalizeTheme(localStorage.getItem(themeStorageKey));
        } catch (error) {
            return "marble";
        }
    }

    function writeStoredTheme(theme) {
        try {
            localStorage.setItem(themeStorageKey, theme);
        } catch (error) {
            // Ignore storage errors and keep the in-memory theme applied.
        }
    }

    function updateThemeOptionUi(theme) {
        document.querySelectorAll("[data-luxury-theme-current]").forEach(function (element) {
            element.textContent = themeLabels[theme] || themeLabels.marble;
        });

        document.querySelectorAll("[data-luxury-theme-option]").forEach(function (button) {
            const isSelected = button.getAttribute("data-luxury-theme-option") === theme;
            button.classList.toggle("is-selected", isSelected);
            button.setAttribute("aria-pressed", isSelected ? "true" : "false");

            const checkIcon = button.querySelector("[data-luxury-theme-check]");
            if (checkIcon) {
                checkIcon.classList.toggle("d-none", !isSelected);
            }
        });
    }

    function applyTheme(theme) {
        const resolvedTheme = normalizeTheme(theme);
        document.documentElement.setAttribute("data-luxury-theme", resolvedTheme);

        if (document.body) {
            supportedThemes.forEach(function (supportedTheme) {
                document.body.classList.remove(themeClassPrefix + supportedTheme);
            });

            document.body.classList.add(themeClassPrefix + resolvedTheme);
        }

        updateThemeOptionUi(resolvedTheme);
        return resolvedTheme;
    }

    function handleThemeSelection(event) {
        const button = event.currentTarget;
        const requestedTheme = button.getAttribute("data-luxury-theme-option");
        const resolvedTheme = applyTheme(requestedTheme);
        writeStoredTheme(resolvedTheme);
    }

    function initializeThemeManager() {
        const initialTheme = readStoredTheme();
        applyTheme(initialTheme);

        document.querySelectorAll("[data-luxury-theme-option]").forEach(function (button) {
            button.addEventListener("click", handleThemeSelection);
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initializeThemeManager);
    } else {
        initializeThemeManager();
    }
})();

(function () {
    const header = document.querySelector(".private-header");
    const handle = document.querySelector("[data-private-navbar-handle]");
    const desktopMedia = window.matchMedia("(min-width: 1200px)");

    if (!header || header.dataset.scrollBehaviorInitialized === "true") {
        return;
    }

    header.dataset.scrollBehaviorInitialized = "true";

    let lastScrollY = window.scrollY;
    let isHidden = false;
    let ticking = false;

    function applyHiddenState(shouldHide) {
        const hiddenState = Boolean(shouldHide);
        if (isHidden === hiddenState) {
            return;
        }

        header.classList.toggle("private-header-hidden", hiddenState);
        header.classList.toggle("private-header-revealed", !hiddenState);

        if (handle) {
            handle.setAttribute("aria-expanded", hiddenState ? "false" : "true");
        }

        isHidden = hiddenState;
    }

    function shouldFreezeState() {
        return header.matches(":hover")
            || header.contains(document.activeElement)
            || document.body.classList.contains("modal-open")
            || header.querySelector(".dropdown-menu.show") !== null
            || header.querySelector(".navbar-collapse.show") !== null;
    }

    function revealNavbar() {
        applyHiddenState(false);
    }

    function handlePointerEnter() {
        revealNavbar();
    }

    function handlePointerLeave() {
        queueNavbarUpdate();
    }

    function handleFocusIn() {
        revealNavbar();
    }

    function handleFocusOut() {
        window.requestAnimationFrame(function () {
            queueNavbarUpdate();
        });
    }

    function updateNavbarState() {
        ticking = false;

        const currentScrollY = Math.max(window.scrollY, 0);

        if (!desktopMedia.matches) {
            applyHiddenState(false);
            lastScrollY = currentScrollY;
            return;
        }

        if (shouldFreezeState()) {
            revealNavbar();
            lastScrollY = currentScrollY;
            return;
        }

        const scrollDelta = currentScrollY - lastScrollY;
        const nearTopThreshold = 40;
        const hideThreshold = 120;
        const deltaThreshold = 8;

        if (currentScrollY <= nearTopThreshold) {
            applyHiddenState(false);
        } else if (scrollDelta <= -deltaThreshold) {
            applyHiddenState(false);
        } else if (scrollDelta >= deltaThreshold && currentScrollY > hideThreshold) {
            applyHiddenState(true);
        }

        lastScrollY = currentScrollY;
    }

    function queueNavbarUpdate() {
        if (ticking) {
            return;
        }

        ticking = true;
        window.requestAnimationFrame(updateNavbarState);
    }

    header.addEventListener("mouseenter", handlePointerEnter);
    header.addEventListener("mouseleave", handlePointerLeave);
    header.addEventListener("focusin", handleFocusIn);
    header.addEventListener("focusout", handleFocusOut);

    if (handle) {
        handle.addEventListener("click", function () {
            revealNavbar();
            handle.focus();
        });
    }

    window.addEventListener("scroll", queueNavbarUpdate, { passive: true });
    window.addEventListener("resize", queueNavbarUpdate, { passive: true });

    if (typeof desktopMedia.addEventListener === "function") {
        desktopMedia.addEventListener("change", queueNavbarUpdate);
    } else if (typeof desktopMedia.addListener === "function") {
        desktopMedia.addListener(queueNavbarUpdate);
    }

    header.classList.add("private-header-revealed");
    if (handle) {
        handle.setAttribute("aria-expanded", "true");
    }
    queueNavbarUpdate();
})();
