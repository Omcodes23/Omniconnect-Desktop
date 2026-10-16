namespace OmniConnect.Shared.DTOs;

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Username, string Email, string Password, string DisplayName);
public record AuthResponse(string Token, string RefreshToken, DateTime ExpiresAt, UserProfileDto User);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
