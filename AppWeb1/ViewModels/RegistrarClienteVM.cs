// Models/ViewModels/RegistrarClienteVM.cs
using System.ComponentModel.DataAnnotations;

namespace AppWeb1.ViewModels
{
    public class RegistrarClienteVM
    {
        // Usuario
        [Required, StringLength(100)]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Contraseña { get; set; } = string.Empty;

        [Required, Compare(nameof(Contraseña), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string ConfirmarContraseña { get; set; } = string.Empty;

        // Cliente
        [Required, StringLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Apellido { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Domicilio { get; set; }

        [Required, StringLength(20)]
        public string NumTelefono { get; set; } = string.Empty;
    }
}
