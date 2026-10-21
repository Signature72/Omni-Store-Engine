namespace OmniStore.InventoryService.Entities;

public enum ReservationStatus
{
    Reserved = 0,
    Committed = 1,
    Released = 2,
    Expired = 3
}

public class StockReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CommittedAtUtc { get; set; }
}
