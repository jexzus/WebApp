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

        // GET: Usuario/Login
        public IActionResult Login()
        {
            // Si ya está logueado, redirigir según rol
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

        // POST: Usuario/Login
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
                    // Establecer sesión
                    HttpContext.Session.SetString("Usuario", usuario.NombreUsuario);
                    HttpContext.Session.SetString("Rol", usuario.Rol ?? "cliente");
                    HttpContext.Session.SetInt32("IdUsuario", usuario.Id);

                    // Debug para verificar
                    System.Diagnostics.Debug.WriteLine($"Login exitoso - Usuario: {usuario.NombreUsuario}, Rol: {usuario.Rol}");

                    // Redirigir según rol
                    if (!string.IsNullOrEmpty(usuario.Rol) && usuario.Rol.ToLowerInvariant() == "admin")
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    else
                    {
                        return RedirectToAction("Catalogo", "Cliente");
                    }
                }
                else
                {
                    ViewBag.Mensaje = "Usuario o contraseña incorrectos.";
                    return View();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Login: {ex.Message}");
                ViewBag.Mensaje = "Error interno del servidor.";
                return View();
            }
        }

        // GET: Usuario/Logout
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // GET: Usuario/RegistrarCliente
        public IActionResult RegistrarCliente()
        {
            return View();
        }

        // POST: Usuario/RegistrarCliente
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

                ViewBag.Mensaje = "Cliente registrado exitosamente. Puede iniciar sesión.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al registrar cliente: {ex.Message}");
                ModelState.AddModelError("", "Error al registrar el cliente.");
                return View(model);
            }
        }

        // GET: Usuario/CrearAdmin
        public IActionResult CrearAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (string.IsNullOrEmpty(rol) || rol.ToLowerInvariant() != "admin")
                return RedirectToAction("Login");

            return View();
        }

        // POST: Usuario/CrearAdmin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearAdmin(Usuario admin)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (string.IsNullOrEmpty(rol) || rol.ToLowerInvariant() != "admin")
                return RedirectToAction("Login");

            try
            {
                if (_context.Usuarios.Any(u => u.NombreUsuario == admin.NombreUsuario))
                {
                    ModelState.AddModelError("", "El nombre de usuario ya está registrado.");
                    return View(admin);
                }

                if (ModelState.IsValid)
                {
                    admin.Rol = "admin";
                    _context.Usuarios.Add(admin);
                    await _context.SaveChangesAsync();

                    ViewBag.Mensaje = "Administrador creado exitosamente.";
                    return RedirectToAction("Index", "Admin");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al crear admin: {ex.Message}");
                ModelState.AddModelError("", "Error al crear el administrador.");
            }

            return View(admin);
        }
    }
}