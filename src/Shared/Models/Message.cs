using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.Models;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public Guid? ReceiverId { get; set; }
    public Guid? CommunityId { get; set; }
    public Guid? ChannelId { get; set; }
    public MessageType Type { get; set; } = MessageType.Text;
    public string Content { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public long? FileSize { get; set; }
    public string? FileName { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Sent;
    public Guid? ReplyToId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string ClientMessageId { get; set; } = string.Empty;

    // Navigation property
    public User Sender { get; set; } = null!;
}
