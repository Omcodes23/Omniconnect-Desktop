using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Shared.DTOs;

/// <summary>
/// Information about an active gateway session bridging two devices on different transports.
/// </summary>
/// <param name="SessionId">Unique session identifier.</param>
/// <param name="GatewayDeviceId">Device acting as the gateway.</param>
/// <param name="DeviceAId">First bridged device identifier.</param>
/// <param name="DeviceATransport">Transport used by the first device.</param>
/// <param name="DeviceBId">Second bridged device identifier.</param>
/// <param name="DeviceBTransport">Transport used by the second device.</param>
/// <param name="State">Current session state.</param>
/// <param name="PacketsForwarded">Number of packets forwarded.</param>
/// <param name="BytesForwarded">Total bytes forwarded.</param>
/// <param name="CreatedAt">When the session was created.</param>
/// <param name="LastActivityAt">When the last packet was forwarded.</param>
public record GatewaySessionDto(
    Guid SessionId,
    string GatewayDeviceId,
    string DeviceAId,
    TransportType DeviceATransport,
    string DeviceBId,
    TransportType DeviceBTransport,
    GatewaySessionState State,
    long PacketsForwarded,
    long BytesForwarded,
    DateTime CreatedAt,
    DateTime? LastActivityAt
);

/// <summary>
/// Request to create a new gateway session between two devices.
/// </summary>
/// <param name="DeviceAId">First device identifier.</param>
/// <param name="DeviceATransport">Transport used by the first device.</param>
/// <param name="DeviceBId">Second device identifier.</param>
/// <param name="DeviceBTransport">Transport used by the second device.</param>
public record CreateGatewaySessionRequest(
    string DeviceAId,
    TransportType DeviceATransport,
    string DeviceBId,
    TransportType DeviceBTransport
);

/// <summary>
/// Payload for forwarding a packet through a gateway session.
/// </summary>
/// <param name="SessionId">Target gateway session.</param>
/// <param name="SourceDeviceId">Originating device.</param>
/// <param name="ContentType">Type of content being sent.</param>
/// <param name="Payload">Binary payload data.</param>
/// <param name="TextPayload">Optional text payload.</param>
/// <param name="RequireAck">Whether delivery acknowledgment is requested.</param>
public record ForwardPacketRequest(
    Guid SessionId,
    string SourceDeviceId,
    string ContentType,
    byte[]? Payload,
    string? TextPayload,
    bool RequireAck
);

/// <summary>
/// Result of forwarding a packet through a gateway session.
/// </summary>
/// <param name="Success">Whether the packet was forwarded successfully.</param>
/// <param name="PacketId">The assigned packet identifier.</param>
/// <param name="TargetTransport">The transport used for delivery.</param>
/// <param name="ErrorMessage">Error message if forwarding failed.</param>
public record ForwardPacketResult(
    bool Success,
    Guid? PacketId,
    TransportType TargetTransport,
    string? ErrorMessage
);