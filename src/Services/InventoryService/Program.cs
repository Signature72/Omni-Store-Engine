using System.Net;
using MassTransit;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using OmniStore.InventoryService.Consumers;
using OmniStore.InventoryService.Data;
using OmniStore.InventoryService.Grpc;
using OmniStore.InventoryService.Services;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// ARCHITECTURE NOTE: Inventory Microservice (.NET 8 + gRPC + MassTransit)
// Pattern: "Sync Reserve + Async Commit"
//   - Phase 1 (Sync Reserve): Exposes high-throughput gRPC endpoint on Port 5202 (HTTP/2)
//     for low-latency stock reservation during checkout.
//   - Phase 2 (Async Commit): Subscribes via MassTransit RabbitMQ Consumer to 'OrderPaidEvent'
//     to permanently commit stock deductions asynchronously.
//   - Database: Dedicated PostgreSQL database 'inventory_db'.
// =============================================================================

// 1. Configure Dual-Port Kestrel: Port 5102 for HTTP/1.1 (REST), Port 5202 for HTTP/2 (gRPC)
builder.WebHost.ConfigureKestrel(options =>
{
    // REST API Endpoint
    options.Listen(IPAddress.Any, 5102, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    // High-Performance gRPC Inter-Service Endpoint (HTTP/2 Prior Knowledge)
    options.Listen(IPAddress.Any, 5202, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// 2. Database Context Configuration
var connectionString = builder.Configuration.GetConnectionString("InventoryDbConnection")
    ?? "Host=localhost;Port=5432;Database=inventory_db;Username=omni_admin;Password=omni_password123;";

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    });
});

// 3. Application Services
builder.Services.AddScoped<IInventoryManager, InventoryManager>();

// 4. Register gRPC Server
builder.Services.AddGrpc();

// 5. Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPaidConsumer>();

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

        cfg.ReceiveEndpoint("inventory-order-paid-queue", e =>
        {
            e.ConfigureConsumer<OrderPaidConsumer>(context);
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
        Title = "OmniStore Inventory API & gRPC Engine",
        Version = "v1",
        Description = "Manages product catalog, atomic stock reservations (gRPC Port 5202), and event-driven stock commits."
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
app.MapGrpcService<InventoryGrpcService>();

// Auto-seed demo products on startup
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    if (db.Database.CanConnect())
    {
        db.Database.EnsureCreated();
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning("Database connection not available yet: {Message}", ex.Message);
}

app.Run();
