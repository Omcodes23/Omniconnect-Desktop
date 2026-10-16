namespace OmniConnect.Shared.DTOs;

// ── System Health ───────────────────────────────────────────────────────
public record SystemHealthDto(
    string Status,
    double UptimeHours,
    long MemoryUsageMb,
    int ActiveConnections,
    int ActiveUsers5Min,
    double CpuUsagePercent,
    string DatabaseStatus,
    DateTime Timestamp
);

public record DatabaseStatsDto(
    long TotalUsers,
    long TotalMessages,
    long TotalCommunities,
    long TotalFiles,
    long DatabaseSizeMb,
    DateTime Timestamp
);

// ── User Management ────────────────────────────────────────────────────
public record AdminUserDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string Role,
    bool IsEmailVerified,
    bool IsBanned,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime? LastSeenAt,
    long MessageCount
);

public record AdminUserListRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool? Ascending { get; init; }
}

public record AdminUserListResponse(
    List<AdminUserDto> Users,
    int TotalCount,
    int Page,
    int PageSize
);

public record AdminUserActionRequest(
    Guid UserId,
    string Action,   // "ban", "unban", "verify_email", "reset_password", "delete"
    string? Reason
);

public record AdminUserActionResponse(
    bool Success,
    string Message
);

// ── Content Moderation ──────────────────────────────────────────────────
public record ModerateContentRequest(
    Guid? MessageId,
    string Content,
    string Action  // "flag", "remove", "approve"
);

public record ModerateContentResponse(
    bool Success,
    string ModerationResult,
    string? ActionTaken
);

public record FlaggedContentDto(
    Guid Id,
    Guid? MessageId,
    Guid? UserId,
    string Content,
    string Reason,
    string Status,
    DateTime FlaggedAt
);

// ── System Configuration ────────────────────────────────────────────────
public record SystemConfigDto(
    string Key,
    string Value,
    string? Description,
    DateTime UpdatedAt
);

public record UpdateSystemConfigRequest(
    string Key,
    string Value
);