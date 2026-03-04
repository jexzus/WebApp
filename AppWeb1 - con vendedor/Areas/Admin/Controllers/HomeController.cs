using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using static AppWeb1.Security.Roles; // Necesario para usar AdminOVendedor

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

        // Método auxiliar (ya no se usa en Index, pero puede servir en otras acciones)
        private bool EsAdmin()
        {
            var rol = HttpContext.Session.GetString("Rol");
            return !string.IsNullOrEmpty(rol) && rol.Equals("admin", StringComparison.OrdinalIgnoreCase);
        }

        //
        // ESTA ES LA ACCIÓN MODIFICADA SEGÚN TUS INSTRUCCIONES
        //
        public IActionResult Index()
        {
            // Antes: Verificaba solo EsAdmin()
            // Después: Verifica si es Admin O Vendedor
            if (!AdminOVendedor(HttpContext.Session))
            {
                // Si no es ninguno, lo redirige al Login (fuera del área Admin)
                return RedirectToAction("Login", "Usuario", new { area = "" });
            }

            // Pasa el nombre de usuario y el rol a la vista
            ViewBag.Usuario = HttpContext.Session.GetString("Usuario");
            ViewBag.Rol = HttpContext.Session.GetString("Rol");

            return View();
        }
    }
}