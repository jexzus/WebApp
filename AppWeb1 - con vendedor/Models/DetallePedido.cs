using System.ComponentModel.DataAnnotations.Schema;

namespace AppWeb1.Models
{
    public partial class DetallePedido
    {
        public int IdDetalle { get; set; }
        public int NumPedido { get; set; }
        public int IdProducto { get; set; }
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }  // Ya no es nullable para evitar errores con ??

        [NotMapped]
        public decimal Subtotal => PrecioUnitario * Cantidad;

        // Navegación
        public virtual Producto? IdProductoNavigation { get; set; } = default!;
        public virtual Pedido? NumPedidoNavigation { get; set; } = default!;
    }
}