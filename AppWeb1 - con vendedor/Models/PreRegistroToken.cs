using System;

namespace AppWeb1.Models
{
    public class PreRegistroToken
    {
        public int Id { get; set; }
        public string Email { get; set; } = "";
        public string TokenHash { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public bool Usado { get; set; } = false;  // ← Esto es lo más importante
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Tipo { get; set; } = "registro";
    }
}
