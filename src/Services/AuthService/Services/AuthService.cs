using Microsoft.EntityFrameworkCore;
using OmniStore.AuthService.Data;
using OmniStore.AuthService.DTOs;
using OmniStore.AuthService.Entities;

namespace OmniStore.AuthService.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<UserProfileResponse?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class AuthService : IAuthService
{
    private readonly AuthDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AuthDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser)
        {
            throw new InvalidOperationException($"Email '{request.Email}' is already registered.");
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            FullName = request.FullName.Trim(),
            Role = "Customer",
            LineUserId = request.LineUserId?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);

        // Generate tokens
        var (accessToken, expiresAtUtc) = _tokenService.GenerateAccessToken(user);
        var refreshTokenString = _tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = refreshTokenString,
            UserId = user.Id,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New user registered successfully: {UserId} ({Email})", user.Id, user.Email);

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            accessToken,
            refreshTokenString,
            expiresAtUtc
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var (accessToken, expiresAtUtc) = _tokenService.GenerateAccessToken(user);
        var refreshTokenString = _tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = refreshTokenString,
            UserId = user.Id,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User logged in successfully: {UserId} ({Email})", user.Id, user.Email);

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            accessToken,
            refreshTokenString,
            expiresAtUtc
        );
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var existingToken = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (existingToken == null || !existingToken.IsActive || existingToken.User == null)
        {
            throw new UnauthorizedAccessException("Invalid, expired, or revoked refresh token.");
        }

        // Revoke current refresh token (Token Rotation Security Pattern)
        var newRefreshTokenString = _tokenService.GenerateRefreshToken();
        existingToken.IsRevoked = true;
        existingToken.ReplacedByToken = newRefreshTokenString;

        // Generate new access & refresh tokens
        var (accessToken, expiresAtUtc) = _tokenService.GenerateAccessToken(existingToken.User);

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = newRefreshTokenString,
            UserId = existingToken.UserId,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Rotated refresh token for user {UserId}", existingToken.UserId);

        return new AuthResponse(
            existingToken.User.Id,
            existingToken.User.Email,
            existingToken.User.FullName,
            existingToken.User.Role,
            accessToken,
            newRefreshTokenString,
            expiresAtUtc
        );
    }

    public async Task<UserProfileResponse?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null) return null;

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.LineUserId,
            user.CreatedAtUtc
        );
    }
}
