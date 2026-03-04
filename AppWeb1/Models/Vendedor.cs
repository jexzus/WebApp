namespace AppWeb1.Models
{
    public class Vendedor
    {
        public int Id { get; set; }

        // Relación con Usuario
        public int IdUsuario { get; set; }
        public Usuario? Usuario { get; set; }

        // Campos opcionales
        public string? Nombre { get; set; }
        public string? Apellido { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaAlta { get; set; } = DateTime.UtcNow;
    }
}
