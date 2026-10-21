namespace OmniStore.OrderService.Models;

public class CartItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public decimal SubTotal => Quantity * UnitPrice;
}

public class Cart
{
    public string CartId { get; set; } = string.Empty;
    public List<CartItem> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.SubTotal);
    public int TotalItems => Items.Sum(i => i.Quantity);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public record AddCartItemRequest(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string? ImageUrl
);

public record UpdateCartItemRequest(
    int Quantity
);

public record CheckoutRequest(
    string CartId,
    Guid UserId,
    string CustomerName,
    string CustomerEmail,
    string? CustomerLineUserId,
    string PaymentMethod
);

public record CheckoutResponse(
    Guid OrderId,
    decimal TotalAmount,
    string Status,
    string TransactionId,
    DateTime CreatedAtUtc,
    string Message
);
