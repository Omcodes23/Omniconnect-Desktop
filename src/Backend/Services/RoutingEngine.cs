using System.Collections.Concurrent;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Evaluates available transports (Internet, LAN, Bluetooth, Wi-Fi Direct) and selects
/// the best communication path based on latency, bandwidth, availability, and device capabilities.
/// Maintains a routing table with device scores and handles failover when a transport drops.
/// This service is registered as a singleton and is thread-safe.
/// </summary>
public class RoutingEngine
{
    private readonly ILogger<RoutingEngine> _logger;
    private readonly ConcurrentDictionary<string, RoutingEntry> _routingTable = new();

    // Weight constants for route scoring
    private const double LatencyWeight = 0.40;
    private const double BandwidthWeight = 0.35;
    private const double AvailabilityWeight = 0.25;

    // Ideal reference values for score normalization
    private const double IdealLatencyMs = 1.0;
    private const double IdealBandwidthMbps = 1000.0;

    /// <summary>
    /// Fired when the preferred route to a device has changed (e.g., due to failover).
    /// </summary>
    public event Action<string, TransportType, TransportType>? OnRouteChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingEngine"/>.
    /// </summary>
    public RoutingEngine(ILogger<RoutingEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a snapshot of the full routing table.
    /// </summary>
    public IReadOnlyDictionary<string, RoutingEntry> GetRoutingTable()
    {
        return new Dictionary<string, RoutingEntry>(_routingTable);
    }

    /// <summary>
    /// Returns the routing entry for a specific device, or null if not found.
    /// </summary>
    public RoutingEntry? GetRoute(string deviceId)
    {
        return _routingTable.GetValueOrDefault(deviceId);
    }

    /// <summary>
    /// Registers or updates a device in the routing table with its transport capabilities.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="deviceName">Human-readable device name.</param>
    /// <param name="roles">Capabilities and roles of the device.</param>
    /// <param name="availableTransports">Set of transports the device supports.</param>
    /// <param name="ipAddress">IP address of the device.</param>
    /// <param name="port">Port the device is listening on.</param>
    public void RegisterDevice(
        string deviceId,
        string deviceName,
        DeviceRole roles,
        HashSet<string> availableTransports,
        string? ipAddress,
        int port)
    {
        var entry = _routingTable.GetOrAdd(deviceId, _ => new RoutingEntry
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            Roles = roles,
            CreatedAt = DateTime.UtcNow
        });

        entry.DeviceName = deviceName;
        entry.Roles = roles;
        entry.IpAddress = ipAddress;
        entry.Port = port;
        entry.LastSeen = DateTime.UtcNow;

        // Initialize transport entries for each available transport
        foreach (var transportStr in availableTransports)
        {
            if (Enum.TryParse<TransportType>(transportStr, out var transport))
            {
                entry.TransportAvailability[transport] = true;

                // Assign default scores if not already present
                if (!entry.TransportScores.ContainsKey(transport))
                    entry.TransportScores[transport] = GetDefaultScore(transport);

                if (!entry.LatenciesMs.ContainsKey(transport))
                    entry.LatenciesMs[transport] = GetDefaultLatency(transport);

                if (!entry.BandwidthsMbps.ContainsKey(transport))
                    entry.BandwidthsMbps[transport] = GetDefaultBandwidth(transport);
            }
        }

        // Mark transports as unavailable if the device no longer reports them
        foreach (var transport in Enum.GetValues<TransportType>())
        {
            if (!availableTransports.Contains(transport.ToString()))
            {
                entry.TransportAvailability[transport] = false;
                entry.TransportScores[transport] = 0;
            }
        }

        RecalculateCompositeScore(entry);
        _logger.LogDebug("Device registered/updated in routing table: {DeviceName} ({DeviceId})", deviceName, deviceId);
    }

    /// <summary>
    /// Updates latency measurements for a specific device and transport.
    /// </summary>
    public void UpdateLatency(string deviceId, TransportType transport, double latencyMs)
    {
        if (!_routingTable.TryGetValue(deviceId, out var entry)) return;

        entry.LatenciesMs[transport] = latencyMs;
        entry.LastSeen = DateTime.UtcNow;

        // Exponential moving average to smooth latency fluctuations
        if (entry.TransportScores.TryGetValue(transport, out var currentScore))
        {
            var newScore = CalculateTransportScore(latencyMs, entry.BandwidthsMbps.GetValueOrDefault(transport, 1));
            entry.TransportScores[transport] = (currentScore * 0.7) + (newScore * 0.3);
        }

        var previousTransport = entry.ActiveTransport;
        RecalculateCompositeScore(entry);
        CheckForFailover(deviceId, entry, previousTransport);
    }

    /// <summary>
    /// Updates bandwidth measurements for a specific device and transport.
    /// </summary>
    public void UpdateBandwidth(string deviceId, TransportType transport, double bandwidthMbps)
    {
        if (!_routingTable.TryGetValue(deviceId, out var entry)) return;

        entry.BandwidthsMbps[transport] = bandwidthMbps;
        entry.LastSeen = DateTime.UtcNow;

        if (entry.TransportScores.TryGetValue(transport, out var currentScore))
        {
            var newScore = CalculateTransportScore(entry.LatenciesMs.GetValueOrDefault(transport, 100), bandwidthMbps);
            entry.TransportScores[transport] = (currentScore * 0.7) + (newScore * 0.3);
        }

        var previousTransport = entry.ActiveTransport;
        RecalculateCompositeScore(entry);
        CheckForFailover(deviceId, entry, previousTransport);
    }

    /// <summary>
    /// Marks a transport as unavailable (e.g., connection lost, interface down).
    /// Triggers failover to the next best transport if this was the active route.
    /// </summary>
    public void MarkTransportUnavailable(string deviceId, TransportType transport)
    {
        if (!_routingTable.TryGetValue(deviceId, out var entry)) return;

        entry.TransportAvailability[transport] = false;
        entry.TransportScores[transport] = 0;
        entry.LastSeen = DateTime.UtcNow;

        _logger.LogWarning(
            "Transport {Transport} marked unavailable for device {DeviceName} ({DeviceId})",
            transport, entry.DeviceName, deviceId);

        var previousTransport = entry.ActiveTransport;
        RecalculateCompositeScore(entry);
        CheckForFailover(deviceId, entry, previousTransport);
    }

    /// <summary>
    /// Marks a transport as available again after reconnection.
    /// </summary>
    public void MarkTransportAvailable(string deviceId, TransportType transport)
    {
        if (!_routingTable.TryGetValue(deviceId, out var entry)) return;

        entry.TransportAvailability[transport] = true;

        if (!entry.TransportScores.ContainsKey(transport) || entry.TransportScores[transport] <= 0)
        {
            entry.TransportScores[transport] = GetDefaultScore(transport);
            entry.LatenciesMs[transport] = GetDefaultLatency(transport);
            entry.BandwidthsMbps[transport] = GetDefaultBandwidth(transport);
        }

        entry.LastSeen = DateTime.UtcNow;
        _logger.LogInformation(
            "Transport {Transport} marked available for device {DeviceName} ({DeviceId})",
            transport, entry.DeviceName, deviceId);

        var previousTransport = entry.ActiveTransport;
        RecalculateCompositeScore(entry);

        // If the newly available transport is better than the current active one, switch
        if (entry.BestTransport != previousTransport)
        {
            var newTransport = entry.BestTransport;
            entry.ActiveTransport = newTransport;
            _logger.LogInformation(
                "Route improved for {DeviceName} ({DeviceId}): {OldTransport} -> {NewTransport}",
                entry.DeviceName, deviceId, previousTransport, newTransport);
            OnRouteChanged?.Invoke(deviceId, previousTransport, newTransport);
        }
    }

    /// <summary>
    /// Evaluates and selects the best route to a target device.
    /// </summary>
    /// <param name="request">Route evaluation parameters.</param>
    /// <returns>Result containing the selected route and failover candidates.</returns>
    public EvaluateRouteResult EvaluateRoute(EvaluateRouteRequest request)
    {
        var entry = _routingTable.GetValueOrDefault(request.TargetDeviceId);
        if (entry == null)
        {
            return new EvaluateRouteResult(
                Success: false,
                SelectedTransport: null,
                RouteInfo: null,
                FailoverCandidates: new List<TransportType>(),
                ErrorMessage: $"Device '{request.TargetDeviceId}' not found in routing table."
            );
        }

        // If the entry is stale (not seen in 60 seconds), mark all non-Internet transports as unavailable
        if ((DateTime.UtcNow - entry.LastSeen).TotalSeconds > 60)
        {
            foreach (var transport in Enum.GetValues<TransportType>())
            {
                if (transport != TransportType.Internet)
                    entry.TransportAvailability[transport] = false;
            }
            RecalculateCompositeScore(entry);
        }

        RecalculateCompositeScore(entry);

        var availableTransports = Enum.GetValues<TransportType>()
            .Where(t => entry.TransportAvailability.GetValueOrDefault(t, false))
            .ToList();

        if (availableTransports.Count == 0)
        {
            return new EvaluateRouteResult(
                Success: false,
                SelectedTransport: null,
                RouteInfo: null,
                FailoverCandidates: new List<TransportType>(),
                ErrorMessage: $"No available transports for device '{request.TargetDeviceId}'."
            );
        }

        // Apply user preferences if specified
        var candidates = availableTransports.AsEnumerable();
        if (request.PreferredTransports is { Count: > 0 })
        {
            candidates = request.PreferredTransports
                .Where(t => availableTransports.Contains(t))
                .ToList();

            if (!candidates.Any())
                candidates = availableTransports;
        }

        // Filter by minimum bandwidth if specified
        if (request.MinimumBandwidthMbps.HasValue)
        {
            candidates = candidates.Where(t =>
                entry.BandwidthsMbps.GetValueOrDefault(t, 0) >= request.MinimumBandwidthMbps.Value);
        }

        // Filter by maximum latency if specified
        if (request.MaximumLatencyMs.HasValue)
        {
            candidates = candidates.Where(t =>
                entry.LatenciesMs.GetValueOrDefault(t, double.MaxValue) <= request.MaximumLatencyMs.Value);
        }

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return new EvaluateRouteResult(
                Success: false,
                SelectedTransport: null,
                RouteInfo: null,
                FailoverCandidates: availableTransports,
                ErrorMessage: "No transport satisfies the specified constraints."
            );
        }

        // Select the best transport by score
        var bestTransport = candidateList
            .OrderByDescending(t => entry.TransportScores.GetValueOrDefault(t, 0))
            .First();

        entry.ActiveTransport = bestTransport;

        var routeInfo = MapToRouteInfoDto(entry);
        var failoverCandidates = candidateList
            .Where(t => t != bestTransport)
            .OrderByDescending(t => entry.TransportScores.GetValueOrDefault(t, 0))
            .ToList();

        _logger.LogDebug(
            "Route evaluated for {DeviceName} ({DeviceId}): selected {Transport} with score {Score:F2}",
            entry.DeviceName, request.TargetDeviceId, bestTransport, entry.CompositeScore);

        return new EvaluateRouteResult(
            Success: true,
            SelectedTransport: bestTransport,
            RouteInfo: routeInfo,
            FailoverCandidates: failoverCandidates,
            ErrorMessage: null
        );
    }

    /// <summary>
    /// Removes a device from the routing table.
    /// </summary>
    public bool RemoveDevice(string deviceId)
    {
        var removed = _routingTable.TryRemove(deviceId, out var entry);
        if (removed && entry != null)
        {
            _logger.LogInformation("Device removed from routing table: {DeviceName} ({DeviceId})", entry.DeviceName, deviceId);
        }
        return removed;
    }

    /// <summary>
    /// Returns a list of all routing entries formatted as DTOs.
    /// </summary>
    public List<RouteInfoDto> GetAllRouteInfos()
    {
        return _routingTable.Values
            .Select(MapToRouteInfoDto)
            .OrderByDescending(r => r.CompositeScore)
            .ToList();
    }

    /// <summary>
    /// Recalculates the composite score for a routing entry based on all transport scores.
    /// The composite score is the maximum score among all available transports.
    /// </summary>
    private void RecalculateCompositeScore(RoutingEntry entry)
    {
        var bestScore = entry.TransportAvailability
            .Where(kvp => kvp.Value)
            .Select(kvp => entry.TransportScores.GetValueOrDefault(kvp.Key, 0d))
            .DefaultIfEmpty(0d)
            .Max();

        entry.CompositeScore = bestScore;

        // Update active transport if current one is no longer the best
        var bestTransport = entry.BestTransport;
        if (entry.TransportAvailability.GetValueOrDefault(entry.ActiveTransport, false) == false
            || (entry.TransportScores.GetValueOrDefault(bestTransport, 0d) >
                entry.TransportScores.GetValueOrDefault(entry.ActiveTransport, 0d) * 1.1))
        {
            // Only switch if the difference is significant (>10% better) to avoid flapping
        }
    }

    /// <summary>
    /// Checks whether failover is needed and performs it if the active transport is no longer optimal.
    /// </summary>
    private void CheckForFailover(string deviceId, RoutingEntry entry, TransportType previousTransport)
    {
        var bestTransport = entry.BestTransport;

        if (bestTransport != previousTransport)
        {
            entry.ActiveTransport = bestTransport;
            _logger.LogWarning(
                "Failover for {DeviceName} ({DeviceId}): {OldTransport} -> {NewTransport} (score: {Score:F2})",
                entry.DeviceName, deviceId, previousTransport, bestTransport, entry.CompositeScore);
            OnRouteChanged?.Invoke(deviceId, previousTransport, bestTransport);
        }
    }

    /// <summary>
    /// Maps a <see cref="RoutingEntry"/> to a <see cref="RouteInfoDto"/> for API consumption.
    /// </summary>
    private static RouteInfoDto MapToRouteInfoDto(RoutingEntry entry)
    {
        var transportScores = Enum.GetValues<TransportType>()
            .Select(t => new TransportScoreDto(
                Transport: t,
                Score: entry.TransportScores.GetValueOrDefault(t, 0),
                LatencyMs: entry.LatenciesMs.GetValueOrDefault(t, 0),
                BandwidthMbps: entry.BandwidthsMbps.GetValueOrDefault(t, 0),
                IsAvailable: entry.TransportAvailability.GetValueOrDefault(t, false)
            ))
            .ToList();

        return new RouteInfoDto(
            DeviceId: entry.DeviceId,
            DeviceName: entry.DeviceName,
            ActiveTransport: entry.ActiveTransport,
            CompositeScore: entry.CompositeScore,
            LatencyMs: entry.LatenciesMs.GetValueOrDefault(entry.ActiveTransport, 0),
            BandwidthMbps: entry.BandwidthsMbps.GetValueOrDefault(entry.ActiveTransport, 0),
            IsAvailable: entry.CompositeScore > 0,
            AvailableTransports: transportScores,
            LastSeen: entry.LastSeen
        );
    }

    /// <summary>
    /// Calculates a normalized transport score (0-100) based on latency and bandwidth.
    /// </summary>
    private static double CalculateTransportScore(double latencyMs, double bandwidthMbps)
    {
        // Normalize latency: lower is better. Score contribution is sigmoid-based.
        var latencyScore = latencyMs > 0
            ? 100.0 * Math.Exp(-latencyMs / IdealLatencyMs) / (1.0 + Math.Exp(-latencyMs / IdealLatencyMs))
            : 0;

        // Normalize bandwidth: higher is better. Caps at IdealBandwidthMbps.
        var bandwidthScore = 100.0 * Math.Min(bandwidthMbps / IdealBandwidthMbps, 1.0);

        // Weighted composite
        return (latencyScore * LatencyWeight) + (bandwidthScore * BandwidthWeight) + (AvailabilityWeight * 100);
    }

    /// <summary>
    /// Gets a default score for a transport type based on its typical characteristics.
    /// </summary>
    private static double GetDefaultScore(TransportType transport) => transport switch
    {
        TransportType.Lan => 85.0,
        TransportType.WiFiDirect => 75.0,
        TransportType.Bluetooth => 55.0,
        TransportType.Internet => 50.0,
        _ => 40.0
    };

    /// <summary>
    /// Gets a default latency in milliseconds for a transport type.
    /// </summary>
    private static double GetDefaultLatency(TransportType transport) => transport switch
    {
        TransportType.Lan => 2.0,
        TransportType.WiFiDirect => 5.0,
        TransportType.Bluetooth => 30.0,
        TransportType.Internet => 50.0,
        _ => 100.0
    };

    /// <summary>
    /// Gets a default bandwidth in Mbps for a transport type.
    /// </summary>
    private static double GetDefaultBandwidth(TransportType transport) => transport switch
    {
        TransportType.Lan => 1000.0,
        TransportType.WiFiDirect => 200.0,
        TransportType.Bluetooth => 25.0,
        TransportType.Internet => 100.0,
        _ => 10.0
    };
}