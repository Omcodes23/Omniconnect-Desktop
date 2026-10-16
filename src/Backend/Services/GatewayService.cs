using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Forwards packets between different transports (e.g., LAN to Bluetooth),
/// translates message formats between transport protocols, and manages gateway
/// sessions between connected devices. This service is registered as scoped,
/// but gateway sessions are stored in a shared in-memory store that persists
/// across requests.
/// </summary>
public class GatewayService
{
    private readonly ILogger<GatewayService> _logger;
    private readonly RoutingEngine _routingEngine;

    // Shared in-memory session store (static to persist across scoped instances)
    private static readonly ConcurrentDictionary<Guid, GatewaySession> Sessions = new();
    private static readonly ConcurrentDictionary<string, ConcurrentBag<Guid>> DeviceSessions = new();
    private static long _totalPacketsForwarded;
    private static long _totalBytesForwarded;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayService"/>.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="routingEngine">The routing engine for transport evaluation.</param>
    public GatewayService(ILogger<GatewayService> logger, RoutingEngine routingEngine)
    {
        _logger = logger;
        _routingEngine = routingEngine;
    }

    /// <summary>
    /// Creates a new gateway session that bridges two devices on different transports.
    /// </summary>
    /// <param name="request">Session creation parameters.</param>
    /// <param name="gatewayDeviceId">The device identifier acting as the gateway.</param>
    /// <returns>A DTO representing the created session.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a session between the two devices already exists.</exception>
    public Task<GatewaySessionDto> CreateSessionAsync(CreateGatewaySessionRequest request, string gatewayDeviceId)
    {
        // Check if an active session already exists between these two devices
        var existingSession = FindExistingSession(request.DeviceAId, request.DeviceBId);
        if (existingSession != null && existingSession.State == GatewaySessionState.Active)
        {
            throw new InvalidOperationException(
                $"An active gateway session ({existingSession.SessionId}) already exists between " +
                $"{request.DeviceAId} and {request.DeviceBId}.");
        }

        var session = new GatewaySession
        {
            SessionId = Guid.NewGuid(),
            GatewayDeviceId = gatewayDeviceId,
            DeviceAId = request.DeviceAId,
            DeviceATransport = request.DeviceATransport,
            DeviceBId = request.DeviceBId,
            DeviceBTransport = request.DeviceBTransport,
            State = GatewaySessionState.Active,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        Sessions[session.SessionId] = session;

        // Index by device for lookups
        DeviceSessions.AddOrUpdate(
            request.DeviceAId,
            _ => new ConcurrentBag<Guid> { session.SessionId },
            (_, bag) => { bag.Add(session.SessionId); return bag; });

        DeviceSessions.AddOrUpdate(
            request.DeviceBId,
            _ => new ConcurrentBag<Guid> { session.SessionId },
            (_, bag) => { bag.Add(session.SessionId); return bag; });

        _logger.LogInformation(
            "Gateway session {SessionId} created: {DeviceA} ({TransportA}) <-> {DeviceB} ({TransportB}) via gateway {Gateway}",
            session.SessionId, request.DeviceAId, request.DeviceATransport,
            request.DeviceBId, request.DeviceBTransport, gatewayDeviceId);

        return Task.FromResult(MapToDto(session));
    }

    /// <summary>
    /// Forwards a packet through an active gateway session, translating between
    /// the source and target transport formats as needed.
    /// </summary>
    /// <param name="request">The packet forwarding request.</param>
    /// <returns>Result indicating success or failure of the forwarding operation.</returns>
    public async Task<ForwardPacketResult> ForwardPacketAsync(ForwardPacketRequest request)
    {
        if (!Sessions.TryGetValue(request.SessionId, out var session))
        {
            return new ForwardPacketResult(
                Success: false,
                PacketId: null,
                TargetTransport: TransportType.Internet,
                ErrorMessage: $"Gateway session '{request.SessionId}' not found."
            );
        }

        if (session.State != GatewaySessionState.Active)
        {
            return new ForwardPacketResult(
                Success: false,
                PacketId: null,
                TargetTransport: TransportType.Internet,
                ErrorMessage: $"Gateway session '{request.SessionId}' is not active (state: {session.State})."
            );
        }

        // Determine the target device and its transport
        var targetDeviceId = request.SourceDeviceId == session.DeviceAId
            ? session.DeviceBId
            : session.DeviceAId;

        var targetTransport = request.SourceDeviceId == session.DeviceAId
            ? session.DeviceBTransport
            : session.DeviceATransport;

        // Build the network packet
        var packet = new NetworkPacket
        {
            PacketId = Guid.NewGuid(),
            SourceDeviceId = request.SourceDeviceId,
            TargetDeviceId = targetDeviceId,
            SourceTransport = request.SourceDeviceId == session.DeviceAId
                ? session.DeviceATransport
                : session.DeviceBTransport,
            TargetTransport = targetTransport,
            ContentType = request.ContentType,
            Payload = request.Payload ?? Array.Empty<byte>(),
            TextPayload = request.TextPayload,
            RequireAck = request.RequireAck,
            CreatedAt = DateTime.UtcNow
        };

        // Translate payload format if needed
        try
        {
            var translatedPayload = TranslatePayload(packet, session);
            packet.Payload = translatedPayload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Payload translation failed for packet {PacketId} in session {SessionId}",
                packet.PacketId, request.SessionId);
            return new ForwardPacketResult(
                Success: false,
                PacketId: packet.PacketId,
                TargetTransport: targetTransport,
                ErrorMessage: $"Payload translation failed: {ex.Message}"
            );
        }

        // Update session metrics
        Interlocked.Increment(ref _totalPacketsForwarded);
        Interlocked.Add(ref _totalBytesForwarded, packet.PayloadSize);

        session.PacketsForwarded++;
        session.BytesForwarded += packet.PayloadSize;
        session.LastActivityAt = DateTime.UtcNow;

        _logger.LogDebug(
            "Packet {PacketId} forwarded through session {SessionId}: {ContentType} " +
            "({SourceTransport} -> {TargetTransport}), size: {Size} bytes",
            packet.PacketId, request.SessionId, request.ContentType,
            packet.SourceTransport, packet.TargetTransport, packet.PayloadSize);

        // If acknowledgment is requested, simulate delivery acknowledgment
        if (request.RequireAck)
        {
            await SimulateDeliveryAckAsync(packet);
        }

        return new ForwardPacketResult(
            Success: true,
            PacketId: packet.PacketId,
            TargetTransport: targetTransport,
            ErrorMessage: null
        );
    }

    /// <summary>
    /// Closes a gateway session, terminating the bridge between the two devices.
    /// </summary>
    /// <param name="sessionId">The session to close.</param>
    /// <returns>True if the session was found and closed; false otherwise.</returns>
    public Task<bool> CloseSessionAsync(Guid sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogWarning("Attempted to close non-existent gateway session {SessionId}", sessionId);
            return Task.FromResult(false);
        }

        session.State = GatewaySessionState.Closed;
        session.LastActivityAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Gateway session {SessionId} closed. Total packets forwarded: {Packets}, total bytes: {Bytes}",
            sessionId, session.PacketsForwarded, session.BytesForwarded);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Suspends a gateway session, pausing packet forwarding without terminating the bridge.
    /// </summary>
    /// <param name="sessionId">The session to suspend.</param>
    /// <returns>True if the session was found and suspended; false otherwise.</returns>
    public Task<bool> SuspendSessionAsync(Guid sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(false);
        }

        session.State = GatewaySessionState.Suspended;
        session.LastActivityAt = DateTime.UtcNow;

        _logger.LogInformation("Gateway session {SessionId} suspended", sessionId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Resumes a previously suspended gateway session.
    /// </summary>
    /// <param name="sessionId">The session to resume.</param>
    /// <returns>True if the session was found and resumed; false otherwise.</returns>
    public Task<bool> ResumeSessionAsync(Guid sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(false);
        }

        if (session.State != GatewaySessionState.Suspended)
        {
            _logger.LogWarning(
                "Attempted to resume non-suspended gateway session {SessionId} (state: {State})",
                sessionId, session.State);
            return Task.FromResult(false);
        }

        session.State = GatewaySessionState.Active;
        session.LastActivityAt = DateTime.UtcNow;

        _logger.LogInformation("Gateway session {SessionId} resumed", sessionId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Retrieves details for a specific gateway session.
    /// </summary>
    public Task<GatewaySessionDto?> GetSessionAsync(Guid sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<GatewaySessionDto?>(null);
        }

        return Task.FromResult<GatewaySessionDto?>(MapToDto(session));
    }

    /// <summary>
    /// Lists all active gateway sessions.
    /// </summary>
    public Task<List<GatewaySessionDto>> GetActiveSessionsAsync()
    {
        var activeSessions = Sessions.Values
            .Where(s => s.State == GatewaySessionState.Active)
            .Select(MapToDto)
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        return Task.FromResult(activeSessions);
    }

    /// <summary>
    /// Lists all gateway sessions involving a specific device.
    /// </summary>
    public Task<List<GatewaySessionDto>> GetSessionsForDeviceAsync(string deviceId)
    {
        if (!DeviceSessions.TryGetValue(deviceId, out var sessionIds))
        {
            return Task.FromResult(new List<GatewaySessionDto>());
        }

        var sessions = sessionIds
            .Select(id => Sessions.GetValueOrDefault(id))
            .Where(s => s != null)
            .Select(s => MapToDto(s!))
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        return Task.FromResult(sessions);
    }

    /// <summary>
    /// Returns aggregate forwarding statistics across all gateway sessions.
    /// </summary>
    public Task<GatewayStats> GetStatisticsAsync()
    {
        var activeCount = Sessions.Values.Count(s => s.State == GatewaySessionState.Active);
        var totalCount = Sessions.Count;

        return Task.FromResult(new GatewayStats
        {
            TotalSessionsCreated = totalCount,
            ActiveSessions = activeCount,
            TotalPacketsForwarded = Interlocked.Read(ref _totalPacketsForwarded),
            TotalBytesForwarded = Interlocked.Read(ref _totalBytesForwarded)
        });
    }

    /// <summary>
    /// Translates a packet's payload from the source transport format to the target transport format.
    /// Different transports may use different serialization, encoding, or compression schemes.
    /// </summary>
    private byte[] TranslatePayload(NetworkPacket packet, GatewaySession session)
    {
        // If both transports are the same, no translation needed
        if (packet.SourceTransport == packet.TargetTransport)
        {
            return packet.Payload;
        }

        // Perform format adaptation based on transport pair
        var transportPair = (Source: packet.SourceTransport, Target: packet.TargetTransport);

        return transportPair switch
        {
            // Internet -> LAN: Usually direct passthrough, both use TCP/IP
            (TransportType.Internet, TransportType.Lan) or
            (TransportType.Lan, TransportType.Internet) =>
                packet.Payload,

            // Internet/WiFiDirect -> Bluetooth: Bluetooth has smaller MTU (typically ~672 bytes),
            // so we need to ensure the payload fits. For this implementation, we tag the payload
            // with transport metadata for the receiving end.
            (TransportType.Internet, TransportType.Bluetooth) or
            (TransportType.Lan, TransportType.Bluetooth) or
            (TransportType.WiFiDirect, TransportType.Bluetooth) =>
                AdaptForBluetooth(packet.Payload, packet.TextPayload),

            // Bluetooth -> Internet/LAN/WiFiDirect: Remove Bluetooth framing, reconstruct
            (TransportType.Bluetooth, TransportType.Internet) or
            (TransportType.Bluetooth, TransportType.Lan) or
            (TransportType.Bluetooth, TransportType.WiFiDirect) =>
                RemoveBluetoothFraming(packet.Payload),

            // WiFiDirect <-> LAN: Both are IP-based, minimal adaptation needed
            (TransportType.WiFiDirect, TransportType.Lan) or
            (TransportType.Lan, TransportType.WiFiDirect) =>
                packet.Payload,

            // Internet <-> WiFiDirect: Passthrough, both use standard IP networking
            (TransportType.Internet, TransportType.WiFiDirect) or
            (TransportType.WiFiDirect, TransportType.Internet) =>
                packet.Payload,

            // Default: passthrough with warning
            _ =>
                HandleUnknownTranslation(packet)
        };
    }

    /// <summary>
    /// Adapts a payload for Bluetooth transport, which has a smaller MTU.
    /// For payloads exceeding typical Bluetooth MTU, wraps them in a segmented format
    /// that the receiving gateway can reassemble.
    /// </summary>
    private static byte[] AdaptForBluetooth(byte[] payload, string? textPayload)
    {
        const int bluetoothMtu = 672;

        // If there's a text payload, prefer that for small messages
        if (!string.IsNullOrEmpty(textPayload) && Encoding.UTF8.GetByteCount(textPayload) <= bluetoothMtu)
        {
            var wrapper = new BluetoothPayloadWrapper
            {
                Format = "text",
                Data = textPayload,
                OriginalSize = Encoding.UTF8.GetByteCount(textPayload)
            };
            return SerializeWrapper(wrapper);
        }

        // If the binary payload fits, wrap it directly
        if (payload.Length <= bluetoothMtu)
        {
            var wrapper = new BluetoothPayloadWrapper
            {
                Format = "binary",
                Data = Convert.ToBase64String(payload),
                OriginalSize = payload.Length
            };
            return SerializeWrapper(wrapper);
        }

        // Payload exceeds Bluetooth MTU - split into segments
        var segments = new List<string>();
        for (var offset = 0; offset < payload.Length; offset += bluetoothMtu)
        {
            var segmentSize = Math.Min(bluetoothMtu, payload.Length - offset);
            var segment = new byte[segmentSize];
            Buffer.BlockCopy(payload, offset, segment, 0, segmentSize);
            segments.Add(Convert.ToBase64String(segment));
        }

        var segmentedWrapper = new BluetoothPayloadWrapper
        {
            Format = "binary",
            Data = string.Join("|", segments),
            OriginalSize = payload.Length,
            SegmentCount = segments.Count
        };

        return SerializeWrapper(segmentedWrapper);
    }

    /// <summary>
    /// Removes Bluetooth transport framing and reconstructs the original payload.
    /// </summary>
    private static byte[] RemoveBluetoothFraming(byte[] bluetoothPayload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(bluetoothPayload);
            var wrapper = JsonSerializer.Deserialize<BluetoothPayloadWrapper>(json);

            if (wrapper == null)
                return bluetoothPayload;

            if (wrapper.Format == "text" && !string.IsNullOrEmpty(wrapper.Data))
            {
                return Encoding.UTF8.GetBytes(wrapper.Data);
            }

            if (wrapper.Format == "binary" && !string.IsNullOrEmpty(wrapper.Data))
            {
                if (wrapper.SegmentCount > 1)
                {
                    // Reassemble segmented payload
                    var segmentStrings = wrapper.Data.Split('|');
                    var totalSize = segmentStrings.Sum(s => Convert.FromBase64String(s).Length);
                    var result = new byte[totalSize];
                    var offset = 0;

                    foreach (var segment in segmentStrings)
                    {
                        var segmentBytes = Convert.FromBase64String(segment);
                        Buffer.BlockCopy(segmentBytes, 0, result, offset, segmentBytes.Length);
                        offset += segmentBytes.Length;
                    }

                    return result;
                }

                return Convert.FromBase64String(wrapper.Data);
            }
        }
        catch (JsonException)
        {
            // Not a wrapped payload, return as-is
        }

        return bluetoothPayload;
    }

    /// <summary>
    /// Handles translation for unknown or unsupported transport pairs.
    /// Logs a warning and returns the payload unchanged.
    /// </summary>
    private byte[] HandleUnknownTranslation(NetworkPacket packet)
    {
        _logger.LogWarning(
            "No translation defined for transport pair: {SourceTransport} -> {TargetTransport}. " +
            "Forwarding payload unchanged.",
            packet.SourceTransport, packet.TargetTransport);
        return packet.Payload;
    }

    /// <summary>
    /// Serializes a Bluetooth payload wrapper to JSON bytes.
    /// </summary>
    private static byte[] SerializeWrapper(BluetoothPayloadWrapper wrapper)
    {
        var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Simulates a delivery acknowledgment for a packet as an asynchronous operation.
    /// In a real implementation, this would involve waiting for a transport-level ACK.
    /// </summary>
    private async Task SimulateDeliveryAckAsync(NetworkPacket packet)
    {
        // Simulate transport round-trip time
        var delay = packet.TargetTransport switch
        {
            TransportType.Lan => 5,
            TransportType.WiFiDirect => 10,
            TransportType.Bluetooth => 50,
            TransportType.Internet => 100,
            _ => 50
        };

        await Task.Delay(delay);

        _logger.LogTrace(
            "Delivery acknowledgment received for packet {PacketId} to device {TargetDevice} via {Transport}",
            packet.PacketId, packet.TargetDeviceId, packet.TargetTransport);
    }

    /// <summary>
    /// Finds an existing session between two devices regardless of direction.
    /// </summary>
    private static GatewaySession? FindExistingSession(string deviceAId, string deviceBId)
    {
        return Sessions.Values.FirstOrDefault(s =>
            (s.DeviceAId == deviceAId && s.DeviceBId == deviceBId) ||
            (s.DeviceAId == deviceBId && s.DeviceBId == deviceAId));
    }

    /// <summary>
    /// Maps a <see cref="GatewaySession"/> entity to a <see cref="GatewaySessionDto"/>.
    /// </summary>
    private static GatewaySessionDto MapToDto(GatewaySession session)
    {
        return new GatewaySessionDto(
            SessionId: session.SessionId,
            GatewayDeviceId: session.GatewayDeviceId,
            DeviceAId: session.DeviceAId,
            DeviceATransport: session.DeviceATransport,
            DeviceBId: session.DeviceBId,
            DeviceBTransport: session.DeviceBTransport,
            State: session.State,
            PacketsForwarded: session.PacketsForwarded,
            BytesForwarded: session.BytesForwarded,
            CreatedAt: session.CreatedAt,
            LastActivityAt: session.LastActivityAt
        );
    }
}

/// <summary>
/// Statistics for the gateway forwarding system.
/// </summary>
public class GatewayStats
{
    /// <summary>Total number of sessions created since service start.</summary>
    public long TotalSessionsCreated { get; init; }

    /// <summary>Number of currently active sessions.</summary>
    public int ActiveSessions { get; init; }

    /// <summary>Total packets forwarded across all sessions.</summary>
    public long TotalPacketsForwarded { get; init; }

    /// <summary>Total bytes forwarded across all sessions.</summary>
    public long TotalBytesForwarded { get; init; }
}

/// <summary>
/// Internal wrapper for adapting payloads to Bluetooth's smaller MTU.
/// </summary>
internal class BluetoothPayloadWrapper
{
    public string Format { get; set; } = "binary";
    public string? Data { get; set; }
    public int OriginalSize { get; set; }
    public int SegmentCount { get; set; } = 1;
}