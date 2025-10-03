using AppWeb1.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

    // 🔽 Clave foránea a Usuario
    public int IdUsuario { get; set; }

    [ForeignKey("IdUsuario")]
    public Usuario Usuario { get; set; } = null!;

    public virtual ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
}
