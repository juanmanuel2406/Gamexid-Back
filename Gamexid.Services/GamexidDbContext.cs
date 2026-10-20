using Gamexid.Models;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Services;

public class GamexidDbContext(DbContextOptions<GamexidDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SerializedUnit> SerializedUnits => Set<SerializedUnit>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementItem> InventoryMovementItems => Set<InventoryMovementItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<Order>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Order>().HasIndex(x => new { x.BranchId, x.Created });
        model.Entity<Order>().HasIndex(x => new { x.BranchId, x.Reference }).IsUnique();
        model.Entity<Order>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Order>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Order>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrderItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrderItem>().HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
        model.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(x => x.Total).HasPrecision(18, 2);
        model.Entity<OrderItem>().HasMany(x => x.Serials).WithOne().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SerialNumber>().HasIndex(x => x.Serial).IsUnique();
        model.Entity<SerialNumber>().HasIndex(x => x.SerializedUnitId).IsUnique();
        model.Entity<SerialNumber>().HasOne<SerializedUnit>().WithMany().HasForeignKey(x => x.SerializedUnitId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
        model.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        model.Entity<Product>().HasIndex(x => x.Ean).IsUnique();
        model.Entity<SerializedUnit>().HasIndex(x => x.SerialNumber).IsUnique();
        model.Entity<User>().HasIndex(x => x.Email).IsUnique();
        model.Entity<SerializedUnit>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SerializedUnit>().HasOne<Branch>().WithMany().HasForeignKey(x => x.CurrentBranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.InventoryMovementId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<InventoryMovement>().HasOne<User>().WithMany().HasForeignKey(x => x.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasOne<Branch>().WithMany().HasForeignKey(x => x.SourceBranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasOne<Branch>().WithMany().HasForeignKey(x => x.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovementItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovementItem>().HasOne<SerializedUnit>().WithMany().HasForeignKey(x => x.SerializedUnitId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<User>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
