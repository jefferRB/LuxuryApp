function cargarPuestos() {
    $("#contenedorPuestos").load("/Puestos/ModalPuestos");
}

function obtenerPuestoToken() {
    return $("#puestosTokenContainer input[name='__RequestVerificationToken']").val()
        || $("input[name='__RequestVerificationToken']").first().val();
}

function renderPuestoMessage(message, type) {
    const alertType = type || "danger";
    $("#puestosMessageContainer").html(
        `<div class="alert alert-${alertType} alert-dismissible fade show" role="alert">
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        </div>`);
}

function togglePuesto(id) {
    $("#puestosMessageContainer").html("");

    $.ajax({
        url: "/Puestos/ToggleActivo",
        type: "POST",
        data: { id: id },
        headers: {
            "RequestVerificationToken": obtenerPuestoToken()
        }
    }).done(function () {
        cargarPuestos();
    }).fail(function (response) {
        renderPuestoMessage(response.responseText || "No fue posible actualizar el estado del puesto.");
    });
}

function mostrarFormularioPuesto(id) {
    const url = id ? `/Puestos/FormPuesto?id=${id}` : "/Puestos/FormPuesto";
    $("#formPuestoContainer").load(url);
}

function guardarPuesto() {
    const form = $("#formPuesto");
    $("#puestosMessageContainer").html("");

    $.ajax({
        url: "/Puestos/Save",
        type: "POST",
        data: form.serialize(),
        headers: {
            "RequestVerificationToken": form.find("input[name='__RequestVerificationToken']").val(),
            "X-Requested-With": "XMLHttpRequest"
        },
        success: function (response) {
            if (typeof response === "string" && response.trim().startsWith("<")) {
                $("#formPuestoContainer").html(response);
                return;
            }

            $("#formPuestoContainer").html("");
            cargarPuestos();
        },
        error: function (response) {
            if (response.responseText && response.responseText.trim().startsWith("<")) {
                $("#formPuestoContainer").html(response.responseText);
                return;
            }

            renderPuestoMessage(response.responseText || "No fue posible guardar el puesto.");
        }
    });
}

function eliminarPuesto(id) {
    if (!confirm("¿Seguro que deseas eliminar este puesto? Esta acción no se puede deshacer.")) {
        return;
    }

    $("#puestosMessageContainer").html("");

    $.ajax({
        url: "/Puestos/Delete",
        type: "POST",
        data: { id: id },
        headers: {
            "RequestVerificationToken": obtenerPuestoToken()
        }
    }).done(function () {
        $("#formPuestoContainer").html("");
        cargarPuestos();
    }).fail(function (response) {
        renderPuestoMessage(response.responseText || "No fue posible eliminar el puesto.");
    });
}
