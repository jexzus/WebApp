// ============================================================
// ARCHIVO: Areas/Cliente/Controllers/ClienteController.cs
// ============================================================
// ★ FIX CRÍTICO: CancelarPedidoAjax ahora EXISTE
//   (antes faltaba → el JS hacía fetch() → 404 HTML → JSON.parse fallaba)
// ★ NUEVO: IHubContext<DeliveryHub> inyectado
// ★ NUEVO: CancelarPedidoAjax emite "PedidoCancelado" via SignalR
// ★ NUEVO: Lógica de aviso devolución (Para retirar / En reparto)
// ============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppWeb1.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using AppWeb1.Models;
using AppWeb1.Helpers;
using AppWeb1.Hubs;
using Microsoft.AspNetCore.SignalR;
using MercadoPago.Config;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Preference;
using MercadoPago.Error;
using Microsoft.Extensions.Configuration;
using ClienteModel = AppWeb1.Models.Cliente;

namespace AppWeb1.Areas.Cliente.Controllers
{
    [Area("Cliente")]
    public class ClienteController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<ClienteController> _logger;
        private readonly IConfiguration _config;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public ClienteController(
            DeliveryDBContext context,
            ILogger<ClienteController> logger,
            IConfiguration config,
            IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _logger = logger;
            _config = config;
            _hubContext = hubContext;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private bool EsCliente() =>
            string.Equals(HttpContext.Session.GetString("Rol"), "cliente", StringComparison.OrdinalIgnoreCase);

        private string? UsuarioActual() => HttpContext.Session.GetString("Usuario");

        private async Task<ClienteModel?> GetClienteActualAsync()
        {
            var usuarioNombre = UsuarioActual();
            if (string.IsNullOrEmpty(usuarioNombre)) return null;
            var usuario = await _context.Usuarios
                .Include(u => u.Clientes)
                .FirstOrDefaultAsync(u => u.NombreUsuario == usuarioNombre);
            return usuario?.Clientes.FirstOrDefault();
        }

        // ── Catálogo ──────────────────────────────────────────────────────
        public async Task<IActionResult> Catalogo()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var productos = await _context.Productos.OrderBy(p => p.NombreProducto).ToListAsync();
            return View(productos);
        }

        public async Task<IActionResult> AgregarCarrito(int id)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();
            return View(producto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AgregarCarrito(int idProducto, int cantidad)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            if (cantidad <= 0) cantidad = 1;
            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito") ?? new List<DetallePedido>();
            var producto = _context.Productos.FirstOrDefault(p => p.IdProducto == idProducto);
            if (producto == null) { TempData["Error"] = "El producto no existe."; return RedirectToAction(nameof(Catalogo)); }
            if (producto.Precio == null || producto.Precio <= 0) { TempData["Error"] = "El producto no tiene un precio válido."; return RedirectToAction(nameof(Catalogo)); }
            var existente = carrito.FirstOrDefault(p => p.IdProducto == idProducto);
            if (existente != null) existente.Cantidad += cantidad;
            else carrito.Add(new DetallePedido { IdProducto = idProducto, Cantidad = cantidad, PrecioUnitario = producto.Precio.Value, IdProductoNavigation = producto });
            HttpContext.Session.SetObjectAsJson("Carrito", carrito);
            TempData["Success"] = "Producto agregado al carrito.";
            return RedirectToAction(nameof(Catalogo));
        }

        public IActionResult Carrito()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito") ?? new List<DetallePedido>();
            foreach (var item in carrito)
                item.IdProductoNavigation = _context.Productos.FirstOrDefault(p => p.IdProducto == item.IdProducto);
            return View(carrito);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarDelCarrito(int indice)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito != null && indice >= 0 && indice < carrito.Count) { carrito.RemoveAt(indice); HttpContext.Session.SetObjectAsJson("Carrito", carrito); TempData["Success"] = "Producto eliminado."; }
            else TempData["Error"] = "No se pudo eliminar el producto del carrito.";
            return RedirectToAction(nameof(Carrito));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPedido(string tipoEntrega, string? observaciones)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito == null || !carrito.Any()) { TempData["Error"] = "Carrito vacío."; return RedirectToAction(nameof(Catalogo)); }
            ClienteModel? cliente = await GetClienteActualAsync();
            if (cliente == null) { TempData["Error"] = "Error de sesión."; return RedirectToAction("Login", "Usuario", new { area = "" }); }
            if (string.IsNullOrWhiteSpace(tipoEntrega)) { TempData["Error"] = "Debe seleccionar modo de entrega."; return RedirectToAction(nameof(Carrito)); }
            var pedido = new Pedido { IdCliente = cliente.IdCliente, FechaPedido = DateTime.Now, EstadoPedido = "Pendiente", Observaciones = observaciones, MontoTotal = carrito.Sum(c => c.PrecioUnitario * c.Cantidad), EstadoPago = "pendiente", ModoEntrega = tipoEntrega };
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Pedidos.Add(pedido);
                await _context.SaveChangesAsync();
                foreach (var item in carrito) _context.DetallePedidos.Add(new DetallePedido { NumPedido = pedido.NumPedido, IdProducto = item.IdProducto, Cantidad = item.Cantidad, PrecioUnitario = item.PrecioUnitario });
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                HttpContext.Session.Remove("Carrito");
                return RedirectToAction(nameof(PagarPedido), new { numPedido = pedido.NumPedido });
            }
            catch (Exception ex) { await tx.RollbackAsync(); _logger.LogError(ex, "Error al confirmar pedido"); TempData["Error"] = "No se pudo confirmar el pedido."; return RedirectToAction(nameof(Carrito)); }
        }

        [HttpGet]
        public async Task<IActionResult> PagarPedido(int numPedido)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            var pedido = await _context.Pedidos.Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation).Include(p => p.IdClienteNavigation).FirstOrDefaultAsync(p => p.NumPedido == numPedido);
            if (pedido == null) { TempData["Error"] = "El pedido no se encontró."; return RedirectToAction(nameof(EstadoPedido)); }
            if (string.Equals(pedido.EstadoPago, "aprobado", StringComparison.OrdinalIgnoreCase)) { TempData["Info"] = "Este pedido ya ha sido pagado."; return RedirectToAction(nameof(EstadoPedido)); }
            MercadoPagoConfig.AccessToken = _config["MercadoPago:AccessToken"];
            var baseUrl = _config["MercadoPago:BasePublicUrl"]!.TrimEnd('/');
            var items = pedido.DetallePedidos.Select(ci => new PreferenceItemRequest { Title = ci.IdProductoNavigation?.NombreProducto ?? "Producto", Quantity = ci.Cantidad, UnitPrice = (decimal)ci.PrecioUnitario, CurrencyId = "ARS" }).ToList();
            var prefReq = new PreferenceRequest { Items = items, Payer = new PreferencePayerRequest { Email = pedido.IdClienteNavigation?.Email ?? "comprador@example.com" }, BackUrls = new PreferenceBackUrlsRequest { Success = $"{baseUrl}/Pagos/Retorno?status=success&np={pedido.NumPedido}", Failure = $"{baseUrl}/Pagos/Retorno?status=failure&np={pedido.NumPedido}", Pending = $"{baseUrl}/Pagos/Retorno?status=pending&np={pedido.NumPedido}" }, AutoReturn = "approved", ExternalReference = pedido.NumPedido.ToString(), NotificationUrl = $"{baseUrl}/Pagos/Webhook" };
            try { var pref = await new PreferenceClient().CreateAsync(prefReq); pedido.MpPreferenceId = pref.Id; _context.Pedidos.Update(pedido); await _context.SaveChangesAsync(); return Redirect(pref.SandboxInitPoint); }
            catch (MercadoPagoApiException ex) { _logger.LogError(ex, "Error MP"); TempData["Error"] = $"Error MP: {ex.Message}"; return RedirectToAction(nameof(Carrito)); }
            catch (Exception ex) { _logger.LogError(ex, "Error pago"); TempData["Error"] = "Error al procesar el pago."; return RedirectToAction(nameof(Carrito)); }
        }

        // ── EstadoPedido ──────────────────────────────────────────────────
        public async Task<IActionResult> EstadoPedido()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            ClienteModel? cliente = await GetClienteActualAsync();
            if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });
            var pedidos = await _context.Pedidos
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .Where(p => p.IdCliente == cliente.IdCliente)
                .OrderByDescending(p => p.FechaPedido)
                .ToListAsync();
            ViewBag.ClienteId = cliente.IdCliente;
            return View("EstadoPedido", pedidos);
        }

        // ── ★ FIX CRÍTICO: CancelarPedidoAjax (antes NO EXISTÍA) ────────
        [HttpPost]
        [Route("/Cliente/CancelarPedidoAjax")]
        public async Task<IActionResult> CancelarPedidoAjax([FromBody] CancelarPedidoRequest request)
        {
            if (!EsCliente())
                return Json(new { success = false, message = "No autorizado." });

            try
            {
                ClienteModel? cliente = await GetClienteActualAsync();
                if (cliente == null)
                    return Json(new { success = false, message = "No se encontró tu perfil de cliente." });

                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.Repartidor)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido
                                           && p.IdCliente == cliente.IdCliente);

                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado o no te pertenece." });

                if (pedido.EstadoPedido == "Entregado")
                    return Json(new { success = false, message = "No podés cancelar un pedido ya entregado." });

                if (pedido.EstadoPedido == "Cancelado")
                    return Json(new { success = false, message = "Este pedido ya está cancelado." });

                // ── Calcular si necesita aviso de devolución ──
                bool requiereAvisoDevolucion =
                    pedido.EstadoPedido == "Para retirar" ||
                    pedido.EstadoPedido == "En reparto";

                int? idRepartidorAsignado = pedido.IdRepartidor;
                string estadoAnterior = pedido.EstadoPedido;
                string modoEntrega = pedido.ModoEntrega ?? "Domicilio";

                // ── Cancelar ──
                pedido.EstadoPedido = "Cancelado";

                // Liberar repartidor si estaba en reparto
                if (estadoAnterior == "En reparto" && idRepartidorAsignado != null)
                {
                    var repartidor = await _context.Repartidores
                        .FirstOrDefaultAsync(r => r.Id == idRepartidorAsignado.Value);
                    if (repartidor != null && repartidor.Disponibilidad == "Ocupado")
                        repartidor.Disponibilidad = "Activo";
                }

                await _context.SaveChangesAsync();

                // ── Limpiar tracking de rechazos del Hub para este pedido ──
                // Evita que llegue una notificación "ignorado" a los admins
                // sobre un pedido que ya fue cancelado por el cliente.
                AppWeb1.Hubs.DeliveryHub.ClearPendingDelivery(request.NumPedido);

                // ── ★ SignalR: PedidoCancelado → todos los actores ──
                var nombreCliente = $"{pedido.IdClienteNavigation?.Nombre} {pedido.IdClienteNavigation?.Apellido}".Trim();
                var productosStr = string.Join(", ", pedido.DetallePedidos
                    .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}"));

                await _hubContext.Clients.All.SendAsync("PedidoCancelado", new
                {
                    numPedido = pedido.NumPedido,
                    estadoAnterior = estadoAnterior,
                    modoEntrega = modoEntrega,
                    cliente = nombreCliente,
                    idRepartidorAnterior = idRepartidorAsignado,
                    idCliente = cliente.IdCliente,
                    productos = productosStr,
                    montoTotal = pedido.MontoTotal?.ToString("C") ?? "$0,00"
                });

                _logger.LogInformation(
                    "Pedido #{Num} cancelado por cliente. EstadoAnterior={Estado} Rep={Rep}",
                    request.NumPedido, estadoAnterior, idRepartidorAsignado);

                return Json(new
                {
                    success = true,
                    message = $"Pedido #{request.NumPedido} cancelado correctamente.",
                    requiereAvisoDevolucion = requiereAvisoDevolucion,
                    estadoAnterior = estadoAnterior
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar pedido #{Num}", request.NumPedido);
                return Json(new { success = false, message = "Error interno. Intentá de nuevo." });
            }
        }

        // ── Exports ───────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DescargarExcel()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            try
            {
                ClienteModel? cliente = await GetClienteActualAsync();
                if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });
                var pedidos = await _context.Pedidos.Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation).Where(p => p.IdCliente == cliente.IdCliente).OrderByDescending(p => p.FechaPedido).ToListAsync();
                var bytes = AppWeb1.Models.ExportHelper.GenerarExcelPedidos(pedidos);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MisPedidos.xlsx");
            }
            catch (Exception ex) { _logger.LogError(ex, "Error Excel"); TempData["Error"] = "Error al generar el archivo."; return RedirectToAction(nameof(EstadoPedido)); }
        }

        [HttpGet]
        public async Task<IActionResult> DescargarPDF()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            try
            {
                ClienteModel? cliente = await GetClienteActualAsync();
                if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });
                var pedidos = await _context.Pedidos.Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation).Where(p => p.IdCliente == cliente.IdCliente).OrderByDescending(p => p.FechaPedido).ToListAsync();
                var pdfBytes = AppWeb1.Models.ExportHelper.GenerarPdfPedidos(pedidos);
                if (pdfBytes == null || pdfBytes.Length == 0) { TempData["Error"] = "Error al generar el PDF."; return RedirectToAction(nameof(EstadoPedido)); }
                return File(pdfBytes, "application/pdf", "MisPedidos.pdf");
            }
            catch (Exception ex) { _logger.LogError(ex, "Error PDF"); TempData["Error"] = "Error al generar el archivo."; return RedirectToAction(nameof(EstadoPedido)); }
        }
    }

    // ── DTO ──────────────────────────────────────────────────────────────────
    public class CancelarPedidoRequest
    {
        public int NumPedido { get; set; }
    }
}