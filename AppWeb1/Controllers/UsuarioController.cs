using AppWeb1.Data;
using AppWeb1.Helpers;
using AppWeb1.Models;
using AppWeb1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace AppWeb1.Controllers
{
    public class UsuarioController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<UsuarioController> _logger;

        private const string SK_PreRegEmail = "PreRegEmail";
        private const string SK_PreRegCodeHash = "PreRegCodeHash";
        private const string SK_PreRegCodeUntil = "PreRegCodeExpiry";
        private const string SK_PreRegPwdHash = "PreRegPwdHash";

        // Nueva constante para Recuperar Contraseña
        private const string SK_RecupEmail = "RecupEmail";

        public UsuarioController(DeliveryDBContext context, IEmailSender emailSender, ILogger<UsuarioController> logger)
        {
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ======================= HELPERS DE AUTORIZACIÓN =======================

        private bool EsSuperAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) && rol.Equals("superadmin", StringComparison.OrdinalIgnoreCase);
        }

        private bool EsAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) && rol.Equals("admin", StringComparison.OrdinalIgnoreCase);
        }

        private bool EsAdminOSuperAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) &&
                   (rol.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                    rol.Equals("superadmin", StringComparison.OrdinalIgnoreCase));
        }

        // ======================= LOGIN =======================

        [HttpGet]
        public IActionResult Login()
        {
            var rolActual = HttpContext.Session.GetString("Rol");
            if (!string.IsNullOrEmpty(rolActual))
            {
                return RedirectBasedOnRole(rolActual);
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string nombreUsuario, string contraseña)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrWhiteSpace(contraseña))
                {
                    TempData["Error"] = "Debe completar usuario y contraseña.";
                    return View();
                }

                var usuario = await _context.Usuarios
                    .FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);

                if (usuario != null && BCrypt.Net.BCrypt.Verify(contraseña, usuario.Contraseña))
                {
                    var userRol = usuario.Rol ?? "cliente";
                    HttpContext.Session.SetString("Usuario", usuario.NombreUsuario);
                    HttpContext.Session.SetString("Rol", userRol);
                    HttpContext.Session.SetInt32("IdUsuario", usuario.Id);

                    TempData["Success"] = $"¡Bienvenido, {usuario.NombreUsuario}!";

                    return RedirectBasedOnRole(userRol);
                }

                TempData["Error"] = "Usuario o contraseña incorrectos.";
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Login para el usuario {nombreUsuario}.", nombreUsuario);
                TempData["Error"] = "Error interno del servidor.";
                return View();
            }
        }

        // ======================= LOGOUT =======================

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            TempData["Success"] = "Sesión cerrada correctamente.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult LogoutPost()
        {
            HttpContext.Session.Clear();
            TempData["Success"] = "Sesión cerrada correctamente.";
            return RedirectToAction("Login", "Usuario");
        }

        // ======================= PRE-REGISTRO =======================

        [HttpGet]
        public IActionResult PreRegistro()
        {
            HttpContext.Session.Remove(SK_PreRegEmail);
            HttpContext.Session.Remove(SK_PreRegCodeHash);
            HttpContext.Session.Remove(SK_PreRegCodeUntil);
            HttpContext.Session.Remove(SK_PreRegPwdHash);

            return View(new PreRegistroVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PreRegistro(PreRegistroVM model)
        {
            var email = model.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                ModelState.AddModelError(nameof(model.Email), "Debe ingresar un email válido.");
                return View(model);
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                ModelState.AddModelError(nameof(model.Email), "El formato del email no es válido.");
                return View(model);
            }

            var emailTomado = await _context.Clientes.AnyAsync(c => c.Email == email);
            if (emailTomado)
            {
                ModelState.AddModelError(nameof(model.Email), "Este email ya está registrado. Intenta iniciar sesión.");
                return View(model);
            }

            try
            {
                await EnviarCodigoAsync(email, minutesValid: 10);
                return RedirectToAction("ConfirmarPreRegistro");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "Error SMTP enviando código de pre-registro a {Email}", email);
                ModelState.AddModelError(string.Empty, "No se pudo enviar el email. Verifica que tu dirección sea correcta.");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general enviando código de pre-registro a {Email}", email);
                ModelState.AddModelError(string.Empty, "Error al procesar tu solicitud. Intenta nuevamente más tarde.");
                return View(model);
            }
        }

        // ======================= CONFIRMACIÓN DE PRE-REGISTRO =======================

        [HttpGet]
        public IActionResult ConfirmarPreRegistro()
        {
            var email = HttpContext.Session.GetString(SK_PreRegEmail);
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("PreRegistro");

            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReenviarCodigo()
        {
            var email = HttpContext.Session.GetString(SK_PreRegEmail);
            if (string.IsNullOrWhiteSpace(email))
                return RedirectToAction("PreRegistro");

            try
            {
                await EnviarCodigoAsync(email, minutesValid: 10);
                TempData["Success"] = "Te reenviamos un nuevo código. Revisa tu correo.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reenviando código de pre-registro");
                TempData["Error"] = "No se pudo reenviar el código. Intenta nuevamente más tarde.";
            }
            return RedirectToAction("ConfirmarPreRegistro");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ConfirmarPreRegistro(ConfirmarPreRegistroVM model)
        {
            var email = HttpContext.Session.GetString(SK_PreRegEmail);
            var codeHash = HttpContext.Session.GetString(SK_PreRegCodeHash);
            var untilIso = HttpContext.Session.GetString(SK_PreRegCodeUntil);

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(codeHash) || string.IsNullOrEmpty(untilIso))
                return RedirectToAction("PreRegistro");

            model.Email = model.Email?.Trim() ?? "";
            model.Codigo = model.Codigo?.Trim() ?? "";

            if (!string.Equals(model.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Email), "El email no coincide con el verificado.");
                return View(model);
            }

            if (!DateTime.TryParse(untilIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
                return RedirectToAction("PreRegistro");

            if (DateTime.UtcNow > until)
            {
                ModelState.AddModelError(nameof(model.Codigo), "El código ha vencido. Solicitá uno nuevo.");
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Codigo) || model.Codigo.Length != 6)
            {
                ModelState.AddModelError(nameof(model.Codigo), "El código debe tener 6 dígitos.");
                return View(model);
            }

            if (!BCrypt.Net.BCrypt.Verify(model.Codigo, codeHash))
            {
                ModelState.AddModelError(nameof(model.Codigo), "Código incorrecto.");
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Contraseña) || model.Contraseña.Length < 6)
            {
                ModelState.AddModelError(nameof(model.Contraseña), "La contraseña debe tener al menos 6 caracteres.");
                return View(model);
            }

            if (model.Contraseña != model.ConfirmarContraseña)
            {
                ModelState.AddModelError(nameof(model.ConfirmarContraseña), "Las contraseñas no coinciden.");
                return View(model);
            }

            var pwdHash = BCrypt.Net.BCrypt.HashPassword(model.Contraseña, workFactor: 12);
            HttpContext.Session.SetString(SK_PreRegPwdHash, pwdHash);

            HttpContext.Session.Remove(SK_PreRegCodeHash);
            HttpContext.Session.Remove(SK_PreRegCodeUntil);

            TempData["PreRegOk"] = true;
            return RedirectToAction("RegistrarCliente");
        }

        // ======================= REGISTRO DE CLIENTE =======================

        [HttpGet]
        public IActionResult RegistrarCliente()
        {
            var emailPre = HttpContext.Session.GetString(SK_PreRegEmail);
            var vm = new RegistrarClienteVM();

            if (!string.IsNullOrEmpty(emailPre))
            {
                vm.Email = emailPre;
                ViewBag.PreReg = true;
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarCliente(RegistrarClienteVM model)
        {
            var emailPre = HttpContext.Session.GetString(SK_PreRegEmail);
            var preHash = HttpContext.Session.GetString(SK_PreRegPwdHash);
            bool cameFromPreReg = !string.IsNullOrEmpty(emailPre) && !string.IsNullOrEmpty(preHash);

            model.NombreUsuario = model.NombreUsuario?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim() ?? string.Empty;
            model.NumTelefono = model.NumTelefono?.Trim() ?? string.Empty;

            if (cameFromPreReg)
            {
                model.Email = emailPre!;
                ModelState.Remove(nameof(model.Contraseña));
                ModelState.Remove(nameof(model.ConfirmarContraseña));
            }

            if (!ModelState.IsValid)
            {
                if (cameFromPreReg) ViewBag.PreReg = true;
                TempData["Error"] = "Por favor, revisa los datos ingresados.";
                return View(model);
            }

            if (await _context.Usuarios.AnyAsync(u => u.NombreUsuario == model.NombreUsuario))
            {
                ModelState.AddModelError(nameof(model.NombreUsuario), "El nombre de usuario ya existe.");
                if (cameFromPreReg) ViewBag.PreReg = true;
                TempData["Error"] = "El nombre de usuario ya existe.";
                return View(model);
            }

            if (await _context.Clientes.AnyAsync(c => c.NumTelefono == model.NumTelefono))
            {
                ModelState.AddModelError(nameof(model.NumTelefono), "El teléfono ya está registrado.");
                if (cameFromPreReg) ViewBag.PreReg = true;
                TempData["Error"] = "El teléfono ya está registrado.";
                return View(model);
            }

            if (await _context.Clientes.AnyAsync(c => c.Email == model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "El email ya está registrado.");
                if (cameFromPreReg) ViewBag.PreReg = true;
                TempData["Error"] = "El email ya está registrado.";
                return View(model);
            }

            string hash;
            if (cameFromPreReg)
            {
                hash = preHash!;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.Contraseña) || model.Contraseña.Length < 6)
                {
                    ModelState.AddModelError(nameof(model.Contraseña), "La contraseña debe tener al menos 6 caracteres.");
                    TempData["Error"] = "La contraseña debe tener al menos 6 caracteres.";
                    return View(model);
                }
                if (model.Contraseña != model.ConfirmarContraseña)
                {
                    ModelState.AddModelError(nameof(model.ConfirmarContraseña), "Las contraseñas no coinciden.");
                    TempData["Error"] = "Las contraseñas no coinciden.";
                    return View(model);
                }
                hash = BCrypt.Net.BCrypt.HashPassword(model.Contraseña, workFactor: 12);
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var usuario = new Usuario
                {
                    NombreUsuario = model.NombreUsuario!,
                    Contraseña = hash,
                    Rol = "cliente"
                };
                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                var cliente = new Cliente
                {
                    Nombre = model.Nombre!,
                    Apellido = model.Apellido!,
                    Email = model.Email!,
                    Domicilio = model.Domicilio ?? string.Empty,
                    NumTelefono = model.NumTelefono!,
                    IdUsuario = usuario.Id
                };
                _context.Clientes.Add(cliente);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                if (cameFromPreReg)
                {
                    HttpContext.Session.Remove(SK_PreRegEmail);
                    HttpContext.Session.Remove(SK_PreRegPwdHash);
                }

                TempData["Success"] = "Cuenta creada. Ya podés iniciar sesión.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error al registrar un nuevo cliente.");
                TempData["Error"] = "Ocurrió un error al registrar. Intenta nuevamente.";
                if (cameFromPreReg) ViewBag.PreReg = true;
                return View(model);
            }
        }

        // ======================= GESTIÓN DE USUARIOS (STAFF) =======================

        [HttpGet]
        public async Task<IActionResult> CrearUsuario()
        {
            if (!EsAdminOSuperAdmin())
            {
                var rol = HttpContext.Session.GetString("Rol");
                if (string.IsNullOrEmpty(rol))
                    return RedirectToAction("Login");

                TempData["Error"] = "No tienes permisos para gestionar usuarios.";
                return RedirectBasedOnRole(rol);
            }

            var staffUsuarios = await _context.Usuarios
                .Where(u => u.Rol != null && u.Rol != "cliente")
                .OrderBy(u => u.Rol)
                .ThenBy(u => u.NombreUsuario)
                .ToListAsync();

            ViewBag.StaffUsuarios = staffUsuarios;
            ViewBag.EsSuperAdmin = EsSuperAdmin();

            return View("CrearUsuario", new Usuario());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUsuario(Usuario nuevoUsuario, string Rol, string? Nombre, string? Apellido)
        {
            if (!EsAdminOSuperAdmin())
            {
                return RedirectToAction("AccesoDenegado");
            }

            var rolSesion = HttpContext.Session.GetString("Rol")?.ToLowerInvariant();
            var rolNuevo = (Rol ?? "").Trim().ToLowerInvariant();

            Nombre = (Nombre ?? "").Trim();
            Apellido = (Apellido ?? "").Trim();

            if (string.IsNullOrEmpty(rolNuevo))
            {
                ModelState.AddModelError("Rol", "Debe seleccionar un rol.");
            }
            else if (rolNuevo == "admin" && rolSesion != "superadmin")
            {
                ModelState.AddModelError("Rol", "Solo un SuperAdmin puede crear otros Admins.");
            }
            else if (rolNuevo != "admin" && rolNuevo != "vendedor" && rolNuevo != "repartidor")
            {
                ModelState.AddModelError("Rol", "Rol no válido.");
            }

            if (string.IsNullOrWhiteSpace(nuevoUsuario.NombreUsuario))
                ModelState.AddModelError(nameof(nuevoUsuario.NombreUsuario), "El nombre de usuario es obligatorio.");

            if (string.IsNullOrWhiteSpace(nuevoUsuario.Contraseña) || nuevoUsuario.Contraseña.Length < 6)
                ModelState.AddModelError(nameof(nuevoUsuario.Contraseña), "La contraseña debe tener al menos 6 caracteres.");

            if (await _context.Usuarios.AnyAsync(u => u.NombreUsuario == nuevoUsuario.NombreUsuario))
                ModelState.AddModelError(nameof(nuevoUsuario.NombreUsuario), "El nombre de usuario ya está registrado.");

            if (rolNuevo == "vendedor" || rolNuevo == "repartidor")
            {
                if (string.IsNullOrWhiteSpace(Nombre))
                    ModelState.AddModelError("Nombre", "El nombre es obligatorio para vendedores y repartidores.");

                if (string.IsNullOrWhiteSpace(Apellido))
                    ModelState.AddModelError("Apellido", "El apellido es obligatorio para vendedores y repartidores.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Error al crear el usuario. Revisa los datos.";
                var staffUsuarios = await _context.Usuarios
                    .Where(u => u.Rol != null && u.Rol != "cliente")
                    .OrderBy(u => u.Rol)
                    .ThenBy(u => u.NombreUsuario)
                    .ToListAsync();

                ViewBag.StaffUsuarios = staffUsuarios;
                ViewBag.EsSuperAdmin = EsSuperAdmin();
                ViewBag.StaffNombre = Nombre;
                ViewBag.StaffApellido = Apellido;

                return View("CrearUsuario", nuevoUsuario);
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                nuevoUsuario.Contraseña = BCrypt.Net.BCrypt.HashPassword(nuevoUsuario.Contraseña, workFactor: 12);
                nuevoUsuario.Rol = rolNuevo;

                _context.Usuarios.Add(nuevoUsuario);
                await _context.SaveChangesAsync();

                if (rolNuevo == "vendedor")
                {
                    var vend = new Vendedor
                    {
                        IdUsuario = nuevoUsuario.Id,
                        Activo = true,
                        Nombre = Nombre,
                        Apellido = Apellido
                    };
                    _context.Vendedores.Add(vend);
                }
                else if (rolNuevo == "repartidor")
                {
                    var rep = new Repartidor
                    {
                        IdUsuario = nuevoUsuario.Id,
                        Activo = true,
                        Nombre = Nombre,
                        Apellido = Apellido
                    };
                    _context.Repartidores.Add(rep);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Success"] = $"Usuario '{nuevoUsuario.NombreUsuario}' ({rolNuevo}) creado correctamente.";
                return RedirectToAction("CrearUsuario");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error al crear nuevo usuario de staff.");
                TempData["Error"] = "Error de base de datos al crear el usuario.";

                var staffUsuarios = await _context.Usuarios
                    .Where(u => u.Rol != null && u.Rol != "cliente")
                    .OrderBy(u => u.Rol)
                    .ThenBy(u => u.NombreUsuario)
                    .ToListAsync();

                ViewBag.StaffUsuarios = staffUsuarios;
                ViewBag.EsSuperAdmin = EsSuperAdmin();
                ViewBag.StaffNombre = Nombre;
                ViewBag.StaffApellido = Apellido;

                return View("CrearUsuario", nuevoUsuario);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarAdmin([FromBody] Usuario admin)
        {
            if (!EsAdminOSuperAdmin())
                return Json(new { success = false, message = "No autorizado" });

            var existente = await _context.Usuarios.FindAsync(admin.Id);
            if (existente == null)
                return Json(new { success = false, message = "Usuario no encontrado" });

            if (existente.Rol.Equals("superadmin", StringComparison.OrdinalIgnoreCase) && !EsSuperAdmin())
            {
                return Json(new { success = false, message = "No tienes permisos para modificar a un SuperAdmin" });
            }

            if (existente.Rol.Equals("cliente", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "No se puede modificar un cliente desde este panel" });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(admin.NombreUsuario))
                    return Json(new { success = false, message = "El nombre de usuario es obligatorio." });

                existente.NombreUsuario = admin.NombreUsuario;

                if (!string.IsNullOrWhiteSpace(admin.Contraseña))
                {
                    if (admin.Contraseña.Length < 6)
                        return Json(new { success = false, message = "La contraseña debe tener al menos 6 caracteres." });

                    existente.Contraseña = BCrypt.Net.BCrypt.HashPassword(admin.Contraseña, workFactor: 12);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Usuario actualizado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar usuario con ID: {id}", admin.Id);
                return Json(new { success = false, message = "Error al actualizar: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EliminarAdmin(int id)
        {
            if (!EsAdminOSuperAdmin())
                return Json(new { success = false, message = "No autorizado" });

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null)
                return Json(new { success = false, message = "Usuario no encontrado" });

            if (usuario.Rol.Equals("superadmin", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "No se puede eliminar a un SuperAdmin" });

            var idUsuarioSesion = HttpContext.Session.GetInt32("IdUsuario");
            if (usuario.Id == idUsuarioSesion)
                return Json(new { success = false, message = "No podés eliminar tu propia cuenta" });

            if (usuario.Rol.Equals("cliente", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "No se puede eliminar un cliente desde este panel" });

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (usuario.Rol.Equals("vendedor", StringComparison.OrdinalIgnoreCase))
                {
                    var vendedor = await _context.Vendedores
                        .FirstOrDefaultAsync(v => v.IdUsuario == id);
                    if (vendedor != null) _context.Vendedores.Remove(vendedor);
                }
                else if (usuario.Rol.Equals("repartidor", StringComparison.OrdinalIgnoreCase))
                {
                    var repartidor = await _context.Repartidores
                        .FirstOrDefaultAsync(r => r.IdUsuario == id);

                    if (repartidor != null)
                    {
                        var pedidosAsignados = await _context.Pedidos
                            .Where(p => p.IdRepartidor == repartidor.Id)
                            .ToListAsync();

                        foreach (var pedido in pedidosAsignados)
                        {
                            pedido.IdRepartidor = null;
                            if (pedido.EstadoPedido == "En reparto")
                                pedido.EstadoPedido = "En preparación";
                        }

                        _context.Repartidores.Remove(repartidor);
                    }
                }

                _context.Usuarios.Remove(usuario);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new { success = true, message = "Usuario eliminado correctamente" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error al eliminar usuario con ID: {id}", id);
                var mensajeError = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = "Error al eliminar: " + mensajeError });
            }
        }

        // ======================= RECUPERAR CONTRASEÑA =======================

        [HttpGet]
        public IActionResult RecuperarContrasena()
        {
            // Si ya hay sesión activa, redirigir según rol
            var rolActual = HttpContext.Session.GetString("Rol");
            if (!string.IsNullOrEmpty(rolActual))
                return RedirectBasedOnRole(rolActual);

            return View(new RecuperarContrasenaVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecuperarContrasena(RecuperarContrasenaVM model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var email = model.Email.Trim();

            // Verificar que el email exista en la tabla de Clientes
            var clienteExiste = await _context.Clientes.AnyAsync(c => c.Email == email);
            if (!clienteExiste)
            {
                ModelState.AddModelError(nameof(model.Email), "No existe una cuenta asociada a ese email.");
                return View(model);
            }

            try
            {
                var ahora = DateTime.UtcNow;
                const int minutesValid = 10;

                // Invalidar tokens de recuperación activos previos para este email
                var activosPrevios = await _context.PreRegistros
                    .Where(t => t.Email == email && t.Tipo == "recuperacion" && !t.Usado && t.ExpiresAt > ahora)
                    .ToListAsync();
                foreach (var t in activosPrevios) t.Usado = true;

                // Generar nuevo código y guardarlo
                var code = GenerarCodigo6();
                var codeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 10);
                var expiresAt = ahora.AddMinutes(minutesValid);

                var token = new PreRegistroToken
                {
                    Email = email,
                    TokenHash = codeHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = ahora,
                    Usado = false,
                    Tipo = "recuperacion"
                };
                _context.PreRegistros.Add(token);
                await _context.SaveChangesAsync();

                // Guardar email en sesión para el paso de confirmación
                HttpContext.Session.SetString(SK_RecupEmail, email);

                // Enviar código por email
                string subject = "Recuperación de contraseña";
                string body = $@"<p>Hola,</p>
<p>Recibimos una solicitud para restablecer tu contraseña.</p>
<p>Tu código de verificación es: <strong style=""font-size:20px;letter-spacing:4px"">{System.Net.WebUtility.HtmlEncode(code)}</strong></p>
<p>Este código vence en {minutesValid} minutos.</p>
<p>Si no realizaste esta solicitud, podés ignorar este correo. Tu contraseña no será modificada.</p>";

                await _emailSender.SendAsync(email, subject, body);

                TempData["Success"] = $"📧 Enviamos un código de verificación a {email}. Revisá tu bandeja de entrada.";
                return RedirectToAction("ConfirmarRecuperacion");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar código de recuperación a {Email}", email);
                TempData["Error"] = "No se pudo enviar el email. Intentá nuevamente más tarde.";
                return View(model);
            }
        }

        // ======================= CONFIRMAR RECUPERACIÓN =======================

        [HttpGet]
        public IActionResult ConfirmarRecuperacion()
        {
            var email = HttpContext.Session.GetString(SK_RecupEmail);
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("RecuperarContrasena");

            return View(new ConfirmarRecuperacionVM { Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecuperacion(ConfirmarRecuperacionVM model)
        {
            var emailSesion = HttpContext.Session.GetString(SK_RecupEmail);
            if (string.IsNullOrEmpty(emailSesion))
                return RedirectToAction("RecuperarContrasena");

            // Forzar el email desde la sesión (no confiar en el campo oculto)
            model.Email = emailSesion;
            ModelState.Remove(nameof(model.Email));

            if (!ModelState.IsValid)
                return View(model);

            var ahora = DateTime.UtcNow;

            // Buscar el token válido más reciente para este email
            var tokenGuardado = await _context.PreRegistros
                .Where(t => t.Email == emailSesion
                         && t.Tipo == "recuperacion"
                         && !t.Usado
                         && t.ExpiresAt > ahora)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (tokenGuardado == null)
            {
                ModelState.AddModelError(nameof(model.Codigo), "El código ha vencido o ya fue utilizado. Solicitá uno nuevo.");
                return View(model);
            }

            // Verificar código con BCrypt
            if (!BCrypt.Net.BCrypt.Verify(model.Codigo.Trim(), tokenGuardado.TokenHash))
            {
                ModelState.AddModelError(nameof(model.Codigo), "El código ingresado es incorrecto.");
                return View(model);
            }

            // Buscar el cliente y su usuario asociado
            var cliente = await _context.Clientes
                .Include(c => c.Usuario)
                .FirstOrDefaultAsync(c => c.Email == emailSesion);

            if (cliente?.Usuario == null)
            {
                TempData["Error"] = "No se encontró el usuario asociado a este email.";
                return View(model);
            }

            try
            {
                // Actualizar la contraseña del usuario con el nuevo hash
                cliente.Usuario.Contraseña = BCrypt.Net.BCrypt.HashPassword(model.NuevaContraseña, workFactor: 12);

                // Marcar el token como usado
                tokenGuardado.Usado = true;

                await _context.SaveChangesAsync();

                // Limpiar la sesión de recuperación
                HttpContext.Session.Remove(SK_RecupEmail);

                TempData["Success"] = "✅ Tu contraseña fue actualizada correctamente. Ya podés iniciar sesión.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar contraseña para {Email}", emailSesion);
                TempData["Error"] = "Ocurrió un error al actualizar la contraseña. Intentá nuevamente.";
                return View(model);
            }
        }

        // ======================= REENVIAR CÓDIGO DE RECUPERACIÓN =======================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReenviarCodigoRecuperacion()
        {
            var email = HttpContext.Session.GetString(SK_RecupEmail);
            if (string.IsNullOrWhiteSpace(email))
                return RedirectToAction("RecuperarContrasena");

            try
            {
                var ahora = DateTime.UtcNow;
                const int minutesValid = 10;

                // Verificar cooldown de 30 segundos
                var ultimoToken = await _context.PreRegistros
                    .Where(t => t.Email == email && t.Tipo == "recuperacion")
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();

                if (ultimoToken != null && (ahora - ultimoToken.CreatedAt).TotalSeconds < 30)
                {
                    TempData["Error"] = "Demasiadas solicitudes seguidas. Esperá unos segundos e intentá de nuevo.";
                    return RedirectToAction("ConfirmarRecuperacion");
                }

                // Invalidar tokens activos previos
                var activosPrevios = await _context.PreRegistros
                    .Where(t => t.Email == email && t.Tipo == "recuperacion" && !t.Usado && t.ExpiresAt > ahora)
                    .ToListAsync();
                foreach (var t in activosPrevios) t.Usado = true;

                var code = GenerarCodigo6();
                var codeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 10);
                var expiresAt = ahora.AddMinutes(minutesValid);

                _context.PreRegistros.Add(new PreRegistroToken
                {
                    Email = email,
                    TokenHash = codeHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = ahora,
                    Usado = false,
                    Tipo = "recuperacion"
                });
                await _context.SaveChangesAsync();

                string subject = "Recuperación de contraseña (nuevo código)";
                string body = $@"<p>Hola,</p>
<p>Tu nuevo código de verificación es: <strong style=""font-size:20px;letter-spacing:4px"">{System.Net.WebUtility.HtmlEncode(code)}</strong></p>
<p>Este código vence en {minutesValid} minutos.</p>";

                await _emailSender.SendAsync(email, subject, body);
                TempData["Success"] = "Te reenviamos un nuevo código. Revisá tu correo.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reenviar código de recuperación a {Email}", email);
                TempData["Error"] = "No se pudo reenviar el código. Intentá nuevamente más tarde.";
            }

            return RedirectToAction("ConfirmarRecuperacion");
        }

        // ======================= HELPERS =======================

        private static string GenerarCodigo6()
        {
            var rnd = Random.Shared.Next(0, 1_000_000);
            return rnd.ToString("D6");
        }

        private async Task EnviarCodigoAsync(string email, int minutesValid)
        {
            var ahora = DateTime.UtcNow;
            var lastUntil = HttpContext.Session.GetString(SK_PreRegCodeUntil);
            if (DateTime.TryParse(lastUntil, null, System.Globalization.DateTimeStyles.RoundtripKind, out var untilPrev))
            {
                var emittedAt = untilPrev.AddMinutes(-minutesValid);
                if ((ahora - emittedAt).TotalSeconds < 30)
                {
                    throw new InvalidOperationException("Demasiadas solicitudes seguidas. Intente nuevamente en unos segundos.");
                }
            }

            var code = GenerarCodigo6();
            var codeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 10);
            var expiresAt = ahora.AddMinutes(minutesValid);

            try
            {
                var activosPrevios = await _context.PreRegistros
                    .Where(t => t.Email == email && t.Tipo == "registro" && !t.Usado && t.ExpiresAt > ahora)
                    .ToListAsync();
                foreach (var t in activosPrevios) t.Usado = true;

                var token = new PreRegistroToken
                {
                    Email = email,
                    TokenHash = codeHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = ahora,
                    Usado = false,
                    Tipo = "registro"
                };
                _context.PreRegistros.Add(token);
                await _context.SaveChangesAsync();

                HttpContext.Session.SetString(SK_PreRegEmail, email);
                HttpContext.Session.SetString(SK_PreRegCodeHash, codeHash);
                HttpContext.Session.SetString(SK_PreRegCodeUntil, expiresAt.ToString("O"));

                string subject = "Tu código de verificación";
                string body = $@"<p>Hola,</p>
<p>Tu código de verificación es: <strong style=""font-size:18px"">{WebUtility.HtmlEncode(code)}</strong></p>
<p>Vence en {minutesValid} minutos.</p>
<p>Si no solicitaste este código, ignora este correo.</p>";

                await _emailSender.SendAsync(email, subject, body);
                TempData["Success"] = $"📧 Se envió un código de verificación a {email}. Revisa tu bandeja de entrada.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar código.");
                throw;
            }
        }

        [HttpGet]
        public IActionResult AccesoDenegado()
        {
            ViewBag.Message = "No tienes permisos suficientes para ingresar.";
            return View();
        }

        private IActionResult RedirectBasedOnRole(string rol)
        {
            switch (rol.ToLowerInvariant())
            {
                case "superadmin":
                case "admin":
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                case "vendedor":
                case "repartidor":
                    return RedirectToAction("Index", "Pedidos", new { area = "Admin" });
                case "cliente":
                    return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });
                default:
                    HttpContext.Session.Clear();
                    return RedirectToAction("Login");
            }
        }
    }
}