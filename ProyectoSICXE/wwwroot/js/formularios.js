function getObjectData($form) {
    var unindexed_array = $form.serializeArray();
    var indexed_array = {};

    $.map(unindexed_array, function (n, i) {
        indexed_array[n['name']] = n['value'];
    });

    return indexed_array;
}

function getFormData($form) {
    var unindexed_array = $form.serializeArray();
    let formData = new FormData();

    $.each(unindexed_array, function (i, field) {
        formData.append(field.name, field.value);
    });

    return formData;
}

function scrollCampoVacio(idForm, ajuste = 0) {
    //recuperamos los campos invalidos y scrolleamos hasta el primero
    var camposError = $("#" + idForm + " input:invalid");
    if (camposError && camposError.length > 0) {
        $("#" + idForm).scrollTop($(camposError[0]).offset().top - ajuste);
        $(camposError[0]).focus();
    }
}

async function copiarURLPagina(url, btn) {
    if (navigator.clipboard) {
        btn.removeClass('btn-secondary');
        btn.addClass('btn-success');
        let text = btn.siblings().text();
        await navigator.clipboard.writeText(text);
        setTimeout(function () {
            btn.removeClass('btn-success');
            btn.addClass('btn-secondary');
        }, 800);
        const toast = new bootstrap.Toast($(copiarToast))
        toast.show();
    }
}