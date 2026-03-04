// ============================================================
// ARCHIVO: ViewModels/RecuperarContrasenaVM.cs
// ============================================================
using System.ComponentModel.DataAnnotations;

namespace AppWeb1.ViewModels
{
    public class RecuperarContrasenaVM
    {
        [Required(ErrorMessage = "El email es obligatorio.")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }

    public class ConfirmarRecuperacionVM
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El código es obligatorio.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe tener exactamente 6 dígitos.")]
        [Display(Name = "Código de verificación")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        [Display(Name = "Nueva contraseña")]
        [DataType(DataType.Password)]
        public string NuevaContraseña { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debes confirmar la contraseña.")]
        [Compare(nameof(NuevaContraseña), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        [DataType(DataType.Password)]
        public string ConfirmarContraseña { get; set; } = string.Empty;
    }
}   