namespace OmniConnect.Shared.Models;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CommunityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsVoice { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
