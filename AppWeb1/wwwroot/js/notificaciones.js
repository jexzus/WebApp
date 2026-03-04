/**
 * ============================================================
 * ARCHIVO: wwwroot/js/notificaciones.js
 * Sistema unificado de notificaciones - Jam Burgers
 * ============================================================
 * Expone una función global: mostrarNotificacion(tipo, mensaje, opciones)
 *
 * TIPOS disponibles:
 *   'success'  → toast verde, desaparece en 4s (auto-dismiss)
 *   'error'    → toast rojo, requiere cierre manual
 *   'warning'  → toast amarillo, requiere cierre manual
 *   'info'     → toast azul, desaparece en 4s (auto-dismiss)
 *   'bienvenida' → Swal especial con timer 5s
 *   'logout'   → Swal especial con timer 5s
 *
 * OPCIONES extra (objeto, todas opcionales):
 *   titulo     → sobreescribe el título del Swal
 *   timer      → sobreescribe el tiempo de auto-cierre (ms)
 *   requiresInteraction → true fuerza clic para cerrar en éxito
 * ============================================================
 */

;(function (window) {
    'use strict';

    // ── Paleta de colores de marca ────────────────────────────────────────────
    const BRAND = {
        bg:          '#fff3cd',   // fondo amarillo suave (idéntico al toast de pedidos)
        color:       '#1a1a1a',
        border:      '#ffc107',
        success:     '#198754',
        error:       '#dc3545',
        warning:     '#ffc107',
        info:        '#0dcaf0',
        confirmBtn:  '#f83600',   // gradiente de marca
        cancelBtn:   '#6c757d',
    };

    // ── Configuración base compartida para todos los Swal ────────────────────
    const SWAL_BASE = {
        background:        BRAND.bg,
        color:             BRAND.color,
        confirmButtonColor: BRAND.confirmBtn,
        customClass: {
            title:   'fw-bold fs-5',
            popup:   'border border-warning shadow-sm rounded-4',
            confirmButton: 'btn btn-sm px-4',
        },
        buttonsStyling: true,
    };

    // ── Helpers internos ──────────────────────────────────────────────────────

    /**
     * Escapa HTML para evitar XSS al inyectar strings de TempData en Swal.
     */
    function _esc(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /**
     * Devuelve el elemento <div id="toastContainer"> del Layout.
     * Si no existe, lo crea en el body para no romper en páginas sin layout.
     */
    function _getOrCreateToastContainer() {
        let el = document.getElementById('jamToastContainer');
        if (!el) {
            el = document.createElement('div');
            el.id = 'jamToastContainer';
            el.className = 'toast-container position-fixed top-0 end-0 p-3';
            el.style.zIndex = '1090';
            document.body.appendChild(el);
        }
        return el;
    }

    /**
     * Crea y muestra un Bootstrap Toast.
     * @param {string} mensaje
     * @param {'success'|'error'|'warning'|'info'} tipo
     * @param {boolean} autoDismiss  - si es false, el usuario debe cerrarlo
     */
    function _mostrarToast(mensaje, tipo, autoDismiss) {
        if (!window.bootstrap || !window.bootstrap.Toast) {
            // Fallback si Bootstrap JS no cargó aún
            console.warn('[JAM] Bootstrap no disponible. Mensaje:', mensaje);
            return;
        }

        const bgMap = {
            success: 'text-bg-success',
            error:   'text-bg-danger',
            warning: 'text-bg-warning text-dark',
            info:    'text-bg-info text-dark',
        };
        const iconMap = {
            success: '✔',
            error:   '✖',
            warning: '⚠',
            info:    'ℹ',
        };

        const id      = 'jamToast_' + Date.now();
        const bgClass = bgMap[tipo] || 'text-bg-secondary';
        const icon    = iconMap[tipo] || '';
        const delay   = autoDismiss ? 4500 : 0;

        const html = `
        <div id="${id}" class="toast align-items-center border-0 ${bgClass} mb-2"
             role="alert" aria-live="assertive" aria-atomic="true">
            <div class="d-flex">
                <div class="toast-body fw-semibold">
                    <span class="me-1">${icon}</span>${_esc(mensaje)}
                </div>
                <button type="button"
                        class="btn-close ${tipo === 'success' ? 'btn-close-white' : ''} me-2 m-auto"
                        data-bs-dismiss="toast" aria-label="Cerrar">
                </button>
            </div>
        </div>`;

        const container = _getOrCreateToastContainer();
        container.insertAdjacentHTML('beforeend', html);
        const toastEl = document.getElementById(id);
        const toast   = new bootstrap.Toast(toastEl, { delay, autohide: autoDismiss });
        toast.show();

        // Limpieza tras ocultar
        toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /**
     * mostrarNotificacion(tipo, mensaje, opciones?)
     *
     * Es la función central. Todas las vistas y el layout la llaman.
     *
     * @param {'success'|'error'|'warning'|'info'|'bienvenida'|'logout'} tipo
     * @param {string} mensaje
     * @param {object} [opciones]
     * @param {string}  [opciones.titulo]
     * @param {number}  [opciones.timer]
     * @param {boolean} [opciones.requiresInteraction]
     */
    window.mostrarNotificacion = function (tipo, mensaje, opciones) {
        if (!mensaje) return;
        opciones = opciones || {};

        // ── Casos especiales con Swal ────────────────────────────────────────

        if (tipo === 'bienvenida') {
            if (typeof Swal === 'undefined') {
                _mostrarToast(mensaje, 'success', true);
                return;
            }
            Swal.fire(Object.assign({}, SWAL_BASE, {
                icon:              'success',
                title:             opciones.titulo || '¡Bienvenido!',
                text:              mensaje,
                timer:             opciones.timer  || 5000,
                timerProgressBar:  true,
                showConfirmButton: false,
            }));
            return;
        }

        if (tipo === 'logout') {
            if (typeof Swal === 'undefined') {
                _mostrarToast(mensaje, 'info', true);
                return;
            }
            Swal.fire(Object.assign({}, SWAL_BASE, {
                icon:              'info',
                title:             opciones.titulo || 'Sesión cerrada',
                text:              mensaje,
                timer:             opciones.timer  || 5000,
                timerProgressBar:  true,
                showConfirmButton: false,
            }));
            return;
        }

        // ── Toast estándar para todo lo demás ────────────────────────────────
        // success  → auto-dismiss
        // error    → manual
        // warning  → manual
        // info     → auto-dismiss

        const autoDismiss = opciones.requiresInteraction
            ? false
            : (tipo === 'success' || tipo === 'info');

        _mostrarToast(mensaje, tipo, autoDismiss);
    };

    // ── Retrocompatibilidad: re-exportar mostrarToast de pedidos.js ──────────
    // pedidos.js define su propia mostrarToast(mensaje, esExito).
    // Aquí NO la sobreescribimos; la complementamos solo si NO existe.
    if (typeof window.mostrarToast === 'undefined') {
        window.mostrarToast = function (mensaje, esExito) {
            mostrarNotificacion(esExito === false ? 'error' : 'success', mensaje);
        };
    }

}(window));
