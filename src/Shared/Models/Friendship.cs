namespace OmniConnect.Shared.Models;

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public bool IsAccepted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }

    // Navigation properties
    public User Requester { get; set; } = null!;
    public User Addressee { get; set; } = null!;
}
