using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppWeb1.Models;
using AppWeb1.Helpers;

namespace AppWeb1.Controllers
{
    public class ClienteController : Controller
    {
        private readonly DeliveryDBContext _context;

        public ClienteController(DeliveryDBContext context)
        {
            _context = context;
        }

        private bool EsCliente()
        {
            return HttpContext.Session.GetString("Rol") == "cliente";
        }

        private string? UsuarioActual()
        {
            return HttpContext.Session.GetString("Usuario");
        }

        // GET: Cliente/Catalogo
        public async Task<IActionResult> Catalogo()
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var productos = await _context.Productos.ToListAsync();
            return View(productos);
        }

        // GET: Cliente/AgregarCarrito/5
        public async Task<IActionResult> AgregarCarrito(int id)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            return View(producto);
        }

        // POST: Cliente/AgregarCarrito
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

        // GET: Cliente/Carrito
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

        // POST: Cliente/EliminarDelCarrito
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

        // POST: Cliente/ConfirmarPedido
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPedido(string tipoEntrega, string observaciones)
        {
            if (!EsCliente()) return RedirectToAction("Login", "Usuario");

            var carrito = HttpContext.Session.GetObjectFromJson<List<DetallePedido>>("Carrito");
            if (carrito == null || !carrito.Any())
                return RedirectToAction("Catalogo");

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

            return RedirectToAction("Catalogo");
        }
    }
}
