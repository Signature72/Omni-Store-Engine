namespace OmniStore.OrderService.Services;

public record PaymentResult(bool IsSuccess, string TransactionId, string? ErrorMessage);

public interface IPaymentSimulator
{
    Task<PaymentResult> ProcessPaymentAsync(Guid orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default);
}

public class PaymentSimulator : IPaymentSimulator
{
    private readonly ILogger<PaymentSimulator> _logger;

    public PaymentSimulator(ILogger<PaymentSimulator> logger)
    {
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId,
        decimal amount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Simulating payment processing for Order {OrderId}: Amount ${Amount:F2} via {PaymentMethod}",
            orderId, amount, paymentMethod);

        // Simulate network latency (50ms)
        await Task.Delay(50, cancellationToken);

        if (amount <= 0)
        {
            return new PaymentResult(false, string.Empty, "Invalid payment amount.");
        }

        var transactionId = $"TXN_{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        _logger.LogInformation("Payment approved for Order {OrderId}. Transaction: {TxnId}", orderId, transactionId);

        return new PaymentResult(true, transactionId, null);
    }
}
