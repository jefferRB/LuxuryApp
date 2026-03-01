function cargarServicios() {

    $("#contenedorServicios").load("/Servicios/ModalServicios"); //aca redirige al controller no a la view a la view desde el controller 

}

function toggleServicio(id) {

    $.post("/Servicios/ToggleActivo", { id: id })
        .done(function () {
            cargarServicios();
        });

}

function mostrarFormularioServicio() {

    $("#formServicioContainer")
        .load("/Servicios/FormServicio");

}

function guardarServicio() {

    var form = $("#formServicio");

    $.ajax({
        url: "/Servicios/Create",
        type: "POST",
        data: form.serialize(),
        headers: {
            "RequestVerificationToken":
                $('input[name="__RequestVerificationToken"]').val(),
            "X-Requested-With": "XMLHttpRequest"
        },
        success: function () {

            $("#formServicioContainer").html("");
            cargarServicios();

        },
        error: function (response) {

            $("#formServicioContainer").html(response.responseText);

        }
    });
}

function eliminarServicio(id) {

    if (!confirm("¿Seguro que deseas eliminar este servicio?"))
        return;

    fetch(`/Servicios/Eliminar/${id}`, {
        method: "POST"
    })
        .then(async res => {

            if (res.ok) {
                location.reload();
                return;
            }

            const mensaje = await res.text();
            alert(mensaje);

        })
        .catch(() => {
            alert("Error inesperado al eliminar.");
        });
}

