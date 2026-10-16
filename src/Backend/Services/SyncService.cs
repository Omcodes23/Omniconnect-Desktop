using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class SyncService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SyncService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SyncService(AppDbContext db, ILogger<SyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Push: Accept batch of operations from client ─────────────────────

    public async Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request)
    {
        var results = new List<SyncResultDto>();
        int accepted = 0, conflicts = 0, failed = 0;

        foreach (var op in request.Operations)
        {
            try
            {
                var result = await ProcessOperationAsync(userId, op);
                results.Add(result);

                switch (result.Status)
                {
                    case "synced": accepted++; break;
                    case "conflict": conflicts++; break;
                    case "failed": failed++; break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process sync operation {EntityType}/{EntityId}", op.EntityType, op.EntityId);
                results.Add(new SyncResultDto(op.EntityId, op.EntityType, "failed", op.Version, null, ex.Message));
                failed++;
            }
        }

        return new SyncPushResponse(accepted, conflicts, failed, results);
    }

    // ── Pull: Get operations since last sync ─────────────────────────────

    public async Task<SyncPullResponse> PullAsync(Guid userId, SyncPullRequest request)
    {
        var query = _db.Set<SyncQueueItem>()
            .Where(q => q.UserId == userId && q.Status == "synced")
            .OrderByDescending(q => q.ServerTimestamp);

        if (request.LastSyncedEntityId.HasValue && request.LastSyncedTimestamp.HasValue)
        {
            query = (IOrderedQueryable<SyncQueueItem>)query
                .Where(q => q.ServerTimestamp > request.LastSyncedTimestamp.Value ||
                           (q.ServerTimestamp == request.LastSyncedTimestamp.Value &&
                            string.Compare(q.Id.ToString(), request.LastSyncedEntityId.ToString()!, StringComparison.Ordinal) > 0));
        }

        var items = await query
            .Take(request.Take + 1)
            .ToListAsync();

        var hasMore = items.Count > request.Take;
        if (hasMore) items = items.Take(request.Take).ToList();

        var operations = items.Select(i => new SyncOperationDto(
            i.EntityType, i.EntityId, i.Operation,
            i.PayloadJson, i.ClientTimestamp, i.Version
        )).ToList();

        var last = items.LastOrDefault();

        return new SyncPullResponse(
            operations,
            hasMore,
            last?.EntityId,
            last?.ServerTimestamp
        );
    }

    // ── Conflict Resolution ──────────────────────────────────────────────

    public async Task<ConflictResolutionResponse> ResolveConflictAsync(
        Guid userId, ConflictResolutionRequest request)
    {
        var queueItem = await _db.Set<SyncQueueItem>()
            .FirstOrDefaultAsync(q =>
                q.UserId == userId &&
                q.EntityId == request.EntityId &&
                q.EntityType == request.EntityType &&
                q.Status == "conflict");

        if (queueItem is null)
            throw new InvalidOperationException("No conflict found for the specified entity.");

        string resolvedPayload;
        int newVersion;

        switch (request.Resolution)
        {
            case "use_client":
                resolvedPayload = queueItem.PayloadJson;
                newVersion = queueItem.Version + 1;
                break;

            case "use_server":
                // Fetch current server state
                resolvedPayload = await GetServerPayloadAsync(request.EntityType, request.EntityId);
                newVersion = queueItem.Version;
                break;

            case "merge":
                resolvedPayload = request.MergedPayload ?? queueItem.PayloadJson;
                newVersion = queueItem.Version + 1;
                break;

            default:
                throw new ArgumentException($"Invalid resolution strategy: {request.Resolution}");
        }

        queueItem.Status = "synced";
        queueItem.ConflictResolution = request.Resolution;
        queueItem.PayloadJson = resolvedPayload;
        queueItem.Version = newVersion;
        queueItem.ProcessedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Apply resolved payload to the actual entity
        await ApplyPayloadAsync(request.EntityType, request.EntityId, resolvedPayload, newVersion);

        return new ConflictResolutionResponse(true, newVersion, resolvedPayload);
    }

    // ── Sync Queue Status ────────────────────────────────────────────────

    public async Task<SyncStatusDto> GetSyncStatusAsync(Guid userId)
    {
        var pending = await _db.Set<SyncQueueItem>()
            .CountAsync(q => q.UserId == userId && q.Status == "pending");

        var total = await _db.Set<SyncQueueItem>()
            .CountAsync(q => q.UserId == userId);

        var lastSync = await _db.Set<SyncQueueItem>()
            .Where(q => q.UserId == userId && q.Status == "synced")
            .MaxAsync(q => (DateTime?)q.ProcessedAt);

        return new SyncStatusDto(pending, total, lastSync);
    }

    // ── Private Processing ───────────────────────────────────────────────

    private async Task<SyncResultDto> ProcessOperationAsync(Guid userId, SyncOperationDto op)
    {
        // Check for version conflict
        var existing = await _db.Set<SyncQueueItem>()
            .Where(q => q.UserId == userId && q.EntityId == op.EntityId && q.EntityType == op.EntityType)
            .OrderByDescending(q => q.Version)
            .FirstOrDefaultAsync();

        if (existing is not null && existing.Version > op.Version)
        {
            // Conflict detected: last-write-wins with timestamp comparison
            var existingTimestamp = existing.ClientTimestamp;
            if (op.ClientTimestamp > existingTimestamp)
            {
                // Client's version is newer despite lower version number — accept it
                return await AcceptOperationAsync(userId, op);
            }

            // Flag as conflict, let the client decide
            var conflictItem = new SyncQueueItem
            {
                UserId = userId,
                EntityType = op.EntityType,
                EntityId = op.EntityId,
                Operation = op.Operation,
                PayloadJson = op.PayloadJson,
                ClientTimestamp = op.ClientTimestamp,
                Version = op.Version,
                Status = "conflict",
            };

            _db.Set<SyncQueueItem>().Add(conflictItem);
            await _db.SaveChangesAsync();

            return new SyncResultDto(
                op.EntityId, op.EntityType, "conflict",
                existing.Version, existing.PayloadJson,
                "Version conflict detected. Use conflict resolution endpoint.");
        }

        // No conflict — accept
        return await AcceptOperationAsync(userId, op);
    }

    private async Task<SyncResultDto> AcceptOperationAsync(Guid userId, SyncOperationDto op)
    {
        var item = new SyncQueueItem
        {
            UserId = userId,
            EntityType = op.EntityType,
            EntityId = op.EntityId,
            Operation = op.Operation,
            PayloadJson = op.PayloadJson,
            ClientTimestamp = op.ClientTimestamp,
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = op.Version,
            Status = "synced",
            ProcessedAt = DateTime.UtcNow,
        };

        _db.Set<SyncQueueItem>().Add(item);

        // Apply the operation to the actual entity
        await ApplyPayloadAsync(op.EntityType, op.EntityId, op.PayloadJson, op.Version);

        await _db.SaveChangesAsync();

        return new SyncResultDto(
            op.EntityId, op.EntityType, "synced",
            op.Version, op.PayloadJson, null);
    }

    private async Task ApplyPayloadAsync(string entityType, Guid entityId, string payloadJson, int version)
    {
        // Apply the operation to the relevant entity table based on type
        switch (entityType.ToLowerInvariant())
        {
            case "message":
                await ApplyToMessageAsync(entityId, payloadJson);
                break;

            case "user":
                // User profile sync - handled by auth service
                break;

            case "community":
                // Community sync - handled by community service
                break;

            case "friendship":
                // Friendship sync - handled by friend service
                break;

            default:
                _logger.LogWarning("Unknown entity type for sync application: {EntityType}", entityType);
                break;
        }
    }

    private async Task ApplyToMessageAsync(Guid messageId, string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(payloadJson, JsonOptions);
            var message = await _db.Messages.FindAsync(messageId);

            if (message is null && payload.TryGetProperty("content", out var content))
            {
                // Create new message from sync
                message = new Message
                {
                    Id = messageId,
                    Content = content.GetString() ?? string.Empty,
                    SenderId = payload.TryGetProperty("senderId", out var sid)
                        ? Guid.Parse(sid.GetString()!)
                        : Guid.Empty,
                    SentAt = payload.TryGetProperty("sentAt", out var sa)
                        ? sa.GetDateTime()
                        : DateTime.UtcNow,
                };

                if (payload.TryGetProperty("receiverId", out var rid))
                    message.ReceiverId = Guid.Parse(rid.GetString()!);

                if (payload.TryGetProperty("communityId", out var cid))
                    message.CommunityId = Guid.Parse(cid.GetString()!);

                if (payload.TryGetProperty("channelId", out var chid))
                    message.ChannelId = Guid.Parse(chid.GetString()!);

                if (payload.TryGetProperty("type", out var type))
                    message.Type = Enum.Parse<Shared.Enums.MessageType>(type.GetString()!, ignoreCase: true);

                _db.Messages.Add(message);
            }
            else if (message is not null)
            {
                // Update existing message
                if (payload.TryGetProperty("content", out var content2))
                    message.Content = content2.GetString() ?? message.Content;

                if (payload.TryGetProperty("status", out var status))
                    message.Status = Enum.Parse<Shared.Enums.MessageStatus>(status.GetString()!, ignoreCase: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply sync payload to message {MessageId}", messageId);
        }
    }

    private async Task<string> GetServerPayloadAsync(string entityType, Guid entityId)
    {
        switch (entityType.ToLowerInvariant())
        {
            case "message":
                var message = await _db.Messages.FindAsync(entityId);
                if (message is null) return "{}";
                return JsonSerializer.Serialize(new
                {
                    message.Id,
                    message.Content,
                    message.SenderId,
                    message.ReceiverId,
                    message.Status,
                    message.SentAt,
                }, JsonOptions);

            default:
                return "{}";
        }
    }
}