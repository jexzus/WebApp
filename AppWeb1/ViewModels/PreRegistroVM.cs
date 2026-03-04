// ViewModels/PreRegistroVM.cs
namespace AppWeb1.ViewModels
{
    public class PreRegistroVM
    {
        public string? Email { get; set; }
    }

    public class ConfirmarPreRegistroVM
    {
        public string? Email { get; set; }
        public string? Codigo { get; set; }
        public string? Contraseña { get; set; }
        public string? ConfirmarContraseña { get; set; }
    }
        
}
