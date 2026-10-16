using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Provides routing-related operations for the API layer, delegating to the
/// <see cref="RoutingEngine"/> singleton for route evaluation and transport selection.
/// </summary>
public class RoutingService
{
    private readonly RoutingEngine _routingEngine;
    private readonly ILogger<RoutingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingService"/>.
    /// </summary>
    public RoutingService(RoutingEngine routingEngine, ILogger<RoutingService> logger)
    {
        _routingEngine = routingEngine;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current routing table as a list of route info DTOs.
    /// </summary>
    public Task<List<RouteInfoDto>> GetRoutesAsync()
    {
        return Task.FromResult(_routingEngine.GetAllRouteInfos());
    }

    /// <summary>
    /// Evaluates the best route to a target device based on the given criteria.
    /// </summary>
    public Task<EvaluateRouteResult> EvaluateRouteAsync(EvaluateRouteRequest request)
    {
        var result = _routingEngine.EvaluateRoute(request);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Gets route information for a specific device.
    /// </summary>
    public Task<RouteInfoDto?> GetDeviceRouteAsync(string deviceId)
    {
        var entry = _routingEngine.GetRoute(deviceId);
        if (entry == null)
            return Task.FromResult<RouteInfoDto?>(null);

        var allRoutes = _routingEngine.GetAllRouteInfos();
        var route = allRoutes.FirstOrDefault(r => r.DeviceId == deviceId);
        return Task.FromResult(route);
    }

    /// <summary>
    /// Registers a device with the routing engine for route evaluation.
    /// </summary>
    public Task RegisterDeviceAsync(string deviceId, string deviceName, string roles, List<string> transports, string? ipAddress, int port)
    {
        if (!Enum.TryParse<Shared.Enums.DeviceRole>(roles, ignoreCase: true, out var deviceRole))
            deviceRole = Shared.Enums.DeviceRole.Client;

        _routingEngine.RegisterDevice(
            deviceId, deviceName, deviceRole,
            new HashSet<string>(transports), ipAddress, port);

        _logger.LogInformation("Device {DeviceName} ({DeviceId}) registered with routing engine", deviceName, deviceId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a device from the routing table.
    /// </summary>
    public Task<bool> RemoveDeviceAsync(string deviceId)
    {
        var removed = _routingEngine.RemoveDevice(deviceId);
        return Task.FromResult(removed);
    }
}