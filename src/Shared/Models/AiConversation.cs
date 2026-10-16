namespace OmniConnect.Shared.Models;

public class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MessagesJson { get; set; } = "[]";
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}