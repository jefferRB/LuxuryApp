// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
// ===== CAMBIO TIPO ITEM
document.getElementById("TipoItem").addEventListener("change", function () {



    const tipo = this.value;

    const servicioContainer = document.getElementById("ServicioContainer");
    const productoContainer = document.getElementById("ProductoContainer");

    const servicioSelect = document.getElementById("ServicioId");
    const productoSelect = document.getElementById("ProductoId");

    if (tipo === "servicio") {

        servicioContainer.classList.remove("d-none");
        productoContainer.classList.add("d-none");

        servicioSelect.disabled = false;
        productoSelect.disabled = true;

    } else {

        servicioContainer.classList.add("d-none");
        productoContainer.classList.remove("d-none");

        servicioSelect.disabled = true;
        productoSelect.disabled = false;
    }
});

// ===== AUTOCOMPLETAR SERVICIO
document.getElementById("ServicioId").addEventListener("change", function () {

    let servicioId = this.value;

    if (!servicioId) return;

    fetch(`/Cobros/ObtenerPrecioServicio?id=${servicioId}`)
        .then(res => res.json())
        .then(data => {

            if (data) {
                document.getElementById("Monto").value = data.precio;
            }

        });

});

// ===== AUTOCOMPLETAR PRODUCTO
document.getElementById("ProductoId").addEventListener("change", function () {

    let productoId = this.value;

    if (!productoId) return;

    fetch(`/Cobros/ObtenerPrecioProducto?id=${productoId}`)
        .then(res => res.json())
        .then(data => {

            if (data) {
                document.getElementById("Monto").value = data.precio;
            }

        });

});
function toggleProducto(id) {

    $.post("/Productos/ToggleActivo", { id: id })
        .done(function () {
            location.reload();
        });
}

