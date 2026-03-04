// Areas/Cliente/Controllers/ClienteController.cs
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

// Mercado Pago SDK
using MercadoPago.Config;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Preference;
using MercadoPago.Error;

using Microsoft.Extensions.Configuration;

namespace AppWeb1.Areas.Cliente.Controllers
{
    [Area("Cliente")]
    public class ClienteController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<ClienteController> _logger;
        private readonly IConfiguration _config;

        public ClienteController(
            DeliveryDBContext context,
            ILogger<ClienteController> logger,
            IConfiguration config)
        {
            _context = context;
            _logger = logger;
            _config = config;
        }

        // ===== Helpers de sesión/rol =====
        private bool EsCliente()
        {
            return string.Equals(HttpContext.Session.GetString("Rol"), "cliente", StringComparison.OrdinalIgnoreCase);
        }

        private string? UsuarioActual()
        {
            return HttpContext.Session.GetString("Usuario");
        }

        // ================== VISTAS CLIENTE ==================
        // GET: /Cliente/Cliente/Catalogo
        public async Task<IActionResult> Catalogo()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var productos = await _context.Productos
                .OrderBy(p => p.NombreProducto)
                .ToListAsync();

            return View(productos);
        }

        // GET: /Cliente/Cliente/AgregarCarrito/5
        public async Task<IActionResult> AgregarCarrito(int id)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            return View(producto);
        }

        // POST: /Cliente/Cliente/AgregarCarrito
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AgregarCarrito(int idProducto, int cantidad)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });
            if (cantidad <= 0) cantidad = 1;

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito")
                          ?? new List<DetallePedido>();

            var producto = _context.Productos.FirstOrDefault(p => p.IdProducto == idProducto);
            if (producto == null)
            {
                TempData["Error"] = "El producto no existe o fue eliminado.";
                return RedirectToAction(nameof(Catalogo));
            }

            var existente = carrito.FirstOrDefault(p => p.IdProducto == idProducto);
            if (existente != null)
            {
                existente.Cantidad += cantidad;
            }
            else
            {
                carrito.Add(new DetallePedido
                {
                    IdProducto = idProducto,
                    Cantidad = cantidad,
                    PrecioUnitario = producto.Precio ?? 0m,
                    IdProductoNavigation = producto
                });
            }

            HttpContext.Session.SetObjectAsJson("Carrito", carrito);
            TempData["Success"] = "Producto agregado al carrito.";
            return RedirectToAction(nameof(Catalogo));
        }

        // GET: /Cliente/Cliente/Carrito
        public IActionResult Carrito()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito")
                          ?? new List<DetallePedido>();

            foreach (var item in carrito)
                item.IdProductoNavigation = _context.Productos.FirstOrDefault(p => p.IdProducto == item.IdProducto);

            return View(carrito);
        }

        // POST: /Cliente/Cliente/EliminarDelCarrito
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarDelCarrito(int indice)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito != null && indice >= 0 && indice < carrito.Count)
            {
                carrito.RemoveAt(indice);
                HttpContext.Session.SetObjectAsJson("Carrito", carrito);
                TempData["Success"] = "Producto eliminado del carrito.";
            }
            else
            {
                TempData["Error"] = "No se pudo eliminar el producto del carrito.";
            }

            return RedirectToAction(nameof(Carrito));
        }

        // ==========================================================
        // Confirmar Pedido (con redirección al pago)
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPedido(string tipoEntrega, string? observaciones)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito == null || !carrito.Any())
            {
                TempData["Error"] = "No se puede confirmar un pedido con el carrito vacío.";
                return RedirectToAction(nameof(Catalogo));
            }

            var usuarioNombre = UsuarioActual();
            var usuario = await _context.Usuarios
                .Include(u => u.Clientes)
                .FirstOrDefaultAsync(u => u.NombreUsuario == usuarioNombre);

            if (usuario == null)
            {
                TempData["Error"] = "Error de sesión. Por favor, vuelve a iniciar sesión.";
                return RedirectToAction("Login", "Usuario", new { area = "" });
            }

            var cliente = usuario.Clientes.FirstOrDefault();
            if (cliente == null)
            {
                TempData["Error"] = "Error de sesión. No se encontró tu perfil de cliente.";
                return RedirectToAction("Login", "Usuario", new { area = "" });
            }

            var pedido = new Pedido
            {
                IdCliente = cliente.IdCliente,
                FechaPedido = DateTime.Now,
                EstadoPedido = "Pendiente",
                Observaciones = observaciones,
                MontoTotal = carrito.Sum(c => c.PrecioUnitario * c.Cantidad),
                EstadoPago = "pendiente" // Estado inicial
            };

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Pedidos.Add(pedido);
                await _context.SaveChangesAsync();

                foreach (var item in carrito)
                {
                    _context.DetallePedidos.Add(new DetallePedido
                    {
                        NumPedido = pedido.NumPedido,
                        IdProducto = item.IdProducto,
                        Cantidad = item.Cantidad,
                        PrecioUnitario = item.PrecioUnitario
                    });
                }
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // Redirigimos a la acción PagarPedido con el número de pedido
                return RedirectToAction(nameof(PagarPedido), new { numPedido = pedido.NumPedido });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error al confirmar el pedido");
                TempData["Error"] = "No se pudo confirmar el pedido. Intente nuevamente.";
                return RedirectToAction(nameof(Carrito));
            }
        }

        // ==========================================================
        // Acción de pago con Mercado Pago
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> PagarPedido(int numPedido)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var pedido = await _context.Pedidos
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .Include(p => p.IdClienteNavigation)
                .FirstOrDefaultAsync(p => p.NumPedido == numPedido);

            if (pedido == null)
            {
                TempData["Error"] = "El pedido no se encontró.";
                return RedirectToAction(nameof(EstadoPedido));
            }

            if (string.Equals(pedido.EstadoPago, "aprobado", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Info"] = "Este pedido ya ha sido pagado.";
                return RedirectToAction(nameof(EstadoPedido));
            }

            // 1) Credenciales y base URL
            MercadoPagoConfig.AccessToken = _config["MercadoPago:AccessToken"];
            var baseUrl = _config["MercadoPago:BasePublicUrl"]!.TrimEnd('/');

            // 2) Items desde el pedido
            var items = pedido.DetallePedidos.Select(ci => new PreferenceItemRequest
            {
                Title = ci.IdProductoNavigation?.NombreProducto ?? "Producto",
                Quantity = ci.Cantidad,
                UnitPrice = (decimal)ci.PrecioUnitario,
                CurrencyId = "ARS"
            }).ToList();

            // 3) Back URLs (vuelta desde MP)
            var backUrls = new PreferenceBackUrlsRequest
            {
                Success = $"{baseUrl}/Pagos/Retorno?status=success&np={pedido.NumPedido}",
                Failure = $"{baseUrl}/Pagos/Retorno?status=failure&np={pedido.NumPedido}",
                Pending = $"{baseUrl}/Pagos/Retorno?status=pending&np={pedido.NumPedido}"
            };

            // 4) Email del pagador:
            //    - Si el Usuario.NombreUsuario "parece" un email (contiene '@'), lo usamos.
            //    - Si no, dejamos null para que use el placeholder.
            string? payerEmail = null;
            int? idUsuario = pedido.IdClienteNavigation?.IdUsuario;
            if (idUsuario.HasValue)
            {
                var posible = await _context.Usuarios
                    .Where(u => u.Id == idUsuario.Value)
                    .Select(u => u.NombreUsuario)  // Usuario no tiene Email; usamos NombreUsuario si es email
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(posible) && posible.Contains("@"))
                    payerEmail = posible;
            }

            // 5) Crear preferencia
            var prefReq = new PreferenceRequest
            {
                Items = items,
                Payer = new PreferencePayerRequest { Email = payerEmail ?? "comprador@example.com" },
                BackUrls = backUrls,
                AutoReturn = "approved",
                ExternalReference = pedido.NumPedido.ToString(),
                NotificationUrl = $"{baseUrl}/Pagos/Webhook"
            };

            try
            {
                var prefClient = new PreferenceClient();
                var pref = await prefClient.CreateAsync(prefReq);

                // Guardar ID de preferencia
                pedido.MpPreferenceId = pref.Id;
                _context.Pedidos.Update(pedido);
                await _context.SaveChangesAsync();

                // Logs útiles (se eliminó LiveMode porque no es una propiedad válida en la clase Preference)
                _logger.LogInformation("MP Pref creada: Id={Id} Init={Init}, Sandbox={Sandbox}",
                    pref.Id, pref.InitPoint, pref.SandboxInitPoint);

                // 🔵 SANDBOX: usar siempre SandboxInitPoint para tarjetas demo / entorno de prueba
                return Redirect(pref.SandboxInitPoint);
            }
            catch (MercadoPagoApiException ex)
            {
                _logger.LogError(ex,
                    "Error de la API de Mercado Pago al crear la preferencia: {Status} {Content}",
                    ex.ApiResponse?.StatusCode, ex.ApiResponse?.Content);

                TempData["Error"] = $"Error de la API: {ex.Message}. Por favor, intente de nuevo.";
                return RedirectToAction(nameof(Carrito));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear la preferencia de pago: {Error}", ex.Message);
                TempData["Error"] = "Hubo un problema al procesar el pago. Intente de nuevo.";
                return RedirectToAction(nameof(Carrito));
            }
        }

        // ==========================================================
        // VISTAS DE ESTADO DEL PEDIDO
        // ==========================================================

        // GET: /Cliente/Cliente/EstadoPedido
        public async Task<IActionResult> EstadoPedido()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            var usuarioNombre = UsuarioActual();
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuarioNombre);
            if (usuario == null) return RedirectToAction("Login", "Usuario", new { area = "" });

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == usuario.Id);
            if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });

            var pedidos = await _context.Pedidos
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .Where(p => p.IdCliente == cliente.IdCliente)
                .OrderByDescending(p => p.FechaPedido)
                .ToListAsync();

            return View("EstadoPedido", pedidos);
        }

        // GET: /Cliente/Cliente/DescargarExcel
        [HttpGet]
        public async Task<IActionResult> DescargarExcel()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            try
            {
                var usuarioNombre = UsuarioActual();
                var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuarioNombre);
                if (usuario == null) return RedirectToAction("Login", "Usuario", new { area = "" });

                var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == usuario.Id);
                if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });

                var pedidos = await _context.Pedidos
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p => p.IdCliente == cliente.IdCliente)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                var bytes = ExportHelper.GenerarExcelPedidos(pedidos);
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "MisPedidos.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel del cliente");
                TempData["Error"] = "Error al generar el archivo. Por favor, intenta de nuevo.";
                return RedirectToAction(nameof(EstadoPedido));
            }
        }

        // GET: /Cliente/Cliente/DescargarPDF
        [HttpGet]
        public IActionResult DescargarPDF()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario", new { area = "" });

            try
            {
                var usuarioNombre = UsuarioActual();
                var usuario = _context.Usuarios.FirstOrDefault(u => u.NombreUsuario == usuarioNombre);
                if (usuario == null) return RedirectToAction("Login", "Usuario", new { area = "" });

                var cliente = _context.Clientes.FirstOrDefault(c => c.IdUsuario == usuario.Id);
                if (cliente == null) return RedirectToAction("Login", "Usuario", new { area = "" });

                var pedidos = _context.Pedidos
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p => p.IdCliente == cliente.IdCliente)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToList();

                var pdfBytes = ExportHelper.GenerarPdfPedidos(pedidos);
                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    TempData["Error"] = "Error al generar el PDF. Por favor, intenta de nuevo.";
                    return RedirectToAction(nameof(EstadoPedido));
                }

                return File(pdfBytes, "application/pdf", "MisPedidos.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF del cliente");
                TempData["Error"] = "Error al generar el archivo. Por favor, intenta de nuevo.";
                return RedirectToAction(nameof(EstadoPedido));
            }
        }
    }
}