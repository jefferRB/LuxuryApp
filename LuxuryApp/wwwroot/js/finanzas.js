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