using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.Models;

/// <summary>
/// Represents a generic network packet that can be forwarded between
/// different transports (Internet, LAN, Bluetooth, Wi-Fi Direct).
/// </summary>
public class NetworkPacket
{
    /// <summary>Unique identifier for this packet.</summary>
    public Guid PacketId { get; set; } = Guid.NewGuid();

    /// <summary>Identifier of the source device.</summary>
    public string SourceDeviceId { get; set; } = string.Empty;

    /// <summary>Identifier of the destination device.</summary>
    public string TargetDeviceId { get; set; } = string.Empty;

    /// <summary>The transport over which this packet was originally sent.</summary>
    public TransportType SourceTransport { get; set; } = TransportType.Internet;

    /// <summary>The transport over which this packet should be delivered.</summary>
    public TransportType TargetTransport { get; set; } = TransportType.Internet;

    /// <summary>Payload content type (e.g., "text", "binary", "signal", "presence").</summary>
    public string ContentType { get; set; } = "text";

    /// <summary>Serialized payload data.</summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>Optional text-based payload for small messages.</summary>
    public string? TextPayload { get; set; }

    /// <summary>Timestamp when this packet was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Time-to-live in hops. Decremented each time the packet is forwarded.</summary>
    public int Ttl { get; set; } = 5;

    /// <summary>Whether the packet requires an acknowledgment of delivery.</summary>
    public bool RequireAck { get; set; }

    /// <summary>Size of the payload in bytes.</summary>
    public long PayloadSize => Payload.LongLength + (TextPayload?.Length ?? 0);
}