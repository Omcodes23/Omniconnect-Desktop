namespace OmniConnect.Shared.DTOs;

public record CallDto(
    Guid Id,
    Guid CallerId,
    string CallerName,
    string? CallerAvatar,
    Guid CalleeId,
    string CalleeName,
    string? CalleeAvatar,
    string Type,
    string Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Duration
);

public record InitiateCallRequest(Guid CalleeId, string Type);
public record CallSignalDto(string Type, string Sdp, string? IceCandidate, Guid CallId);