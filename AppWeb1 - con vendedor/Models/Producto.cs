#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http; // importante para usar IFormFile

namespace AppWeb1.Models    
{
    public partial class Producto
    {
        public int IdProducto { get; set; }

        public string NombreProducto { get; set; } = string.Empty;

        public string? Imagen { get; set; } // ✅ Guarda el nombre del archivo en la BDD

        [NotMapped]
        public IFormFile? ImagenArchivo { get; set; } // ✅ No se guarda en la BDD

        public string Descripcion { get; set; } = string.Empty;

        public decimal? Precio { get; set; }

        public virtual ICollection<DetallePedido> DetallePedidos { get; set; } = new List<DetallePedido>();
    }
}
