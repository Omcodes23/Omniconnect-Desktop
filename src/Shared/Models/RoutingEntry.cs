using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.Models;

/// <summary>
/// Represents an entry in the routing table, tracking a device's reachability
/// and quality across multiple transports.
/// </summary>
public class RoutingEntry
{
    /// <summary>Unique identifier for the target device.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Human-readable device name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Capabilities and roles of the target device.</summary>
    public DeviceRole Roles { get; set; } = DeviceRole.Client;

    /// <summary>Scores per transport type indicating route quality (0 = unavailable).</summary>
    public Dictionary<TransportType, double> TransportScores { get; set; } = new();

    /// <summary>Latency in milliseconds per transport.</summary>
    public Dictionary<TransportType, double> LatenciesMs { get; set; } = new();

    /// <summary>Available bandwidth in Mbps per transport.</summary>
    public Dictionary<TransportType, double> BandwidthsMbps { get; set; } = new();

    /// <summary>Whether each transport is currently reachable.</summary>
    public Dictionary<TransportType, bool> TransportAvailability { get; set; } = new();

    /// <summary>The currently preferred transport for this device.</summary>
    public TransportType ActiveTransport { get; set; } = TransportType.Internet;

    /// <summary>Overall composite score for the best route.</summary>
    public double CompositeScore { get; set; }

    /// <summary>Timestamp of the last successful contact with this device.</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp when this entry was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>IP address of the device on the active transport.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Port used by the device on the active transport.</summary>
    public int Port { get; set; }

    /// <summary>Gets the best available transport type based on composite scoring.</summary>
    public TransportType BestTransport
    {
        get
        {
            var best = TransportType.Internet;
            var bestScore = double.MinValue;

            foreach (var (transport, available) in TransportAvailability)
            {
                if (!available) continue;

                var score = TransportScores.GetValueOrDefault(transport, 0d);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = transport;
                }
            }

            return best;
        }
    }
}