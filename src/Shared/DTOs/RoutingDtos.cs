using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.DTOs;

/// <summary>
/// Information about a computed route to a device, including the best transport
/// and quality metrics.
/// </summary>
/// <param name="DeviceId">Unique identifier of the target device.</param>
/// <param name="DeviceName">Human-readable name of the target device.</param>
/// <param name="ActiveTransport">The currently selected transport for this route.</param>
/// <param name="CompositeScore">Overall composite quality score for this route.</param>
/// <param name="LatencyMs">Round-trip latency in milliseconds.</param>
/// <param name="BandwidthMbps">Estimated available bandwidth in Mbps.</param>
/// <param name="IsAvailable">Whether the route is currently reachable.</param>
/// <param name="AvailableTransports">List of all transports available for this device along with their scores.</param>
/// <param name="LastSeen">Timestamp of the last successful contact.</param>
public record RouteInfoDto(
    string DeviceId,
    string DeviceName,
    TransportType ActiveTransport,
    double CompositeScore,
    double LatencyMs,
    double BandwidthMbps,
    bool IsAvailable,
    List<TransportScoreDto> AvailableTransports,
    DateTime LastSeen
);

/// <summary>
/// Score and metrics for a single transport type to a device.
/// </summary>
/// <param name="Transport">The transport type.</param>
/// <param name="Score">Composite quality score (0 = unavailable).</param>
/// <param name="LatencyMs">Latency in milliseconds.</param>
/// <param name="BandwidthMbps">Bandwidth in Mbps.</param>
/// <param name="IsAvailable">Whether the transport is currently reachable.</param>
public record TransportScoreDto(
    TransportType Transport,
    double Score,
    double LatencyMs,
    double BandwidthMbps,
    bool IsAvailable
);

/// <summary>
/// Request to evaluate and select the best route to a target device.
/// </summary>
/// <param name="TargetDeviceId">The device to route to.</param>
/// <param name="PreferredTransports">Optional list of preferred transports, in order of preference.</param>
/// <param name="MinimumBandwidthMbps">Minimum acceptable bandwidth in Mbps.</param>
/// <param name="MaximumLatencyMs">Maximum acceptable latency in milliseconds.</param>
public record EvaluateRouteRequest(
    string TargetDeviceId,
    List<TransportType>? PreferredTransports,
    double? MinimumBandwidthMbps,
    double? MaximumLatencyMs
);

/// <summary>
/// Result of a route evaluation request.
/// </summary>
/// <param name="Success">Whether a viable route was found.</param>
/// <param name="SelectedTransport">The transport selected for routing.</param>
/// <param name="RouteInfo">Detailed route information if found.</param>
/// <param name="FailoverCandidates">Alternative routes that can be used for failover.</param>
/// <param name="ErrorMessage">Error message if routing failed.</param>
public record EvaluateRouteResult(
    bool Success,
    TransportType? SelectedTransport,
    RouteInfoDto? RouteInfo,
    List<TransportType> FailoverCandidates,
    string? ErrorMessage
);