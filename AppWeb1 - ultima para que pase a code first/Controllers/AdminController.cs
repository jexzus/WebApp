#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppWeb1.Models;
using Microsoft.AspNetCore.Http;
using OfficeOpenXml;

namespace AppWeb1.Controllers
{
    public class AdminController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(DeliveryDBContext context, ILogger<AdminController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private bool EsAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) && rol.Equals("admin", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult Index()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            ViewBag.Usuario = HttpContext.Session.GetString("Usuario");
            return View();
        }

        public async Task<IActionResult> GestionCatalogo()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            try
            {
                var productos = await _context.Productos.OrderBy(p => p.NombreProducto).ToListAsync();
                return View(productos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar productos");
                ViewBag.Error = "Error al cargar los productos";
                return View(new List<Producto>());
            }
        }

        public async Task<IActionResult> GestionPedidos()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                var pedidosValidos = pedidos.Where(p =>
                    p.IdClienteNavigation != null &&
                    p.DetallePedidos != null &&
                    p.DetallePedidos.All(d => d.IdProductoNavigation != null)).ToList();

                return View("GestionPedidos", pedidosValidos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar los pedidos");
                ViewBag.Error = "Error al cargar los pedidos.";
                return View("GestionPedidos", new List<Pedido>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> DescargarExcel()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                using var package = new ExcelPackage();
                var hoja = package.Workbook.Worksheets.Add("Pedidos");

                hoja.Cells[1, 1].Value = "N° Pedido";
                hoja.Cells[1, 2].Value = "Cliente";
                hoja.Cells[1, 3].Value = "Domicilio";
                hoja.Cells[1, 4].Value = "Teléfono";
                hoja.Cells[1, 5].Value = "Fecha";
                hoja.Cells[1, 6].Value = "Estado";
                hoja.Cells[1, 7].Value = "Total";

                int fila = 2;
                foreach (var pedido in pedidos)
                {
                    hoja.Cells[fila, 1].Value = pedido.NumPedido;
                    hoja.Cells[fila, 2].Value = $"{pedido.IdClienteNavigation?.Nombre} {pedido.IdClienteNavigation?.Apellido}";
                    hoja.Cells[fila, 3].Value = pedido.IdClienteNavigation?.Domicilio;
                    hoja.Cells[fila, 4].Value = pedido.IdClienteNavigation?.NumTelefono;
                    hoja.Cells[fila, 5].Value = pedido.FechaPedido?.ToString("dd/MM/yyyy HH:mm");
                    hoja.Cells[fila, 6].Value = pedido.EstadoPedido;
                    hoja.Cells[fila, 7].Value = pedido.MontoTotal ?? 0;
                    fila++;
                }

                hoja.Cells.AutoFitColumns();

                var bytes = package.GetAsByteArray();
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PedidosAdmin.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel de pedidos del administrador");
                TempData["Error"] = "No se pudo generar el archivo Excel.";
                return RedirectToAction("GestionPedidos");
            }
        }

        public IActionResult Create()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Producto producto)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            try
            {
                if (ModelState.IsValid)
                {
                    if (producto.ImagenArchivo != null)
                    {
                        var resultadoImagen = await GuardarImagenAsync(producto.ImagenArchivo);
                        if (!resultadoImagen.success)
                        {
                            ModelState.AddModelError("ImagenArchivo", resultadoImagen.error ?? "Error al procesar la imagen");
                            return View(producto);
                        }
                        producto.Imagen = resultadoImagen.fileName;
                    }

                    _context.Add(producto);
                    await _context.SaveChangesAsync();

                    TempData["Mensaje"] = "Producto creado correctamente";
                    return RedirectToAction(nameof(GestionCatalogo));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear producto");
                ModelState.AddModelError("", "Error interno al crear el producto.");
            }

            return View(producto);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            return View(producto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("IdProducto,NombreProducto,Descripcion,Precio")] Producto producto, IFormFile? ImagenArchivo)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id != producto.IdProducto) return NotFound();

            try
            {
                var productoExistente = await _context.Productos.FindAsync(id);
                if (productoExistente == null) return NotFound();

                if (ModelState.IsValid)
                {
                    productoExistente.NombreProducto = producto.NombreProducto;
                    productoExistente.Descripcion = producto.Descripcion;
                    productoExistente.Precio = producto.Precio;

                    if (ImagenArchivo != null && ImagenArchivo.Length > 0)
                    {
                        var resultadoImagen = await GuardarImagenAsync(ImagenArchivo);
                        if (!resultadoImagen.success)
                        {
                            ModelState.AddModelError("ImagenArchivo", resultadoImagen.error ?? "Error al procesar la imagen");
                            return View(productoExistente);
                        }

                        if (!string.IsNullOrEmpty(productoExistente.Imagen))
                        {
                            var rutaAnterior = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", productoExistente.Imagen);
                            if (System.IO.File.Exists(rutaAnterior))
                            {
                                System.IO.File.Delete(rutaAnterior);
                            }
                        }

                        productoExistente.Imagen = resultadoImagen.fileName;
                    }

                    await _context.SaveChangesAsync();

                    TempData["Mensaje"] = "Producto actualizado correctamente";
                    return RedirectToAction(nameof(GestionCatalogo));
                }

                return View(productoExistente);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al editar producto: {id}");
                ModelState.AddModelError("", "Error interno al editar el producto.");
                return View(producto);
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            try
            {
                var producto = await _context.Productos.FindAsync(id);
                if (producto != null)
                {
                    if (!string.IsNullOrEmpty(producto.Imagen))
                    {
                        var rutaImagen = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", producto.Imagen);
                        if (System.IO.File.Exists(rutaImagen))
                        {
                            System.IO.File.Delete(rutaImagen);
                        }
                    }

                    _context.Productos.Remove(producto);
                    await _context.SaveChangesAsync();
                    TempData["Mensaje"] = "Producto eliminado correctamente";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al eliminar producto: {id}");
                TempData["Error"] = "Error al eliminar el producto.";
            }

            return RedirectToAction(nameof(GestionCatalogo));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            return View(producto);
        }

        private async Task<(bool success, string? fileName, string? error)> GuardarImagenAsync(IFormFile archivo)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                    return (false, null, "No se seleccionó ningún archivo");

                var extension = Path.GetExtension(archivo.FileName).ToLower();
                var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (!extensionesPermitidas.Contains(extension))
                    return (false, null, "Solo se permiten archivos JPG, PNG, GIF o WEBP.");

                if (archivo.Length > 5 * 1024 * 1024)
                    return (false, null, "El archivo es demasiado grande. Máximo 5MB.");

                var nuevoNombre = $"{Guid.NewGuid()}{extension}";
                var carpeta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes");

                if (!Directory.Exists(carpeta))
                    Directory.CreateDirectory(carpeta);

                var rutaCompleta = Path.Combine(carpeta, nuevoNombre);

                using (var fileStream = new FileStream(rutaCompleta, FileMode.Create))
                {
                    await archivo.CopyToAsync(fileStream);
                }

                return (true, nuevoNombre, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar imagen");
                return (false, null, "No se pudo guardar la imagen");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CambiarEstadoAjax([FromBody] CambiarEstadoRequest request)
        {
            if (!EsAdmin())
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado" });

                var estadosValidos = new[] { "Pendiente", "En preparación", "En reparto", "Entregado" };
                if (!estadosValidos.Contains(request.NuevoEstado))
                    return Json(new { success = false, message = "Estado no válido" });

                pedido.EstadoPedido = request.NuevoEstado;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Estado actualizado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del pedido");
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }
    }

    public class CambiarEstadoRequest
    {
        public int NumPedido { get; set; }
        public string? NuevoEstado { get; set; }
    }
}
