using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.DTOs;

public record DeviceInfoDto(
    string DeviceId,
    string DeviceName,
    string DeviceModel,
    string Platform,
    DeviceRole Roles,
    HashSet<string> AvailableTransports,
    string? IpAddress,
    int Port,
    ConnectionStatus Status,
    DateTime LastSeen
);

public record DeviceDiscoveryDto(
    string DeviceId,
    string DeviceName,
    string DeviceModel,
    DeviceRole Roles = DeviceRole.Client,
    HashSet<string>? AvailableTransports = null,
    string? IpAddress = null,
    int Port = 0,
    Guid? UserId = null,
    string? UserName = null
);

public record FriendRequestDto(
    Guid Id,
    Guid RequesterId,
    string RequesterName,
    string? RequesterAvatar,
    Guid AddresseeId,
    string AddresseeName,
    string? AddresseeAvatar,
    bool IsAccepted,
    DateTime CreatedAt,
    DateTime? AcceptedAt
);

public record PresenceUpdateDto(Guid UserId, ConnectionStatus Status, DateTime? LastSeenAt);
public record TypingIndicatorDto(Guid UserId, Guid? CommunityId, Guid? ChannelId, bool IsTyping);
