using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppWeb1.Models
{
    [Table("Usuarios")]
    public class Usuario
    {
        public int Id { get; set; }

        [Required]
        public string NombreUsuario { get; set; } = null!;

        [Required]
        public string Contraseña { get; set; } = null!;

        [Required]
        public string Rol { get; set; } = null!;
    }
}
