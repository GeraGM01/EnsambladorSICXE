// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
function desactivaOpcionesMenu() {
    $(".nav-item a").each(function () {
        $(this).removeClass("active");
    });
}

function activaOpcionMenu(IdOpcion) {
    $(IdOpcion + " a").addClass("active");
}

function RecargaDatos() {
    $('#Respuestas').DataTable().ajax.reload();
}