using Microsoft.EntityFrameworkCore;
using Drogueria.Models;

namespace Drogueria.Data;

public class AppDbContex : DbContext
{
    public AppDbContex(DbContextOptions<AppDbContex> options)
        : base(options)
    {
    }

    public DbSet<Usuario> Usuarios { get; set; } = null!;
    public DbSet<Categoria> Categorias { get; set; } = null!;
    public DbSet<Producto> Productos { get; set; } = null!;
    public DbSet<Pedido> Pedidos { get; set; } = null!;
    public DbSet<DetallePedido> DetallesPedido { get; set; } = null!;
    public DbSet<DireccionUsuario> DireccionesUsuario { get; set; } = null!;
    public DbSet<MensajeChat> MensajesChat { get; set; } = null!;



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Usuario — PK Uuid varchar(36)
        modelBuilder.Entity<Usuario>()
            .HasKey(u => u.Uuid);

        modelBuilder.Entity<Usuario>()
            .Property(u => u.Uuid)
            .HasColumnType("varchar(36)");

        // Categoria — PK como char(36) UUID en MySQL
        modelBuilder.Entity<Categoria>()
            .Property(c => c.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        // Producto — PK como char(36) UUID en MySQL
        modelBuilder.Entity<Producto>()
            .Property(p => p.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        // Producto → Categoria: FK char(36), sin delete en cascada
        modelBuilder.Entity<Producto>()
            .Property(p => p.CategoriaId)
            .HasColumnType("char(36)");

        modelBuilder.Entity<Producto>()
            .HasOne(p => p.Categoria)
            .WithMany()
            .HasForeignKey(p => p.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pedido — PK como char(36) UUID en MySQL
        modelBuilder.Entity<Pedido>()
            .Property(p => p.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        modelBuilder.Entity<Pedido>()
            .Property(p => p.UsuarioId)
            .HasColumnType("varchar(36)");

        modelBuilder.Entity<Pedido>()
            .HasOne(p => p.Usuario)
            .WithMany(u => u.Pedidos)
            .HasForeignKey(p => p.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Pedido>()
            .Property(p => p.RepartidorId)
            .HasColumnType("varchar(36)")
            .IsRequired(false);

        modelBuilder.Entity<Pedido>()
            .HasOne(p => p.Repartidor)
            .WithMany(u => u.PedidosAsignados)
            .HasForeignKey(p => p.RepartidorId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Pedido>()
            .HasMany(p => p.Detalles)
            .WithOne(d => d.Pedido)
            .HasForeignKey(d => d.PedidoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Pedido>()
            .Property(p => p.LatitudRepartidor)
            .HasColumnType("double");

        modelBuilder.Entity<Pedido>()
            .Property(p => p.LongitudRepartidor)
            .HasColumnType("double");

            
        // DetallePedido — PK como char(36) UUID en MySQL
        modelBuilder.Entity<DetallePedido>()
            .Property(d => d.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        modelBuilder.Entity<DetallePedido>()
            .Property(d => d.PedidoId)
            .HasColumnType("char(36)");

        modelBuilder.Entity<DetallePedido>()
            .Property(d => d.ProductoId)
            .HasColumnType("char(36)");

        modelBuilder.Entity<DetallePedido>()
            .HasOne(d => d.Producto)
            .WithMany()
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);

        // DireccionUsuario — PK como char(36) UUID en MySQL
        modelBuilder.Entity<DireccionUsuario>()
            .Property(d => d.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        modelBuilder.Entity<DireccionUsuario>()
            .Property(d => d.UsuarioId)
            .HasColumnType("varchar(36)");

        modelBuilder.Entity<DireccionUsuario>()
            .HasOne(d => d.Usuario)
            .WithMany(u => u.Direcciones)
            .HasForeignKey(d => d.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // MensajeChat — PK como char(36) UUID en MySQL
        modelBuilder.Entity<MensajeChat>()
            .Property(m => m.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        modelBuilder.Entity<MensajeChat>()
            .Property(m => m.PedidoId)
            .HasColumnType("char(36)");

        modelBuilder.Entity<MensajeChat>()
            .HasOne(m => m.Pedido)
            .WithMany()
            .HasForeignKey(m => m.PedidoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}