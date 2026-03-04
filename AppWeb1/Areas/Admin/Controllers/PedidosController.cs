// ============================================================
// ARCHIVO: Areas/Admin/Controllers/PedidosController.cs
// ============================================================
// CAMBIOS vs versión anterior:
//   ► ValidarTransicion    → agrega "Para retirar" como estado válido
//   ► Index                → soporte paginación historial repartidor (página query param)
//   ► HistorialRepartidor  → NUEVO endpoint paginado (JSON) para carga dinámica (opcional)
//   ► RevertirAEnPreparacion → NUEVO: solo Admin/SuperAdmin. Desasigna repartidor + SignalR
//   ► CambiarEstadoAjax    → retorna nuevos estados en transiciones (incluye "Para retirar")
//   ► CambiarEstado        → ídem form post
//   ► DTOs                 → RevertirPedidoRequest nuevo
// ============================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using OfficeOpenXml;
using AppWeb1.Models;
using AppWeb1.Data;
using AppWeb1.Hubs;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using static AppWeb1.Security.Roles;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AppWeb1.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PedidosController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<PedidosController> _logger;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public PedidosController(
            DeliveryDBContext context,
            ILogger<PedidosController> logger,
            IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
        }

        // ── Helpers internos ─────────────────────────────────────────────

        private bool PuedeGestionarPedidos() =>
            AppWeb1.Security.Roles.PuedeGestionarPedidos(HttpContext.Session);

        private bool PuedeAsignarRepartidor(ISession s) =>
            AppWeb1.Security.Roles.PuedeAsignarPedidos(s);

        private async Task<int?> GetCurrentRepartidorIdAsync()
        {
            if (!EsRepartidor(HttpContext.Session)) return null;
            var nombreUsuario = HttpContext.Session.GetString("Usuario");
            if (string.IsNullOrEmpty(nombreUsuario)) return null;
            var usuario = await _context.Usuarios.AsNoTracking()
                .FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);
            if (usuario == null) return null;
            var repartidor = await _context.Repartidores.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdUsuario == usuario.Id && r.Activo);
            return repartidor?.Id;
        }

        private async Task<Repartidor?> GetCurrentRepartidorAsync()
        {
            var nombreUsuario = HttpContext.Session.GetString("Usuario");
            if (string.IsNullOrEmpty(nombreUsuario)) return null;
            var usuario = await _context.Usuarios.AsNoTracking()
                .FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);
            if (usuario == null) return null;
            return await _context.Repartidores
                .FirstOrDefaultAsync(r => r.IdUsuario == usuario.Id && r.Activo);
        }

        // ── VALIDACIÓN DE TRANSICIONES EN CASCADA ────────────────────────
        // ★ NUEVO ESTADO "Para retirar": paso intermedio para pedidos de retiro en local
        // Flujo retiro: Pendiente → En preparación → Para retirar → Entregado
        // Flujo domicilio: Pendiente → En preparación → En reparto → Entregado

        private static (bool valida, string mensaje) ValidarTransicion(
            string estadoActual, string nuevoEstado, string? modoEntrega)
        {
            if (estadoActual == "Entregado" || estadoActual == "Cancelado")
                return (false, $"No se puede modificar un pedido en estado '{estadoActual}'.");

            bool esDomicilio = modoEntrega?.Equals("Domicilio", StringComparison.OrdinalIgnoreCase) == true;

            var transicionesValidas = new Dictionary<string, HashSet<string>>
            {
                ["Pendiente"] = new HashSet<string> { "En preparación", "Cancelado" },
                ["En preparación"] = esDomicilio
                    ? new HashSet<string> { "Pendiente", "En reparto", "EsperandoRepartidor", "Cancelado" }
                    // ★ Para retiro en local: En preparación → Para retirar (o directo Entregado si SuperAdmin)
                    : new HashSet<string> { "Pendiente", "Para retirar", "Cancelado" },
                // ★ EsperandoRepartidor: puede volver a "En preparación" (cancelar búsqueda manual)
                ["EsperandoRepartidor"] = new HashSet<string> { "En preparación", "Cancelado" },
                // ★ NUEVO: Para retirar solo para pedidos de retiro
                ["Para retirar"] = new HashSet<string> { "Entregado", "Cancelado" },
                ["En reparto"] = new HashSet<string> { "Entregado", "Cancelado" },
            };

            if (!transicionesValidas.TryGetValue(estadoActual, out var permitidos))
                return (false, $"Estado actual '{estadoActual}' no reconocido.");

            if (!permitidos.Contains(nuevoEstado))
            {
                if (nuevoEstado == "En reparto" && !esDomicilio)
                    return (false, "Los pedidos de retiro en local no pasan a 'En reparto'. Avanzalos a 'Para retirar'.");
                if (nuevoEstado == "Para retirar" && esDomicilio)
                    return (false, "Los pedidos a domicilio no usan el estado 'Para retirar'.");
                if (nuevoEstado == "Entregado" && estadoActual == "Pendiente")
                    return (false, "El pedido debe pasar primero por 'En preparación'.");
                return (false, $"Transición '{estadoActual}' → '{nuevoEstado}' no permitida.");
            }

            return (true, string.Empty);
        }

        // ── INDEX ─────────────────────────────────────────────────────────

        public async Task<IActionResult> Index(int pagina = 1)
        {
            if (!PuedeGestionarPedidos())
                return RedirectToAction("Login", "Usuario", new { area = "" });

            const int TamañoPagina = 10;

            try
            {
                IQueryable<Pedido> q = _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.Repartidor).ThenInclude(r => r.Usuario)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p => p.IdClienteNavigation != null);

                if (EsRepartidor(HttpContext.Session))
                {
                    var repartidorId = await GetCurrentRepartidorIdAsync();

                    if (repartidorId == null)
                    {
                        return View(new List<Pedido>());
                    }

                    // Pedidos activos: solo "En reparto" asignados a este repartidor
                    var pedidosActivos = await q
                        .Where(p => p.EstadoPedido == "En reparto" && p.IdRepartidor == repartidorId.Value)
                        .OrderByDescending(p => p.FechaPedido)
                        .ToListAsync();

                    // ★ HISTORIAL PAGINADO: pedidos completados (Entregado/Cancelado) de este repartidor
                    var historialQuery = _context.Pedidos
                        .Include(p => p.IdClienteNavigation)
                        .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                        .Where(p => p.IdRepartidor == repartidorId.Value
                                 && (p.EstadoPedido == "Entregado" || p.EstadoPedido == "Cancelado"))
                        .OrderByDescending(p => p.FechaPedido);

                    var totalHistorial = await historialQuery.CountAsync();
                    var totalPaginas = (int)Math.Ceiling(totalHistorial / (double)TamañoPagina);
                    if (pagina < 1) pagina = 1;
                    if (pagina > totalPaginas && totalPaginas > 0) pagina = totalPaginas;

                    var historial = await historialQuery
                        .Skip((pagina - 1) * TamañoPagina)
                        .Take(TamañoPagina)
                        .ToListAsync();

                    var rep = await GetCurrentRepartidorAsync();
                    ViewBag.DisponibilidadActual = rep?.Disponibilidad ?? "Inactivo";
                    ViewBag.RepartidorId = rep?.Id;
                    ViewBag.PuedeAsignarDelivery = false;
                    ViewBag.EsRepartidor = true;
                    ViewBag.EsAdminOSuperAdmin = false; // repartidor nunca es admin

                    // Paginación para la vista
                    ViewBag.Historial = historial;
                    ViewBag.PaginaActual = pagina;
                    ViewBag.TotalPaginas = totalPaginas;
                    ViewBag.TotalHistorial = totalHistorial;

                    return View(pedidosActivos);
                }

                var pedidosFiltrados = await q.OrderByDescending(p => p.FechaPedido).ToListAsync();

                ViewBag.PuedeAsignarDelivery = PuedeAsignarRepartidor(HttpContext.Session);
                ViewBag.EsRepartidor = false;
                ViewBag.EsAdminOSuperAdmin = EsAdminOSuperAdmin(HttpContext.Session);

                // Estado "EsperandoRepartidor" ahora está en la BD → no se necesita
                // pasar un HashSet separado. El Razor lee pedido.EstadoPedido directamente.
                return View(pedidosFiltrados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar los pedidos");
                TempData["Error"] = "Error al cargar los pedidos.";
                return View(new List<Pedido>());
            }
        }

        // ── DETALLE ───────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            if (!PuedeGestionarPedidos())
                return RedirectToAction("Login", "Usuario", new { area = "" });

            var pedido = await _context.Pedidos
                .Include(p => p.IdClienteNavigation)
                .Include(p => p.Repartidor).ThenInclude(r => r.Usuario)
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .FirstOrDefaultAsync(p => p.NumPedido == id);

            if (pedido == null)
            {
                TempData["Error"] = "El pedido no existe.";
                return RedirectToAction(nameof(Index));
            }

            if (EsRepartidor(HttpContext.Session))
            {
                var repartidorId = await GetCurrentRepartidorIdAsync();
                if (repartidorId == null || pedido.IdRepartidor != repartidorId.Value)
                {
                    TempData["Error"] = "No tiene permiso para ver este pedido.";
                    return RedirectToAction(nameof(Index));
                }
            }

            ViewBag.PuedeAsignarDelivery = PuedeAsignarRepartidor(HttpContext.Session);
            ViewBag.EsRepartidor = EsRepartidor(HttpContext.Session);

            return View(pedido);
        }

        // ── CAMBIAR ESTADO (POST form, desde Detalle) ─────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, string nuevoEstado)
        {
            if (!PuedeGestionarPedidos())
            {
                TempData["Error"] = "No tenés permiso.";
                return RedirectToAction("Login", "Usuario", new { area = "" });
            }

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null)
            {
                TempData["Error"] = "El pedido no existe.";
                return RedirectToAction(nameof(Index));
            }

            var estadosValidos = new[] { "Pendiente", "En preparación", "Para retirar", "En reparto", "Entregado", "Cancelado" };
            if (string.IsNullOrWhiteSpace(nuevoEstado) || !estadosValidos.Contains(nuevoEstado))
            {
                TempData["Error"] = "Estado no válido.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            if (EsRepartidor(HttpContext.Session))
            {
                if (nuevoEstado != "Entregado")
                {
                    TempData["Error"] = "Solo podés marcar el pedido como 'Entregado'.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }
                var repartidorId = await GetCurrentRepartidorIdAsync();
                if (repartidorId == null || pedido.IdRepartidor != repartidorId.Value)
                {
                    TempData["Error"] = "No tenés permiso para modificar este pedido.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if ((pedido.EstadoPedido == "Entregado" || pedido.EstadoPedido == "Cancelado")
                && !EsSuperAdmin(HttpContext.Session))
            {
                TempData["Error"] = $"No se puede modificar un pedido en estado '{pedido.EstadoPedido}'.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            if (!EsSuperAdmin(HttpContext.Session))
            {
                var (valida, mensaje) = ValidarTransicion(pedido.EstadoPedido, nuevoEstado, pedido.ModoEntrega);
                if (!valida)
                {
                    TempData["Error"] = mensaje;
                    return RedirectToAction(nameof(Detalle), new { id });
                }
            }

            try
            {
                pedido.EstadoPedido = nuevoEstado;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Estado actualizado con éxito.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del pedido {NumPedido}", id);
                TempData["Error"] = "No se pudo actualizar el estado. " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ── CAMBIAR ESTADO AJAX ───────────────────────────────────────────

        [HttpPost]
        [Route("/Admin/CambiarEstadoAjax")]
        public async Task<IActionResult> CambiarEstadoAjax([FromBody] CambiarEstadoRequest request)
        {
            if (!PuedeGestionarPedidos())
                return Json(new { success = false, message = "No autorizado." });

            try
            {
                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado." });

                var estadosValidos = new[] { "Pendiente", "En preparación", "EsperandoRepartidor", "Para retirar", "En reparto", "Entregado", "Cancelado" };
                if (string.IsNullOrWhiteSpace(request.NuevoEstado) || !estadosValidos.Contains(request.NuevoEstado))
                    return Json(new { success = false, message = "Estado no válido." });

                if (EsRepartidor(HttpContext.Session))
                {
                    if (request.NuevoEstado != "Entregado")
                        return Json(new { success = false, message = "Solo podés marcar el pedido como 'Entregado'." });
                    var repartidorId = await GetCurrentRepartidorIdAsync();
                    if (repartidorId == null || pedido.IdRepartidor != repartidorId.Value)
                        return Json(new { success = false, message = "No autorizado para modificar este pedido." });
                }

                if ((pedido.EstadoPedido == "Entregado" || pedido.EstadoPedido == "Cancelado")
                    && !EsSuperAdmin(HttpContext.Session))
                    return Json(new { success = false, message = $"No se puede modificar un pedido en estado '{pedido.EstadoPedido}'." });

                if (!EsSuperAdmin(HttpContext.Session))
                {
                    var (valida, mensaje) = ValidarTransicion(pedido.EstadoPedido, request.NuevoEstado, pedido.ModoEntrega);
                    if (!valida)
                        return Json(new { success = false, message = mensaje });
                }

                string estadoAnteriorA = pedido.EstadoPedido;
                string modoEntregaA = pedido.ModoEntrega ?? "";
                int idClienteA = pedido.IdCliente ?? 0;
                int? idRepartidorA = pedido.IdRepartidor;

                // ★ Si venimos de EsperandoRepartidor y el admin cambia manualmente
                // a otro estado, cancelar la búsqueda activa para evitar que un
                // repartidor acepte un pedido que ya no está listo.
                bool cancelarBusqueda = estadoAnteriorA == "EsperandoRepartidor"
                                     && request.NuevoEstado != "EsperandoRepartidor";

                pedido.EstadoPedido = request.NuevoEstado;
                if (request.NuevoEstado == "Entregado" && idRepartidorA != null)
                {
                    var rep = await _context.Repartidores.FirstOrDefaultAsync(r => r.Id == idRepartidorA.Value);
                    if (rep != null && rep.Disponibilidad == "Ocupado") rep.Disponibilidad = "Activo";
                }
                await _context.SaveChangesAsync();

                // ★ Cancelar búsqueda de repartidor si se cambió manualmente desde EsperandoRepartidor
                if (cancelarBusqueda)
                {
                    DeliveryHub.RemoverPedidoEnBusqueda(pedido.NumPedido);
                    await _hubContext.Clients.Group("Repartidores")
                        .SendAsync("CerrarAlerta", pedido.NumPedido);
                    _logger.LogInformation(
                        "Búsqueda de repartidor cancelada para pedido #{NumPedido} por cambio manual a '{Estado}'",
                        pedido.NumPedido, request.NuevoEstado);
                }

                await _hubContext.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = request.NuevoEstado,
                    estadoAnterior = estadoAnteriorA,
                    modoEntrega = modoEntregaA,
                    idRepartidor = idRepartidorA,
                    idCliente = idClienteA
                });
                // ★ Notificar al cliente específico por canal directo (grupo "Cliente_X")
                // para TODOS los estados, no solo Entregado.
                // Es el canal de respaldo si EstadoCambiadoGlobal no llega correctamente.
                if (idClienteA > 0)
                {
                    await _hubContext.Clients.Group($"Cliente_{idClienteA}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = request.NuevoEstado,
                        modoEntrega = modoEntregaA
                    });
                }
                return Json(new { success = true, message = "Estado actualizado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error AJAX al cambiar estado");
                return Json(new { success = false, message = "Error interno del servidor." });
            }
        }

        // ── ★ NUEVO: REVERTIR PEDIDO A "EN PREPARACIÓN" ───────────────────
        // Solo Admin y SuperAdmin. Desasigna el repartidor y vuelve a emitir SignalR
        // para que los repartidores vean el pedido nuevamente disponible.

        [HttpPost]
        [Route("/Admin/RevertirAEnPreparacionAjax")]
        public async Task<IActionResult> RevertirAEnPreparacionAjax([FromBody] RevertirPedidoRequest req)
        {
            // ★ Solo Admin/SuperAdmin. El Vendedor NO puede revertir.
            if (!EsAdminOSuperAdmin(HttpContext.Session))
                return Json(new { success = false, message = "Solo los Administradores pueden revertir un pedido a 'En preparación'." });

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado." });

                if (pedido.EstadoPedido != "En reparto" && pedido.EstadoPedido != "EsperandoRepartidor")
                    return Json(new { success = false, message = $"Solo se puede revertir un pedido en estado 'En reparto' o 'Esperando Repartidor'. El pedido está en '{pedido.EstadoPedido}'." });

                // Liberar al repartidor actual
                int? idRepartidorAnterior = pedido.IdRepartidor;
                if (idRepartidorAnterior != null)
                {
                    var repartidor = await _context.Repartidores
                        .FirstOrDefaultAsync(r => r.Id == idRepartidorAnterior.Value);
                    if (repartidor != null && repartidor.Disponibilidad == "Ocupado")
                        repartidor.Disponibilidad = "Activo";
                }

                // Desasignar repartidor y revertir estado
                pedido.IdRepartidor = null;
                pedido.EstadoPedido = "En preparación";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // ★ SignalR: notificar cambio global de estado
                await _hubContext.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "En preparación",
                    estadoAnterior = "En reparto",
                    modoEntrega = pedido.ModoEntrega ?? "Domicilio",
                    idRepartidor = (int?)null,
                    idCliente = pedido.IdCliente
                });
                // ★ SignalR: alerta específica al repartidor desasignado
                await _hubContext.Clients.All.SendAsync("PedidoRevertido", new
                {
                    numPedido = pedido.NumPedido,
                    idRepartidorAnterior = idRepartidorAnterior
                });

                // ★ SignalR: notificar al cliente que su pedido volvió a "En preparación"
                var idClienteRev = pedido.IdCliente ?? 0;
                if (idClienteRev > 0)
                {
                    await _hubContext.Clients.Group($"Cliente_{idClienteRev}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "En preparación",
                        modoEntrega = pedido.ModoEntrega ?? "Domicilio"
                    });
                }

                // ★ SignalR: si se pide reemisión de la alerta, emitir NuevoPedidoParaReparto
                if (req.ReemitirAlerta)
                {
                    var cliente = pedido.IdClienteNavigation;
                    var productos = pedido.DetallePedidos
                        .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                        .ToList();

                    var payload = new
                    {
                        numPedido = pedido.NumPedido,
                        cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                        domicilio = cliente?.Domicilio ?? "Sin domicilio",
                        telefono = cliente?.NumTelefono ?? "-",
                        montoTotal = pedido.MontoTotal?.ToString("C") ?? "$0,00",
                        productos = string.Join(", ", productos),
                        observaciones = pedido.Observaciones ?? ""
                    };

                    await _hubContext.Clients.Group("Repartidores")
                        .SendAsync("NuevoPedidoParaReparto", payload);
                }

                // ★ Sacar del tracking de búsqueda al revertir
                DeliveryHub.RemoverPedidoEnBusqueda(req.NumPedido);

                _logger.LogInformation(
                    "Pedido #{NumPedido} revertido a 'En preparación' por {Usuario}. RepartidorAnterior: #{IdRep}",
                    req.NumPedido, HttpContext.Session.GetString("Usuario"), idRepartidorAnterior);

                return Json(new
                {
                    success = true,
                    message = $"Pedido #{req.NumPedido} revertido a 'En preparación'. El repartidor fue liberado."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al revertir pedido #{NumPedido}", req.NumPedido);
                return Json(new { success = false, message = "Error interno al revertir el pedido." });
            }
        }


        // ── ★ NUEVO: OBTENER PEDIDOS DISPONIBLES (para persistencia al cargar) ──────
        // Llamado por el JS del repartidor en DOMContentLoaded.
        // Devuelve los pedidos en "En preparación" sin repartidor asignado, modo Domicilio.

        [HttpGet]
        [Route("/Admin/ObtenerPedidosDisponibles")]
        public async Task<IActionResult> ObtenerPedidosDisponibles()
        {
            // Cualquier usuario autenticado puede llamarlo (el Hub filtra por rol en JS)
            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p =>
                        (p.EstadoPedido == "En preparación" || p.EstadoPedido == "EsperandoRepartidor") &&
                        p.IdRepartidor == null &&
                        p.ModoEntrega == "Domicilio")
                    .AsNoTracking()
                    .ToListAsync();

                var resultado = pedidos.Select(p =>
                {
                    var cliente = p.IdClienteNavigation;
                    var productosStr = p.DetallePedidos
                        .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                        .ToList();
                    return new
                    {
                        numPedido = p.NumPedido,
                        cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                        domicilio = cliente?.Domicilio ?? "Sin domicilio",
                        telefono = cliente?.NumTelefono ?? "-",
                        montoTotal = (p.MontoTotal ?? 0m).ToString("C"),
                        productos = string.Join(", ", productosStr),
                        observaciones = p.Observaciones ?? ""
                    };
                });

                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener pedidos disponibles para repartidores.");
                return Json(Array.Empty<object>());
            }
        }

        // ── SOLICITAR REPARTIDOR AJAX (SignalR) ───────────────────────────

        [HttpPost]
        [Route("/Admin/SolicitarRepartidorAjax")]
        public async Task<IActionResult> SolicitarRepartidorAjax([FromBody] SolicitarRepartidorRequest req)
        {
            if (!PuedeAsignarRepartidor(HttpContext.Session))
                return Json(new { success = false, message = "No autorizado." });

            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado." });

                if (pedido.EstadoPedido != "En preparación" && pedido.EstadoPedido != "EsperandoRepartidor")
                    return Json(new { success = false, message = $"El pedido está en estado '{pedido.EstadoPedido}'. Solo se puede solicitar repartidor desde 'En preparación'." });

                if (pedido.ModoEntrega?.Equals("Domicilio", StringComparison.OrdinalIgnoreCase) != true)
                    return Json(new { success = false, message = "Este pedido es de retiro en local. No necesita repartidor." });

                var cliente = pedido.IdClienteNavigation;
                var productos = pedido.DetallePedidos
                    .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                    .ToList();

                var payload = new
                {
                    numPedido = pedido.NumPedido,
                    cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                    domicilio = cliente?.Domicilio ?? "Sin domicilio",
                    telefono = cliente?.NumTelefono ?? "-",
                    montoTotal = pedido.MontoTotal?.ToString("C") ?? "$0,00",
                    productos = string.Join(", ", productos),
                    observaciones = pedido.Observaciones ?? ""
                };

                // ★ Persistir en BD: "EsperandoRepartidor" sobrevive recargas y
                // se muestra correctamente en AMBAS vistas (Index y Detalle) sin
                // depender de la memoria del servidor.
                if (pedido.EstadoPedido != "EsperandoRepartidor")
                {
                    pedido.EstadoPedido = "EsperandoRepartidor";
                    await _context.SaveChangesAsync();
                }

                await _hubContext.Clients.Group("Repartidores")
                    .SendAsync("NuevoPedidoParaReparto", payload);

                // ★ También mantener en memoria para consultas ultra-rápidas
                DeliveryHub.RegistrarPedidoEnBusqueda(pedido.NumPedido);

                _logger.LogInformation("Broadcast 'NuevoPedidoParaReparto' para pedido #{NumPedido}", pedido.NumPedido);

                return Json(new { success = true, message = "Notificación enviada a los repartidores disponibles." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al solicitar repartidor para pedido #{NumPedido}", req.NumPedido);
                return Json(new { success = false, message = "Error interno al enviar la solicitud." });
            }
        }

        // ── ACEPTAR PEDIDO AJAX (SignalR + Concurrencia) ──────────────────

        [HttpPost]
        [Route("/Admin/AceptarPedidoAjax")]
        public async Task<IActionResult> AceptarPedidoAjax([FromBody] AceptarPedidoRequest req)
        {
            if (!EsRepartidor(HttpContext.Session))
                return Json(new { success = false, message = "Solo los repartidores pueden aceptar pedidos." });

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var pedido = await _context.Pedidos
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado." });

                if ((pedido.EstadoPedido != "En preparación" && pedido.EstadoPedido != "EsperandoRepartidor")
                    || pedido.IdRepartidor != null)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        yaFueTomado = true,
                        message = "Este pedido ya fue tomado por otro repartidor."
                    });
                }

                var repartidor = await GetCurrentRepartidorAsync();
                if (repartidor == null)
                    return Json(new { success = false, message = "No se encontró tu perfil de repartidor." });

                if (repartidor.Disponibilidad != "Activo")
                    return Json(new { success = false, message = "Solo podés aceptar pedidos si tu disponibilidad es 'Activo'." });

                pedido.IdRepartidor = repartidor.Id;
                pedido.EstadoPedido = "En reparto";
                repartidor.Disponibilidad = "Ocupado";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var nombreRepartidor = $"{repartidor.Apellido} {repartidor.Nombre}".Trim();
                if (string.IsNullOrWhiteSpace(nombreRepartidor))
                {
                    var usuario = await _context.Usuarios.AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == repartidor.IdUsuario);
                    nombreRepartidor = usuario?.NombreUsuario ?? "Repartidor";
                }

                // ★ FIX PERSISTENCIA: Notifica a admins con datos del repartidor (actualiza tabla)
                await _hubContext.Clients.Group("Admins").SendAsync("PedidoAceptado", new
                {
                    numPedido = req.NumPedido,
                    nombreRepartidor = nombreRepartidor,
                    repartidorId = repartidor.Id
                });

                // ★ FIX PERSISTENCIA: Notifica a TODOS los repartidores para eliminar el pedido de sus pantallas
                await _hubContext.Clients.Group("Repartidores").SendAsync("PedidoAsignado", new
                {
                    numPedido = req.NumPedido,
                    repartidorId = repartidor.Id
                });

                // ★ CerrarAlerta: cierra el modal en TODOS los repartidores conectados
                await _hubContext.Clients.Group("Repartidores")
                    .SendAsync("CerrarAlerta", req.NumPedido);

                // ★ Sacar del tracking → botón vuelve a "Solicitar Repartidor" en admin
                DeliveryHub.RemoverPedidoEnBusqueda(req.NumPedido);

                // ★ Limpiar el tracking de rechazos para este pedido
                DeliveryHub.ClearPendingDelivery(req.NumPedido);

                _logger.LogInformation(
                    "Pedido #{NumPedido} aceptado por Repartidor #{RepartidorId} ({Nombre})",
                    req.NumPedido, repartidor.Id, nombreRepartidor);

                // Cargar pedido completo para devolver datos a la UI
                var pedidoCompleto = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                var cliente = pedidoCompleto?.IdClienteNavigation;
                var productos2 = pedidoCompleto?.DetallePedidos
                    .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                    .ToList() ?? new List<string>();

                return Json(new
                {
                    success = true,
                    message = $"¡Pedido #{req.NumPedido} aceptado! Ahora estás en camino.",
                    numPedido = req.NumPedido,
                    // ★ Datos para mostrar el pedido directamente en la tabla del repartidor sin F5
                    pedidoData = new
                    {
                        numPedido = pedidoCompleto?.NumPedido,
                        fechaPedido = pedidoCompleto?.FechaPedido?.ToString("dd/MM/yyyy HH:mm"),
                        cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                        telefono = cliente?.NumTelefono ?? "-",
                        domicilio = cliente?.Domicilio ?? "-",
                        productos = string.Join(", ", productos2),
                        montoTotal = (pedidoCompleto?.MontoTotal ?? 0m).ToString("C"),
                        observaciones = pedidoCompleto?.Observaciones ?? ""
                    }
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Conflicto de concurrencia al aceptar pedido #{NumPedido}", req.NumPedido);
                return Json(new
                {
                    success = false,
                    yaFueTomado = true,
                    message = "Este pedido acaba de ser tomado por otro repartidor."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al aceptar pedido #{NumPedido}", req.NumPedido);
                return Json(new { success = false, message = "Error interno. Intentá de nuevo." });
            }
        }

        // ── CAMBIAR DISPONIBILIDAD REPARTIDOR ─────────────────────────────

        [HttpPost]
        [Route("/Admin/CambiarDisponibilidadRepartidor")]
        public async Task<IActionResult> CambiarDisponibilidadRepartidor([FromBody] CambiarDisponibilidadRequest req)
        {
            if (!EsRepartidor(HttpContext.Session))
                return Json(new { success = false, message = "No autorizado." });

            if (req.Disponibilidad == "Ocupado")
                return Json(new { success = false, message = "'Ocupado' lo asigna el sistema automáticamente al aceptar un pedido. Solo podés elegir 'Activo' o 'Inactivo'." });

            var disponibilidadesValidas = new[] { "Activo", "Inactivo" };
            if (string.IsNullOrWhiteSpace(req.Disponibilidad) || !disponibilidadesValidas.Contains(req.Disponibilidad))
                return Json(new { success = false, message = "Estado de disponibilidad no válido." });

            try
            {
                var nombreUsuario = HttpContext.Session.GetString("Usuario");
                var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);
                if (usuario == null) return Json(new { success = false, message = "Usuario no encontrado." });

                var repartidor = await _context.Repartidores
                    .FirstOrDefaultAsync(r => r.IdUsuario == usuario.Id && r.Activo);
                if (repartidor == null) return Json(new { success = false, message = "Repartidor no encontrado." });

                repartidor.Disponibilidad = req.Disponibilidad;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Disponibilidad actualizada a '{req.Disponibilidad}'.",
                    disponibilidad = req.Disponibilidad
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar disponibilidad");
                return Json(new { success = false, message = "Error interno." });
            }
        }

        // ── EXCEL ─────────────────────────────────────────────────────────

        [HttpPost]
        [Route("/Admin/DescargarExcel")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DescargarExcel()
        {
            if (!PuedeGestionarPedidos())
                return RedirectToAction("Login", "Usuario", new { area = "" });

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                IQueryable<Pedido> q = _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.Repartidor).ThenInclude(r => r.Usuario)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation);

                if (EsRepartidor(HttpContext.Session))
                {
                    var repartidorId = await GetCurrentRepartidorIdAsync();
                    q = repartidorId != null
                        ? q.Where(p => p.IdRepartidor == repartidorId.Value)
                        : q.Where(p => false);
                }

                var pedidos = await q.OrderByDescending(p => p.FechaPedido).ToListAsync();

                using var package = new ExcelPackage();
                var hoja = package.Workbook.Worksheets.Add("Pedidos");

                hoja.Cells[1, 1].Value = "N° Pedido";
                hoja.Cells[1, 2].Value = "Cliente";
                hoja.Cells[1, 3].Value = "Domicilio";
                hoja.Cells[1, 4].Value = "Teléfono";
                hoja.Cells[1, 5].Value = "Fecha";
                hoja.Cells[1, 6].Value = "Estado";
                hoja.Cells[1, 7].Value = "Modo Entrega";
                hoja.Cells[1, 8].Value = "Total";
                hoja.Cells[1, 9].Value = "Repartidor";

                using (var range = hoja.Cells[1, 1, 1, 9])
                    range.Style.Font.Bold = true;

                int fila = 2;
                foreach (var p in pedidos)
                {
                    hoja.Cells[fila, 1].Value = p.NumPedido;
                    hoja.Cells[fila, 2].Value = $"{p.IdClienteNavigation?.Nombre} {p.IdClienteNavigation?.Apellido}";
                    hoja.Cells[fila, 3].Value = p.IdClienteNavigation?.Domicilio;
                    hoja.Cells[fila, 4].Value = p.IdClienteNavigation?.NumTelefono;
                    hoja.Cells[fila, 5].Value = p.FechaPedido;
                    hoja.Cells[fila, 5].Style.Numberformat.Format = "dd/MM/yyyy HH:mm";
                    hoja.Cells[fila, 6].Value = p.EstadoPedido;
                    hoja.Cells[fila, 7].Value = p.ModoEntrega;
                    hoja.Cells[fila, 8].Value = p.MontoTotal ?? 0m;
                    hoja.Cells[fila, 8].Style.Numberformat.Format = "$ #,##0.00";
                    var repNombre = p.Repartidor != null
                        ? $"{p.Repartidor.Apellido} {p.Repartidor.Nombre}".Trim()
                        : "Sin asignar";
                    hoja.Cells[fila, 9].Value = repNombre;
                    fila++;
                }

                hoja.Cells.AutoFitColumns();
                var bytes = package.GetAsByteArray();
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Pedidos_{DateTime.Now:yyyyMMdd}.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel");
                TempData["Error"] = "No se pudo generar el Excel.";
                return RedirectToAction(nameof(Index));
            }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class CambiarEstadoRequest
    {
        public int NumPedido { get; set; }
        public string? NuevoEstado { get; set; }
    }

    public class AsignarRepartidorRequest
    {
        public int NumPedido { get; set; }
        public int? IdRepartidor { get; set; }
    }

    public class CambiarDisponibilidadRequest
    {
        public string? Disponibilidad { get; set; }
    }

    public class SolicitarRepartidorRequest
    {
        public int NumPedido { get; set; }
    }

    public class AceptarPedidoRequest
    {
        public int NumPedido { get; set; }
    }

    // ★ NUEVO DTO para revertir
    public class RevertirPedidoRequest
    {
        public int NumPedido { get; set; }
        /// <summary>Si es true, se re-emite la alerta SignalR a los repartidores tras revertir.</summary>
        public bool ReemitirAlerta { get; set; } = true;
    }
}

