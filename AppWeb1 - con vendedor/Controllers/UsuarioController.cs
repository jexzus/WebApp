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

        public UsuarioController(DeliveryDBContext context, IEmailSender emailSender, ILogger<UsuarioController> logger)
        {
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ======================= LOGIN =======================

        [HttpGet]
        public IActionResult Login()
        {
            var rolActual = HttpContext.Session.GetString("Rol");
            if (!string.IsNullOrEmpty(rolActual))
            {
                if (rolActual.Equals("admin", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });
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
                    HttpContext.Session.SetString("Usuario", usuario.NombreUsuario);
                    HttpContext.Session.SetString("Rol", usuario.Rol ?? "cliente");
                    HttpContext.Session.SetInt32("IdUsuario", usuario.Id);

                    TempData["Success"] = $"¡Bienvenido, {usuario.NombreUsuario}!";

                    if (usuario.Rol?.Equals("admin", StringComparison.OrdinalIgnoreCase) == true)
                        return RedirectToAction("Index", "Home", new { area = "Admin" });
                    return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });
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

        // ======================= GESTIÓN DE ADMINISTRADORES =======================

        [HttpGet]
        public async Task<IActionResult> CrearAdmin()
        {
            // *** ESTA ES LA MODIFICACIÓN ***
            var rol = HttpContext.Session.GetString("Rol");
            if (rol == null)
                return RedirectToAction("Login");

            if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
            {
                // Se cambió "NoPermisos" por "Error" según tus instrucciones
                TempData["Error"] = "No tienes permisos suficientes para acceder a la gestión de administradores.";
                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
            // *** FIN DE LA MODIFICACIÓN ***

            var administradores = await _context.Usuarios
                .Where(u => (u.Rol ?? "").ToLower() == "admin")
                .OrderBy(u => u.NombreUsuario)
                .ToListAsync();

            ViewBag.Administradores = administradores;
            return View(new Usuario());
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearAdmin(Usuario admin, string Rol)
        {
            // Solo admin puede crear
            var rolSesion = HttpContext.Session.GetString("Rol");
            if (!string.Equals(rolSesion, "admin", StringComparison.OrdinalIgnoreCase))
            {
                // Redirige a AccesoDenegado
                return RedirectToAction("AccesoDenegado");
            }

            // Validaciones actuales (usuario, contraseña, duplicado)
            if (string.IsNullOrWhiteSpace(admin.NombreUsuario))
                ModelState.AddModelError(nameof(admin.NombreUsuario), "El nombre de usuario es obligatorio.");
            if (string.IsNullOrWhiteSpace(admin.Contraseña) || admin.Contraseña.Length < 6)
                ModelState.AddModelError(nameof(admin.Contraseña), "La contraseña debe tener al menos 6 caracteres.");
            if (await _context.Usuarios.AnyAsync(u => u.NombreUsuario == admin.NombreUsuario))
                ModelState.AddModelError(nameof(admin.NombreUsuario), "El nombre de usuario ya está registrado.");

            if (!ModelState.IsValid)
            {
                // recargar lista admins, como hoy lo haces
                var admins = await _context.Usuarios
                    .Where(u => (u.Rol ?? "").ToLower() == "admin")
                    .OrderBy(u => u.NombreUsuario).ToListAsync();
                ViewBag.Administradores = admins;
                // Mensaje genérico
                TempData["Error"] = "Error al crear el usuario. Revisa los datos.";
                return View(admin);
            }

            // Normalizar rol
            Rol = (Rol ?? "").Trim().ToLower();
            if (Rol != "admin" && Rol != "vendedor") Rol = "admin"; // Default a admin si es inválido

            admin.Rol = Rol;
            admin.Contraseña = BCrypt.Net.BCrypt.HashPassword(admin.Contraseña, workFactor: 12);

            _context.Usuarios.Add(admin);
            await _context.SaveChangesAsync(); // Guarda el Usuario para obtener el ID

            // Si es vendedor, crea la fila en Vendedores
            if (Rol == "vendedor")
            {
                var vend = new Vendedor { IdUsuario = admin.Id, Activo = true };
                _context.Vendedores.Add(vend);
                await _context.SaveChangesAsync(); // Guarda el Vendedor
            }

            // Mensaje genérico
            TempData["Success"] = "Usuario creado correctamente.";
            return RedirectToAction("CrearAdmin");
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarAdmin([FromBody] Usuario admin)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "No autorizado" });

            var existente = await _context.Usuarios.FindAsync(admin.Id);
            if (existente == null || !string.Equals(existente.Rol, "admin", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Administrador no encontrado" });

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
                return Json(new { success = true, message = "Administrador actualizado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar administrador con ID: {id}", admin.Id);
                return Json(new { success = false, message = "Error al actualizar: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EliminarAdmin(int id)
        {
            var rol = HttpContext.Session.GetString("Rol");
            if (!string.Equals(rol, "admin", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "No autorizado" });

            var admin = await _context.Usuarios.FindAsync(id);
            if (admin == null || !string.Equals(admin.Rol, "admin", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Administrador no encontrado" });

            try
            {
                _context.Usuarios.Remove(admin);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Administrador eliminado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar administrador con ID: {id}", id);
                return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
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

            // Throttle: 1 código cada 30s
            var lastUntil = HttpContext.Session.GetString(SK_PreRegCodeUntil);
            if (DateTime.TryParse(lastUntil, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var untilPrev))
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
                // Marcar como usados los códigos anteriores activos
                var activosPrevios = await _context.PreRegistros
                    .Where(t => t.Email == email && t.Tipo == "registro" && !t.Usado && t.ExpiresAt > ahora)
                    .ToListAsync();
                foreach (var t in activosPrevios)
                    t.Usado = true;

                // Guardar el nuevo token en BD
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
                _logger.LogInformation("Token guardado en BD para email: {email}", email);

                // Guardar también en sesión (para acceso rápido)
                HttpContext.Session.SetString(SK_PreRegEmail, email);
                HttpContext.Session.SetString(SK_PreRegCodeHash, codeHash);
                HttpContext.Session.SetString(SK_PreRegCodeUntil, expiresAt.ToString("O"));

                // Enviar email
                string subject = "Tu código de verificación";
                string body = $@"<p>Hola,</p>
<p>Tu código de verificación es: <strong style=""font-size:18px"">{WebUtility.HtmlEncode(code)}</strong></p>
<p>Vence en {minutesValid} minutos.</p>
<p>Si no solicitaste este código, ignora este correo.</p>";

                await _emailSender.SendAsync(email, subject, body);
                _logger.LogInformation("Email enviado a: {email}", email);

                TempData["Success"] = $"📧 Se envió un código de verificación a {email}. Revisa tu bandeja de entrada (y spam).";
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Error de BD al guardar token: {message}", dbEx.Message);
                throw new Exception("Error al guardar el token. Verifica la BD.", dbEx);
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestEmail()
        {
            try
            {
                _logger.LogInformation("Iniciando prueba de envío de email...");

                await _emailSender.SendAsync(
                    "raiburn.jesus@hotmail.com",
                    "Test desde DeliveryApp - " + DateTime.Now.ToString("HH:mm:ss"),
                    $@"<html>
                    <body style='font-family: Arial, sans-serif; padding: 20px;'>
                        <h1 style='color: #28a745;'>✅ ¡Funciona!</h1>
                        <p>El email se envió correctamente desde <strong>DeliveryApp</strong>.</p>
                        <hr>
                        <p><small>Enviado: {DateTime.Now:dd/MM/yyyy HH:mm:ss}</small></p>
                        <p><small>Desde: {_emailSender.GetType().Name}</small></p>
                    </body>
                </html>"
                );

                _logger.LogInformation("Email enviado exitosamente a raiburn.jesus@hotmail.com");

                return Content(
                    "✅ EMAIL ENVIADO EXITOSAMENTE\n\n" +
                    "Destinatario: raiburn.jesus@hotmail.com\n" +
                    "Fecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "\n\n" +
                    "Revisa tu bandeja de entrada (y spam).",
                    "text/plain; charset=utf-8"
                );
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "Error SMTP al enviar email de prueba");
                return Content(
                    "❌ ERROR SMTP\n\n" +
                    $"Mensaje: {smtpEx.Message}\n" +
                    $"StatusCode: {smtpEx.StatusCode}\n\n" +
                    "Verifica:\n" +
                    "- Contraseña de aplicación correcta\n" +
                    "- Verificación en 2 pasos activada\n" +
                    "- Configuración Email:* en appsettings.json",
                    "text/plain; charset=utf-8"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general al enviar email de prueba");
                return Content(
                    "❌ ERROR GENERAL\n\n" +
                    $"Mensaje: {ex.Message}\n" +
                    $"Tipo: {ex.GetType().Name}\n\n" +
                    $"Stack Trace:\n{ex.StackTrace}",
                    "text/plain; charset=utf-8"
                );
            }
        }

        // ======================= ACCESO DENEGADO (AGREGADO) =======================

        [HttpGet]
        public IActionResult AccesoDenegado()
        {
            ViewBag.Message = "No tienes permisos suficientes para ingresar.";
            // si el usuario está logueado y es vendedor, lo devolvemos al panel admin
            ViewBag.ReturnArea = "Admin";
            ViewBag.ReturnController = "Home";
            ViewBag.ReturnAction = "Index";
            return View();
        }
    }
}