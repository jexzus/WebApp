using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppWeb1.Models;
using Microsoft.AspNetCore.Http;
using System.IO;

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
            try
            {
                var rol = HttpContext.Session.GetString("Rol");
                var usuario = HttpContext.Session.GetString("Usuario");
                if (string.IsNullOrEmpty(rol) || string.IsNullOrEmpty(usuario)) return false;
                return rol.Trim().Equals("admin", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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

        // Método auxiliar para guardar imágenes de forma segura
        private async Task<(bool success, string? fileName, string? error)> GuardarImagenAsync(IFormFile archivo)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                    return (false, null, "No se seleccionó ningún archivo");

                // Validar tamaño (máximo 5MB)
                if (archivo.Length > 5 * 1024 * 1024)
                    return (false, null, "El archivo es demasiado grande. Máximo 5MB.");

                // Validar extensión
                var extension = Path.GetExtension(archivo.FileName).ToLower();
                var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (!extensionesPermitidas.Contains(extension))
                    return (false, null, "Solo se permiten archivos JPG, PNG, GIF o WEBP.");

                // Generar nombre único
                var nuevoNombre = $"{Guid.NewGuid()}{extension}";
                var carpetaImagenes = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes");

                // Crear directorio si no existe
                if (!Directory.Exists(carpetaImagenes))
                {
                    Directory.CreateDirectory(carpetaImagenes);
                    _logger.LogInformation($"Directorio creado: {carpetaImagenes}");
                }

                var rutaCompleta = Path.Combine(carpetaImagenes, nuevoNombre);

                _logger.LogInformation($"Intentando guardar imagen en: {rutaCompleta}");

                // Guardar archivo usando using para asegurar liberación de recursos
                using (var fileStream = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write))
                {
                    await archivo.CopyToAsync(fileStream);
                    await fileStream.FlushAsync(); // Asegurar que se escriba al disco
                }

                // Verificar que el archivo se guardó correctamente
                if (System.IO.File.Exists(rutaCompleta))
                {
                    _logger.LogInformation($"Imagen guardada exitosamente: {nuevoNombre}");
                    return (true, nuevoNombre, null);
                }
                else
                {
                    return (false, null, "Error: El archivo no se pudo guardar correctamente");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Error de permisos al guardar imagen");
                return (false, null, "Error de permisos. Verifique los permisos de la carpeta de imágenes.");
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "Directorio no encontrado");
                return (false, null, "Error: No se pudo acceder al directorio de imágenes.");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Error de E/S al guardar imagen");
                return (false, null, "Error de entrada/salida. El archivo puede estar en uso o bloqueado.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al guardar imagen");
                return (false, null, $"Error inesperado: {ex.Message}");
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
                    // Procesar imagen si se proporcionó
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

                    // Guardar producto en base de datos
                    _context.Add(producto);
                    await _context.SaveChangesAsync();

                    TempData["Mensaje"] = "Producto creado correctamente";
                    _logger.LogInformation($"Producto creado: {producto.NombreProducto}");

                    return RedirectToAction(nameof(GestionCatalogo));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear producto");
                ModelState.AddModelError("", "Error interno al crear el producto. Inténtelo nuevamente.");
            }

            return View(producto);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            try
            {
                var producto = await _context.Productos.FindAsync(id);
                if (producto == null) return NotFound();
                return View(producto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cargar producto para editar: {id}");
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Producto producto)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id != producto.IdProducto) return NotFound();

            try
            {
                var productoExistente = await _context.Productos.FindAsync(id);
                if (productoExistente == null) return NotFound();

                if (ModelState.IsValid)
                {
                    // Actualizar campos básicos
                    productoExistente.NombreProducto = producto.NombreProducto;
                    productoExistente.Descripcion = producto.Descripcion;
                    productoExistente.Precio = producto.Precio;

                    // Procesar nueva imagen si se proporcionó
                    if (producto.ImagenArchivo != null)
                    {
                        var resultadoImagen = await GuardarImagenAsync(producto.ImagenArchivo);
                        if (!resultadoImagen.success)
                        {
                            ModelState.AddModelError("ImagenArchivo", resultadoImagen.error ?? "Error al procesar la imagen");
                            return View(productoExistente);
                        }

                        // Eliminar imagen anterior si existe
                        if (!string.IsNullOrEmpty(productoExistente.Imagen))
                        {
                            try
                            {
                                var rutaAnterior = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", productoExistente.Imagen);
                                if (System.IO.File.Exists(rutaAnterior))
                                {
                                    System.IO.File.Delete(rutaAnterior);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"No se pudo eliminar la imagen anterior: {productoExistente.Imagen}");
                            }
                        }

                        productoExistente.Imagen = resultadoImagen.fileName;
                    }

                    _context.Update(productoExistente);
                    await _context.SaveChangesAsync();

                    TempData["Mensaje"] = "Producto actualizado correctamente";
                    _logger.LogInformation($"Producto actualizado: {productoExistente.NombreProducto}");

                    return RedirectToAction(nameof(GestionCatalogo));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar producto: {id}");
                ModelState.AddModelError("", "Error interno al actualizar el producto. Inténtelo nuevamente.");
            }

            return View(producto);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            try
            {
                var producto = await _context.Productos.FindAsync(id);
                if (producto == null) return NotFound();
                return View(producto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cargar producto para eliminar: {id}");
                return NotFound();
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
                    // Eliminar imagen física si existe
                    if (!string.IsNullOrEmpty(producto.Imagen))
                    {
                        try
                        {
                            var rutaImagen = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", producto.Imagen);
                            if (System.IO.File.Exists(rutaImagen))
                            {
                                System.IO.File.Delete(rutaImagen);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"No se pudo eliminar la imagen: {producto.Imagen}");
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
                TempData["Error"] = "Error al eliminar el producto. Puede que esté siendo usado en pedidos.";
            }

            return RedirectToAction(nameof(GestionCatalogo));
        }

        // Resto de métodos sin cambios...
        public async Task<IActionResult> GestionPedidos()
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido).ToListAsync();
                return View("GestionPedidos", pedidos);
            }
            catch
            {
                ViewBag.Error = "Error al cargar los pedidos";
                return View("GestionPedidos", new List<Pedido>());
            }
        }

        public async Task<IActionResult> Detalle(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            var pedido = await _context.Pedidos
                .Include(p => p.IdClienteNavigation)
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .FirstOrDefaultAsync(p => p.NumPedido == id);

            if (pedido == null) return NotFound();
            return View(pedido);
        }

        public async Task<IActionResult> CambiarEstado(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null) return NotFound();
            return View(pedido);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, string nuevoEstado)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null) return NotFound();

            pedido.EstadoPedido = nuevoEstado;
            try
            {
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = "Estado del pedido actualizado correctamente";
            }
            catch
            {
                TempData["Error"] = "Error al actualizar el estado del pedido";
            }

            return RedirectToAction(nameof(GestionPedidos));
        }

        [HttpPost]
        public async Task<IActionResult> CambiarEstadoAjax([FromBody] CambiarEstadoRequest request)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado" });

            try
            {
                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null) return Json(new { success = false, message = "Pedido no encontrado" });

                var estadosValidos = new[] { "Pendiente", "En preparación", "En reparto", "Entregado" };
                if (!estadosValidos.Contains(request.NuevoEstado))
                    return Json(new { success = false, message = "Estado no válido" });

                pedido.EstadoPedido = request.NuevoEstado;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Estado actualizado correctamente" });
            }
            catch
            {
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Usuario");
        }
    }

    public class CambiarEstadoRequest
    {
        public int NumPedido { get; set; }
        public string? NuevoEstado { get; set; }
    }
}