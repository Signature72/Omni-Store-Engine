namespace OmniStore.AuthService.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReplacedByToken { get; set; } // Token rotation audit trail

    public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAtUtc;
}
