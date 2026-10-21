using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniStore.AuthService.Data;
using OmniStore.AuthService.DTOs;
using OmniStore.AuthService.Entities;
using OmniStore.AuthService.Services;
using Xunit;

namespace OmniStore.AuthService.Tests;

public class AuthServiceTests
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthServiceTests()
    {
        _passwordHasher = new BcryptPasswordHasher();

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "Jwt:Secret", "OmniStoreSuperSecretEnterpriseKeyWithMinimum256BitsLength!!" },
            { "Jwt:Issuer", "OmniStore.AuthService" },
            { "Jwt:Audience", "OmniStore.ClientApp" },
            { "Jwt:ExpiryMinutes", "60" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        _tokenService = new JwtTokenService(_configuration);
    }

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AuthDbContext(options);
    }

    [Fact]
    public async Task RegisterAsync_WithValidDetails_ShouldCreateUserAndReturnTokens()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);
        var request = new RegisterRequest("john.doe@example.com", "P@ssword123", "John Doe", "line_12345");

        // Act
        var result = await authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Email.Should().Be("john.doe@example.com");
        result.FullName.Should().Be("John Doe");
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var userInDb = await dbContext.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Email == "john.doe@example.com");
        userInDb.Should().NotBeNull();
        userInDb!.FullName.Should().Be("John Doe");
        userInDb.LineUserId.Should().Be("line_12345");
        userInDb.RefreshTokens.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);
        var request = new RegisterRequest("duplicate@example.com", "Password123", "Duplicate User", null);

        await authService.RegisterAsync(request);

        // Act
        var act = async () => await authService.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);

        var registerRequest = new RegisterRequest("alice@example.com", "SecretPass123!", "Alice Wonderland", null);
        await authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest("alice@example.com", "SecretPass123!");

        // Act
        var result = await authService.LoginAsync(loginRequest);

        // Assert
        result.Should().NotBeNull();
        result.Email.Should().Be("alice@example.com");
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);

        var registerRequest = new RegisterRequest("bob@example.com", "CorrectPass123", "Bob Marley", null);
        await authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest("bob@example.com", "WrongPassword!");

        // Act
        var act = async () => await authService.LoginAsync(loginRequest);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidActiveToken_ShouldRotateAndReturnNewTokens()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);

        var registerResult = await authService.RegisterAsync(new RegisterRequest("charlie@example.com", "Pass12345", "Charlie Brown", null));
        var initialRefreshToken = registerResult.RefreshToken;

        // Act
        var refreshResult = await authService.RefreshTokenAsync(initialRefreshToken);

        // Assert
        refreshResult.Should().NotBeNull();
        refreshResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshResult.RefreshToken.Should().NotBe(initialRefreshToken); // Token must be rotated!

        var oldTokenInDb = await dbContext.RefreshTokens.FirstAsync(r => r.Token == initialRefreshToken);
        oldTokenInDb.IsRevoked.Should().BeTrue();
        oldTokenInDb.ReplacedByToken.Should().Be(refreshResult.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithRevokedToken_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var authService = new Services.AuthService(dbContext, _passwordHasher, _tokenService, NullLogger<Services.AuthService>.Instance);

        var registerResult = await authService.RegisterAsync(new RegisterRequest("david@example.com", "Pass12345", "David Beckham", null));
        var firstRefreshToken = registerResult.RefreshToken;

        // Rotate once
        await authService.RefreshTokenAsync(firstRefreshToken);

        // Act: Try using old rotated/revoked token again (replay attack simulation)
        var act = async () => await authService.RefreshTokenAsync(firstRefreshToken);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid, expired, or revoked refresh token*");
    }

    [Fact]
    public void BcryptPasswordHasher_ShouldHashAndVerifyPasswordCorrectly()
    {
        // Arrange
        var hasher = new BcryptPasswordHasher();
        const string rawPassword = "StrongEnterprisePassword!2026";

        // Act
        var hash = hasher.HashPassword(rawPassword);
        var isValid = hasher.VerifyPassword(rawPassword, hash);
        var isInvalid = hasher.VerifyPassword("WrongPassword", hash);

        // Assert
        hash.Should().StartWith("$2"); // BCrypt salt prefix
        isValid.Should().BeTrue();
        isInvalid.Should().BeFalse();
    }

    [Fact]
    public void JwtTokenService_ShouldGenerateValidJwtWithExpectedClaims()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "token.test@example.com",
            FullName = "Token Tester",
            Role = "Customer",
            LineUserId = "line_tester_1"
        };

        // Act
        var (token, expiresAtUtc) = _tokenService.GenerateAccessToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        expiresAtUtc.Should().BeAfter(DateTime.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be("OmniStore.AuthService");
        jwt.Audiences.Should().Contain("OmniStore.ClientApp");
        jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value.Should().Be(user.Id.ToString());
        jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value.Should().Be(user.Email);
        jwt.Claims.First(c => c.Type == "role" || c.Type == ClaimTypes.Role).Value.Should().Be("Customer");
        jwt.Claims.First(c => c.Type == "line_user_id").Value.Should().Be("line_tester_1");
    }
}
