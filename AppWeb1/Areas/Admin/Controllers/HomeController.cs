using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

// Nota: Ya no se necesita 'using static AppWeb1.Security.Roles;'
// porque el nuevo helper 'EsAdminOSuperAdmin' está definido localmente.

namespace AppWeb1.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // Método auxiliar para verificar si es Admin o SuperAdmin
        private bool EsAdminOSuperAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) &&
                   (rol.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                    rol.Equals("superadmin", StringComparison.OrdinalIgnoreCase));
        }

        public IActionResult Index()
        {
            // Solo Admin y SuperAdmin pueden acceder al panel principal
            if (!EsAdminOSuperAdmin())
            {
                var rol = HttpContext.Session.GetString("Rol");

                // Redirigir según el rol
                if (!string.IsNullOrEmpty(rol))
                {
                    switch (rol.ToLower())
                    {
                        case "vendedor":
                        case "repartidor":
                            return RedirectToAction("Index", "Pedidos", new { area = "Admin" });

                        case "cliente":
                            return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });

                        default:
                            return RedirectToAction("Login", "Usuario", new { area = "" });
                    }
                }

                // Si no hay rol, redirigir al login
                return RedirectToAction("Login", "Usuario", new { area = "" });
            }

            // Si es Admin o SuperAdmin, Pasa el nombre de usuario y el rol a la vista
            ViewBag.Usuario = HttpContext.Session.GetString("Usuario");
            ViewBag.Rol = HttpContext.Session.GetString("Rol");

            return View();
        }
    }
}