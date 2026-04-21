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

    if (!navbar) {
        return;
    }

    const syncNavbarState = () => {
        navbar.classList.toggle("is-scrolled", window.scrollY > 12);
    };

    syncNavbarState();
    window.addEventListener("scroll", syncNavbarState, { passive: true });
});
