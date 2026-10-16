namespace OmniConnect.Shared.Models;

public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public bool MessagesEnabled { get; set; } = true;
    public bool FriendRequestsEnabled { get; set; } = true;
    public bool CallsEnabled { get; set; } = true;
    public bool CommunityInvitesEnabled { get; set; } = true;
    public bool SystemAnnouncementsEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public string? FcmDeviceToken { get; set; }
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}