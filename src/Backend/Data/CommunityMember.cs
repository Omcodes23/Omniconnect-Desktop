using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Data;

public class CommunityMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CommunityId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Member";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Community Community { get; set; } = null!;
    public User User { get; set; } = null!;
}
