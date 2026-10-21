using MassTransit;
using Microsoft.EntityFrameworkCore;
using OmniStore.OrderService.Entities;

namespace OmniStore.OrderService.Data;

/// <summary>
/// Architecture: Database-per-Service & Transactional Outbox Pattern
/// OrderDbContext holds business entities (Order, OrderItem) AND MassTransit Outbox tables
/// (InboxState, OutboxMessage, OutboxState).
/// When an order is saved, OrderPaidEvent is committed to OutboxMessage in the SAME atomic transaction!
/// </summary>
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Order Entity
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerName).IsRequired().HasMaxLength(150);
            entity.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(256);
            entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(o => o.UserId);

            entity.HasMany(o => o.Items)
                  .WithOne(i => i.Order)
                  .HasForeignKey(i => i.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OrderItem Entity
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        // Configure MassTransit Outbox Tables
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
