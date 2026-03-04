using Microsoft.AspNetCore.Identity;

namespace AppWeb1.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? Nombre { get; set; }
        public string? Apellido { get; set; }
        public string? Domicilio { get; set; }
        public string? NumTelefono { get; set; }
    }
}
