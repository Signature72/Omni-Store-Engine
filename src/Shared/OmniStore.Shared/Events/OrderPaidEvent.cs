namespace OmniStore.Shared.Events;

/// <summary>
/// Domain Integration Event: Published when an order has been successfully paid.
/// Published via MassTransit EF Core Transactional Outbox Pattern to guarantee At-Least-Once Delivery.
/// Consumed by:
///   1. InventoryService (Async Commit: permanently deducts stock & marks reservation Committed).
///   2. NotificationService (Sends simulated LINE Flex Message receipt).
/// </summary>
public record OrderPaidEvent
{
    public Guid OrderId { get; init; }
    public Guid UserId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerLineUserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public List<OrderItemDto> Items { get; init; } = new();
}

public record OrderItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
