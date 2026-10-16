using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public NotificationService(AppDbContext db, ILogger<NotificationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── In-App Notifications ─────────────────────────────────────────────

    public async Task<Notification> SendNotificationAsync(
        Guid userId, string type, string title, string body,
        object? data = null, string? imageUrl = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            DataJson = data is not null ? JsonSerializer.Serialize(data, JsonOptions) : null,
            ImageUrl = imageUrl,
        };

        _db.Set<Notification>().Add(notification);
        await _db.SaveChangesAsync();

        // Attempt push notification if enabled
        await TrySendPushNotificationAsync(userId, notification);

        return notification;
    }

    public async Task<List<Notification>> GetNotificationsAsync(
        Guid userId, bool unreadOnly = false, int skip = 0, int take = 50)
    {
        var query = _db.Set<Notification>()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _db.Set<Notification>()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification is null) return false;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId)
    {
        var count = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow));

        return count;
    }

    public async Task<bool> DeleteNotificationAsync(Guid userId, Guid notificationId)
    {
        var notification = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification is null) return false;

        _db.Set<Notification>().Remove(notification);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ClearAllNotificationsAsync(Guid userId)
    {
        await _db.Set<Notification>()
            .Where(n => n.UserId == userId)
            .ExecuteDeleteAsync();
    }

    // ── Notification Preferences ─────────────────────────────────────────

    public async Task<NotificationPreference> GetPreferencesAsync(Guid userId)
    {
        var prefs = await _db.Set<NotificationPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (prefs is null)
        {
            prefs = new NotificationPreference { UserId = userId };
            _db.Set<NotificationPreference>().Add(prefs);
            await _db.SaveChangesAsync();
        }

        return prefs;
    }

    public async Task<NotificationPreference> UpdatePreferencesAsync(
        Guid userId, Action<NotificationPreference> update)
    {
        var prefs = await GetPreferencesAsync(userId);
        update(prefs);
        prefs.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return prefs;
    }

    public async Task UpdateFcmTokenAsync(Guid userId, string fcmToken)
    {
        var prefs = await GetPreferencesAsync(userId);
        prefs.FcmDeviceToken = fcmToken;
        prefs.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ── FCM Push Notification (Firebase Admin SDK stub) ──────────────────

    private async Task TrySendPushNotificationAsync(Guid userId, Notification notification)
    {
        try
        {
            var prefs = await _db.Set<NotificationPreference>()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (prefs is null || !prefs.PushEnabled || string.IsNullOrEmpty(prefs.FcmDeviceToken))
                return;

            // Check quiet hours
            if (!string.IsNullOrEmpty(prefs.QuietHoursStart) && !string.IsNullOrEmpty(prefs.QuietHoursEnd))
            {
                var now = DateTime.UtcNow.TimeOfDay;
                var start = TimeSpan.Parse(prefs.QuietHoursStart);
                var end = TimeSpan.Parse(prefs.QuietHoursEnd);

                if (start <= end)
                {
                    if (now >= start && now <= end) return;
                }
                else
                {
                    // Handles overnight ranges (e.g., 22:00 - 06:00)
                    if (now >= start || now <= end) return;
                }
            }

            // Check individual notification type preferences
            if (!IsNotificationTypeEnabled(prefs, notification.Type))
                return;

            // Firebase Admin SDK stub — in production, use:
            // FirebaseMessaging.DefaultInstance.SendAsync(new Message { ... })
            var fcmMessage = new
            {
                token = prefs.FcmDeviceToken,
                notification = new
                {
                    title = notification.Title,
                    body = notification.Body,
                },
                data = notification.DataJson is not null
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(notification.DataJson, JsonOptions)
                    : new Dictionary<string, string>(),
            };

            // Log the FCM message that would be sent
            _logger.LogInformation(
                "FCM push notification would be sent to device {Token}: {Title}",
                prefs.FcmDeviceToken[..Math.Min(20, prefs.FcmDeviceToken.Length)] + "...",
                notification.Title);

            // Mark as delivered in the notification record
            notification.IsDelivered = true;
            await _db.SaveChangesAsync();

            _logger.LogDebug("Push notification processed for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send push notification to user {UserId}", userId);
            // Don't throw — push notification failure shouldn't block the app
        }
    }

    private static bool IsNotificationTypeEnabled(NotificationPreference prefs, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "message" => prefs.MessagesEnabled,
            "friend_request" => prefs.FriendRequestsEnabled,
            "call" => prefs.CallsEnabled,
            "community_invite" => prefs.CommunityInvitesEnabled,
            "system" or "system_announcement" => prefs.SystemAnnouncementsEnabled,
            _ => true,
        };
    }
}