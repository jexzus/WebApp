using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace AppWeb1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // Página pública de bienvenida (no redirijo automáticamente).
        // Si querés que “saltee” a cada panel según rol, descomentar el bloque comentado.
        public IActionResult Index()
        {
            // // Redirección opcional por rol:
            // var rol = HttpContext.Session.GetString("Rol");
            // if (!string.IsNullOrEmpty(rol))
            // {
            //     if (rol.Equals("admin", StringComparison.OrdinalIgnoreCase))
            //         return RedirectToAction("Index", "Home", new { area = "Admin" });
            //     return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });
            // }

            ViewBag.Usuario = HttpContext.Session.GetString("Usuario");
            return View();
        }

        public IActionResult Privacy() => View();
    }
}
