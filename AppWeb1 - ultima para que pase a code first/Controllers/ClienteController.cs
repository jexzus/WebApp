using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppWeb1.Models;
using AppWeb1.Helpers;
using System.Globalization;

namespace AppWeb1.Controllers
{
    public class ClienteController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<ClienteController> _logger;

        public ClienteController(DeliveryDBContext context, ILogger<ClienteController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private bool EsCliente()
        {
            return HttpContext.Session.GetString("Rol") == "cliente";
        }

        private string? UsuarioActual()
        {
            return HttpContext.Session.GetString("Usuario");
        }

        public async Task<IActionResult> Catalogo()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");
            var productos = await _context.Productos.ToListAsync();
            return View(productos);
        }

        public async Task<IActionResult> AgregarCarrito(int id)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");
            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();
            return View(producto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AgregarCarrito(int idProducto, int cantidad)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito") ?? new List<DetallePedido>();
            var producto = _context.Productos.FirstOrDefault(p => p.IdProducto == idProducto);
            if (producto == null) return NotFound();

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
                    PrecioUnitario = producto.Precio ?? 0,
                    IdProductoNavigation = producto
                });
            }

            HttpContext.Session.SetObjectAsJson("Carrito", carrito);
            return RedirectToAction("Catalogo");
        }

        public IActionResult Carrito()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito") ?? new List<DetallePedido>();

            foreach (var item in carrito)
            {
                item.IdProductoNavigation = _context.Productos.FirstOrDefault(p => p.IdProducto == item.IdProducto);
            }

            return View(carrito);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarDelCarrito(int indice)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito != null && indice >= 0 && indice < carrito.Count)
            {
                carrito.RemoveAt(indice);
                HttpContext.Session.SetObjectAsJson("Carrito", carrito);
            }

            return RedirectToAction("Carrito");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPedido(string tipoEntrega, string observaciones)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito == null || !carrito.Any()) return RedirectToAction("Catalogo");

            var usuario = UsuarioActual();
            var usuarioObj = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuario);
            if (usuarioObj == null) return RedirectToAction("Login", "Usuario");

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == usuarioObj.Id);
            if (cliente == null) return RedirectToAction("Login", "Usuario");

            var pedido = new Pedido
            {
                IdCliente = cliente.IdCliente,
                FechaPedido = DateTime.Now,
                EstadoPedido = "Pendiente",
                Observaciones = observaciones,
                MontoTotal = carrito.Sum(c => c.PrecioUnitario * c.Cantidad)
            };

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
            HttpContext.Session.Remove("Carrito");
            TempData["PedidoExitoso"] = true;

            return RedirectToAction("Carrito");
        }

        public async Task<IActionResult> EstadoPedido()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var usuario = UsuarioActual();
            var usuarioObj = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuario);
            if (usuarioObj == null) return RedirectToAction("Login", "Usuario");

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == usuarioObj.Id);
            if (cliente == null) return RedirectToAction("Login", "Usuario");

            var pedidos = await _context.Pedidos
                .Include(p => p.DetallePedidos)
                    .ThenInclude(d => d.IdProductoNavigation)
                .Where(p => p.IdCliente == cliente.IdCliente)
                .OrderByDescending(p => p.FechaPedido)
                .ToListAsync();

            return View("EstadoPedido", pedidos);
        }

        [HttpGet]
        public async Task<IActionResult> DescargarExcel()
        {
            try
            {
                var usuario = HttpContext.Session.GetString("Usuario");
                var usuarioObj = await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuario);
                if (usuarioObj == null) return RedirectToAction("Login", "Usuario");

                var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == usuarioObj.Id);
                if (cliente == null) return RedirectToAction("Login", "Usuario");

                var pedidos = await _context.Pedidos
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p => p.IdCliente == cliente.IdCliente)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                var excelBytes = ExportHelper.GenerarExcelPedidos(pedidos);
                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MisPedidos.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel en memoria");
                return StatusCode(500, "Error interno al generar el Excel.");
            }   
        }

        public IActionResult DescargarPDF()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            try
            {
                var usuario = UsuarioActual();
                var usuarioObj = _context.Usuarios.FirstOrDefault(u => u.NombreUsuario == usuario);
                if (usuarioObj == null) return RedirectToAction("Login", "Usuario");

                var cliente = _context.Clientes.FirstOrDefault(c => c.IdUsuario == usuarioObj.Id);
                if (cliente == null) return RedirectToAction("Login", "Usuario");

                var pedidos = _context.Pedidos
                    .Where(p => p.IdCliente == cliente.IdCliente)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToList();

                var pdfBytes = ExportHelper.GenerarPdfPedidos(pedidos);

                if (pdfBytes.Length == 0)
                {
                    return StatusCode(500, "Error al generar el PDF. Intente con Imprimir.");

                }

                return File(pdfBytes, "application/pdf", "MisPedidos.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF");
                return StatusCode(500, "Error interno al generar el PDF.");
            }
        }

    }
}
