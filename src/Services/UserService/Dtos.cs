namespace UserService;

public record RegisterRequest(string Email, string Password, string FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, Guid UserId, string Email, string Role, DateTime ExpiresAtUtc);
public record UserProfileResponse(Guid Id, string Email, string FullName, string? Phone, string Role);
