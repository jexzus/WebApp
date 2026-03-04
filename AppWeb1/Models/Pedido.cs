#nullable enable // Habilita el contexto de nulidad para este archivo

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AppWeb1.Models;

public partial class Pedido
{
    public int NumPedido { get; set; }

    public int? IdCliente { get; set; }

    public DateTime? FechaPedido { get; set; }

    [Required]
    public string EstadoPedido { get; set; } = "Pendiente"; // Unificado a mayúscula inicial

    public decimal? MontoTotal { get; set; }

    public string? Observaciones { get; set; } // Ahora acepta valores nulos explícitamente

    // ==========================================================
    // MEJORAS: Modo de Entrega y Repartidor
    // ==========================================================

    /// <summary>
    /// Define cómo se entregará el pedido.
    /// Valores esperados: "Domicilio" o "Tienda".
    /// </summary>
    public string? ModoEntrega { get; set; }   // <-- CAMPO AGREGADO

    // Campos para la asignación del repartidor (ya existentes)
    public int? IdRepartidor { get; set; }        // FK opcional

    public Repartidor? Repartidor { get; set; }   // Navegación

    // ==========================================================
    // Campos para la integración con Mercado Pago
    // ==========================================================

    public string EstadoPago { get; set; } = "pendiente"; // Mantengo "pendiente" en minúscula

    public string? MpPreferenceId { get; set; }

    public string? MpPaymentId { get; set; }

    public string? MpStatusDetail { get; set; }

    // ==========================================================
    // Relaciones de navegación
    // ==========================================================

    public virtual ICollection<DetallePedido> DetallePedidos { get; set; } = new List<DetallePedido>();

    public virtual Cliente? IdClienteNavigation { get; set; } // Ahora acepta valores nulos explícitamente
}