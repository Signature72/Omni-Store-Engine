using MassTransit;
using OmniStore.NotificationService.Consumers;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// ARCHITECTURE NOTE: Notification Microservice (.NET 8 + MassTransit Consumer)
// Role: Listens asynchronously to 'OrderPaidEvent' and dispatches customer notifications.
// Pattern: Event-Driven Architecture (EDA) & LINE Messaging API Simulation.
// =============================================================================

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPaidNotificationConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
        var rmqPort = ushort.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : (ushort)5672;
        var rmqUser = builder.Configuration["RabbitMQ:Username"] ?? "omni_guest";
        var rmqPass = builder.Configuration["RabbitMQ:Password"] ?? "omni_guest123";

        cfg.Host(rmqHost, rmqPort, "/", h =>
        {
            h.Username(rmqUser);
            h.Password(rmqPass);
        });

        cfg.ReceiveEndpoint("notification-order-paid-queue", e =>
        {
            e.ConfigureConsumer<OrderPaidNotificationConsumer>(context);
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
        });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "OmniStore Notification Service",
        Version = "v1",
        Description = "Consumes OrderPaidEvent and simulates LINE messaging webhook deliveries."
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/healthz");
app.MapControllers();

app.Run();
