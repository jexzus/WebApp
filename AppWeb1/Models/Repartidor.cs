// ============================================================
// ARCHIVO: Models/Repartidor.cs
// ============================================================
namespace AppWeb1.Models
{
    public class Repartidor
    {
        public int Id { get; set; }

        // Relación con Usuario
        public int IdUsuario { get; set; }
        public Usuario? Usuario { get; set; }

        // Datos opcionales
        public string? Nombre { get; set; }
        public string? Apellido { get; set; }

        // CAMPO NUEVO: Disponibilidad de 3 estados
        // Valores válidos: "Activo" | "Ocupado" | "Inactivo"
        // Por defecto: "Activo"
        public string Disponibilidad { get; set; } = "Activo";

        // Mantenemos Activo como bool para compatibilidad (indica si el usuario está dado de baja del sistema)
        public bool Activo { get; set; } = true;

        public DateTime FechaAlta { get; set; } = DateTime.UtcNow;
    }
}