// ProductoController.cs
using Microsoft.AspNetCore.Mvc;
using AppWeb1.Models;
using Microsoft.EntityFrameworkCore;

namespace AppWeb1.Controllers
{
    public class ProductoController : Controller
    {
        private readonly DeliveryDBContext _context;

        public ProductoController(DeliveryDBContext context)
        {
            _context = context;
        }

        private bool EsAdmin()
        {
            return HttpContext.Session.GetString("Rol") == "admin";
        }

        public async Task<IActionResult> Index(string busqueda)
        {
            IQueryable<Producto> productos = _context.Productos;

            if (!string.IsNullOrEmpty(busqueda))
            {
                productos = productos.Where(p => EF.Functions.Collate(p.NombreProducto, "Latin1_General_CS_AS")!.Contains(busqueda));
            }

            return View(await productos.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var producto = await _context.Productos.FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null) return NotFound();
            return View(producto);
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

            if (ModelState.IsValid)
            {
                if (producto.ImagenArchivo != null && producto.ImagenArchivo.Length > 0)
                {
                    try
                    {
                        var nombreArchivo = Path.GetFileName(producto.ImagenArchivo.FileName);
                        var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();
                        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif" };

                        if (!extensionesPermitidas.Contains(extension))
                        {
                            ModelState.AddModelError("ImagenArchivo", "Solo se permiten archivos de imagen (.jpg, .png, .gif).");
                            return View(producto);
                        }

                        var nuevoNombre = $"{Guid.NewGuid()}{extension}";
                        var carpetaImagenes = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes");
                        if (!Directory.Exists(carpetaImagenes))
                            Directory.CreateDirectory(carpetaImagenes);

                        var ruta = Path.Combine(carpetaImagenes, nuevoNombre);

                        using (var stream = new FileStream(ruta, FileMode.Create))
                        {
                            await producto.ImagenArchivo.CopyToAsync(stream);
                        }

                        producto.Imagen = nuevoNombre;
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", "Error al guardar la imagen: " + ex.Message);
                        return View(producto);
                    }
                }

                _context.Add(producto);
                await _context.SaveChangesAsync();
                return RedirectToAction("GestionCatalogo", "Admin");
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
        public async Task<IActionResult> Edit(int id, Producto producto)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id != producto.IdProducto) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var productoExistente = await _context.Productos.FindAsync(id);
                    if (productoExistente == null) return NotFound();

                    productoExistente.NombreProducto = producto.NombreProducto;
                    productoExistente.Descripcion = producto.Descripcion;
                    productoExistente.Precio = producto.Precio;

                    if (producto.ImagenArchivo != null && producto.ImagenArchivo.Length > 0)
                    {
                        var nombreArchivo = Path.GetFileName(producto.ImagenArchivo.FileName);
                        var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();
                        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif" };

                        if (!extensionesPermitidas.Contains(extension))
                        {
                            ModelState.AddModelError("ImagenArchivo", "Solo se permiten archivos de imagen (.jpg, .png, .gif).");
                            return View(producto);
                        }

                        var nuevoNombre = $"{Guid.NewGuid()}{extension}";
                        var ruta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", nuevoNombre);

                        using (var stream = new FileStream(ruta, FileMode.Create))
                        {
                            await producto.ImagenArchivo.CopyToAsync(stream);
                        }

                        productoExistente.Imagen = nuevoNombre;
                    }

                    _context.Update(productoExistente);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Productos.Any(e => e.IdProducto == id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction("GestionCatalogo", "Admin");
            }
            return View(producto);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");
            if (id == null) return NotFound();

            var producto = await _context.Productos.FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null) return NotFound();

            return View(producto);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!EsAdmin()) return RedirectToAction("Login", "Usuario");

            var producto = await _context.Productos.FindAsync(id);
            if (producto != null)
            {
                _context.Productos.Remove(producto);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("GestionCatalogo", "Admin");
        }
    }
}
