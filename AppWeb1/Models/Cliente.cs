using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AppWeb1.Models;

namespace AppWeb1.Models    
{
    public class Cliente
    {
        public int IdCliente { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [RegularExpression(@"^[a-zA-ZáéíóúÁÉÍÓÚüÜñÑ\s]+$", ErrorMessage = "Solo se permiten letras")]
        public string Nombre { get; set; } = null!;

        [Required(ErrorMessage = "El apellido es obligatorio")]
        [RegularExpression(@"^[a-zA-ZáéíóúÁÉÍÓÚüÜñÑ\s]+$", ErrorMessage = "Solo se permiten letras")]
        public string Apellido { get; set; } = null!;

        [Required(ErrorMessage = "El número de teléfono es obligatorio")]
        [RegularExpression(@"^\d+$", ErrorMessage = "Solo se permiten números")]
        public string NumTelefono { get; set; } = null!;

        [Required(ErrorMessage = "El domicilio es obligatorio")]
        public string Domicilio { get; set; } = null!;

        // ✅ NUEVO: Email (coincide con HasMaxLength(200) del Fluent API)
        [Required(ErrorMessage = "El email es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de email inválido")]
        [MaxLength(200)]
        public string Email { get; set; } = null!;

        // 🔽 Clave foránea a Usuario
        public int IdUsuario { get; set; }

        [ForeignKey(nameof(IdUsuario))]
        public Usuario Usuario { get; set; } = null!;

        public virtual ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
    }
}
