namespace OmniConnect.Shared.DTOs;

// ── Sync Operations ─────────────────────────────────────────────────────
public record SyncOperationDto(
    string EntityType,
    Guid EntityId,
    string Operation,
    string PayloadJson,
    long ClientTimestamp,
    int Version
);

public record SyncPushRequest(
    List<SyncOperationDto> Operations,
    Guid? LastSyncedEntityId,
    long? LastSyncedTimestamp
);

public record SyncPushResponse(
    int AcceptedCount,
    int ConflictCount,
    int FailedCount,
    List<SyncResultDto> Results
);

public record SyncPullRequest(
    Guid? LastSyncedEntityId,
    long? LastSyncedTimestamp,
    int Take = 100
);

public record SyncPullResponse(
    List<SyncOperationDto> Operations,
    bool HasMore,
    Guid? NextEntityId,
    long? NextTimestamp
);

public record SyncResultDto(
    Guid EntityId,
    string EntityType,
    string Status,         // "synced", "conflict", "failed"
    int Version,
    string? ServerPayload,
    string? ErrorMessage
);

// ── Sync Queue Management ───────────────────────────────────────────────
public record SyncQueueItemDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Operation,
    string PayloadJson,
    long ClientTimestamp,
    int Version,
    string Status,
    DateTime CreatedAt
);

public record SyncStatusDto(
    int PendingCount,
    int TotalCount,
    DateTime? LastSyncAt
);

// ── Conflict Resolution ─────────────────────────────────────────────────
public record ConflictResolutionRequest(
    Guid EntityId,
    string EntityType,
    string Resolution,    // "use_client", "use_server"
    string? MergedPayload
);

public record ConflictResolutionResponse(
    bool Success,
    int NewVersion,
    string ResolvedPayload
);