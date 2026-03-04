// ============================================================
// ARCHIVO: Hubs/DeliveryHub.cs
// ============================================================
// CORRECCIONES vs versión rota:
//   ★ FIX CRÍTICO: Agrega método estático ResetRechazos(int numPedido)
//     que el PedidosController llama pero que faltaba en el Hub
//     → Sin este método el proyecto NO COMPILA y nada funciona.
//
// EVENTOS SERVIDOR → CLIENTE:
//   "NuevoPedidoParaReparto"   → Grupo "Repartidores"
//   "PedidoAceptado"           → Todos
//   "ErrorAlAceptar"           → connectionId específico
//   "PedidoRevertido"          → Todos
//   "PedidoCancelado"          → Todos
//   "AlertaPedidoIgnorada"     → Grupo "Admins"
//   "AlertaAdminVista"         → Grupo "Admins"
//   "EstadoCambiadoGlobal"     → Todos
//
// MÉTODOS CLIENTE → SERVIDOR:
//   "MarcarAlertaVista"        → Notifica a todos los admins que ya la vieron
//   "RechazarPedido"           → Repartidor ignora; si todos ignoran → notifica admins
// ============================================================

using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace AppWeb1.Hubs
{
    public class DeliveryHub : Hub
    {
        // ── Tracking de rechazos por pedido ──────────────────────────────
        // key: numPedido  →  set de connectionIds de repartidores que rechazaron
        private static readonly ConcurrentDictionary<int, HashSet<string>> _pendingDeliveries
            = new ConcurrentDictionary<int, HashSet<string>>();

        // ── Conexiones de repartidores activos ───────────────────────────
        private static readonly ConcurrentDictionary<string, (int repartidorId, string disponibilidad)> _repartidorConnections
            = new ConcurrentDictionary<string, (int, string)>();

        // ★ NUEVO: Pedidos actualmente en búsqueda de repartidor.
        // Persiste el botón "Esperando..." en la vista Admin al recargar (sin columna en BD).
        private static readonly ConcurrentDictionary<int, DateTime> _pedidosEnBusqueda
            = new ConcurrentDictionary<int, DateTime>();

        /// <summary>
        /// Al conectar, agrega el cliente al grupo según el rol enviado por QueryString.
        /// El cliente envía: /deliveryHub?rol=repartidor&disponibilidad=Activo&repartidorId=3
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var rol = httpContext?.Request.Query["rol"].ToString()?.ToLowerInvariant() ?? "";
            var disponibilidad = httpContext?.Request.Query["disponibilidad"].ToString() ?? "Inactivo";
            var repartidorIdStr = httpContext?.Request.Query["repartidorId"].ToString() ?? "0";
            int.TryParse(repartidorIdStr, out int repartidorId);

            switch (rol)
            {
                case "admin":
                case "superadmin":
                case "vendedor":
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
                    break;

                case "repartidor":
                    // ★ FIX PERSISTENCIA: se une al grupo SIEMPRE, sin importar disponibilidad.
                    // Antes solo entraba si disponibilidad == "Activo", ahora TODOS reciben eventos.
                    // El filtro de disponibilidad queda solo en el JS del cliente.
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Repartidores");
                    if (repartidorId > 0)
                    {
                        // Seguimos trackeando para el conteo de "todos rechazaron"
                        _repartidorConnections[Context.ConnectionId] = (repartidorId, disponibilidad);
                    }
                    break;

                case "cliente":
                    var clienteIdStr = httpContext?.Request.Query["clienteId"].ToString() ?? "0";
                    if (!string.IsNullOrEmpty(clienteIdStr))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, $"Cliente_{clienteIdStr}");
                    }
                    break;
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _repartidorConnections.TryRemove(Context.ConnectionId, out _);
            await base.OnDisconnectedAsync(exception);
        }

        // ── MÉTODO: Repartidor rechaza/ignora un pedido ──────────────────
        /// <summary>
        /// ★ COMPORTAMIENTO ACTUALIZADO:
        /// Este método sigue disponible para el flujo legacy de notificación al Admin.
        /// Sin embargo, con el nuevo sistema, "Ignorar" NO llama a este método desde el JS.
        /// "Ignorar" solo oculta el modal localmente y agrega al Set pedidosIgnorados en memoria.
        /// El pedido sigue visible en la sección "Pedidos Disponibles" del repartidor.
        /// Si TODOS los repartidores activos rechazan, se notifica a los Admins (flujo legacy).
        /// </summary>
        public async Task RechazarPedido(int numPedido)
        {
            var connectionId = Context.ConnectionId;

            var rechazados = _pendingDeliveries.GetOrAdd(numPedido, _ => new HashSet<string>());
            lock (rechazados)
            {
                rechazados.Add(connectionId);
            }

            var totalActivos = _repartidorConnections.Count(kvp => kvp.Value.disponibilidad == "Activo");

            bool todosRechazaron;
            lock (rechazados)
            {
                todosRechazaron = totalActivos > 0 && rechazados.Count >= totalActivos;
            }

            if (todosRechazaron)
            {
                _pendingDeliveries.TryRemove(numPedido, out _);
                await Clients.Group("Admins").SendAsync("AlertaPedidoIgnorada", new
                {
                    numPedido = numPedido,
                    mensaje = $"⚠️ Ningún repartidor aceptó el pedido #{numPedido}. Revisá la asignación.",
                    tipo = "todos_rechazaron"
                });
            }
        }

        // ── MÉTODO: Admin marca alerta como vista ────────────────────────
        /// <summary>
        /// Llamado por un Admin/Vendedor cuando hace clic en "Entendido".
        /// Notifica a TODOS los demás admins para que oculten esa alerta.
        /// </summary>
        public async Task MarcarAlertaVista(int numPedido)
        {
            _pendingDeliveries.TryRemove(numPedido, out _);
            await Clients.Group("Admins").SendAsync("AlertaAdminVista", new
            {
                numPedido = numPedido
            });
        }

        // ── MÉTODO: Registrar timeout de pedido ──────────────────────────
        public async Task NotificarTimeoutPedido(int numPedido)
        {
            await Clients.Group("Admins").SendAsync("AlertaPedidoIgnorada", new
            {
                numPedido = numPedido,
                mensaje = $"⏰ El pedido #{numPedido} lleva más de 5 minutos sin ser aceptado.",
                tipo = "timeout"
            });
        }

        // ── MÉTODO ESTÁTICO: Resetea el tracking de rechazos ─────────────
        /// <summary>
        /// ★ FIX: Faltaba este método. El PedidosController lo llama en
        ///   SolicitarRepartidorAjax y RevertirAEnPreparacionAjax para que
        ///   todos los repartidores puedan volver a recibir la alerta del pedido.
        /// </summary>
        public static void ResetRechazos(int numPedido)
        {
            _pendingDeliveries.TryRemove(numPedido, out _);
        }

        // ── MÉTODO ESTÁTICO: Limpia el tracking al cancelar un pedido ────
        /// <summary>
        /// Llamado desde controllers (ej: ClienteController al cancelar un pedido)
        /// para limpiar el tracking de rechazos y evitar notificaciones huérfanas.
        /// </summary>
        public static void ClearPendingDelivery(int numPedido)
        {
            _pendingDeliveries.TryRemove(numPedido, out _);
        }

        // ── ★ NUEVOS MÉTODOS: tracking de pedidos en búsqueda ───────────

        /// <summary>
        /// Registra que un pedido está en búsqueda activa de repartidor.
        /// Llamado por SolicitarRepartidorAjax al emitir el broadcast SignalR.
        /// </summary>
        public static void RegistrarPedidoEnBusqueda(int numPedido)
        {
            _pedidosEnBusqueda[numPedido] = DateTime.UtcNow;
        }

        /// <summary>
        /// Remueve un pedido del tracking de búsqueda.
        /// Llamado cuando un repartidor acepta el pedido o cuando se revierte.
        /// </summary>
        public static void RemoverPedidoEnBusqueda(int numPedido)
        {
            _pedidosEnBusqueda.TryRemove(numPedido, out _);
        }

        /// <summary>
        /// Devuelve los números de pedido actualmente en búsqueda de repartidor.
        /// Usado por el Index action para renderizar "Esperando..." en la vista Admin
        /// al recargar la página sin necesidad de una columna extra en la BD.
        /// </summary>
        public static HashSet<int> GetPedidosEnBusqueda()
        {
            return new HashSet<int>(_pedidosEnBusqueda.Keys);
        }
    }
}

