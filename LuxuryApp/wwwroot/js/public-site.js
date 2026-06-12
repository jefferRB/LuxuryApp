document.addEventListener("DOMContentLoaded", () => {
    const navbar = document.querySelector("[data-public-navbar]");

    if (window.AOS) {
        window.AOS.init({
            duration: 700,
            easing: "ease-out-cubic",
            once: true,
            offset: 40
        });
    }

    if (navbar) {
        const syncNavbarState = () => {
            navbar.classList.toggle("is-scrolled", window.scrollY > 12);
        };
        syncNavbarState();
        window.addEventListener("scroll", syncNavbarState, { passive: true });
    }

    // Password toggle (show/hide)
    document.querySelectorAll("[data-pw-toggle]").forEach(btn => {
        btn.addEventListener("click", () => {
            const wrap = btn.closest(".auth-input-wrap");
            if (!wrap) return;
            const input = wrap.querySelector("input");
            if (!input) return;
            const isPassword = input.type === "password";
            input.type = isPassword ? "text" : "password";
            const icon = btn.querySelector("i");
            if (icon) icon.className = isPassword ? "bi bi-eye-slash" : "bi bi-eye";
            btn.setAttribute("aria-label", isPassword ? "Ocultar contraseña" : "Mostrar contraseña");
        });
    });

    // Prevent double submit on auth forms
    document.querySelectorAll("[data-prevent-double-submit]").forEach(form => {
        form.addEventListener("submit", () => {
            const btn = form.querySelector("[type='submit']");
            if (btn && !btn.disabled) {
                setTimeout(() => { btn.disabled = true; }, 0);
            }
        });
    });
});
