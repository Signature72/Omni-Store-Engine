using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// ARCHITECTURE NOTE: API Gateway (YARP)
// Role: Central Ingress point for all client requests.
// Responsibilities:
//   1. Routing external traffic to internal microservices.
//   2. Rate Limiting to prevent DDoS and brute-force attacks.
//   3. CORS enforcement for the Angular frontend.
//   4. Distributed Tracing via Correlation ID propagation.
//   5. JWT Token forwarding downstream (RequestHeadersCopy).
// =============================================================================

// 1. Add YARP Reverse Proxy loaded from appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 2. Configure Enterprise CORS for Angular Frontend
const string FrontendCorsPolicy = "FrontendCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders("X-Correlation-ID", "Retry-After");
    });
});

// 3. Configure Rate Limiting Policies (Defense-in-Depth)
builder.Services.AddRateLimiter(options =>
{
    // Rejection response format
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            statusCode = 429,
            message = "Too Many Requests. Rate limit exceeded. Please wait before retrying.",
            timestamp = DateTime.UtcNow
        }, cancellationToken: token);
    };

    // Global / Default Policy: Fixed window (100 req/min per IP)
    options.AddPolicy("default-policy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // Stricter Auth Policy: Protects /api/auth/* from brute-force password spraying (15 req/min per IP)
    options.AddPolicy("auth-policy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// 4. Add Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// =============================================================================
// PIPELINE MIDDLEWARE CONFIGURATION
// =============================================================================

// Middleware A: Distributed Tracing & Correlation ID Propagation
app.Use(async (context, next) =>
{
    const string CorrelationIdHeader = "X-Correlation-ID";

    // Extract existing Correlation ID or generate a new high-entropy GUID
    if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) ||
        string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        context.Request.Headers[CorrelationIdHeader] = correlationId;
    }

    // Attach to response header for client-side observability & debugging
    context.Response.Headers[CorrelationIdHeader] = correlationId;

    var startTime = DateTime.UtcNow;
    await next();
    var duration = DateTime.UtcNow - startTime;

    // Structured logging for ingress audit
    app.Logger.LogInformation(
        "[{CorrelationId}] Ingress: {Method} {Path} responded {StatusCode} in {DurationMs}ms",
        correlationId,
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        duration.TotalMilliseconds);
});

// Middleware B: CORS & Rate Limiting
app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();

// =============================================================================
// INFRASTRUCTURE & MONITORING ENDPOINTS
// =============================================================================

// Health Check Endpoint
app.MapHealthChecks("/healthz");

// Gateway Metadata Endpoint
app.MapGet("/api/gateway/info", () => Results.Ok(new
{
    service = "OmniStore.ApiGateway",
    status = "Healthy",
    version = "1.0.0",
    engine = "YARP (.NET 8)",
    timestamp = DateTime.UtcNow,
    routes = new[] { "/api/auth/*", "/api/inventory/*", "/api/orders/*", "/api/cart/*" }
}));

// Map YARP Reverse Proxy
app.MapReverseProxy();

app.Run();
