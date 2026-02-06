$("#ServicioId").change(function () {

    let id = $(this).val();

    $.get("/Servicios/ObtenerPrecio", { id: id }, function (data) {
        $("#Monto").val(data.precio);
    });

});



$("#Cobro_ServicioId").change(function () {

    let servicioId = $(this).val();

    $.get("/Cobros/ObtenerPrecioServicio", { id: servicioId }, function (data) {

        if (data) {
            $("#Cobro_Monto").val(data.precio);
        }

    });

});



    function formatearMoneda(valor) {

        valor = valor.replace(/[^\d]/g, "");

    if (!valor) return "";

    return new Intl.NumberFormat("es-CR").format(valor);
    }

    function limpiarMoneda(valor) {
        return valor.replace(/[^\d]/g, "");
    }

document.querySelectorAll(".money-input").forEach(input => {

    if (input.value) {
        input.value = formatearMoneda(input.value);
    }

});

    document.querySelectorAll(".money-input").forEach(input => {

        // Formatear mientras escribe
        input.addEventListener("input", function () {

            let limpio = limpiarMoneda(this.value);
            this.value = formatearMoneda(limpio);

        });


    // Limpiar antes de enviar formulario
    input.closest("form").addEventListener("submit", function () {

        input.value = limpiarMoneda(input.value);

        });

    });

