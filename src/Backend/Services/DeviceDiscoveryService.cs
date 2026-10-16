using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OmniConnect.Shared.Converters;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Background service that discovers nearby devices using UDP multicast (cross-platform mDNS/Zeroconf approach),
/// periodically broadcasts device presence, and maintains a local cache of discovered peers.
/// </summary>
public class DeviceDiscoveryService : BackgroundService
{
    private readonly ILogger<DeviceDiscoveryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeviceDiscoveryOptions _options;

    private readonly ConcurrentDictionary<string, DeviceInfo> _discoveredDevices = new();
    private readonly ConcurrentDictionary<string, DateTime> _deviceExpirations = new();

    private UdpClient? _broadcastClient;
    private UdpClient? _listenerClient;
    private readonly string _deviceId;
    private readonly string _deviceName;

    /// <summary>
    /// Fired when a new device is discovered for the first time.
    /// </summary>
    public event Action<DeviceInfo>? OnDeviceDiscovered;

    /// <summary>
    /// Fired when a previously discovered device is no longer reachable.
    /// </summary>
    public event Action<string>? OnDeviceExpired;

    /// <summary>
    /// Fired when a device's presence is updated (e.g., status change).
    /// </summary>
    public event Action<DeviceInfo>? OnDeviceUpdated;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceDiscoveryService"/>.
    /// </summary>
    public DeviceDiscoveryService(
        ILogger<DeviceDiscoveryService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IOptions<DeviceDiscoveryOptions>? options = null)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _options = options?.Value ?? new DeviceDiscoveryOptions();

        _deviceId = _configuration["Device:Id"] ?? $"device-{Environment.MachineName}-{Guid.NewGuid():N}"[..20];
        _deviceName = _configuration["Device:Name"] ?? Environment.MachineName;
    }

    /// <summary>
    /// The multicast group address used for device discovery.
    /// </summary>
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.100.50");

    /// <summary>
    /// The port used for device discovery broadcasts.
    /// </summary>
    public static readonly int DiscoveryPort = 45678;

    /// <summary>
    /// The device id this server node broadcasts as. Clients use it to hide
    /// the machine they are running on from the devices list.
    /// </summary>
    public string SelfDeviceId => _deviceId;

    /// <summary>
    /// Returns a snapshot of all currently discovered devices.
    /// </summary>
    public IReadOnlyCollection<DeviceInfo> GetDiscoveredDevices()
    {
        PurgeExpiredDevices();
        return _discoveredDevices.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Returns information about a specific discovered device, or null if not found.
    /// </summary>
    public DeviceInfo? GetDevice(string deviceId)
    {
        PurgeExpiredDevices();
        return _discoveredDevices.GetValueOrDefault(deviceId);
    }

    /// <summary>
    /// Registers this device with the server API for central presence tracking.
    /// </summary>
    public async Task RegisterWithServerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var serverUrl = _configuration["Device:ServerUrl"] ?? "http://localhost:5000";
            var client = _httpClientFactory.CreateClient("DeviceRegistration");
            client.BaseAddress = new Uri(serverUrl);

            var dto = new DeviceDiscoveryDto(
                DeviceId: _deviceId,
                DeviceName: _deviceName,
                DeviceModel: Environment.OSVersion.ToString(),
                Roles: DeviceRole.Client | DeviceRole.Gateway,
                AvailableTransports: new HashSet<string>
                {
                    TransportType.Internet.ToString(),
                    TransportType.Lan.ToString()
                },
                IpAddress: GetLocalIpAddress(),
                Port: _options.ServicePort
            );

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/api/devices/register", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully registered with server at {ServerUrl}. DeviceId: {DeviceId}", serverUrl, _deviceId);
            }
            else
            {
                _logger.LogWarning("Server registration returned {StatusCode} for device {DeviceId}", response.StatusCode, _deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register device {DeviceId} with server. Continuing in local-only mode.", _deviceId);
        }
    }

    /// <summary>
    /// Main execution loop for the background service. Periodically broadcasts device presence
    /// and listens for discovery messages from other devices.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DeviceDiscoveryService starting. DeviceId: {DeviceId}, Name: {DeviceName}, Multicast: {MulticastGroup}:{Port}",
            _deviceId, _deviceName, MulticastGroup, DiscoveryPort);

        try
        {
            InitializeUdpClients();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize UDP clients for discovery. Service will not start.");
            return;
        }

        // Start the listener in the background
        var listenTask = ListenForDevicesAsync(stoppingToken);

        // Register with the API server
        await RegisterWithServerAsync(stoppingToken);

        // Periodic broadcast loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastPresenceAsync(stoppingToken);
                PurgeExpiredDevices();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in device discovery broadcast cycle");
            }

            await Task.Delay(_options.BroadcastIntervalMs, stoppingToken);
        }

        // Wait for the listener to finish
        try { await listenTask; } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Initializes the UDP broadcast and listener clients.
    /// </summary>
    private void InitializeUdpClients()
    {
        // Listener: bind to the discovery port on all interfaces
        _listenerClient = new UdpClient(AddressFamily.InterNetwork);
        _listenerClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listenerClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _listenerClient.JoinMulticastGroup(MulticastGroup);
        _listenerClient.MulticastLoopback = false;
        _logger.LogDebug("Discovery listener bound to port {Port} on all interfaces", DiscoveryPort);

        // Broadcaster: send from a random port
        _broadcastClient = new UdpClient(AddressFamily.InterNetwork);
        _broadcastClient.EnableBroadcast = true;
        _broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _broadcastClient.MulticastLoopback = false;
        _logger.LogDebug("Discovery broadcaster initialized");
    }

    /// <summary>
    /// Broadcasts this device's presence information to the multicast group.
    /// </summary>
    private async Task BroadcastPresenceAsync(CancellationToken cancellationToken)
    {
        if (_broadcastClient == null) return;

        try
        {
            var localIp = GetLocalIpAddress();
            var roles = DeviceRole.Client;
            if (_configuration.GetValue<bool>("Device:EnableGateway")) roles |= DeviceRole.Gateway;
            if (_configuration.GetValue<bool>("Device:EnableAiHost")) roles |= DeviceRole.AiHost;

            var discoveryInfo = new DeviceDiscoveryDto(
                DeviceId: _deviceId,
                DeviceName: _deviceName,
                DeviceModel: Environment.OSVersion.ToString(),
                Roles: roles,
                AvailableTransports: GetAvailableTransports(),
                IpAddress: localIp,
                Port: _options.ServicePort
            );

            var json = JsonSerializer.Serialize(discoveryInfo, JsonDefaults.Options);

            var bytes = Encoding.UTF8.GetBytes(json);
            var multicastEndpoint = new IPEndPoint(MulticastGroup, DiscoveryPort);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            await _broadcastClient.SendAsync(bytes, bytes.Length, multicastEndpoint);
            try { await _broadcastClient.SendAsync(bytes, bytes.Length, broadcastEndpoint); } catch { }
            _logger.LogTrace("Broadcast presence: {DeviceName} ({DeviceId})", _deviceName, _deviceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to broadcast presence");
        }
    }

    /// <summary>
    /// Continuously listens for UDP multicast messages from other devices on the network.
    /// </summary>
    private async Task ListenForDevicesAsync(CancellationToken cancellationToken)
    {
        if (_listenerClient == null) return;

        _logger.LogInformation("Started listening for device discovery messages");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _listenerClient.ReceiveAsync(cancellationToken);
                var json = Encoding.UTF8.GetString(result.Buffer);

                var discoveryDto = JsonSerializer.Deserialize<DeviceDiscoveryDto>(json, JsonDefaults.Options);

                if (discoveryDto == null || string.IsNullOrEmpty(discoveryDto.DeviceId))
                    continue;

                // Ignore our own broadcasts
                if (discoveryDto.DeviceId == _deviceId)
                    continue;

                UpdateDiscoveredDevice(discoveryDto, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error receiving discovery message");
            }
        }
    }

    /// <summary>
    /// Updates the local cache with a discovered device's information.
    /// </summary>
    private void UpdateDiscoveredDevice(DeviceDiscoveryDto dto, IPEndPoint remoteEndpoint)
    {
        var isNew = !_discoveredDevices.ContainsKey(dto.DeviceId);
        var wasExpired = _deviceExpirations.TryGetValue(dto.DeviceId, out var oldExpiry) && oldExpiry < DateTime.UtcNow;

        var deviceInfo = new DeviceInfo
        {
            DeviceId = dto.DeviceId,
            DeviceName = dto.DeviceName,
            DeviceModel = dto.DeviceModel,
            Platform = dto.DeviceModel?.Contains("Android") == true ? "Android" : (dto.DeviceModel?.Contains("iOS") == true ? "iOS" : Environment.OSVersion.Platform.ToString()),
            Roles = dto.Roles,
            AvailableTransports = dto.AvailableTransports?.Count > 0 ? dto.AvailableTransports : new HashSet<string> { "LAN" },
            IpAddress = dto.IpAddress ?? remoteEndpoint.Address.ToString(),
            Port = dto.Port,
            UserId = dto.UserId,
            UserName = dto.UserName,
            Status = ConnectionStatus.Online,
            LastSeen = DateTime.UtcNow
        };

        _discoveredDevices[dto.DeviceId] = deviceInfo;
        _deviceExpirations[dto.DeviceId] = DateTime.UtcNow.AddSeconds(_options.DeviceExpirationSeconds);

        if (isNew || wasExpired)
        {
            _logger.LogInformation("Discovered new device: {DeviceName} ({DeviceId}) at {IpAddress}:{Port}",
                dto.DeviceName, dto.DeviceId, deviceInfo.IpAddress, deviceInfo.Port);
            OnDeviceDiscovered?.Invoke(deviceInfo);
        }
        else
        {
            _logger.LogTrace("Updated device: {DeviceName} ({DeviceId})", dto.DeviceName, dto.DeviceId);
            OnDeviceUpdated?.Invoke(deviceInfo);
        }
    }

    /// <summary>
    /// Removes devices from the cache that have not been seen within the expiration window.
    /// </summary>
    private void PurgeExpiredDevices()
    {
        var now = DateTime.UtcNow;
        var expiredIds = _deviceExpirations
            .Where(kvp => kvp.Value < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in expiredIds)
        {
            if (_discoveredDevices.TryRemove(id, out var removed))
            {
                _deviceExpirations.TryRemove(id, out _);
                _logger.LogInformation("Device expired: {DeviceName} ({DeviceId})", removed.DeviceName, id);
                OnDeviceExpired?.Invoke(id);
            }
        }
    }

    /// <summary>
    /// Gets the set of transport types this device currently supports.
    /// </summary>
    private HashSet<string> GetAvailableTransports()
    {
        var transports = new HashSet<string> { TransportType.Internet.ToString() };

        if (IsNetworkAvailable())
            transports.Add(TransportType.Lan.ToString());

        // Bluetooth and Wi-Fi Direct availability are platform-specific;
        // they can be enabled via configuration for now.
        if (_configuration.GetValue<bool>("Device:EnableBluetooth"))
            transports.Add(TransportType.Bluetooth.ToString());

        if (_configuration.GetValue<bool>("Device:EnableWiFiDirect"))
            transports.Add(TransportType.WiFiDirect.ToString());

        return transports;
    }

    /// <summary>
    /// Checks whether the local machine has an active network interface available for LAN communication.
    /// </summary>
    private static bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the local IPv4 address of this machine, or "127.0.0.1" if none is found.
    /// </summary>
    private static string? GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return ip?.ToString();
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// Releases the UDP clients used by the discovery service.
    /// </summary>
    public override void Dispose()
    {
        try
        {
            if (_listenerClient != null)
            {
                try { _listenerClient.DropMulticastGroup(MulticastGroup); } catch { }
                _listenerClient.Close();
                (_listenerClient as IDisposable)?.Dispose();
            }

            _broadcastClient?.Close();
            (_broadcastClient as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing UDP clients");
        }

        base.Dispose();
    }
}

/// <summary>
/// Configuration options for the device discovery service.
/// </summary>
public class DeviceDiscoveryOptions
{
    /// <summary>Interval in milliseconds between presence broadcasts. Default: 5000ms.</summary>
    public int BroadcastIntervalMs { get; set; } = 5000;

    /// <summary>Time in seconds before a device is considered expired. Default: 30s.</summary>
    public int DeviceExpirationSeconds { get; set; } = 30;

    /// <summary>The port that local services are listening on, broadcast to peers.</summary>
    public int ServicePort { get; set; } = 5000;
}