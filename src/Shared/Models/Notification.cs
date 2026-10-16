namespace OmniConnect.Shared.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty; // "message", "friend_request", "call", "system"
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public bool IsDelivered { get; set; }
    public string? FcmMessageId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}