function cargarPuestos() {

    $("#contenedorPuestos").load("/Puestos/ModalPuestos");

}

function togglePuesto(id) {

    $.post("/Puestos/ToggleActivo", { id: id })
        .done(function () {
            cargarPuestos();
        });

}

function mostrarFormularioPuesto() {

    $("#formPuestoContainer")
        .load("/Puestos/FormPuesto");

}

function guardarPuesto() {

    var form = $("#formPuesto");

    $.ajax({
        url: "/Puestos/Create",
        type: "POST",
        data: form.serialize(),
        headers: {
            "RequestVerificationToken":
                $('input[name="__RequestVerificationToken"]').val(),
            "X-Requested-With": "XMLHttpRequest"
        },
        success: function () {

            $("#formPuestoContainer").html("");
            cargarPuestos();

        },
        error: function (response) {

            $("#formPuestoContainer").html(response.responseText);

        }
    });
}