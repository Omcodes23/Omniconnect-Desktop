namespace OmniConnect.Shared.Models;

public class CallRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CallerId { get; set; }
    public Guid CalleeId { get; set; }
    public CallType Type { get; set; } = CallType.Audio;
    public CallStatus Status { get; set; } = CallStatus.Initiated;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public int Duration { get; set; }
}

public enum CallType { Audio = 0, Video = 1 }
public enum CallStatus { Initiated = 0, Accepted = 1, Rejected = 2, Missed = 3, Ended = 4 }