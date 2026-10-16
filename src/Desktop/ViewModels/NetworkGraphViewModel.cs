using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Desktop.Services;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Desktop.ViewModels;

public partial class NetworkGraphViewModel : ViewModelBase
{
    private readonly ApiService _api;
    private System.Timers.Timer? _refreshTimer;

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isNotEmpty;

    partial void OnDeviceCountChanged(int value)
    {
        IsEmpty = value == 0;
        IsNotEmpty = value > 0;
    }

    public ObservableCollection<GraphDeviceItem> Devices { get; } = new();

    public NetworkGraphViewModel(ApiService api)
    {
        _api = api;
    }

    public void StartAutoRefresh()
    {
        StopAutoRefresh();
        _refreshTimer = new System.Timers.Timer(10000);
        _refreshTimer.Elapsed += async (_, _) => await RefreshDevicesAsync();
        _refreshTimer.AutoReset = true;
        _refreshTimer.Start();
        _ = RefreshDevicesAsync();
    }

    public void StopAutoRefresh()
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var devices = await _api.GetDiscoveredDevicesAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Devices.Clear();
                var list = devices?.ToList() ?? new List<DeviceDiscoveryDto>();

                // If no devices discovered from backend yet, add a local self-node so the graph isn't completely empty
                if (list.Count == 0)
                {
                    list.Add(new DeviceDiscoveryDto(
                        "local-desktop-node",
                        "Desktop (You)",
                        "Windows PC",
                        DeviceRole.Client | DeviceRole.ServiceHost,
                        new HashSet<string> { "LAN", "Bluetooth", "Internet" },
                        "127.0.0.1",
                        5000,
                        null,
                        null
                    ));
                }

                var index = 0;
                foreach (var d in list)
                {
                    var transport = d.AvailableTransports?.FirstOrDefault() ?? "LAN";
                    Devices.Add(new GraphDeviceItem
                    {
                        DeviceId = d.DeviceId,
                        DeviceName = d.DeviceName,
                        DeviceModel = d.DeviceModel,
                        Roles = d.Roles,
                        AvailableTransports = d.AvailableTransports ?? new HashSet<string> { "LAN" },
                        IpAddress = d.IpAddress,
                        Port = d.Port,
                        TransportLabel = string.Join(", ", d.AvailableTransports ?? new HashSet<string> { "LAN" }),
                        TransportColor = GetTransportColor(transport),
                        Index = index++
                    });
                }
                DeviceCount = Devices.Count;
                ComputeGraphLayout();
            });
        }
        catch
        {
            // Fallback self node on exception/offline
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Devices.Clear();
                Devices.Add(new GraphDeviceItem());
                DeviceCount = Devices.Count;
                ComputeGraphLayout();
            });
        }
    }

    public void ComputeGraphLayout()
    {
        var count = Devices.Count;
        if (count == 0) return;

        var centerX = 260.0;
        var centerY = 200.0;
        var radius = Math.Min(centerX, centerY) - 40;

        for (int i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count - Math.PI / 2;
            Devices[i].X = centerX + radius * Math.Cos(angle);
            Devices[i].Y = centerY + radius * Math.Sin(angle);
        }
    }

    public List<GraphEdge> ComputeEdges()
    {
        var edges = new List<GraphEdge>();
        var devices = Devices.ToList();

        for (int i = 0; i < devices.Count; i++)
        {
            for (int j = i + 1; j < devices.Count; j++)
            {
                // Connect devices that share at least one transport
                var sharedTransports = devices[i].AvailableTransports?
                    .Intersect(devices[j].AvailableTransports ?? new HashSet<string>())
                    .ToList();

                if (sharedTransports != null && sharedTransports.Count > 0)
                {
                    edges.Add(new GraphEdge
                    {
                        FromIndex = i,
                        ToIndex = j,
                        Color = GetTransportColor(sharedTransports[0])
                    });
                }
            }
        }

        return edges;
    }

    private static string GetTransportColor(string transport)
    {
        return transport?.ToLowerInvariant() switch
        {
            "bluetooth" => "#3b82f6",
            "lan" => "#10b981",
            "wifi" or "wi-fi" or "wifidirect" => "#06b6d4",
            "internet" => "#f59e0b",
            _ => "#6366f1"
        };
    }

    [RelayCommand]
    private void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
    }
}

public class GraphDeviceItem
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public DeviceRole Roles { get; set; }
    public HashSet<string>? AvailableTransports { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    public string TransportLabel { get; set; } = "LAN";
    public string TransportColor { get; set; } = "#10b981";
    public int Index { get; set; }

    // Computed layout positions
    public double X { get; set; }
    public double Y { get; set; }

    public string RolesDisplay
    {
        get
        {
            var roles = new List<string>();
            if (Roles.HasFlag(DeviceRole.Client)) roles.Add("Client");
            if (Roles.HasFlag(DeviceRole.ServiceHost)) roles.Add("Host");
            if (Roles.HasFlag(DeviceRole.Gateway)) roles.Add("Gateway");
            if (Roles.HasFlag(DeviceRole.AiHost)) roles.Add("AI");
            return roles.Count > 0 ? string.Join(", ", roles) : "Device";
        }
    }

    public string Initials => DeviceName.Length > 0
        ? DeviceName[..1].ToUpperInvariant()
        : "?";
}

public class GraphEdge
{
    public int FromIndex { get; set; }
    public int ToIndex { get; set; }
    public string Color { get; set; } = "#6366f1";
}
