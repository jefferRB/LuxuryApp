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
