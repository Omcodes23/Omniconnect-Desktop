using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.Models;

/// <summary>
/// Represents a gateway session that bridges communication between two devices
/// operating on different transports.
/// </summary>
public class GatewaySession
{
    /// <summary>Unique identifier for this gateway session.</summary>
    public Guid SessionId { get; set; } = Guid.NewGuid();

    /// <summary>Identifier of the device acting as the gateway.</summary>
    public string GatewayDeviceId { get; set; } = string.Empty;

    /// <summary>Identifier of device A in the bridged connection.</summary>
    public string DeviceAId { get; set; } = string.Empty;

    /// <summary>Transport used by device A.</summary>
    public TransportType DeviceATransport { get; set; } = TransportType.Internet;

    /// <summary>Identifier of device B in the bridged connection.</summary>
    public string DeviceBId { get; set; } = string.Empty;

    /// <summary>Transport used by device B.</summary>
    public TransportType DeviceBTransport { get; set; } = TransportType.Internet;

    /// <summary>Current state of the gateway session.</summary>
    public GatewaySessionState State { get; set; } = GatewaySessionState.Pending;

    /// <summary>Timestamp when the session was established.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp of the last packet forwarded through this session.</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>Total number of packets forwarded through this session.</summary>
    public long PacketsForwarded { get; set; }

    /// <summary>Total bytes forwarded through this session.</summary>
    public long BytesForwarded { get; set; }

    /// <summary>Arbitrary metadata key-value pairs for this session.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Defines the lifecycle states of a gateway session.
/// </summary>
public enum GatewaySessionState
{
    /// <summary>Session has been requested but not yet established.</summary>
    Pending = 0,

    /// <summary>Session is active and forwarding packets.</summary>
    Active = 1,

    /// <summary>Session has been paused or suspended.</summary>
    Suspended = 2,

    /// <summary>Session has been terminated.</summary>
    Closed = 3,

    /// <summary>Session failed to establish or encountered an unrecoverable error.</summary>
    Failed = 4
}