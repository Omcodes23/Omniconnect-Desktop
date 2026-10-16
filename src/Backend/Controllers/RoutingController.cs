using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

/// <summary>
/// Exposes the RoutingEngine to clients so they can see the best transport
/// (Internet / LAN / WiFi Direct / Bluetooth) to reach any discovered device,
/// selected by the lowest composite score (latency, bandwidth, availability).
/// </summary>
[ApiController]
[Route("api/routing")]
[Authorize]
public class RoutingController : ControllerBase
{
    private readonly RoutingService _routingService;

    public RoutingController(RoutingService routingService) => _routingService = routingService;

    /// <summary>All registered device routes with their best transport.</summary>
    [HttpGet("routes")]
    public async Task<ActionResult<List<RouteInfoDto>>> GetRoutes()
    {
        var routes = await _routingService.GetRoutesAsync();
        return Ok(routes);
    }

    /// <summary>Best route (lowest composite score) to a specific device.</summary>
    [HttpGet("best/{deviceId}")]
    public async Task<ActionResult<RouteInfoDto?>> GetBestRoute(string deviceId)
    {
        var route = await _routingService.GetDeviceRouteAsync(deviceId);
        if (route == null) return NotFound(new { error = "Device not found in routing table" });
        return Ok(route);
    }

    /// <summary>Evaluate a route to a target with explicit criteria.</summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluateRouteResult>> Evaluate([FromBody] EvaluateRouteRequest request)
    {
        var result = await _routingService.EvaluateRouteAsync(request);
        return Ok(result);
    }
}
