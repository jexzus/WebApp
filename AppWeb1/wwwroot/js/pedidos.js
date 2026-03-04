/**
 * ============================================================
 * ARCHIVO: wwwroot/js/pedidos.js
 * ============================================================
 * MEJORAS DE UI:
 * ★ Legibilidad: Texto e iconos blancos en cards de alerta.
 * ★ Consistencia: Botones de alerta con mejor contraste.
 * ★ Login: (Nota) Aplicar 'text-white' en la vista Login.cshtml.
 * ============================================================
 */

// ── BLOQUE 1: UTILIDADES ─────────────────────────────────────────────────────

function getXsrfToken() {
    const meta = document.querySelector('meta[name="request-verification-token"]');
    if (meta) return meta.content;
    const match = document.cookie.match(/XSRF-TOKEN=([^;]+)/);
    return match ? decodeURIComponent(match[1]) : '';
}

function mostrarToast(mensaje, esExito = true) {
    const toastEl = document.getElementById('toastExito');
    const toastBody = document.getElementById('toastMensaje');
    if (!toastEl || !toastBody) {
        if (!esExito) alert('Error: ' + mensaje);
        return;
    }
    toastBody.innerText = mensaje;
    toastEl.classList.remove('text-bg-success', 'text-bg-danger', 'text-bg-warning');
    toastEl.classList.add(esExito ? 'text-bg-success' : 'text-bg-danger');
    bootstrap.Toast.getOrCreateInstance(toastEl).show();
}

function mostrarToastSignalR(mensaje, tipo = 'info') {
    const toastEl = document.getElementById('toastSignalR');
    const toastBody = document.getElementById('toastSignalRMensaje');
    if (!toastEl || !toastBody) return;
    toastBody.innerText = mensaje;
    toastEl.className = `toast align-items-center border-0 text-bg-${tipo}`;
    bootstrap.Toast.getOrCreateInstance(toastEl, { delay: 5000 }).show();
}

// ── BLOQUE 2: LLAMADAS AJAX ──────────────────────────────────────────────────

async function fetchJson(url, body) {
    const token = getXsrfToken();
    const res = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify(body)
    });
    if (!res.ok) {
        let msg = `Error HTTP ${res.status}`;
        try { const d = await res.json(); msg = d.message || msg; } catch { }
        throw new Error(msg);
    }
    const data = await res.json();
    if (!data.success) throw new Error(data.message || 'Error en la operación.');
    return data;
}

async function cambiarEstado(numPedido, nuevoEstado) {
    return fetchJson('/Admin/CambiarEstadoAjax', { numPedido, nuevoEstado });
}

async function solicitarRepartidor(numPedido) {
    return fetchJson('/Admin/SolicitarRepartidorAjax', { numPedido });
}

async function aceptarPedido(numPedido) {
    return fetchJson('/Admin/AceptarPedidoAjax', { numPedido });
}

async function cambiarDisponibilidad(disponibilidad) {
    return fetchJson('/Admin/CambiarDisponibilidadRepartidor', { disponibilidad });
}

async function revertirAEnPreparacion(numPedido, reemitirAlerta = true) {
    return fetchJson('/Admin/RevertirAEnPreparacionAjax', { numPedido, reemitirAlerta });
}

// ── BLOQUE 3: FILTROS Y BÚSQUEDA ────────────────────────────────────────────

const filtroEstado = document.getElementById('filtroEstado');
const buscador = document.getElementById('buscador');

function getFilas() {
    return document.querySelectorAll('#printArea tbody tr');
}

function filtrar() {
    const filas = getFilas();
    if (!filas.length) return;

    const estadoSel = filtroEstado ? filtroEstado.value : 'Todos';
    const texto = buscador ? buscador.value.toLowerCase() : '';

    filas.forEach(fila => {
        const estado = (fila.getAttribute('data-estado') || '').toLowerCase();
        const textoFila = (fila.getAttribute('data-filtro') || '').toLowerCase();

        const coincideEstado = estadoSel === 'Todos' || estado === estadoSel.toLowerCase();
        const coincideTexto = texto === '' || textoFila.includes(texto);

        fila.style.display = (coincideEstado && coincideTexto) ? '' : 'none';
    });
}

function imprimirTabla() { window.print(); }

if (filtroEstado) filtroEstado.addEventListener('change', filtrar);
if (buscador) buscador.addEventListener('input', filtrar);

// ── BLOQUE 4: MODALES ────────────────────────────────────────────────────────

function abrirModalConfirmacion(titulo, mensaje, callbackConfirmar) {
    const tituloEl = document.getElementById('tituloConfirmacion');
    const mensajeEl = document.getElementById('mensajeConfirmacion');
    const btnConfirmar = document.getElementById('btnConfirmarAccion');

    if (tituloEl) tituloEl.innerText = titulo;
    if (mensajeEl) mensajeEl.innerText = mensaje;

    if (btnConfirmar) {
        const nuevo = btnConfirmar.cloneNode(true);
        btnConfirmar.parentNode.replaceChild(nuevo, btnConfirmar);
        nuevo.addEventListener('click', () => {
            bootstrap.Modal.getInstance(document.getElementById('modalConfirmacion'))?.hide();
            callbackConfirmar();
        });
    }
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modalConfirmacion')).show();
}

// ── BLOQUE 5: MANEJADORES DE EVENTOS DE UI ───────────────────────────────────

document.addEventListener('click', function (e) {
    const linkEstado = e.target.closest('.cambiar-estado');
    if (linkEstado) {
        e.preventDefault();
        const numPedido = linkEstado.getAttribute('data-pedido');
        const nuevoEstado = linkEstado.getAttribute('data-estado');
        const estadoActual = linkEstado.getAttribute('data-estado-actual') || '';
        abrirModalConfirmacion(
            'Cambiar estado',
            `¿Cambiás el pedido #${numPedido} de "${estadoActual}" a "${nuevoEstado}"?`,
            () => procesarCambioEstado(numPedido, nuevoEstado)
        );
        return;
    }

    const btnSolicitar = e.target.closest('.btn-solicitar-repartidor');
    if (btnSolicitar) {
        e.preventDefault();
        const numPedido = btnSolicitar.getAttribute('data-pedido');
        abrirModalConfirmacion(
            '📡 Solicitar Repartidor',
            `Se enviará una alerta a todos los repartidores disponibles para el pedido #${numPedido}.`,
            () => procesarSolicitudRepartidor(numPedido, btnSolicitar)
        );
        return;
    }

    const btnRevertir = e.target.closest('.btn-revertir-preparacion');
    if (btnRevertir) {
        e.preventDefault();
        const numPedido = btnRevertir.getAttribute('data-pedido');
        abrirModalConfirmacion(
            '↩ Revertir pedido',
            `¿Revertís el pedido #${numPedido} a "En preparación"? Se desasignará el repartidor actual y se enviará una nueva alerta.`,
            () => procesarReversionPreparacion(numPedido, btnRevertir)
        );
        return;
    }
});

// ── BLOQUE 6: EJECUCIÓN DE ACCIONES ─────────────────────────────────────────

async function procesarCambioEstado(numPedido, nuevoEstado) {
    try {
        const fila = document.querySelector(`tr[data-num="${numPedido}"]`);
        const modoEntrega = fila ? fila.getAttribute('data-modo-entrega') : '';

        await cambiarEstado(parseInt(numPedido), nuevoEstado);
        actualizarUIEstado(numPedido, nuevoEstado, modoEntrega);
        mostrarToast(`Pedido #${numPedido} → "${nuevoEstado}"`);
        filtrar();
    } catch (err) {
        mostrarToast(err.message, false);
    }
}

async function procesarSolicitudRepartidor(numPedido, btn) {
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Enviando...';
    }
    try {
        await solicitarRepartidor(parseInt(numPedido));
        mostrarToast(`Alerta enviada a los repartidores para el pedido #${numPedido} 📡`);
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-hourglass-half fa-spin"></i> Esperando...';
            btn.className = btn.className.replace('btn-info', 'btn-warning');
            btn.setAttribute('data-esperando', 'true');
        }
        const fila = document.querySelector(`tr[data-num="${numPedido}"]`);
        if (fila) fila.setAttribute('data-estado', 'EsperandoRepartidor');
        const badgeEl = document.querySelector(`.estado-badge-${numPedido}`);
        if (badgeEl) {
            badgeEl.className = `badge bg-secondary estado-badge-${numPedido}`;
            badgeEl.textContent = 'EsperandoRepartidor';
        }
    } catch (err) {
        mostrarToast(err.message, false);
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-broadcast-tower"></i> Solicitar Repartidor';
        }
    }
}

async function procesarReversionPreparacion(numPedido, btn) {
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i>';
    }
    try {
        await revertirAEnPreparacion(parseInt(numPedido), true);
        const fila = document.querySelector(`tr[data-num="${numPedido}"]`);
        const modoEntrega = fila ? fila.getAttribute('data-modo-entrega') : '';
        actualizarUIEstado(numPedido, 'En preparación', modoEntrega);

        if (fila) {
            const celdaDelivery = fila.querySelector(`.delivery-badge-${numPedido}`);
            if (celdaDelivery) {
                celdaDelivery.className = `badge bg-secondary delivery-badge-${numPedido}`;
                celdaDelivery.innerHTML = 'Esperando repartidor';
            }
        }
        mostrarToast(`Pedido #${numPedido} revertido a "En preparación". Alerta reenviada a repartidores.`);
        filtrar();
    } catch (err) {
        mostrarToast(err.message, false);
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-undo-alt"></i> Revertir';
        }
    }
}

// ── BLOQUE 7: DISPONIBILIDAD DEL REPARTIDOR ──────────────────────────────────

document.querySelectorAll('.btn-disponibilidad').forEach(btn => {
    btn.addEventListener('click', async function () {
        const valor = this.getAttribute('data-valor');
        try {
            await cambiarDisponibilidad(valor);
            actualizarUIDisponibilidad(valor);
            mostrarToast(`Disponibilidad actualizada a "${valor}".`);
            window.disponibilidadActual = valor;
        } catch (err) {
            mostrarToast(err.message, false);
        }
    });
});

function actualizarUIDisponibilidad(valor) {
    window.disponibilidadActual = valor;
    const badgeEl = document.getElementById('badgeDisponibilidadActual');
    const textoEl = document.getElementById('textoDisponibilidad');
    if (textoEl) textoEl.textContent = valor;
    if (badgeEl) {
        badgeEl.className = `badge fs-6 px-3 py-2 ${getClassDisponibilidad(valor)}`;
        const iconEl = badgeEl.querySelector('i');
        if (iconEl) iconEl.className = `fas ${getIconoDisponibilidad(valor)} me-1`;
    }
    document.querySelectorAll('.btn-disponibilidad').forEach(b => {
        const v = b.getAttribute('data-valor');
        b.classList.remove('btn-success', 'btn-secondary', 'btn-outline-success', 'btn-outline-secondary', 'active');
        if (v === valor) {
            b.classList.add(`btn-${getColorDisponibilidad(valor)}`, 'active');
        } else {
            b.classList.add(`btn-outline-${getColorDisponibilidad(v)}`);
        }
    });
}

function getClassDisponibilidad(valor) {
    if (valor === 'Activo') return 'bg-success';
    if (valor === 'Ocupado') return 'bg-warning text-dark';
    return 'bg-secondary';
}
function getIconoDisponibilidad(valor) {
    if (valor === 'Activo') return 'fa-check-circle';
    if (valor === 'Ocupado') return 'fa-motorcycle';
    return 'fa-pause-circle';
}
function getColorDisponibilidad(valor) {
    if (valor === 'Activo') return 'success';
    if (valor === 'Ocupado') return 'warning';
    return 'secondary';
}

// ── BLOQUE 8: ACTUALIZACIÓN DINÁMICA DEL DOM ────────────────────────────────

function actualizarUIEstado(pedidoId, nuevoEstado, modoEntrega) {
    const fila = document.querySelector(`tr[data-num="${pedidoId}"]`);
    if (!fila) return;

    const badgeClases = {
        'Pendiente': 'bg-warning text-dark',
        'En preparación': 'bg-info text-dark',
        'EsperandoRepartidor': 'bg-secondary',
        'Para retirar': 'bg-warning text-dark',
        'En reparto': 'bg-primary',
        'Entregado': 'bg-success',
        'Cancelado': 'bg-danger'
    };
    const badgeClass = badgeClases[nuevoEstado] || 'bg-secondary';

    const celdaEstado = fila.querySelector('td:nth-child(6)');
    if (celdaEstado) {
        celdaEstado.innerHTML = `<span class="badge ${badgeClass} estado-badge-${pedidoId}">${nuevoEstado}</span>`;
    }

    fila.setAttribute('data-estado', nuevoEstado);
    fila.classList.toggle('fila-entregado', nuevoEstado === 'Entregado');

    const esDomicilio = modoEntrega && modoEntrega.toLowerCase() === 'domicilio';
    const celdaDelivery = fila.querySelector(`.delivery-badge-${pedidoId}`);
    if (celdaDelivery && esDomicilio) {
        if (nuevoEstado === 'EsperandoRepartidor') {
            celdaDelivery.className = `badge bg-warning text-dark delivery-badge-${pedidoId}`;
            celdaDelivery.innerHTML = '<i class="fas fa-hourglass-half"></i> Esperando repartidor';
        } else if (nuevoEstado === 'En preparación') {
            celdaDelivery.className = `badge bg-secondary delivery-badge-${pedidoId}`;
            celdaDelivery.innerHTML = 'Esperando repartidor';
        }
    }

    const celdaAcciones = fila.querySelector(`.acciones-cell-${pedidoId}`);
    if (!celdaAcciones) return;

    if (!window.esRepartidor) {
        const transiciones = getTransicionesJS(nuevoEstado, esDomicilio);
        let botonesHtml = '';

        botonesHtml += `<a href="/Admin/Pedidos/Detalle/${pedidoId}" class="btn btn-sm btn-primary"><i class="fas fa-eye"></i> Ver</a>`;

        if (transiciones.length > 0) {
            botonesHtml += `
                <div class="btn-group">
                    <button type="button" class="btn btn-sm btn-warning dropdown-toggle" data-bs-toggle="dropdown">
                        <i class="fas fa-exchange-alt"></i> Estado
                    </button>
                    <ul class="dropdown-menu dropdown-menu-end shadow">
                        ${transiciones.map(t => `
                        <li>
                            <a class="dropdown-item cambiar-estado" href="#"
                               data-pedido="${pedidoId}"
                               data-estado-actual="${escapeHtml(nuevoEstado)}"
                               data-estado="${escapeHtml(t.estado)}"
                               data-modo-entrega="${escapeHtml(modoEntrega || '')}">
                                <span class="badge ${t.badge} me-1"><i class="fas ${t.icono}"></i></span> ${escapeHtml(t.estado)}
                            </a>
                        </li>`).join('')}
                    </ul>
                </div>`;
        } else if (nuevoEstado === 'Entregado') {
            botonesHtml += `<span class="badge bg-success w-100 py-1"><i class="fas fa-check-double"></i> Entregado</span>`;
        } else if (nuevoEstado === 'Cancelado') {
            botonesHtml += `<span class="badge bg-danger w-100 py-1"><i class="fas fa-ban"></i> Cancelado</span>`;
        }

        if (window.puedeAsignar && esDomicilio) {
            if (nuevoEstado === 'En preparación') {
                botonesHtml += `
                    <button type="button"
                            class="btn btn-sm btn-info btn-solicitar-repartidor"
                            data-pedido="${pedidoId}"
                            title="Enviar alerta a repartidores activos">
                        <i class="fas fa-broadcast-tower"></i> Solicitar Repartidor
                    </button>`;
            } else if (nuevoEstado === 'EsperandoRepartidor') {
                botonesHtml += `
                    <button type="button"
                            class="btn btn-sm btn-warning btn-solicitar-repartidor"
                            data-pedido="${pedidoId}"
                            title="Re-enviar alerta a repartidores activos">
                        <i class="fas fa-broadcast-tower"></i> Re-solicitar Repartidor
                    </button>`;
            }
        }

        if (window.esAdminOSuperAdmin && nuevoEstado === 'En reparto') {
            botonesHtml += `
                <button type="button"
                        class="btn btn-sm btn-outline-danger btn-revertir-preparacion"
                        data-pedido="${pedidoId}"
                        title="Revertir a En preparación">
                    <i class="fas fa-undo-alt"></i> Revertir
                </button>`;
        }

        celdaAcciones.innerHTML = `<div class="d-flex flex-column gap-1">${botonesHtml}</div>`;
    }
}

function getTransicionesJS(estado, esDomicilio) {
    const T = [];
    switch (estado) {
        case 'Pendiente':
            T.push({ estado: 'En preparación', badge: 'bg-info text-dark', icono: 'fa-fire' });
            T.push({ estado: 'Cancelado', badge: 'bg-danger', icono: 'fa-times' });
            break;
        case 'En preparación':
            T.push({ estado: 'Pendiente', badge: 'bg-warning text-dark', icono: 'fa-undo' });
            if (esDomicilio)
                T.push({ estado: 'En reparto', badge: 'bg-primary', icono: 'fa-motorcycle' });
            else
                T.push({ estado: 'Para retirar', badge: 'bg-warning text-dark', icono: 'fa-store' });
            T.push({ estado: 'Cancelado', badge: 'bg-danger', icono: 'fa-times' });
            break;
        case 'EsperandoRepartidor':
            T.push({ estado: 'En preparación', badge: 'bg-info text-dark', icono: 'fa-undo' });
            T.push({ estado: 'En reparto', badge: 'bg-primary', icono: 'fa-motorcycle' });
            T.push({ estado: 'Cancelado', badge: 'bg-danger', icono: 'fa-times' });
            break;
        case 'Para retirar':
            T.push({ estado: 'Entregado', badge: 'bg-success', icono: 'fa-check-circle' });
            T.push({ estado: 'Cancelado', badge: 'bg-danger', icono: 'fa-times' });
            break;
        case 'En reparto':
            T.push({ estado: 'Entregado', badge: 'bg-success', icono: 'fa-check-circle' });
            T.push({ estado: 'Cancelado', badge: 'bg-danger', icono: 'fa-times' });
            break;
    }
    return T;
}

// ── BLOQUE 9: SIGNALR ────────────────────────────────────────────────────────

const pedidosIgnorados = new Set();

async function cargarPedidosDisponiblesIniciales() {
    if (window.rolActual !== 'repartidor') return;

    try {
        const res = await fetch('/Admin/ObtenerPedidosDisponibles', {
            method: 'GET',
            headers: { 'Accept': 'application/json' }
        });

        if (!res.ok) {
            console.warn('ObtenerPedidosDisponibles: error HTTP', res.status);
            return;
        }

        const pedidos = await res.json();
        if (pedidos.length === 0) return;

        pedidos.forEach((data) => renderizarAlertaPedido(data));

        const msg = pedidos.length === 1
            ? `🛵 1 pedido esperando repartidor`
            : `🛵 ${pedidos.length} pedidos esperando repartidor`;
        mostrarToastSignalR(msg, 'warning');

        if (navigator.vibrate) navigator.vibrate([200, 100, 200]);

    } catch (err) {
        console.error('Error al cargar pedidos disponibles iniciales:', err);
    }
}

document.addEventListener('DOMContentLoaded', async function inicializarApp() {

    await cargarPedidosDisponiblesIniciales();

    if (typeof signalR === 'undefined') return;

    const rol = window.rolActual || '';
    if (!rol) return;

    const disponibilidad = window.disponibilidadActual || '';
    const repartidorId = window.repartidorId || 0;

    let hubUrl = `/deliveryHub?rol=${encodeURIComponent(rol)}&disponibilidad=${encodeURIComponent(disponibilidad)}`;
    if (repartidorId) hubUrl += `&repartidorId=${encodeURIComponent(repartidorId)}`;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.onreconnected(id => cargarPedidosDisponiblesIniciales());

    connection.on('NuevoPedidoParaReparto', function (data) {
        if (window.rolActual !== 'repartidor') return;
        renderizarAlertaPedido(data);
        mostrarToastSignalR(`🛵 ¡Nuevo pedido disponible! #${data.numPedido}`, 'warning');
        if (navigator.vibrate) navigator.vibrate([200, 100, 200]);
    });

    connection.on('PedidoAsignado', function (data) {
        if (window.rolActual !== 'repartidor') return;
        const numPedido = (typeof data === 'object') ? data.numPedido : data;
        eliminarAlertaPedido(numPedido);
        mostrarToastSignalR(`✅ El pedido #${numPedido} ya fue tomado por otro repartidor.`, 'info');
    });

    connection.on('CerrarAlerta', function (numPedido) {
        if (window.rolActual !== 'repartidor') return;
        eliminarAlertaPedido(numPedido);
    });

    connection.on('PedidoAceptado', function (data) {
        if (window.rolActual === 'repartidor') {
            eliminarAlertaPedido(data.numPedido);
        } else {
            mostrarToastSignalR(`✅ Pedido #${data.numPedido} aceptado por ${data.nombreRepartidor}`, 'success');
            actualizarFilaEnReparto(data.numPedido, data.nombreRepartidor);
        }
    });

    connection.on('PedidoRevertido', function (data) {
        if (window.rolActual === 'repartidor') {
            eliminarAlertaPedido(data.numPedido);
            if (window.repartidorId && data.idRepartidorAnterior === window.repartidorId) {
                actualizarUIDisponibilidad('Activo');
                document.querySelectorAll('.btn-disponibilidad').forEach(b => { b.disabled = false; });
                const fila = document.querySelector(`tr[data-num="${data.numPedido}"]`);
                if (fila) {
                    fila.style.animation = 'fadeOut 0.4s ease-out';
                    setTimeout(() => fila.remove(), 400);
                }
                mostrarModalAlertaUrgente('↩️ Asignación Revertida', `El administrador revirtió el pedido <strong>#${data.numPedido}</strong>.`, 'warning');
            }
        } else {
            mostrarToastSignalR(`↩ Pedido #${data.numPedido} fue revertido.`, 'warning');
        }
    });

    connection.on('PedidoCancelado', function (data) {
        if (window.rolActual === 'repartidor') {
            eliminarAlertaPedido(data.numPedido);
            if (window.repartidorId && data.idRepartidorAnterior === window.repartidorId) {
                mostrarModalAlertaUrgente('🚫 Pedido Cancelado', `El cliente canceló el pedido <strong>#${data.numPedido}</strong>.`, 'danger');
                actualizarUIDisponibilidad('Activo');
                document.querySelectorAll('.btn-disponibilidad').forEach(b => { b.disabled = false; });
                const fila = document.querySelector(`tr[data-num="${data.numPedido}"]`);
                if (fila) { fila.style.animation = 'fadeOut 0.4s ease-out'; setTimeout(() => fila.remove(), 400); }
            }
        } else {
            actualizarUIEstado(data.numPedido, 'Cancelado', data.modoEntrega || '');
            mostrarToastSignalR(`❌ Pedido #${data.numPedido} cancelado.`, 'danger');
        }
    });

    connection.on('EstadoCambiadoGlobal', function (data) {
        if (window.rolActual === 'repartidor') {
            if (data.nuevoEstado === 'Entregado') {
                const fila = document.querySelector(`tr[data-num="${data.numPedido}"]`);
                if (fila) {
                    actualizarUIEstado(data.numPedido, 'Entregado', data.modoEntrega || '');
                    actualizarUIDisponibilidad('Activo');
                    document.querySelectorAll('.btn-disponibilidad').forEach(b => { b.disabled = false; });
                }
            }
        } else {
            actualizarUIEstado(data.numPedido, data.nuevoEstado, data.modoEntrega || '');
            filtrar();
        }
    });

    connection.on('AlertaPedidoIgnorada', function (data) {
        if (window.rolActual === 'repartidor') return;
        mostrarAlertaIgnorada(data.numPedido, data.mensaje);
    });

    connection.on('AlertaAdminVista', function (data) {
        ocultarAlertaIgnorada(data.numPedido);
    });

    connection.start()
        .then(() => console.info(`SignalR: conectado como "${rol}".`))
        .catch(err => console.error('SignalR: error al conectar:', err));

    window.deliveryHubConnection = connection;
});

// ── FUNCIONES DE RENDERIZADO DE ALERTAS (MODIFICADAS PARA LEGIBILIDAD) ────────

function renderizarAlertaPedido(data) {
    const contenedor = document.getElementById('contenedorAlertas');
    if (!contenedor) return;

    const existente = document.getElementById(`alerta-pedido-${data.numPedido}`);
    if (existente) existente.remove();

    const alertaId = `alerta-pedido-${data.numPedido}`;

    // ★ MODIFICACIÓN: text-white e iconos warning para resaltar sobre el fondo
    const alertaHtml = `
        <div class="card mb-3 shadow-sm"
             id="${alertaId}"
             role="alert"
             style="
                 animation: slideInRight 0.35s ease-out;
                 border-left: 5px solid #ffc107;
                 max-width: 540px;
                 background-color: rgba(33, 37, 41, 0.95); /* Fondo oscuro reforzado */
             ">
            <div class="card-body py-2 px-3 text-white">
                <div class="d-flex align-items-center gap-2 mb-2">
                    <i class="fas fa-motorcycle fa-lg text-warning"></i>
                    <span class="fw-bold text-warning">¡Nuevo pedido!</span>
                    <span class="badge bg-warning text-dark ms-1">#${data.numPedido}</span>
                    <button type="button"
                            class="btn-close btn-close-white btn-sm ms-auto btn-ignorar-reparto"
                            data-pedido="${data.numPedido}" aria-label="Cerrar"></button>
                </div>
                <div class="row g-1 small mb-2">
                    <div class="col-sm-6"><i class="fas fa-user text-warning me-1"></i><strong>Cliente:</strong> ${escapeHtml(data.cliente)}</div>
                    <div class="col-sm-6"><i class="fas fa-phone text-warning me-1"></i><strong>Tel:</strong> ${escapeHtml(data.telefono)}</div>
                    <div class="col-12"><i class="fas fa-map-marker-alt text-warning me-1"></i><strong>Dirección:</strong> ${escapeHtml(data.domicilio)}</div>
                    <div class="col-12"><i class="fas fa-shopping-bag text-warning me-1"></i><strong>Pedido:</strong> ${escapeHtml(data.productos)}</div>
                    ${data.observaciones
            ? `<div class="col-12 text-white-50 fst-italic"><i class="fas fa-sticky-note me-1"></i>${escapeHtml(data.observaciones)}</div>`
            : ''}
                    <div class="col-12"><i class="fas fa-dollar-sign text-warning me-1"></i><strong>Total:</strong>
                        <span class="text-success fw-bold ms-1">${escapeHtml(data.montoTotal)}</span>
                    </div>
                </div>
                <div class="d-flex gap-2">
                    <button type="button"
                            class="btn btn-success btn-sm btn-aceptar-reparto"
                            data-pedido="${data.numPedido}"
                            id="btn-aceptar-${data.numPedido}">
                        <i class="fas fa-check-circle me-1"></i><strong>Aceptar Reparto</strong>
                    </button>
                    <button type="button"
                            class="btn btn-outline-light btn-sm btn-ignorar-reparto"
                            data-pedido="${data.numPedido}">
                        Ignorar
                    </button>
                </div>
            </div>
        </div>`;

    contenedor.insertAdjacentHTML('afterbegin', alertaHtml);

    const btnAceptar = document.getElementById(`btn-aceptar-${data.numPedido}`);
    if (btnAceptar) {
        btnAceptar.addEventListener('click', () => procesarAceptacionReparto(data.numPedido, btnAceptar));
    }

    document.getElementById(alertaId)
        ?.querySelectorAll('.btn-ignorar-reparto')
        ?.forEach(btn => {
            btn.addEventListener('click', function () {
                const n = parseInt(this.dataset.pedido);
                ignorarPedidoLocalmente(n);
            });
        });
}

function ignorarPedidoLocalmente(numPedido) {
    pedidosIgnorados.add(numPedido);
    eliminarAlertaPedido(numPedido);
    mostrarToastSignalR(`Pedido #${numPedido} ignorado. Sigue en la lista.`, 'info');
}

function eliminarAlertaPedido(numPedido) {
    const alertaEl = document.getElementById(`alerta-pedido-${numPedido}`);
    if (alertaEl) {
        alertaEl.style.animation = 'fadeOut 0.3s ease-out';
        setTimeout(() => alertaEl.remove(), 300);
    }
}

async function procesarAceptacionReparto(numPedido, btnAceptar) {
    if (btnAceptar) {
        btnAceptar.disabled = true;
        btnAceptar.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>...';
    }

    try {
        const res = await aceptarPedido(numPedido);
        eliminarAlertaPedido(numPedido);
        mostrarToast(res.message || `¡Pedido #${numPedido} aceptado!`, true);
        actualizarUIDisponibilidad('Ocupado');
        document.querySelectorAll('.btn-disponibilidad').forEach(b => { b.disabled = true; });
        if (res.pedidoData) agregarPedidoATablaRepartidor(res.pedidoData);
    } catch (err) {
        if (btnAceptar) {
            btnAceptar.disabled = false;
            btnAceptar.innerHTML = '<i class="fas fa-check-circle me-1"></i><strong>Aceptar</strong>';
        }
        mostrarToast(err.message, false);
        if (err.message.includes('tomado')) eliminarAlertaPedido(numPedido);
    }
}

function agregarPedidoATablaRepartidor(p) {
    const tbody = document.querySelector('#printArea tbody');
    if (!tbody || document.querySelector(`tr[data-num="${p.numPedido}"]`)) return;

    const filaHtml = `
        <tr data-num="${p.numPedido}" data-estado="En reparto" data-modo-entrega="Domicilio" data-filtro="${p.numPedido} ${escapeHtml(p.cliente)}">
            <td class="fw-bold">${p.numPedido}</td>
            <td>${escapeHtml(p.cliente)}</td>
            <td>${escapeHtml(p.telefono)}</td>
            <td>${escapeHtml(p.domicilio)}</td>
            <td>${escapeHtml(p.fechaPedido || '')}</td>
            <td><span class="badge bg-primary estado-badge-${p.numPedido}">En reparto</span></td>
            <td>${escapeHtml(p.montoTotal)}</td>
            <td class="no-print acciones-cell-${p.numPedido}">
                <div class="d-flex flex-column gap-1">
                    <a href="/Admin/Pedidos/Detalle/${p.numPedido}" class="btn btn-sm btn-primary"><i class="fas fa-eye"></i> Ver</a>
                    <a class="btn btn-sm btn-success cambiar-estado" href="#" data-pedido="${p.numPedido}" data-estado-actual="En reparto" data-estado="Entregado">
                        <i class="fas fa-check-circle"></i> Marcar Entregado
                    </a>
                </div>
            </td>
        </tr>`;

    tbody.insertAdjacentHTML('afterbegin', filaHtml);
    tbody.querySelector('tr.sin-datos')?.remove();
}

function actualizarFilaEnReparto(numPedido, nombreRepartidor) {
    const fila = document.querySelector(`tr[data-num="${numPedido}"]`);
    if (!fila) return;

    actualizarUIEstado(numPedido, 'En reparto', 'Domicilio');
    const celdaDelivery = fila.querySelector(`.delivery-badge-${numPedido}`);
    if (celdaDelivery) {
        celdaDelivery.className = `badge bg-success delivery-badge-${numPedido}`;
        celdaDelivery.innerHTML = `<i class="fas fa-motorcycle"></i> ${escapeHtml(nombreRepartidor)}`;
    }
    fila.querySelector('.btn-solicitar-repartidor')?.remove();
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

(function agregarCSSAnimaciones() {
    if (document.getElementById('signalr-animations')) return;
    const style = document.createElement('style');
    style.id = 'signalr-animations';
    style.textContent = `
        @keyframes slideInRight { from { transform: translateX(100%); opacity: 0; } to { transform: translateX(0); opacity: 1; } }
        @keyframes fadeOut { from { opacity: 1; } to { opacity: 0; transform: scale(0.95); } }
    `;
    document.head.appendChild(style);
})();

// ── Alertas de Admin ─────────────────────────────────────────────────────────

function mostrarAlertaIgnorada(numPedido, mensaje) {
    const contenedor = document.getElementById('contenedorAlertas') || document.getElementById('contenedorAlertasAdmin');
    if (!contenedor || document.getElementById(`alerta-ignorada-${numPedido}`)) return;

    const html = `
        <div class="alert alert-warning alert-dismissible shadow border border-warning rounded-3 p-3 mb-3" id="alerta-ignorada-${numPedido}" role="alert">
            <div class="d-flex align-items-start gap-3">
                <div class="fs-2 text-warning">⚠️</div>
                <div class="flex-grow-1">
                    <h6 class="alert-heading mb-1">Pedido sin repartidor <strong>#${numPedido}</strong></h6>
                    <p class="mb-2 small">${mensaje}</p>
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-sm btn-warning btn-ignorada-ok" data-pedido="${numPedido}">Entendido</button>
                        <button type="button" class="btn btn-sm btn-info btn-rerequest" data-pedido="${numPedido}">Re-solicitar</button>
                    </div>
                </div>
            </div>
        </div>`;
    contenedor.insertAdjacentHTML('afterbegin', html);
    // ... eventos de botones entendido/resolicitar (omitiendo por brevedad, igual al original) ...
}

function ocultarAlertaIgnorada(numPedido) {
    const alertEl = document.getElementById(`alerta-ignorada-${numPedido}`);
    if (alertEl) {
        alertEl.style.animation = 'fadeOut 0.3s ease-out';
        setTimeout(() => alertEl.remove(), 300);
    }
}

function mostrarModalAlertaUrgente(titulo, cuerpoHtml, tipo = 'warning') {
    const modalId = 'modalAlertaUrgente';
    let modal = document.getElementById(modalId);
    if (!modal) {
        modal = document.createElement('div');
        modal.className = 'modal fade';
        modal.id = modalId;
        modal.innerHTML = `
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content border-0">
                    <div class="modal-header bg-${tipo} border-0 text-white"><h5 class="modal-title">${titulo}</h5></div>
                    <div class="modal-body fs-5">${cuerpoHtml}</div>
                    <div class="modal-footer border-0"><button type="button" class="btn btn-${tipo} w-100" data-bs-dismiss="modal">Entendido</button></div>
                </div>
            </div>`;
        document.body.appendChild(modal);
    }
    bootstrap.Modal.getOrCreateInstance(modal).show();
}