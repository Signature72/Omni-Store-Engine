using System.Text.Json;
using MassTransit;
using OmniStore.Shared.Events;

namespace OmniStore.NotificationService.Consumers;

/// <summary>
/// Architecture: Notification Bounded Context (Asynchronous Event Consumer)
/// Consumes OrderPaidEvent from RabbitMQ broker.
/// Generates and dispatches a simulated LINE Messaging API Flex Message receipt
/// to the customer's LINE account.
/// </summary>
public class OrderPaidNotificationConsumer : IConsumer<OrderPaidEvent>
{
    private readonly ILogger<OrderPaidNotificationConsumer> _logger;

    public OrderPaidNotificationConsumer(ILogger<OrderPaidNotificationConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var order = context.Message;
        _logger.LogInformation("Processing LINE Notification for Order {OrderId} (Customer: {Name})", order.OrderId, order.CustomerName);

        // Build simulated LINE Flex Message Bubble Payload (Rich Receipt)
        var lineFlexMessage = new
        {
            to = string.IsNullOrWhiteSpace(order.CustomerLineUserId) ? "U_MOCK_LINE_USER_DEFAULT" : order.CustomerLineUserId,
            messages = new object[]
            {
                new
                {
                    type = "flex",
                    altText = $"Receipt for Order #{order.OrderId.ToString()[..8].ToUpper()}",
                    contents = new
                    {
                        type = "bubble",
                        header = new
                        {
                            type = "box",
                            layout = "vertical",
                            contents = new object[]
                            {
                                new { type = "text", text = "OMNISTORE ENGINE", weight = "bold", color = "#1DB446", size = "sm" },
                                new { type = "text", text = "Payment Received!", weight = "bold", size = "xl", margin = "md" }
                            }
                        },
                        body = new
                        {
                            type = "box",
                            layout = "vertical",
                            contents = new object[]
                            {
                                new { type = "text", text = $"Order ID: #{order.OrderId.ToString()[..8].ToUpper()}", size = "xs", color = "#aaaaaa" },
                                new { type = "text", text = $"Date: {order.PaidAtUtc:yyyy-MM-dd HH:mm:ss} UTC", size = "xs", color = "#aaaaaa", margin = "xs" },
                                new { type = "separator", margin = "lg" },
                                new
                                {
                                    type = "box",
                                    layout = "vertical",
                                    margin = "lg",
                                    contents = order.Items.Select(item => new
                                    {
                                        type = "box",
                                        layout = "horizontal",
                                        contents = new object[]
                                        {
                                            new { type = "text", text = $"{item.ProductName} x{item.Quantity}", size = "sm", color = "#555555", flex = 4 },
                                            new { type = "text", text = $"${item.UnitPrice * item.Quantity:F2}", size = "sm", color = "#111111", align = "end", flex = 2 }
                                        }
                                    }).ToArray()
                                },
                                new { type = "separator", margin = "lg" },
                                new
                                {
                                    type = "box",
                                    layout = "horizontal",
                                    margin = "lg",
                                    contents = new object[]
                                    {
                                        new { type = "text", text = "TOTAL PAID", weight = "bold", size = "md", color = "#333333" },
                                        new { type = "text", text = $"${order.TotalAmount:F2}", weight = "bold", size = "lg", color = "#1DB446", align = "end" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Simulate sending to https://api.line.me/v2/bot/message/push
        await Task.Delay(30, context.CancellationToken);

        _logger.LogInformation(
            "LINE Messaging API Push Notification SIMULATED successfully for User {LineUser}:\n{Payload}",
            lineFlexMessage.to,
            JsonSerializer.Serialize(lineFlexMessage, new JsonSerializerOptions { WriteIndented = true }));
    }
}
