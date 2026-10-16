namespace OmniConnect.Shared.Models;

public class SyncQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty; // "create", "update", "delete"
    public string PayloadJson { get; set; } = string.Empty;
    public long ClientTimestamp { get; set; }
    public long ServerTimestamp { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "pending"; // "pending", "synced", "conflict", "failed"
    public string? ConflictResolution { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}