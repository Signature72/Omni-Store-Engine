using MassTransit;
using OmniStore.OrderService.Data;
using OmniStore.OrderService.Entities;
using OmniStore.OrderService.Models;
using OmniStore.Shared.Events;
using OmniStore.Shared.Protos;

namespace OmniStore.OrderService.Services;

public interface ICheckoutService
{
    Task<CheckoutResponse> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);
}

public class CheckoutService : ICheckoutService
{
    private readonly OrderDbContext _dbContext;
    private readonly ICartService _cartService;
    private readonly InventoryProtoService.InventoryProtoServiceClient _inventoryClient;
    private readonly IPaymentSimulator _paymentSimulator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        OrderDbContext dbContext,
        ICartService cartService,
        InventoryProtoService.InventoryProtoServiceClient inventoryClient,
        IPaymentSimulator paymentSimulator,
        IPublishEndpoint publishEndpoint,
        ILogger<CheckoutService> logger)
    {
        _dbContext = dbContext;
        _cartService = cartService;
        _inventoryClient = inventoryClient;
        _paymentSimulator = paymentSimulator;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<CheckoutResponse> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        // ---------------------------------------------------------------------
        // 1. Fetch Cart from Redis
        // ---------------------------------------------------------------------
        var cart = await _cartService.GetCartAsync(request.CartId);
        if (cart.Items.Count == 0)
        {
            throw new InvalidOperationException("Cannot checkout an empty shopping cart.");
        }

        var orderId = Guid.NewGuid();
        _logger.LogInformation("Initiating Checkout for Order {OrderId} (Cart: {CartId}, Amount: ${Amount:F2})",
            orderId, request.CartId, cart.TotalAmount);

        // ---------------------------------------------------------------------
        // 2. Phase 1: Sync Reserve (gRPC call to Inventory Service)
        // ---------------------------------------------------------------------
        var reserveRequest = new ReserveStockRequest
        {
            OrderId = orderId.ToString(),
            TtlSeconds = 900 // 15-minute hold
        };

        foreach (var item in cart.Items)
        {
            reserveRequest.Items.Add(new StockItemRequest
            {
                ProductId = item.ProductId.ToString(),
                Quantity = item.Quantity
            });
        }

        _logger.LogInformation("Calling Inventory gRPC server to temporarily hold stock for Order {OrderId}...", orderId);
        var reserveResponse = await _inventoryClient.ReserveStockAsync(reserveRequest, cancellationToken: cancellationToken);

        if (!reserveResponse.IsSuccess)
        {
            _logger.LogWarning("Checkout rejected: Stock reservation failed for Order {OrderId}: {Error}",
                orderId, reserveResponse.ErrorMessage);

            throw new InvalidOperationException($"Stock reservation failed: {reserveResponse.ErrorMessage}");
        }

        Guid.TryParse(reserveResponse.ReservationId, out var reservationId);
        _logger.LogInformation("Stock reserved successfully. Reservation ID: {ReservationId}", reservationId);

        // ---------------------------------------------------------------------
        // 3. Process Payment (Simulator)
        // ---------------------------------------------------------------------
        var paymentResult = await _paymentSimulator.ProcessPaymentAsync(
            orderId, cart.TotalAmount, request.PaymentMethod, cancellationToken);

        if (!paymentResult.IsSuccess)
        {
            _logger.LogError("Payment failed for Order {OrderId}. Releasing stock reservation hold...", orderId);

            // Compensating action: release held stock
            await _inventoryClient.ReleaseStockReservationAsync(new ReleaseStockReservationRequest
            {
                OrderId = orderId.ToString(),
                ReservationId = reservationId.ToString()
            }, cancellationToken: cancellationToken);

            throw new InvalidOperationException($"Payment failed: {paymentResult.ErrorMessage}");
        }

        // ---------------------------------------------------------------------
        // 4. Phase 2: Transactional Outbox Pattern (Single DB Transaction)
        // ---------------------------------------------------------------------
        var order = new Order
        {
            Id = orderId,
            UserId = request.UserId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerLineUserId = request.CustomerLineUserId,
            TotalAmount = cart.TotalAmount,
            Status = OrderStatus.Paid,
            PaymentTransactionId = paymentResult.TransactionId,
            InventoryReservationId = reservationId,
            CreatedAtUtc = DateTime.UtcNow,
            PaidAtUtc = DateTime.UtcNow
        };

        foreach (var item in cart.Items)
        {
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        _dbContext.Orders.Add(order);

        // Publish OrderPaidEvent through MassTransit Transactional Outbox
        var orderPaidEvent = new OrderPaidEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            CustomerLineUserId = order.CustomerLineUserId ?? string.Empty,
            TotalAmount = order.TotalAmount,
            PaidAtUtc = order.PaidAtUtc.Value,
            Items = order.Items.Select(i => new OrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        await _publishEndpoint.Publish(orderPaidEvent, cancellationToken);

        // Atomically commit Order + OutboxMessage to order_db!
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} and OutboxMessage successfully saved to OrderDB.", orderId);

        // ---------------------------------------------------------------------
        // 5. Clean up temporary state in Redis
        // ---------------------------------------------------------------------
        await _cartService.ClearCartAsync(request.CartId);

        return new CheckoutResponse(
            order.Id,
            order.TotalAmount,
            order.Status.ToString(),
            paymentResult.TransactionId,
            order.CreatedAtUtc,
            "Order created and payment processed successfully. Stock will be permanently committed via Outbox Event."
        );
    }
}
