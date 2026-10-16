using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.DTOs;

public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    ConnectionStatus Status,
    DateTime? LastSeenAt,
    DateTime CreatedAt,
    UserRole Role = UserRole.User
);

public record UserProfileDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    string? Bio,
    ConnectionStatus Status,
    DateTime? LastSeenAt,
    DateTime CreatedAt,
    UserRole Role = UserRole.User
) : UserDto(Id, Username, DisplayName, AvatarUrl, Bio, Status, LastSeenAt, CreatedAt, Role);

public record UpdateProfileRequest(string? DisplayName, string? Bio, string? AvatarUrl);