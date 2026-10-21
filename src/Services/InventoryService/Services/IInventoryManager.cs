using Microsoft.EntityFrameworkCore;
using OmniStore.InventoryService.Data;
using OmniStore.InventoryService.Entities;

namespace OmniStore.InventoryService.Services;

public record ReserveItemDto(Guid ProductId, int Quantity);

public record ReserveStockResult(bool IsSuccess, Guid ReservationId, string? ErrorMessage);

public interface IInventoryManager
{
    Task<ReserveStockResult> ReserveStockAsync(Guid orderId, IEnumerable<ReserveItemDto> items, int ttlSeconds, CancellationToken cancellationToken = default);
    Task<bool> CommitStockReservationAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<bool> ReleaseStockReservationAsync(Guid orderId, Guid? reservationId = null, CancellationToken cancellationToken = default);
}

public class InventoryManager : IInventoryManager
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<InventoryManager> _logger;

    public InventoryManager(InventoryDbContext dbContext, ILogger<InventoryManager> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReserveStockResult> ReserveStockAsync(
        Guid orderId,
        IEnumerable<ReserveItemDto> items,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        var itemsList = items.ToList();
        if (itemsList.Count == 0)
        {
            return new ReserveStockResult(false, Guid.Empty, "No items requested for reservation.");
        }

        var expiresAtUtc = DateTime.UtcNow.AddSeconds(ttlSeconds > 0 ? ttlSeconds : 900);
        var reservationBatchId = Guid.NewGuid();

        // Perform stock check & reservation atomically
        var productIds = itemsList.Select(i => i.ProductId).Distinct().ToList();
        var products = await _dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Validation phase: ensure every product has enough available stock
        foreach (var item in itemsList)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                _logger.LogWarning("Reservation failed: Product {ProductId} not found.", item.ProductId);
                return new ReserveStockResult(false, Guid.Empty, $"Product {item.ProductId} does not exist.");
            }

            if (product.AvailableStock < item.Quantity)
            {
                _logger.LogWarning(
                    "Reservation failed: Insufficient stock for {ProductName} ({ProductId}). Available: {Available}, Requested: {Requested}",
                    product.Name, product.Id, product.AvailableStock, item.Quantity);

                return new ReserveStockResult(
                    false,
                    Guid.Empty,
                    $"Insufficient stock for '{product.Name}'. Available: {product.AvailableStock}, Requested: {item.Quantity}");
            }
        }

        // Reservation phase: hold stock
        foreach (var item in itemsList)
        {
            var product = products[item.ProductId];
            product.AvailableStock -= item.Quantity;
            product.ReservedStock += item.Quantity;

            var reservation = new StockReservation
            {
                Id = reservationBatchId,
                OrderId = orderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Status = ReservationStatus.Reserved,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = expiresAtUtc
            };

            _dbContext.StockReservations.Add(reservation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully reserved stock for Order {OrderId} (ReservationId: {ReservationId}, ExpiresAt: {ExpiresAt})",
            orderId, reservationBatchId, expiresAtUtc);

        return new ReserveStockResult(true, reservationBatchId, null);
    }

    public async Task<bool> CommitStockReservationAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        // Idempotency check: find all active reservations for this order
        var reservations = await _dbContext.StockReservations
            .Include(r => r.Product)
            .Where(r => r.OrderId == orderId)
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
        {
            _logger.LogWarning("No stock reservations found to commit for Order {OrderId}", orderId);
            return false;
        }

        // If already committed, skip (Idempotency pattern)
        if (reservations.All(r => r.Status == ReservationStatus.Committed))
        {
            _logger.LogInformation("Stock for Order {OrderId} was already committed. Skipping.", orderId);
            return true;
        }

        foreach (var reservation in reservations)
        {
            if (reservation.Status == ReservationStatus.Reserved)
            {
                reservation.Status = ReservationStatus.Committed;
                reservation.CommittedAtUtc = DateTime.UtcNow;

                // Permanent deduction: clear out the temporary reserved hold
                if (reservation.Product != null)
                {
                    reservation.Product.ReservedStock -= reservation.Quantity;
                    if (reservation.Product.ReservedStock < 0)
                    {
                        reservation.Product.ReservedStock = 0;
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Permanently committed stock deduction for Order {OrderId}", orderId);
        return true;
    }

    public async Task<bool> ReleaseStockReservationAsync(Guid orderId, Guid? reservationId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StockReservations
            .Include(r => r.Product)
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Reserved);

        if (reservationId.HasValue && reservationId.Value != Guid.Empty)
        {
            query = query.Where(r => r.Id == reservationId.Value);
        }

        var reservations = await query.ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            return false;
        }

        foreach (var reservation in reservations)
        {
            reservation.Status = ReservationStatus.Released;

            if (reservation.Product != null)
            {
                reservation.Product.AvailableStock += reservation.Quantity;
                reservation.Product.ReservedStock -= reservation.Quantity;
                if (reservation.Product.ReservedStock < 0)
                {
                    reservation.Product.ReservedStock = 0;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Released stock reservations for Order {OrderId}", orderId);
        return true;
    }
}
