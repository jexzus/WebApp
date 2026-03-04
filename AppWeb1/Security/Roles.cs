// ============================================================
// ARCHIVO: Security/Roles.cs
// ============================================================
using Microsoft.AspNetCore.Http;

namespace AppWeb1.Security
{
    public static class Roles
    {
        public const string SuperAdmin = "superadmin";
        public const string Admin = "admin";
        public const string Vendedor = "vendedor";
        public const string Repartidor = "repartidor";
        public const string Cliente = "cliente";

        // ── Helpers de verificación de rol ──────────────────────────────

        public static bool EsSuperAdmin(ISession s)
            => Igual(s, SuperAdmin);

        public static bool EsAdmin(ISession s)
            => Igual(s, Admin);

        public static bool EsAdminOSuperAdmin(ISession s)
            => EsAdmin(s) || EsSuperAdmin(s);

        public static bool EsVendedor(ISession s)
            => Igual(s, Vendedor);

        public static bool EsRepartidor(ISession s)
            => Igual(s, Repartidor);

        // ── Permisos de negocio ──────────────────────────────────────────

        /// <summary>
        /// Puede ver y gestionar pedidos: SuperAdmin, Admin, Vendedor y Repartidor.
        /// </summary>
        public static bool PuedeGestionarPedidos(ISession s)
            => EsSuperAdmin(s) || EsAdmin(s) || EsVendedor(s) || EsRepartidor(s);

        /// <summary>
        /// Puede cambiar estado de pedidos (menos a "Entregado"):
        /// SuperAdmin, Admin, Vendedor.
        /// El Repartidor solo puede pasar de "En reparto" a "Entregado".
        /// </summary>
        public static bool PuedeCambiarEstado(ISession s)
            => EsSuperAdmin(s) || EsAdmin(s) || EsVendedor(s);

        /// <summary>
        /// Puede asignar repartidores a pedidos: SuperAdmin, Admin, Vendedor.
        /// </summary>
        public static bool PuedeAsignarPedidos(ISession s)
            => EsSuperAdmin(s) || EsAdmin(s) || EsVendedor(s);

        /// <summary>
        /// Puede marcar un pedido como Entregado: Repartidor (solo el suyo) y SuperAdmin.
        /// </summary>
        public static bool PuedeMarcarEntregado(ISession s)
            => EsRepartidor(s) || EsSuperAdmin(s);

        // ── Helper privado ───────────────────────────────────────────────

        private static bool Igual(ISession s, string rol)
        {
            var r = s.GetString("Rol");
            return !string.IsNullOrEmpty(r) &&
                   r.Equals(rol, StringComparison.OrdinalIgnoreCase);
        }
    }
}