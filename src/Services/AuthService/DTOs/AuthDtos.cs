namespace OmniStore.AuthService.DTOs;

public record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string? LineUserId
);

public record LoginRequest(
    string Email,
    string Password
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record AuthResponse(
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc
);

public record UserProfileResponse(
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    string? LineUserId,
    DateTime CreatedAtUtc
);
