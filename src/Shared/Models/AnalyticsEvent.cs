namespace OmniConnect.Shared.Models;

public class AnalyticsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventName { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}