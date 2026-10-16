namespace OmniConnect.Shared.DTOs;

public record CommunityDto(
    Guid Id,
    string Name,
    string? Description,
    string? IconUrl,
    Guid OwnerId,
    string OwnerName,
    bool IsPublic,
    int MemberCount,
    DateTime CreatedAt
);

public record CreateCommunityRequest(string Name, string? Description, bool IsPublic);
public record UpdateCommunityRequest(string? Name, string? Description, bool? IsPublic);

public record ChannelDto(
    Guid Id,
    Guid CommunityId,
    string Name,
    string? Description,
    bool IsVoice,
    int SortOrder
);

public record CreateChannelRequest(string Name, string? Description, bool IsVoice);
public record UpdateChannelRequest(string? Name, string? Description, bool? IsVoice, int? SortOrder);

public record CommunityMemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    DateTime JoinedAt
);
