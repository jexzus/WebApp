using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AppWeb1.Models
{
    public class Usuario
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string NombreUsuario { get; set; } = null!;

        [MaxLength(100)]
        public string Contraseña { get; set; } = null!;

        [MaxLength(20)]
        public string Rol { get; set; } = null!;

        // ✅ NUEVO: navegación inversa para Cliente
        public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    }
}
