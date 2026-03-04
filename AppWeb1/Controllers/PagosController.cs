using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MercadoPago.Config;
using MercadoPago.Client.Payment;
using AppWeb1.Data;
using AppWeb1.Models;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http; // Necesario para HttpContext.Session

public class PagosController : Controller
{
    private readonly DeliveryDBContext _db;
    private readonly ILogger<PagosController> _logger;
    private readonly IConfiguration _cfg;

    public PagosController(DeliveryDBContext db, ILogger<PagosController> logger, IConfiguration cfg)
    {
        _db = db;
        _logger = logger;
        _cfg = cfg;
    }

    // === Retorno visual para el usuario ===
    // GET /Pagos/Retorno?status=success|pending|failure&np=123
    [HttpGet]
    public async Task<IActionResult> Retorno(string status, int np)
    {
        var pedido = await _db.Pedidos.FindAsync(np);
        if (pedido == null)
        {
            _logger.LogWarning($"Pedido {np} no encontrado al intentar mostrar el retorno.");
            return RedirectToAction("Catalogo", "Cliente", new { area = "Cliente" });
        }

        // Limpiar el carrito solo si el pago fue exitoso y el usuario está presente.
        if (status == "success")
        {
            HttpContext.Session.Remove("Carrito");
            _logger.LogInformation($"Carrito limpiado para el pedido {np}.");
        }

        ViewBag.Status = status;
        return View("ResultadoPago", pedido);
    }

    // === Webhook (fuente de verdad del pago) ===
    public class MpWebhookBody
    {
        public string? Type { get; set; }
        public MpWebhookData? Data { get; set; }
    }
    public class MpWebhookData { public string? Id { get; set; } }

    // POST /Pagos/Webhook
    [HttpPost]
    public async Task<IActionResult> Webhook([FromBody] MpWebhookBody body)
    {
        try
        {
            var type = body?.Type;
            var idStr = body?.Data?.Id;

            // Soporte a envíos antiguos por querystring (?type=payment&id=xxx)
            if (string.IsNullOrEmpty(type)) type = Request.Query["type"];
            if (string.IsNullOrEmpty(idStr)) idStr = Request.Query["id"];

            // Ignoramos notificaciones no relacionadas con pagos.
            if (type != "payment" || string.IsNullOrEmpty(idStr))
            {
                return Ok();
            }

            MercadoPagoConfig.AccessToken = _cfg["MercadoPago:AccessToken"];
            var payClient = new PaymentClient();
            var payment = await payClient.GetAsync(long.Parse(idStr));

            // ExternalReference = NumPedido que enviamos en la preferencia
            if (!int.TryParse(payment.ExternalReference, out var numPedido))
            {
                _logger.LogWarning($"ExternalReference inválido: {payment.ExternalReference}.");
                return Ok();
            }

            var pedido = await _db.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == numPedido);
            if (pedido == null)
            {
                _logger.LogWarning($"Pedido {numPedido} no encontrado en la base de datos.");
                return Ok();
            }

            _logger.LogInformation($"Procesando webhook para pedido {numPedido}. Estado MP: {payment.Status}");

            pedido.MpPaymentId = payment.Id.ToString();
            pedido.MpStatusDetail = payment.StatusDetail;
            pedido.EstadoPago = payment.Status switch
            {
                "approved" => "aprobado",
                "rejected" => "rechazado",
                _ => "pendiente"
            };

            await _db.SaveChangesAsync();
            _logger.LogInformation($"Pedido {numPedido} actualizado a '{pedido.EstadoPago}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error procesando webhook de MP.");
            // Respondemos 200 (Ok) igual para evitar reintentos infinitos por un error en nuestra app.
        }
        return Ok();
    }
}