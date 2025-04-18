//recibe id en formato #nombre
function configuraDialogo(id, titulo, mensajeHtml) {
    $(id).find(".modal-title").text(titulo);
    $(id).find(".modal-body").html(mensajeHtml);
}

//recibe id en formato #nombre
function mostrarDialogo(id) {
    $(id).modal("show");
}

//recibe id en formato #nombre
function ocultarDialogo(id) {
    $(id).modal("hide");
}

//la función es una cadena con el nombre de la función a ligar con todo y parámetros
function agregarEventoOnClickBoton(id, idBoton, funcion) {
    $(id).find(".modal-footer").find(idBoton).attr("onClick", funcion);
}

//function mostrarLoading() {
//    mostrarDialogo("#loader");
//}

//function ocultarLoading() {
//    ocultarDialogo("#loader");
//}