using Microsoft.AspNetCore.Mvc;
using AppWeb1.Models;
using Microsoft.EntityFrameworkCore;

namespace AppWeb1.Controllers
{
    public class UsuarioController : Controller
    {
        private readonly DeliveryDBContext _context;

        public UsuarioController(DeliveryDBContext context)
        {
            _context = context;
        }

        public IActionResult Login()
        {
            var rolActual = HttpContext.Session.GetString("Rol");
            if (!string.IsNullOrEmpty(rolActual))
            {
                if (rolActual.ToLowerInvariant() == "admin")
                    return RedirectToAction("Index", "Admin");
                else
                    return RedirectToAction("Catalogo", "Cliente");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string nombreUsuario, string contraseña)
        {
            try
            {
                if (string.IsNullOrEmpty(nombreUsuario) || string.IsNullOrEmpty(contraseña))
                {
                    ViewBag.Mensaje = "Por favor ingrese usuario y contraseña.";
                    return View();
                }

                var usuario = await _context.Usuarios
                    .FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario && u.Contraseña == contraseña);

                if (usuario != null)
                {
                    HttpContext.Session.SetString("Usuario", usuario.NombreUsuario);
                    HttpContext.Session.SetString("Rol", usuario.Rol ?? "cliente");
                    HttpContext.Session.SetInt32("IdUsuario", usuario.Id);

                    if (usuario.Rol?.ToLowerInvariant() == "admin")
                        return RedirectToAction("Index", "Admin");
                    else
                        return RedirectToAction("Catalogo", "Cliente");
                }

                ViewBag.Mensaje = "Usuario o contraseña incorrectos.";
                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Login: {ex.Message}");
                ViewBag.Mensaje = "Error interno del servidor.";
                return View();
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        public IActionResult RegistrarCliente()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarCliente(RegistroClienteViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                if (_context.Usuarios.Any(u => u.NombreUsuario == model.NombreUsuario))
                {
                    ModelState.AddModelError("", "Ese nombre de usuario ya está registrado.");
                    return View(model);
                }

                if (_context.Clientes.Any(c => c.NumTelefono == model.NumTelefono))
                {
                    ModelState.AddModelError("", "Ya existe un cliente con ese número de teléfono.");
                    return View(model);
                }

                var usuario = new Usuario
                {
                    NombreUsuario = model.NombreUsuario,
                    Contraseña = model.Contraseña,
                    Rol = "cliente"
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                var cliente = new Cliente
                {
                    Nombre = model.Nombre,
                    Apellido = model.Apellido,
                    NumTelefono = model.NumTelefono,
                    Domicilio = model.Domicilio,
                    IdUsuario = usuario.Id
                };

                _context.Clientes.Add(cliente);
                await _context.SaveChangesAsync();

                TempData["RegistroExitoso"] = true;
                return RedirectToAction("Login");
            }
            catch
            {
                ModelState.AddModelError("", "Error al registrar el cliente.");
                return View(model);
            }
        }

        public async Task<IActionResult> CrearAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (rol?.ToLowerInvariant() != "admin")
                return RedirectToAction("Login");

            var administradores = await _context.Usuarios
                .Where(u => u.Rol.ToLower() == "admin")
                .OrderBy(u => u.NombreUsuario)
                .ToListAsync();

            ViewBag.Administradores = administradores;

            return View(new Usuario());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearAdmin(Usuario admin)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (rol?.ToLowerInvariant() != "admin")
                return RedirectToAction("Login");

            if (_context.Usuarios.Any(u => u.NombreUsuario == admin.NombreUsuario))
            {
                ModelState.AddModelError("", "El nombre de usuario ya está registrado.");
            }

            if (!ModelState.IsValid)
            {
                var admins = await _context.Usuarios
                    .Where(u => u.Rol.ToLower() == "admin")
                    .ToListAsync();
                ViewBag.Administradores = admins;
                return View(admin);
            }

            admin.Rol = "admin";
            _context.Usuarios.Add(admin);
            await _context.SaveChangesAsync();

            TempData["AdminCreado"] = true;
            return RedirectToAction("CrearAdmin");
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarAdmin([FromBody] Usuario admin)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (rol?.ToLowerInvariant() != "admin")
                return Json(new { success = false, message = "No autorizado" });

            var existente = await _context.Usuarios.FindAsync(admin.Id);
            if (existente == null || existente.Rol.ToLower() != "admin")
                return Json(new { success = false, message = "Administrador no encontrado" });

            try
            {
                existente.NombreUsuario = admin.NombreUsuario;
                existente.Contraseña = admin.Contraseña;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Administrador actualizado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EliminarAdmin(int id)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (rol?.ToLowerInvariant() != "admin")
                return Json(new { success = false, message = "No autorizado" });

            var admin = await _context.Usuarios.FindAsync(id);
            if (admin == null || admin.Rol.ToLower() != "admin")
                return Json(new { success = false, message = "Administrador no encontrado" });

            try
            {
                _context.Usuarios.Remove(admin);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Administrador eliminado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
        }
    }
}
