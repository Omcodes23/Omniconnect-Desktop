using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Backend.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthService(AppDbContext db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            throw new InvalidOperationException("Email already registered.");
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            throw new InvalidOperationException("Username already taken.");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            DisplayName = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _jwt.GenerateAccessToken(user.Id, user.Username, nameof(UserRole.User));
        var refreshToken = _jwt.GenerateRefreshToken();

        return new AuthResponse(token, refreshToken, expiresAt, MapToDto(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        user.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _jwt.GenerateAccessToken(user.Id, user.Username, nameof(UserRole.User));
        var refreshToken = _jwt.GenerateRefreshToken();

        return new AuthResponse(token, refreshToken, expiresAt, MapToDto(user));
    }

    private static UserProfileDto MapToDto(User user) => new(
        user.Id, user.Username, user.DisplayName, user.Email,
        user.AvatarUrl, user.Bio, ConnectionStatus.Online,
        user.LastSeenAt, user.CreatedAt,
        user.Email == "admin@omniconnect.local" ? UserRole.Admin : UserRole.User
    );

    private static UserDto MapToUserDto(User user) => new(
        user.Id, user.Username, user.DisplayName, user.AvatarUrl,
        user.Bio, ConnectionStatus.Online, user.LastSeenAt, user.CreatedAt
    );
}
