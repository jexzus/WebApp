    using System.IO;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Hosting;
    using AppWeb1.Models;
    using AppWeb1.Data;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    namespace AppWeb1.Areas.Admin.Controllers
    {
        [Area("Admin")]
        public class CatalogoController : Controller
        {
            private readonly DeliveryDBContext _context;
            private readonly ILogger<CatalogoController> _logger;
            private readonly IWebHostEnvironment _env;

            public CatalogoController(DeliveryDBContext context, ILogger<CatalogoController> logger, IWebHostEnvironment env)
            {
                _context = context;
                _logger = logger;
                _env = env;
            }

            // El método EsAdmin() se elimina ya que la nueva lógica lo reemplaza.

            // GET: /Admin/Catalogo
            public async Task<IActionResult> Index()
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    // Usa TempData["Error"] como solicitaste
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }
                // *** FIN DE LA MODIFICACIÓN ***

                try
                {
                    var productos = await _context.Productos
                        .OrderBy(p => p.NombreProducto)
                        .ToListAsync();

                    return View(productos);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al cargar productos");
                    TempData["Error"] = "Error al cargar los productos.";
                    return View(new List<Producto>());
                }
            }

            // GET: /Admin/Catalogo/Create
            public IActionResult Create()
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                return View();
            }

            // POST: /Admin/Catalogo/Create
            [HttpPost]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Create(Producto producto)
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Revisá los datos del producto.";
                    return View(producto);
                }

                try
                {
                    if (producto.ImagenArchivo != null)
                    {
                        var (ok, fileName, error) = await GuardarImagenAsync(producto.ImagenArchivo);
                        if (!ok)
                        {
                            ModelState.AddModelError("ImagenArchivo", error ?? "Error al procesar la imagen");
                            return View(producto);
                        }
                        producto.Imagen = fileName;
                    }

                    _context.Productos.Add(producto);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Producto creado con éxito.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear producto");
                    TempData["Error"] = "No se pudo crear el producto. " + ex.Message;
                    return View(producto);
                }
            }

            // GET: /Admin/Catalogo/Edit/5
            public async Task<IActionResult> Edit(int? id)
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                if (id == null)
                {
                    TempData["Error"] = "ID de producto no especificado.";
                    return RedirectToAction(nameof(Index));
                }

                var producto = await _context.Productos.FindAsync(id);
                if (producto == null)
                {
                    TempData["Error"] = "El producto no existe.";
                    return RedirectToAction(nameof(Index));
                }

                return View(producto);
            }

            // POST: /Admin/Catalogo/Edit/5
            [HttpPost]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Edit(int id, [Bind("IdProducto,NombreProducto,Descripcion,Precio,Imagen")] Producto producto, IFormFile? ImagenArchivo)
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                if (id != producto.IdProducto)
                {
                    TempData["Error"] = "ID de producto inválido.";
                    return RedirectToAction(nameof(Index));
                }

                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Revisá los datos del producto.";
                    return View(producto);
                }

                try
                {
                    var existente = await _context.Productos.FindAsync(id);
                    if (existente == null)
                    {
                        TempData["Error"] = "El producto no existe.";
                        return RedirectToAction(nameof(Index));
                    }

                    existente.NombreProducto = producto.NombreProducto;
                    existente.Descripcion = producto.Descripcion;
                    existente.Precio = producto.Precio;

                    if (ImagenArchivo != null && ImagenArchivo.Length > 0)
                    {
                        var (ok, fileName, error) = await GuardarImagenAsync(ImagenArchivo);
                        if (!ok)
                        {
                            ModelState.AddModelError("ImagenArchivo", error ?? "Error al procesar la imagen");
                            return View(existente);
                        }

                        if (!string.IsNullOrEmpty(existente.Imagen))
                        {
                            var rutaAnterior = Path.Combine(_env.WebRootPath, "imagenes", existente.Imagen);
                            if (System.IO.File.Exists(rutaAnterior))
                                System.IO.File.Delete(rutaAnterior);
                        }
                        existente.Imagen = fileName!;
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Producto actualizado con éxito.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al editar producto {Id}", id);
                    TempData["Error"] = "No se pudo actualizar el producto. " + ex.Message;
                    return View(producto);
                }
            }

            // GET: /Admin/Catalogo/Delete/5
            public async Task<IActionResult> Delete(int? id)
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                if (id == null) return NotFound();

                var producto = await _context.Productos.FindAsync(id);
                if (producto == null) return NotFound();

                return View(producto);
            }

            // POST: /Admin/Catalogo/Delete/5
            [HttpPost, ActionName("Delete")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> DeleteConfirmed(int id)
            {
                // *** LÓGICA DE AUTORIZACIÓN MODIFICADA ***
                var rol = HttpContext.Session.GetString("Rol");
                if (rol == null)
                    return RedirectToAction("Login", "Usuario", new { area = "" });

                if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "No tienes permisos suficientes para acceder a Gestión de Catálogo.";
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }

                var producto = await _context.Productos.FindAsync(id);
                if (producto == null)
                {
                    TempData["Error"] = "El producto no existe.";
                    return RedirectToAction(nameof(Index));
                }

                try
                {
                    if (!string.IsNullOrEmpty(producto.Imagen))
                    {
                        var ruta = Path.Combine(_env.WebRootPath, "imagenes", producto.Imagen);
                        if (System.IO.File.Exists(ruta)) System.IO.File.Delete(ruta);
                    }

                    _context.Productos.Remove(producto);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Producto eliminado con éxito.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al eliminar producto {Id}", id);
                    TempData["Error"] = "No se pudo eliminar el producto. " + ex.Message;
                }

                return RedirectToAction(nameof(Index));
            }

            // ===== helper de archivos =====
            private async Task<(bool success, string? fileName, string? error)> GuardarImagenAsync(IFormFile archivo)
            {
                try
                {
                    if (archivo == null || archivo.Length == 0)
                        return (false, null, "No se seleccionó ningún archivo.");

                    var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
                    var permitidas = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    if (!permitidas.Contains(extension))
                        return (false, null, "Solo se permiten JPG, PNG, GIF o WEBP.");

                    if (archivo.Length > 5 * 1024 * 1024) // 5 MB
                        return (false, null, "El archivo es demasiado grande. Máximo 5MB.");

                    var nuevoNombre = $"{Guid.NewGuid():N}{extension}";
                    var carpeta = Path.Combine(_env.WebRootPath, "imagenes");
                    if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);

                    var ruta = Path.Combine(carpeta, nuevoNombre);
                    using (var fs = new FileStream(ruta, FileMode.Create))
                        await archivo.CopyToAsync(fs);

                    return (true, nuevoNombre, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al guardar imagen");
                    return (false, null, "No se pudo guardar la imagen.");
                }
            }
        }
    }