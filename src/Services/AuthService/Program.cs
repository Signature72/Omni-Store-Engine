using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OmniStore.AuthService.Data;
using OmniStore.AuthService.Services;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// ARCHITECTURE NOTE: Auth Microservice (.NET 8)
// Role: Handles Identity, Authentication, and Token Lifecycle.
// Principles:
//   - BCrypt work factor 12 for high entropy password hashing.
//   - Stateless JWT Access Tokens for horizontal scalability across downstream services.
//   - Statefully tracked Refresh Tokens with rotation to mitigate stolen token risks.
//   - DB-per-service: Dedicated PostgreSQL database 'auth_db'.
// =============================================================================

// 1. Database Context Configuration
var connectionString = builder.Configuration.GetConnectionString("AuthDbConnection")
    ?? "Host=localhost;Port=5432;Database=auth_db;Username=omni_admin;Password=omni_password123;";

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    });
});

// 2. Application Services Dependency Injection
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// 3. JWT Authentication Configuration
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "OmniStoreSuperSecretEnterpriseKeyWithMinimum256BitsLength!!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "OmniStore.AuthService";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OmniStore.ClientApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Dev environment
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero // Strict expiration enforcement
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 4. Swagger with JWT Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OmniStore Auth Microservice API",
        Version = "v1",
        Description = "Handles User Registration, Login, Token Refresh, and JWT issuance."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapControllers();

// Auto-create database schema if running with active database
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.EnsureCreated();
}
catch (Exception ex)
{
    app.Logger.LogWarning("Database connection not available yet: {Message}. EnsureCreated skipped.", ex.Message);
}

app.Run();
