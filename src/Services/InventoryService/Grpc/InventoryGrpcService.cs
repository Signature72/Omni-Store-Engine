using Grpc.Core;
using OmniStore.InventoryService.Services;
using OmniStore.Shared.Protos;

namespace OmniStore.InventoryService.Grpc;

public class InventoryGrpcService : InventoryProtoService.InventoryProtoServiceBase
{
    private readonly IInventoryManager _inventoryManager;
    private readonly ILogger<InventoryGrpcService> _logger;

    public InventoryGrpcService(IInventoryManager inventoryManager, ILogger<InventoryGrpcService> logger)
    {
        _inventoryManager = inventoryManager;
        _logger = logger;
    }

    public override async Task<ReserveStockResponse> ReserveStock(ReserveStockRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC ReserveStock received for Order {OrderId} with {ItemCount} items", request.OrderId, request.Items.Count);

        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            return new ReserveStockResponse
            {
                IsSuccess = false,
                ErrorMessage = "Invalid Order ID GUID format."
            };
        }

        var reserveItems = new List<ReserveItemDto>();
        foreach (var item in request.Items)
        {
            if (!Guid.TryParse(item.ProductId, out var productId))
            {
                return new ReserveStockResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Invalid Product ID GUID format: '{item.ProductId}'"
                };
            }

            reserveItems.Add(new ReserveItemDto(productId, item.Quantity));
        }

        var result = await _inventoryManager.ReserveStockAsync(orderId, reserveItems, request.TtlSeconds, context.CancellationToken);

        return new ReserveStockResponse
        {
            IsSuccess = result.IsSuccess,
            ReservationId = result.ReservationId.ToString(),
            ErrorMessage = result.ErrorMessage ?? string.Empty
        };
    }

    public override async Task<ReleaseStockReservationResponse> ReleaseStockReservation(ReleaseStockReservationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC ReleaseStockReservation received for Order {OrderId}", request.OrderId);

        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            return new ReleaseStockReservationResponse { IsSuccess = false, Message = "Invalid Order ID." };
        }

        Guid? reservationId = null;
        if (Guid.TryParse(request.ReservationId, out var parsedResId))
        {
            reservationId = parsedResId;
        }

        var success = await _inventoryManager.ReleaseStockReservationAsync(orderId, reservationId, context.CancellationToken);

        return new ReleaseStockReservationResponse
        {
            IsSuccess = success,
            Message = success ? "Stock reservation released." : "No active reservation found to release."
        };
    }
}
