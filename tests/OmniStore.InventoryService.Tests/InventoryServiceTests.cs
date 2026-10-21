using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniStore.InventoryService.Consumers;
using OmniStore.InventoryService.Data;
using OmniStore.InventoryService.Entities;
using OmniStore.InventoryService.Services;
using OmniStore.Shared.Events;
using Xunit;

namespace OmniStore.InventoryService.Tests;

public class InventoryServiceTests
{
    private InventoryDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new InventoryDbContext(options);
    }

    [Fact]
    public async Task ReserveStockAsync_WhenSufficientStock_ShouldHoldStockAndCreateReservation()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "TEST-LAPTOP",
            Name = "Test Laptop",
            Price = 1200m,
            AvailableStock = 10,
            ReservedStock = 0
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var orderId = Guid.NewGuid();
        var items = new List<ReserveItemDto> { new(product.Id, 3) };

        // Act
        var result = await manager.ReserveStockAsync(orderId, items, 900);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ReservationId.Should().NotBeEmpty();

        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        updatedProduct!.AvailableStock.Should().Be(7);
        updatedProduct.ReservedStock.Should().Be(3);

        var reservation = await dbContext.StockReservations.FirstOrDefaultAsync(r => r.OrderId == orderId);
        reservation.Should().NotBeNull();
        reservation!.Status.Should().Be(ReservationStatus.Reserved);
        reservation.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task ReserveStockAsync_WhenInsufficientStock_ShouldFailAndNotChangeStock()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "LOW-STOCK-ITEM",
            Name = "Low Stock Item",
            Price = 50m,
            AvailableStock = 2,
            ReservedStock = 0
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var orderId = Guid.NewGuid();
        var items = new List<ReserveItemDto> { new(product.Id, 5) }; // Requesting 5, only 2 available

        // Act
        var result = await manager.ReserveStockAsync(orderId, items, 900);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Insufficient stock");

        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        updatedProduct!.AvailableStock.Should().Be(2); // Unaltered
        updatedProduct.ReservedStock.Should().Be(0);

        var reservations = await dbContext.StockReservations.CountAsync();
        reservations.Should().Be(0);
    }

    [Fact]
    public async Task ReserveStockAsync_WhenProductNotFound_ShouldReturnFailure()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var nonExistentId = Guid.NewGuid();
        var items = new List<ReserveItemDto> { new(nonExistentId, 1) };

        // Act
        var result = await manager.ReserveStockAsync(Guid.NewGuid(), items, 900);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task CommitStockReservationAsync_WhenReserved_ShouldCommitAndDeductReservedStock()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "PHONE-15",
            Name = "Smartphone 15",
            Price = 999m,
            AvailableStock = 10,
            ReservedStock = 0
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var orderId = Guid.NewGuid();

        // 1. Reserve 2 items first (Phase 1)
        await manager.ReserveStockAsync(orderId, new[] { new ReserveItemDto(product.Id, 2) }, 900);

        // 2. Commit (Phase 2 - Payment confirmed)
        var commitResult = await manager.CommitStockReservationAsync(orderId);

        // Assert
        commitResult.Should().BeTrue();

        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        updatedProduct!.AvailableStock.Should().Be(8); // Reduced in Phase 1
        updatedProduct.ReservedStock.Should().Be(0);  // Cleared in Phase 2

        var reservation = await dbContext.StockReservations.FirstAsync(r => r.OrderId == orderId);
        reservation.Status.Should().Be(ReservationStatus.Committed);
        reservation.CommittedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CommitStockReservationAsync_WhenAlreadyCommitted_ShouldBeIdempotent()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "IDEM-TEST",
            Name = "Idempotency Test Item",
            Price = 100m,
            AvailableStock = 5,
            ReservedStock = 0
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var orderId = Guid.NewGuid();

        await manager.ReserveStockAsync(orderId, new[] { new ReserveItemDto(product.Id, 1) }, 900);
        await manager.CommitStockReservationAsync(orderId); // First commit

        // Act: Second commit (Simulating RabbitMQ message redelivery / duplicate)
        var secondCommitResult = await manager.CommitStockReservationAsync(orderId);

        // Assert
        secondCommitResult.Should().BeTrue();

        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        updatedProduct!.AvailableStock.Should().Be(4);
        updatedProduct.ReservedStock.Should().Be(0); // Should not become negative!
    }

    [Fact]
    public async Task ReleaseStockReservationAsync_WhenReserved_ShouldRestoreAvailableStock()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "CANCELLED-ORDER",
            Name = "Cancelled Product",
            Price = 250m,
            AvailableStock = 10,
            ReservedStock = 0
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var manager = new InventoryManager(dbContext, NullLogger<InventoryManager>.Instance);
        var orderId = Guid.NewGuid();

        await manager.ReserveStockAsync(orderId, new[] { new ReserveItemDto(product.Id, 4) }, 900);

        // Act: Payment aborted or timed out -> Release hold
        var releaseResult = await manager.ReleaseStockReservationAsync(orderId);

        // Assert
        releaseResult.Should().BeTrue();

        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        updatedProduct!.AvailableStock.Should().Be(10); // Restored!
        updatedProduct.ReservedStock.Should().Be(0);

        var reservation = await dbContext.StockReservations.FirstAsync(r => r.OrderId == orderId);
        reservation.Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public async Task OrderPaidConsumer_ShouldCallCommitOnInventoryManager()
    {
        // Arrange
        var mockManager = new Mock<IInventoryManager>();
        mockManager
            .Setup(m => m.CommitStockReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var consumer = new OrderPaidConsumer(mockManager.Object, NullLogger<OrderPaidConsumer>.Instance);
        var orderId = Guid.NewGuid();

        var consumeContextMock = new Mock<ConsumeContext<OrderPaidEvent>>();
        consumeContextMock.Setup(c => c.Message).Returns(new OrderPaidEvent
        {
            OrderId = orderId,
            TotalAmount = 500m,
            CustomerName = "Jane Doe"
        });

        // Act
        await consumer.Consume(consumeContextMock.Object);

        // Assert
        mockManager.Verify(m => m.CommitStockReservationAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
