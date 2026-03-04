namespace AppWeb1.Security
{
    public static class Roles
    {
        public const string Admin = "admin";
        public const string Vendedor = "vendedor";
        public static bool EsAdmin(ISession s) =>
            string.Equals(s.GetString("Rol"), Admin, StringComparison.OrdinalIgnoreCase);
        public static bool EsVendedor(ISession s) =>
            string.Equals(s.GetString("Rol"), Vendedor, StringComparison.OrdinalIgnoreCase);
        public static bool AdminOVendedor(ISession s) => EsAdmin(s) || EsVendedor(s);
    }
}
