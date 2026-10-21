using Grpc.Net.Client;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OmniStore.OrderService.Data;
using OmniStore.OrderService.Services;
using OmniStore.Shared.Protos;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// ARCHITECTURE NOTE: Order Microservice (.NET 8 + MassTransit Outbox + Redis)
// Pattern: "Sync Reserve + Async Commit" Orchestrator
//   1. Redis: High-throughput ephemeral storage for shopping carts.
//   2. gRPC Client: Synchronously calls InventoryService (Port 5202) for atomic stock reservation.
//   3. MassTransit Transactional Outbox: Persists OrderPaidEvent into PostgreSQL 'order_db'
//      within the exact same ACID transaction as the Order record.
//      Guarantees At-Least-Once Delivery to RabbitMQ without 2PC overhead.
// =============================================================================

// 1. Database Configuration
var connectionString = builder.Configuration.GetConnectionString("OrderDbConnection")
    ?? "Host=localhost;Port=5432;Database=order_db;Username=omni_admin;Password=omni_password123;";

builder.Services.AddDbContext<OrderDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    });
});

// 2. MassTransit with EF Core Transactional Outbox Pattern
builder.Services.AddMassTransit(x =>
{
    // Configure PostgreSQL Outbox
    x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

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
    });
});

// 3. Redis Connection Configuration
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    try
    {
        return ConnectionMultiplexer.Connect(redisConn);
    }
    catch (Exception)
    {
        // Allow starting even if Redis isn't running yet during early local bootstrap
        var config = ConfigurationOptions.Parse(redisConn);
        config.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(config);
    }
});

// 4. gRPC Client for Inter-Service Synchronous Stock Reservation
var inventoryGrpcUrl = builder.Configuration["GrpcEndpoints:Inventory"] ?? "http://localhost:5202";

builder.Services.AddSingleton(services =>
{
    // In .NET 8, calling unencrypted HTTP/2 (h2c) requires this socket switch
    var channel = GrpcChannel.ForAddress(inventoryGrpcUrl, new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true
        }
    });
    return new InventoryProtoService.InventoryProtoServiceClient(channel);
});

// 5. Application Services Dependency Injection
builder.Services.AddScoped<ICartService, RedisCartService>();
builder.Services.AddScoped<IPaymentSimulator, PaymentSimulator>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "OmniStore Order & Checkout API",
        Version = "v1",
        Description = "Manages Redis Cart, gRPC Stock Reservation, and Transactional Outbox Checkout."
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

// Auto-create database schema if running with active database
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
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
