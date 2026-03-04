using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AppWeb1.Models;

namespace AppWeb1.Data
{
    // Usamos IdentityDbContext para que en el futuro puedas migrar a Identity sin romper nada.
    public class DeliveryDBContext : IdentityDbContext<ApplicationUser>
    {
        public DeliveryDBContext(DbContextOptions<DeliveryDBContext> options) : base(options) { }

        public DbSet<PreRegistroToken> PreRegistros { get; set; } = default!;
        public DbSet<Usuario> Usuarios { get; set; } = default!;
        public DbSet<Cliente> Clientes { get; set; } = default!;
        public DbSet<Producto> Productos { get; set; } = default!;
        public DbSet<Pedido> Pedidos { get; set; } = default!;
        public DbSet<DetallePedido> DetallePedidos { get; set; } = default!;
        public DbSet<Vendedor> Vendedores { get; set; } = default!;
        // ----- MEJORA: DbSet de Repartidor agregado -----
        public DbSet<Repartidor> Repartidores { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // MUY IMPORTANTE para Identity

            // ===== USUARIO =====
            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.ToTable("Usuario");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NombreUsuario).HasMaxLength(100).IsUnicode(false);
                entity.Property(e => e.Contraseña).HasMaxLength(100).IsUnicode(false);
                entity.Property(e => e.Rol).HasMaxLength(20).IsUnicode(false);
            });

            // ===== CLIENTE =====
            modelBuilder.Entity<Cliente>(entity =>
            {
                entity.ToTable("Cliente");
                entity.HasKey(e => e.IdCliente);

                entity.Property(e => e.Nombre).HasMaxLength(50).IsUnicode(false);
                entity.Property(e => e.Apellido).HasMaxLength(50).IsUnicode(false);
                entity.Property(e => e.Domicilio).HasMaxLength(100).IsUnicode(false);
                entity.Property(e => e.NumTelefono).HasMaxLength(20).IsUnicode(false);
                entity.Property(e => e.Email).HasMaxLength(200).IsUnicode(false);

                entity.HasOne(c => c.Usuario)
                      .WithMany(u => u.Clientes)
                      .HasForeignKey(c => c.IdUsuario)
                      .HasConstraintName("FK_Cliente_Usuario");
            });

            // ===== PRODUCTO =====
            modelBuilder.Entity<Producto>(entity =>
            {
                entity.ToTable("Producto");
                entity.HasKey(e => e.IdProducto);

                entity.Property(e => e.NombreProducto).HasMaxLength(100).IsUnicode(false);
                entity.Property(e => e.Descripcion).HasColumnType("text");
                entity.Property(e => e.Precio).HasColumnType("decimal(10, 2)");
                entity.Property(e => e.Imagen).HasMaxLength(255).IsUnicode(false);
            });

            // ===== PEDIDO =====
            // (Bloque consolidado: incluye la nueva relación con Repartidor)
            modelBuilder.Entity<Pedido>(entity =>
            {
                entity.ToTable("Pedido");
                entity.HasKey(e => e.NumPedido);

                entity.Property(e => e.FechaPedido).HasColumnType("datetime");
                entity.Property(e => e.EstadoPedido).HasMaxLength(20).IsUnicode(false);
                entity.Property(e => e.MontoTotal).HasColumnType("decimal(10, 2)");
                entity.Property(e => e.Observaciones).HasColumnType("text");

                // Relación con Cliente
                entity.HasOne(d => d.IdClienteNavigation)
                      .WithMany(p => p.Pedidos)
                      .HasForeignKey(d => d.IdCliente)
                      .HasConstraintName("FK_Pedido_Cliente");

                // ----- MEJORA: Relación con Repartidor agregada -----
                entity.HasOne(p => p.Repartidor)
                      .WithMany() // Repartidor no necesita una lista de Pedidos
                      .HasForeignKey(p => p.IdRepartidor)
                      .IsRequired(false) // Un pedido puede crearse sin repartidor asignado
                      .HasConstraintName("FK_Pedido_Repartidor");
            });

            // ===== DETALLE PEDIDO =====
            modelBuilder.Entity<DetallePedido>(entity =>
            {
                entity.ToTable("DetallePedido");
                entity.HasKey(e => e.IdDetalle);

                entity.Property(e => e.PrecioUnitario).HasColumnType("decimal(10, 2)");

                entity.HasOne(d => d.IdProductoNavigation)
                      .WithMany(p => p.DetallePedidos)
                      .HasForeignKey(d => d.IdProducto)
                      .HasConstraintName("FK_DetallePedido_Producto");

                entity.HasOne(d => d.NumPedidoNavigation)
                      .WithMany(p => p.DetallePedidos)
                      .HasForeignKey(d => d.NumPedido)
                      .HasConstraintName("FK_DetallePedido_Pedido");
            });

            // ===== PRE-REGISTROS (Tokens de verificación) =====
            modelBuilder.Entity<PreRegistroToken>(entity =>
            {
                entity.ToTable("PreRegistros"); // 👈 Nombre exacto de tu tabla SQL
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Email)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(e => e.TokenHash)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(e => e.Tipo)
                      .HasMaxLength(20)
                      .HasDefaultValue("registro");

                entity.Property(e => e.Usado)
                      .IsRequired()
                      .HasColumnType("bit")
                      .ValueGeneratedNever(); // <- fuerza a EF a enviar el valor

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("datetime2")
                      .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.Property(e => e.ExpiresAt)
                      .HasColumnType("datetime2");

                entity.HasIndex(e => new { e.Email, e.Tipo, e.Usado });

            }); 

            // ========================== VENDEDOR ================================
            // (Estaba anidado incorrectamente dentro de PreRegistros)
            modelBuilder.Entity<Vendedor>(e =>
            {
                e.ToTable("Vendedores");
                e.HasKey(v => v.Id);
                e.HasOne(v => v.Usuario)
                 .WithMany() // si Usuario no tiene colección de Vendedores
                 .HasForeignKey(v => v.IdUsuario)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ========================= REPARTIDOR ===================================
            // (Estaba anidado incorrectamente dentro de PreRegistros)
            modelBuilder.Entity<Repartidor>(e =>
            {
                e.ToTable("Repartidores");
                e.HasKey(r => r.Id);

                e.HasOne(r => r.Usuario)
                 .WithMany() // Usuario no tiene colección de Repartidores
                 .HasForeignKey(r => r.IdUsuario)
                 .OnDelete(DeleteBehavior.Cascade);
            });
                        
        }
    }
}