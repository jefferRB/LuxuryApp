function obtenerServicioAntiForgeryToken() {

    return $("#serviciosAntiForgeryForm input[name='__RequestVerificationToken']").val()
        || $("#formServicio input[name='__RequestVerificationToken']").val();
}

function cargarServicios() {

    $("#contenedorServicios").load("/Servicios/ModalServicios");
}

function toggleServicio(id) {

    $.ajax({
        url: "/Servicios/ToggleActivo",
        type: "POST",
        data: { id: id },
        headers: {
            "RequestVerificationToken": obtenerServicioAntiForgeryToken()
        }
    })
        .done(function () {
            cargarServicios();
        })
        .fail(function (response) {
            alert(response.responseText || "No fue posible actualizar el estado del servicio.");
        });
}

function mostrarFormularioServicio() {

    $("#formServicioContainer")
        .load("/Servicios/FormServicio");
}

function guardarServicio() {

    var form = $("#formServicio");

    $.ajax({
        url: "/Servicios/Save",
        type: "POST",
        data: form.serialize(),
        headers: {
            "RequestVerificationToken": obtenerServicioAntiForgeryToken(),
            "X-Requested-With": "XMLHttpRequest"
        },
        success: function (response, status, xhr) {

            const contentType = xhr.getResponseHeader("content-type") || "";

            if (contentType.includes("text/html")) {
                $("#formServicioContainer").html(response);
                return;
            }

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
        method: "POST",
        headers: {
            "RequestVerificationToken": obtenerServicioAntiForgeryToken()
        }
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
