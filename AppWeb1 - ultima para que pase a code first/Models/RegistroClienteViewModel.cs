namespace AppWeb1.Models
{
    public class RegistroClienteViewModel
    {
        // Datos del Usuario
        public string NombreUsuario { get; set; } = null!;
        public string Contraseña { get; set; } = null!;

        // Datos del Cliente
        public string Nombre { get; set; } = null!;
        public string Apellido { get; set; } = null!;
        public string NumTelefono { get; set; } = null!;
        public string Domicilio { get; set; } = null!;
    }
}
