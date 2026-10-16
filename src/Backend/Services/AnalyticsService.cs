using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class AnalyticsService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnalyticsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AnalyticsService(AppDbContext db, ILogger<AnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Event Tracking ───────────────────────────────────────────────────

    public async Task TrackEventAsync(string eventName, Guid? userId = null, string? sessionId = null,
        string? ipAddress = null, string? userAgent = null, object? metadata = null)
    {
        var analyticsEvent = new AnalyticsEvent
        {
            EventName = eventName,
            UserId = userId,
            SessionId = sessionId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            MetadataJson = metadata is not null
                ? JsonSerializer.Serialize(metadata, JsonOptions)
                : null,
        };

        _db.Set<AnalyticsEvent>().Add(analyticsEvent);
        await _db.SaveChangesAsync();

        _logger.LogDebug("Tracked event: {EventName} for user {UserId}", eventName, userId);
    }

    public async Task TrackEventFromRequestAsync(string eventName, HttpContext httpContext, object? metadata = null)
    {
        var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var userId = userIdClaim is not null ? Guid.Parse(userIdClaim.Value) : (Guid?)null;

        await TrackEventAsync(
            eventName,
            userId,
            httpContext.Request.Headers["X-Session-Id"].FirstOrDefault(),
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers["User-Agent"].FirstOrDefault(),
            metadata
        );
    }

    // ── User Statistics ──────────────────────────────────────────────────

    public async Task<object> GetUserStatisticsAsync()
    {
        var totalUsers = await _db.Users.CountAsync();
        var verifiedUsers = await _db.Users.CountAsync(u => u.IsEmailVerified);
        var bannedUsers = await _db.Users.CountAsync(u => u.IsDeleted);

        // Users registered in last 7 days
        var newUsers7d = await _db.Users
            .CountAsync(u => u.CreatedAt >= DateTime.UtcNow.AddDays(-7));

        // Users registered in last 30 days
        var newUsers30d = await _db.Users
            .CountAsync(u => u.CreatedAt >= DateTime.UtcNow.AddDays(-30));

        return new
        {
            totalUsers,
            verifiedUsers,
            bannedUsers,
            newUsers7d,
            newUsers30d,
            verificationRate = totalUsers > 0 ? Math.Round((double)verifiedUsers / totalUsers * 100, 1) : 0,
        };
    }

    // ── Message Volume ───────────────────────────────────────────────────

    public async Task<object> GetMessageVolumeAsync(int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var totalMessages = await _db.Messages
            .CountAsync(m => m.SentAt >= since);

        var messagesByDay = await _db.Messages
            .Where(m => m.SentAt >= since)
            .GroupBy(m => m.SentAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderByDescending(g => g.date)
            .ToListAsync();

        var avgPerDay = messagesByDay.Count > 0
            ? Math.Round(messagesByDay.Average(g => g.count), 1)
            : 0;

        return new
        {
            totalMessages,
            averagePerDay = avgPerDay,
            daysAnalyzed = days,
            messagesByDay,
        };
    }

    // ── Active Users ─────────────────────────────────────────────────────

    public async Task<object> GetActiveUsersAsync()
    {
        var now = DateTime.UtcNow;

        var active5Min = await _db.Users
            .CountAsync(u => u.LastSeenAt >= now.AddMinutes(-5));

        var active1Hour = await _db.Users
            .CountAsync(u => u.LastSeenAt >= now.AddHours(-1));

        var active24Hours = await _db.Users
            .CountAsync(u => u.LastSeenAt >= now.AddHours(-24));

        var active7Days = await _db.Users
            .CountAsync(u => u.LastSeenAt >= now.AddDays(-7));

        var active30Days = await _db.Users
            .CountAsync(u => u.LastSeenAt >= now.AddDays(-30));

        return new
        {
            active5Minutes = active5Min,
            active1Hour,
            active24Hours,
            active7Days,
            active30Days,
            now,
        };
    }

    // ── General Stats ────────────────────────────────────────────────────

    public async Task<object> GetGeneralStatsAsync()
    {
        var totalMessages = await _db.Messages.CountAsync();
        var totalCommunities = await _db.Communities.CountAsync();
        var totalChannels = await _db.Channels.CountAsync();
        var totalFriendships = await _db.Friendships.CountAsync();

        // Messages in last 24h
        var messages24h = await _db.Messages
            .CountAsync(m => m.SentAt >= DateTime.UtcNow.AddHours(-24));

        return new
        {
            totalMessages,
            totalCommunities,
            totalChannels,
            totalFriendships,
            messagesLast24Hours = messages24h,
        };
    }

    // ── Event Query ──────────────────────────────────────────────────────

    public async Task<List<object>> GetEventsAsync(string? eventName = null, Guid? userId = null,
        DateTime? from = null, DateTime? to = null, int skip = 0, int take = 100)
    {
        var query = _db.Set<AnalyticsEvent>().AsQueryable();

        if (!string.IsNullOrEmpty(eventName))
            query = query.Where(e => e.EventName == eventName);

        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId);

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var events = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return events.Select(e => (object)new
        {
            e.Id,
            e.EventName,
            e.UserId,
            e.SessionId,
            e.IpAddress,
            e.MetadataJson,
            e.Timestamp,
        }).ToList();
    }
}