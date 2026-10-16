namespace OmniConnect.Shared.DTOs;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    bool IsRead,
    DateTime CreatedAt
);