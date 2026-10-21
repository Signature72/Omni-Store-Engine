using MassTransit;
using OmniStore.InventoryService.Services;
using OmniStore.Shared.Events;

namespace OmniStore.InventoryService.Consumers;

/// <summary>
/// Architecture: Async Commit Phase
/// Consumes OrderPaidEvent from RabbitMQ broker.
/// Permanently commits the stock deduction for the order and cleans up reservation holds.
/// Idempotent by design to handle potential message redeliveries safely.
/// </summary>
public class OrderPaidConsumer : IConsumer<OrderPaidEvent>
{
    private readonly IInventoryManager _inventoryManager;
    private readonly ILogger<OrderPaidConsumer> _logger;

    public OrderPaidConsumer(IInventoryManager inventoryManager, ILogger<OrderPaidConsumer> logger)
    {
        _inventoryManager = inventoryManager;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Consuming OrderPaidEvent for Order {OrderId} (Total: ${TotalAmount:F2}, Customer: {CustomerName})",
            message.OrderId, message.TotalAmount, message.CustomerName);

        var committed = await _inventoryManager.CommitStockReservationAsync(message.OrderId, context.CancellationToken);

        if (committed)
        {
            _logger.LogInformation("Stock deduction successfully committed for Order {OrderId}", message.OrderId);
        }
        else
        {
            _logger.LogWarning("Stock commit was skipped or had no active reservation for Order {OrderId}", message.OrderId);
        }
    }
}
