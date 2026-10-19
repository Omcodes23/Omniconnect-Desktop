using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConnect.Desktop.Services;

/// <summary>
/// UDP-based LAN discovery that mirrors the mobile implementation.
/// Listens for multicast/subnet broadcasts and broadcasts own presence.
/// </summary>
public sealed class LanDiscoveryService : IDisposable
{
    private readonly UdpClient? _receiver;
    private readonly UdpClient _sender;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _broadcastTimer;
    private readonly Timer _purgeTimer;
    private bool _disposed;

    public event Action<DiscoveredDevice>? DeviceDiscovered;
    public event Action<string>? DeviceLost;

    public IReadOnlyList<DiscoveredDevice> Devices => _devices.Values.ToList();

    public LanDiscoveryService(int listenPort = 45678)
    {
        var multicastGroup = IPAddress.Parse("239.255.100.50");

        // The backend also listens on this port. On Windows, binding the same UDP
        // endpoint twice requires SO_REUSEADDR *before* Bind, otherwise it throws
        // SocketException 10048 and takes the whole app down at login.
        _receiver = TryBindReceiver(listenPort, multicastGroup);

        _sender = new UdpClient(0) { EnableBroadcast = true, MulticastLoopback = true };

        _broadcastTimer = new Timer(_ => BroadcastPresence(), null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        _purgeTimer = new Timer(_ => PurgeStale(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        if (_receiver is not null)
            _ = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Binds the discovery receiver, tolerating a port already owned by the backend
    /// or another OmniConnect instance. Returns null if the port is unusable; in that
    /// case outgoing presence broadcasts still work, we just cannot hear others.
    /// </summary>
    private static UdpClient? TryBindReceiver(int listenPort, IPAddress multicastGroup)
    {
        try
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            client.EnableBroadcast = true;

            try { client.JoinMulticastGroup(multicastGroup); } catch { /* multicast may fail on some networks */ }

            return client;
        }
        catch (SocketException)
        {
            // Port unavailable — degrade gracefully instead of crashing the UI.
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public void SetSelfInfo(string deviceId, string deviceName, string deviceModel, string? userId = null, string? userName = null, string? ipAddress = null, int port = 5001)
    {
        SelfDeviceId = deviceId;
        SelfDeviceName = deviceName;
        SelfDeviceModel = deviceModel;
        SelfUserId = userId;
        SelfUserName = userName;
        SelfIpAddress = ipAddress ?? GetLocalIpAddress();
        SelfPort = port;
    }

    public string SelfDeviceId { get; private set; } = string.Empty;
    public string SelfDeviceName { get; private set; } = "Desktop";
    public string SelfDeviceModel { get; private set; } = Environment.OSVersion.VersionString;
    public string? SelfUserId { get; private set; }
    public string? SelfUserName { get; private set; }
    public string SelfIpAddress { get; private set; } = string.Empty;
    public int SelfPort { get; private set; } = 5001;

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_receiver is null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _receiver.ReceiveAsync(ct);
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { /* ignore parse errors */ }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint remoteEp)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var deviceId = root.GetProperty("deviceId").GetString() ?? "";
            if (string.IsNullOrEmpty(deviceId) || deviceId == SelfDeviceId) return;

            var device = new DiscoveredDevice
            {
                DeviceId = deviceId,
                DeviceName = root.GetProperty("deviceName").GetString() ?? "Device",
                DeviceModel = root.GetProperty("deviceModel").GetString() ?? "",
                IpAddress = root.TryGetProperty("ipAddress", out var ipProp) && ipProp.ValueKind != JsonValueKind.Null
                    ? ipProp.GetString() ?? remoteEp.Address.ToString()
                    : remoteEp.Address.ToString(),
                Port = root.TryGetProperty("port", out var portProp) && portProp.ValueKind == JsonValueKind.Number
                    ? portProp.GetInt32()
                    : 5001,
                UserId = root.TryGetProperty("userId", out var uidProp) && uidProp.ValueKind != JsonValueKind.Null
                    ? uidProp.GetString()
                    : null,
                UserName = root.TryGetProperty("userName", out var unProp) && unProp.ValueKind != JsonValueKind.Null
                    ? unProp.GetString()
                    : null,
                AvailableTransports = root.TryGetProperty("availableTransports", out var tProp) && tProp.ValueKind == JsonValueKind.Array
                    ? tProp.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string> { "LAN" },
                LastSeen = DateTime.UtcNow
            };

            var isNew = _devices.TryAdd(deviceId, device);
            if (!isNew)
            {
                _devices[deviceId] = device; // update
            }

            DeviceDiscovered?.Invoke(device);
        }
        catch { /* malformed packet */ }
    }

    private void BroadcastPresence()
    {
        if (string.IsNullOrEmpty(SelfDeviceId)) return;

        var payload = new
        {
            deviceId = SelfDeviceId,
            deviceName = SelfDeviceName,
            deviceModel = SelfDeviceModel,
            roles = "Client",
            availableTransports = new[] { "LAN", "Internet" },
            port = SelfPort,
            ipAddress = SelfIpAddress,
            userId = SelfUserId,
            userName = SelfUserName
        };

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        try
        {
            // Multicast
            _sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("239.255.100.50"), 45678));
        }
        catch { }

        try
        {
            // Subnet broadcast
            _sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, 45678));
        }
        catch { }
    }

    private void PurgeStale()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-15);
        var stale = _devices.Where(kvp => kvp.Value.LastSeen < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var id in stale)
        {
            if (_devices.TryRemove(id, out var removed))
            {
                DeviceLost?.Invoke(id);
            }
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _broadcastTimer.Dispose();
        _purgeTimer.Dispose();
        try { _receiver?.Close(); } catch { }
        try { _sender.Close(); } catch { }
    }
}

public sealed class DiscoveredDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 5001;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public List<string> AvailableTransports { get; set; } = new() { "LAN" };
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}