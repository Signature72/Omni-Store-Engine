using FluentAssertions;
using Grpc.Core;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniStore.OrderService.Data;
using OmniStore.OrderService.Entities;
using OmniStore.OrderService.Models;
using OmniStore.OrderService.Services;
using OmniStore.Shared.Events;
using OmniStore.Shared.Protos;
using Xunit;

namespace OmniStore.OrderService.Tests;

public class OrderServiceTests
{
    private OrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new OrderDbContext(options);
    }

    private static AsyncUnaryCall<TResponse> CreateAsyncUnaryCall<TResponse>(TResponse response)
    {
        return new AsyncUnaryCall<TResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    [Fact]
    public async Task CheckoutAsync_WhenCartHasItemsAndInventoryHasStock_ShouldCreatePaidOrderAndPublishEvent()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();

        var productId = Guid.NewGuid();
        var cartId = "cart_user_1";
        var mockCart = new Cart
        {
            CartId = cartId,
            Items = new List<CartItem>
            {
                new()
                {
                    ProductId = productId,
                    ProductName = "Enterprise Mechanical Keyboard",
                    UnitPrice = 199.00m,
                    Quantity = 2
                }
            }
        };

        var mockCartService = new Mock<ICartService>();
        mockCartService.Setup(c => c.GetCartAsync(cartId)).ReturnsAsync(mockCart);
        mockCartService.Setup(c => c.ClearCartAsync(cartId)).ReturnsAsync(true);

        var mockGrpcClient = new Mock<InventoryProtoService.InventoryProtoServiceClient>();
        mockGrpcClient
            .Setup(g => g.ReserveStockAsync(It.IsAny<ReserveStockRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(new ReserveStockResponse
            {
                IsSuccess = true,
                ReservationId = Guid.NewGuid().ToString()
            }));

        var mockPayment = new Mock<IPaymentSimulator>();
        mockPayment
            .Setup(p => p.ProcessPaymentAsync(It.IsAny<Guid>(), 398.00m, "CreditCard", default))
            .ReturnsAsync(new PaymentResult(true, "TXN_MOCK_SUCCESS_123", null));

        var mockPublish = new Mock<IPublishEndpoint>();

        var checkoutService = new CheckoutService(
            dbContext,
            mockCartService.Object,
            mockGrpcClient.Object,
            mockPayment.Object,
            mockPublish.Object,
            NullLogger<CheckoutService>.Instance);

        var request = new CheckoutRequest(
            cartId,
            Guid.NewGuid(),
            "John Enterprise",
            "john@enterprise.com",
            "line_john_1",
            "CreditCard"
        );

        // Act
        var result = await checkoutService.CheckoutAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Paid");
        result.TotalAmount.Should().Be(398.00m);
        result.TransactionId.Should().Be("TXN_MOCK_SUCCESS_123");

        // Verify Order in DB
        var savedOrder = await dbContext.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == result.OrderId);
        savedOrder.Should().NotBeNull();
        savedOrder!.Status.Should().Be(OrderStatus.Paid);
        savedOrder.Items.Should().HaveCount(1);
        savedOrder.Items.First().SubTotal.Should().Be(398.00m);

        // Verify MassTransit Outbox publish
        mockPublish.Verify(p => p.Publish(It.IsAny<OrderPaidEvent>(), default), Times.Once);

        // Verify Redis Cart cleanup
        mockCartService.Verify(c => c.ClearCartAsync(cartId), Times.Once);
    }

    [Fact]
    public async Task CheckoutAsync_WhenCartIsEmpty_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cartId = "empty_cart";

        var mockCartService = new Mock<ICartService>();
        mockCartService.Setup(c => c.GetCartAsync(cartId)).ReturnsAsync(new Cart { CartId = cartId });

        var mockGrpcClient = new Mock<InventoryProtoService.InventoryProtoServiceClient>();
        var mockPayment = new Mock<IPaymentSimulator>();
        var mockPublish = new Mock<IPublishEndpoint>();

        var checkoutService = new CheckoutService(
            dbContext,
            mockCartService.Object,
            mockGrpcClient.Object,
            mockPayment.Object,
            mockPublish.Object,
            NullLogger<CheckoutService>.Instance);

        var request = new CheckoutRequest(cartId, Guid.NewGuid(), "Empty User", "empty@test.com", null, "CreditCard");

        // Act
        var act = async () => await checkoutService.CheckoutAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty shopping cart*");
    }

    [Fact]
    public async Task CheckoutAsync_WhenInventoryStockReservationFails_ShouldAbortAndThrowException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cartId = "cart_stock_fail";
        var mockCart = new Cart
        {
            CartId = cartId,
            Items = new List<CartItem>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Sold Out Item", UnitPrice = 100m, Quantity = 10 }
            }
        };

        var mockCartService = new Mock<ICartService>();
        mockCartService.Setup(c => c.GetCartAsync(cartId)).ReturnsAsync(mockCart);

        var mockGrpcClient = new Mock<InventoryProtoService.InventoryProtoServiceClient>();
        mockGrpcClient
            .Setup(g => g.ReserveStockAsync(It.IsAny<ReserveStockRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(new ReserveStockResponse
            {
                IsSuccess = false,
                ErrorMessage = "Insufficient stock for 'Sold Out Item'."
            }));

        var mockPayment = new Mock<IPaymentSimulator>();
        var mockPublish = new Mock<IPublishEndpoint>();

        var checkoutService = new CheckoutService(
            dbContext,
            mockCartService.Object,
            mockGrpcClient.Object,
            mockPayment.Object,
            mockPublish.Object,
            NullLogger<CheckoutService>.Instance);

        var request = new CheckoutRequest(cartId, Guid.NewGuid(), "Buyer", "buyer@test.com", null, "CreditCard");

        // Act
        var act = async () => await checkoutService.CheckoutAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Insufficient stock*");

        // Payment should NEVER be called if reservation failed!
        mockPayment.Verify(p => p.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), default), Times.Never);

        // No order should be saved
        var ordersCount = await dbContext.Orders.CountAsync();
        ordersCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckoutAsync_WhenPaymentFails_ShouldReleaseReservationAndThrowException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cartId = "cart_pay_fail";
        var reservationGuid = Guid.NewGuid();

        var mockCart = new Cart
        {
            CartId = cartId,
            Items = new List<CartItem>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Premium Item", UnitPrice = 500m, Quantity = 1 }
            }
        };

        var mockCartService = new Mock<ICartService>();
        mockCartService.Setup(c => c.GetCartAsync(cartId)).ReturnsAsync(mockCart);

        var mockGrpcClient = new Mock<InventoryProtoService.InventoryProtoServiceClient>();
        mockGrpcClient
            .Setup(g => g.ReserveStockAsync(It.IsAny<ReserveStockRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(new ReserveStockResponse
            {
                IsSuccess = true,
                ReservationId = reservationGuid.ToString()
            }));

        mockGrpcClient
            .Setup(g => g.ReleaseStockReservationAsync(It.IsAny<ReleaseStockReservationRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(new ReleaseStockReservationResponse { IsSuccess = true }));

        var mockPayment = new Mock<IPaymentSimulator>();
        mockPayment
            .Setup(p => p.ProcessPaymentAsync(It.IsAny<Guid>(), 500m, "CreditCard", default))
            .ReturnsAsync(new PaymentResult(false, string.Empty, "Card declined: insufficient funds."));

        var mockPublish = new Mock<IPublishEndpoint>();

        var checkoutService = new CheckoutService(
            dbContext,
            mockCartService.Object,
            mockGrpcClient.Object,
            mockPayment.Object,
            mockPublish.Object,
            NullLogger<CheckoutService>.Instance);

        var request = new CheckoutRequest(cartId, Guid.NewGuid(), "Declined User", "user@test.com", null, "CreditCard");

        // Act
        var act = async () => await checkoutService.CheckoutAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Payment failed: Card declined*");

        // Compensating transaction must be invoked!
        mockGrpcClient.Verify(g => g.ReleaseStockReservationAsync(
            It.Is<ReleaseStockReservationRequest>(r => r.ReservationId == reservationGuid.ToString()), null, null, default), Times.Once);

        // Event should NOT be published
        mockPublish.Verify(p => p.Publish(It.IsAny<OrderPaidEvent>(), default), Times.Never);
    }

    [Fact]
    public void Cart_AddItem_ShouldCalculateTotalAmountAndQuantityCorrectly()
    {
        // Arrange
        var cart = new Cart { CartId = "test_cart" };
        cart.Items.Add(new CartItem { ProductId = Guid.NewGuid(), ProductName = "Item 1", UnitPrice = 100m, Quantity = 2 });
        cart.Items.Add(new CartItem { ProductId = Guid.NewGuid(), ProductName = "Item 2", UnitPrice = 50m, Quantity = 3 });

        // Assert
        cart.TotalItems.Should().Be(5);
        cart.TotalAmount.Should().Be(350m);
    }

    [Fact]
    public async Task PaymentSimulator_WithValidAmount_ShouldReturnApprovedTransaction()
    {
        // Arrange
        var simulator = new PaymentSimulator(NullLogger<PaymentSimulator>.Instance);
        var orderId = Guid.NewGuid();

        // Act
        var result = await simulator.ProcessPaymentAsync(orderId, 150m, "LinePay");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TransactionId.Should().StartWith("TXN_");
        result.ErrorMessage.Should().BeNull();
    }
}
