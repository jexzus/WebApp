using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using OfficeOpenXml;
using AppWeb1.Models;
using AppWeb1.Data;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using static AppWeb1.Security.Roles; // 1. Se agregó el using static AppWeb1.Security.Roles;

namespace AppWeb1.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PedidosController : Controller
    {
        private readonly DeliveryDBContext _context;
        private readonly ILogger<PedidosController> _logger;

        public PedidosController(DeliveryDBContext context, ILogger<PedidosController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // 2. Nueva función de validación que permite Admin o Vendedor (reemplaza a EsAdmin)
        private bool PuedeGestionarPedidos() => AdminOVendedor(HttpContext.Session);

        // GET: /Admin/Pedidos
        public async Task<IActionResult> Index()
        {
            // 3. Modificación del control de acceso y redirección
            if (!PuedeGestionarPedidos())
                return RedirectToAction("AccesoDenegado", "Usuario", new { area = "" });

            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                var pedidosValidos = pedidos.Where(p =>
                    p.IdClienteNavigation != null &&
                    p.DetallePedidos != null &&
                    p.DetallePedidos.All(d => d.IdProductoNavigation != null)
                ).ToList();

                return View(pedidosValidos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar los pedidos");
                TempData["Error"] = "Error al cargar los pedidos.";
                return View(new List<Pedido>());
            }
        }

        // GET: /Admin/Pedidos/Detalle/123
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            // 3. Modificación del control de acceso y redirección
            if (!PuedeGestionarPedidos())
                return RedirectToAction("AccesoDenegado", "Usuario", new { area = "" });

            var pedido = await _context.Pedidos
                .Include(p => p.IdClienteNavigation)
                .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                .FirstOrDefaultAsync(p => p.NumPedido == id);

            if (pedido == null)
            {
                TempData["Error"] = "El pedido no existe.";
                return RedirectToAction(nameof(Index));
            }

            return View(pedido);
        }

        // POST: /Admin/Pedidos/CambiarEstado (Acción tradicional que usa Forms)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, string nuevoEstado)
        {
            // 3. Modificación del control de acceso y redirección
            if (!PuedeGestionarPedidos())
            {
                TempData["Error"] = "No tienes permiso para realizar esta acción.";
                return RedirectToAction("AccesoDenegado", "Usuario", new { area = "" });
            }

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null)
            {
                TempData["Error"] = "El pedido no existe.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var estadosValidos = new[] { "Pendiente", "En preparación", "En reparto", "Entregado", "Cancelado" };
                if (string.IsNullOrWhiteSpace(nuevoEstado) || !estadosValidos.Contains(nuevoEstado))
                {
                    TempData["Error"] = "Estado no válido.";
                    return RedirectToAction(nameof(Index));
                }

                pedido.EstadoPedido = nuevoEstado;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Estado del pedido actualizado con éxito.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del pedido {NumPedido}", id);
                TempData["Error"] = "No se pudo actualizar el estado del pedido. " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/DescargarExcel (Acción tradicional que usa Forms)
        [HttpPost]
        [Route("/Admin/DescargarExcel")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DescargarExcel()
        {
            // 3. Modificación del control de acceso y redirección
            if (!PuedeGestionarPedidos())
                return RedirectToAction("AccesoDenegado", "Usuario", new { area = "" });

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                using var package = new ExcelPackage();
                var hoja = package.Workbook.Worksheets.Add("Pedidos");

                hoja.Cells[1, 1].Value = "N° Pedido";
                hoja.Cells[1, 2].Value = "Cliente";
                hoja.Cells[1, 3].Value = "Domicilio";
                hoja.Cells[1, 4].Value = "Teléfono";
                hoja.Cells[1, 5].Value = "Fecha";
                hoja.Cells[1, 6].Value = "Estado";
                hoja.Cells[1, 7].Value = "Total";

                int fila = 2;
                foreach (var p in pedidos)
                {
                    hoja.Cells[fila, 1].Value = p.NumPedido;
                    hoja.Cells[fila, 2].Value = $"{p.IdClienteNavigation?.Nombre} {p.IdClienteNavigation?.Apellido}";
                    hoja.Cells[fila, 3].Value = p.IdClienteNavigation?.Domicilio;
                    hoja.Cells[fila, 4].Value = p.IdClienteNavigation?.NumTelefono;
                    hoja.Cells[fila, 5].Value = p.FechaPedido?.ToString("dd/MM/yyyy HH:mm");
                    hoja.Cells[fila, 6].Value = p.EstadoPedido;
                    hoja.Cells[fila, 7].Value = p.MontoTotal ?? 0m;
                    fila++;
                }

                hoja.Cells.AutoFitColumns();

                var bytes = package.GetAsByteArray();
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PedidosAdmin.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel de pedidos (admin)");
                TempData["Error"] = "No se pudo generar el archivo Excel.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: /Admin/CambiarEstadoAjax (ACCIÓN AJAX)
        [HttpPost]
        [Route("/Admin/CambiarEstadoAjax")]
        public async Task<IActionResult> CambiarEstadoAjax([FromBody] CambiarEstadoRequest request)
        {
            // 3. Modificación del control de acceso
            if (!PuedeGestionarPedidos())
                // Siempre retorna JSON
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null)
                    return Json(new { success = false, message = "Pedido no encontrado" });

                var estadosValidos = new[] { "Pendiente", "En preparación", "En reparto", "Entregado", "Cancelado" };
                if (string.IsNullOrWhiteSpace(request.NuevoEstado) || !estadosValidos.Contains(request.NuevoEstado))
                    return Json(new { success = false, message = "Estado no válido" });

                pedido.EstadoPedido = request.NuevoEstado;
                await _context.SaveChangesAsync();

                // Siempre retorna JSON con resultado de éxito
                return Json(new { success = true, message = "Estado actualizado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del pedido");
                // Siempre retorna JSON con error
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }
    }

    public class CambiarEstadoRequest
    {
        public int NumPedido { get; set; }
        public string? NuevoEstado { get; set; }
    }
}