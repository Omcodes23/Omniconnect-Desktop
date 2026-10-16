using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;

    public AnalyticsController(AnalyticsService analyticsService) => _analyticsService = analyticsService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Track a custom event for analytics.
    /// </summary>
    [HttpPost("track")]
    public async Task<ActionResult> TrackEvent(
        [FromQuery] string eventName,
        [FromBody] object? metadata = null)
    {
        await _analyticsService.TrackEventAsync(
            eventName,
            GetUserId(),
            HttpContext.Request.Headers["X-Session-Id"].FirstOrDefault(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].FirstOrDefault(),
            metadata);

        return NoContent();
    }

    /// <summary>
    /// Get user statistics: total, verified, new registrations.
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult> GetUserStats()
    {
        var result = await _analyticsService.GetUserStatisticsAsync();
        return Ok(result);
    }

    /// <summary>
    /// Get message volume statistics over a specified number of days.
    /// </summary>
    [HttpGet("messages")]
    public async Task<ActionResult> GetMessageVolume([FromQuery] int days = 30)
    {
        var result = await _analyticsService.GetMessageVolumeAsync(days);
        return Ok(result);
    }

    /// <summary>
    /// Get active user counts for various time windows (5min, 1h, 24h, 7d, 30d).
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult> GetActiveUsers()
    {
        var result = await _analyticsService.GetActiveUsersAsync();
        return Ok(result);
    }

    /// <summary>
    /// Get general platform statistics (messages, communities, channels, friendships).
    /// </summary>
    [HttpGet("general")]
    public async Task<ActionResult> GetGeneralStats()
    {
        var result = await _analyticsService.GetGeneralStatsAsync();
        return Ok(result);
    }

    /// <summary>
    /// Query raw analytics events with optional filters.
    /// </summary>
    [HttpGet("events")]
    public async Task<ActionResult> GetEvents(
        [FromQuery] string? eventName = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var result = await _analyticsService.GetEventsAsync(eventName, userId, from, to, skip, take);
        return Ok(result);
    }
}