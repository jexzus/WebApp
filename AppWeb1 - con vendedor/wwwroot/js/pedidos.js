/**
 * Bloque de funciones de Antiforgery y AJAX
 */

// Función para obtener el token XSRF desde la meta tag
function getXsrfToken() {
    const el = document.querySelector('meta[name="request-verification-token"]');
    return el ? el.content : '';
}

// Función principal para manejar la llamada AJAX de cambio de estado
async function cambiarEstado(numPedido, nuevoEstado) {
    const token = getXsrfToken();

    if (!token) {
        throw new Error('Token de verificación no encontrado. Recargue la página.');
    }

    const res = await fetch('/Admin/CambiarEstadoAjax', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token // << CLAVE DE SEGURIDAD
        },
        // Los nombres de las propiedades deben coincidir con el modelo CambiarEstadoRequest en C#
        body: JSON.stringify({ numPedido, nuevoEstado })
    });

    const contentType = res.headers.get('content-type') || '';

    if (!res.ok) {
        // Intenta leer el texto del error, pero si no es JSON, captura el error HTTP
        const txt = await res.text();
        throw new Error(`HTTP Error ${res.status}: ${txt.substring(0, 100)}...`);
    }

    if (!contentType.includes('application/json')) {
        const txt = await res.text();
        throw new Error(`Respuesta del servidor no es JSON. Contenido: ${txt.substring(0, 100)}...`);
    }

    const data = await res.json();

    if (!data.success) {
        throw new Error(data.message || 'No se pudo actualizar el estado por un error interno.');
    }

    return data;
}

/**
 * Bloque de Lógica de UI (Filtros, Modals y Eventos)
 */

function imprimirTabla() { window.print(); }

const filtroEstado = document.getElementById("filtroEstado");
const buscador = document.getElementById("buscador");
const filas = document.querySelectorAll("#printArea tbody tr");

function filtrar() {
    const estadosSeleccionados = Array.from(filtroEstado.selectedOptions).map(opt => opt.value.toLowerCase());
    const texto = buscador.value.toLowerCase();

    filas.forEach(fila => {
        const estado = (fila.getAttribute("data-estado") || "").toLowerCase();
        const textoFila = (fila.getAttribute("data-filtro") || "").toLowerCase();
        const coincideEstado = estadosSeleccionados.length === 0 || estadosSeleccionados.includes(estado);
        const coincideTexto = texto === "" || textoFila.includes(texto);
        fila.style.display = (coincideEstado && coincideTexto) ? "" : "none";
    });
}

// Inicializar filtros
if (filtroEstado) filtroEstado.addEventListener("change", filtrar);
if (buscador) buscador.addEventListener("input", filtrar);
if (filtroEstado || buscador) filtrar();


let pedidoActual = null;
let estadoActual = null;

function mostrarToast(mensaje, esExito = true) {
    const toastEl = document.getElementById("toastExito");
    const toastBody = document.getElementById("toastMensaje");

    toastBody.innerText = mensaje;
    toastEl.classList.remove(esExito ? 'text-bg-danger' : 'text-bg-success');
    toastEl.classList.add(esExito ? 'text-bg-success' : 'text-bg-danger');

    const toast = new bootstrap.Toast(toastEl);
    toast.show();
}

// Manejo del evento click en los botones de cambio de estado
document.addEventListener('click', function (e) {
    if (e.target.classList.contains('cambiar-estado')) {
        e.preventDefault();

        // 1. Almacenar datos y mostrar modal
        pedidoActual = e.target.getAttribute('data-pedido');
        estadoActual = e.target.getAttribute('data-estado');

        document.getElementById('tituloConfirmacion').innerText = 'Cambiar estado';
        document.getElementById('mensajeConfirmacion').innerText =
            `¿Está seguro que desea cambiar el estado del pedido ${pedidoActual} a ${estadoActual}?`;

        const modal = new bootstrap.Modal(document.getElementById('modalConfirmacion'));
        modal.show();

        // 2. Definir acción al confirmar en el modal
        document.getElementById('btnConfirmarAccion').onclick = async function () {

            // Cierra el modal inmediatamente para no bloquear la UI
            bootstrap.Modal.getInstance(document.getElementById('modalConfirmacion')).hide();

            try {
                // Llama a la nueva función asíncrona con el token incluido
                const numPedidoInt = parseInt(pedidoActual);
                await cambiarEstado(numPedidoInt, estadoActual);

                // 3. Actualizar la interfaz de usuario tras el éxito
                const fila = document.querySelector(`tr[data-num="${pedidoActual}"]`);
                if (fila) {
                    const estadoCell = fila.cells[5];
                    let badgeClass = 'bg-warning';
                    switch (estadoActual) {
                        case 'En preparación': badgeClass = 'bg-info'; break;
                        case 'En reparto': badgeClass = 'bg-primary'; break;
                        case 'Entregado': badgeClass = 'bg-success'; break;
                        case 'Cancelado': badgeClass = 'bg-danger'; break; // Asumiendo que 'Cancelado' también es un estado
                        default: break;
                    }
                    estadoCell.innerHTML = `<span class="badge ${badgeClass}">${estadoActual}</span>`;
                    fila.setAttribute('data-estado', estadoActual);
                    fila.classList.toggle('fila-entregado', estadoActual === 'Entregado');
                }

                filtrar();
                mostrarToast("Estado actualizado correctamente");

            } catch (err) {
                // 4. Manejo de errores
                console.error("Error en la operación AJAX:", err);
                mostrarToast(err.message, false); // Muestra el error en rojo (false para esExito)
            }
        };
    }
});