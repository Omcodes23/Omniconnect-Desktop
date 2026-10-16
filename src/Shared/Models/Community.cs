namespace OmniConnect.Shared.Models;

public class Community
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    // Navigation properties
    public User Owner { get; set; } = null!;
    public List<Channel> Channels { get; set; } = new();
}
