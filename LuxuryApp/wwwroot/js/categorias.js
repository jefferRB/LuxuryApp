function cargarCategorias() {

    $("#contenedorCategorias")
        .load("/Categorias/ModalCategorias");

}

function toggleCategoria(id) {

    $.post("/Categorias/ToggleActivo", { id: id })
        .done(function () {
            cargarCategorias();
        });

}

function mostrarFormularioCategoria() {

    $("#formCategoriaContainer")
        .load("/Categorias/FormCategoria");

}

function guardarCategoria() {

    var form = $("#formCategoria");

    $.ajax({
        url: "/Categorias/Create",
        type: "POST",
        data: form.serialize(),
        headers: {
            "RequestVerificationToken":
                $('input[name="__RequestVerificationToken"]').val(),
            "X-Requested-With": "XMLHttpRequest"
        },
        success: function () {

            $("#formCategoriaContainer").html("");
            cargarCategorias();

        },
        error: function (response) {

            $("#formCategoriaContainer")
                .html(response.responseText);

        }
    });
}
