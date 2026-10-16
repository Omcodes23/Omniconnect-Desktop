using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class AdminService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminService> _logger;

    public AdminService(AppDbContext db, ILogger<AdminService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── System Health ────────────────────────────────────────────────────

    public async Task<SystemHealthDto> GetSystemHealthAsync()
    {
        var process = Process.GetCurrentProcess();
        var uptime = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalHours;

        var activeConnections = await GetActiveConnectionCountAsync();

        var activeUsers5Min = await _db.Users
            .CountAsync(u => u.LastSeenAt >= DateTime.UtcNow.AddMinutes(-5));

        var memoryMb = (long)Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), MidpointRounding.AwayFromZero);

        string dbStatus;
        try
        {
            await _db.Database.CanConnectAsync();
            dbStatus = "healthy";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            dbStatus = "unhealthy";
        }

        return new SystemHealthDto(
            "healthy",
            Math.Round(uptime, 1),
            (long)memoryMb,
            activeConnections,
            activeUsers5Min,
            Math.Round(process.TotalProcessorTime.TotalSeconds / process.UserProcessorTime.TotalSeconds * 100, 1),
            dbStatus,
            DateTime.UtcNow
        );
    }

    public async Task<DatabaseStatsDto> GetDatabaseStatsAsync()
    {
        var totalUsers = await _db.Users.IgnoreQueryFilters().CountAsync();
        var totalMessages = await _db.Messages.IgnoreQueryFilters().CountAsync();
        var totalCommunities = await _db.Communities.IgnoreQueryFilters().CountAsync();
        var totalFiles = 0L; // File count from FileService stats

        // Estimate database size from PostgreSQL
        long dbSizeMb = 0;
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT pg_database_size(current_database()) / (1024 * 1024)::bigint AS size_mb";
            var result = await cmd.ExecuteScalarAsync();
            if (result is not null)
                dbSizeMb = Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get database size");
        }

        return new DatabaseStatsDto(
            totalUsers, totalMessages, totalCommunities,
            totalFiles, dbSizeMb, DateTime.UtcNow
        );
    }

    // ── User Management ──────────────────────────────────────────────────

    public async Task<AdminUserListResponse> GetUsersAsync(AdminUserListRequest request)
    {
        var query = _db.Users.IgnoreQueryFilters().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search) ||
                u.DisplayName.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();

        query = (request.SortBy?.ToLower()) switch
        {
            "username" => request.Ascending == true
                ? query.OrderBy(u => u.Username)
                : query.OrderByDescending(u => u.Username),
            "email" => request.Ascending == true
                ? query.OrderBy(u => u.Email)
                : query.OrderByDescending(u => u.Email),
            "createdat" => request.Ascending == true
                ? query.OrderBy(u => u.CreatedAt)
                : query.OrderByDescending(u => u.CreatedAt),
            "lastseenat" => request.Ascending == true
                ? query.OrderBy(u => u.LastSeenAt)
                : query.OrderByDescending(u => u.LastSeenAt),
            _ => query.OrderByDescending(u => u.CreatedAt),
        };

        var users = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        var userDtos = users.Select(u => new AdminUserDto(
            u.Id, u.Username, u.Email, u.DisplayName,
            "user", u.IsEmailVerified, false, u.IsDeleted,
            u.CreatedAt, u.LastSeenAt, 0
        )).ToList();

        return new AdminUserListResponse(userDtos, totalCount, request.Page, request.PageSize);
    }

    public async Task<AdminUserActionResponse> PerformUserActionAsync(AdminUserActionRequest request)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId);

        if (user is null)
            return new AdminUserActionResponse(false, "User not found.");

        switch (request.Action.ToLower())
        {
            case "ban":
                user.IsDeleted = true;
                await _db.SaveChangesAsync();
                _logger.LogWarning("User {UserId} banned by admin. Reason: {Reason}", user.Id, request.Reason);
                return new AdminUserActionResponse(true, "User has been banned.");

            case "unban":
                user.IsDeleted = false;
                await _db.SaveChangesAsync();
                return new AdminUserActionResponse(true, "User has been unbanned.");

            case "verify_email":
                user.IsEmailVerified = true;
                await _db.SaveChangesAsync();
                return new AdminUserActionResponse(true, "Email verified.");

            case "reset_password":
                var tempPassword = Guid.NewGuid().ToString("N")[..12];
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
                await _db.SaveChangesAsync();
                return new AdminUserActionResponse(true, $"Password has been reset. Temporary password: {tempPassword}");

            case "delete":
                _db.Users.Remove(user);
                await _db.SaveChangesAsync();
                _logger.LogWarning("User {UserId} permanently deleted by admin.", user.Id);
                return new AdminUserActionResponse(true, "User has been permanently deleted.");

            default:
                return new AdminUserActionResponse(false, $"Unknown action: {request.Action}");
        }
    }

    // ── Content Moderation ───────────────────────────────────────────────

    public async Task<ModerateContentResponse> ModerateContentAsync(ModerateContentRequest request)
    {
        switch (request.Action.ToLower())
        {
            case "flag":
                // Log the flagged content for review
                _logger.LogWarning("Content flagged for moderation: {Content}", request.Content);
                return new ModerateContentResponse(true, "flagged", "Content has been flagged for review.");

            case "remove":
                if (request.MessageId.HasValue)
                {
                    var message = await _db.Messages.FindAsync(request.MessageId.Value);
                    if (message is not null)
                    {
                        message.DeletedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                        return new ModerateContentResponse(true, "removed", "Message has been removed.");
                    }
                    return new ModerateContentResponse(false, "not_found", "Message not found.");
                }
                return new ModerateContentResponse(false, "no_message_id", "MessageId is required for removal.");

            case "approve":
                return new ModerateContentResponse(true, "approved", "Content has been approved.");

            default:
                return new ModerateContentResponse(false, "unknown_action", $"Unknown action: {request.Action}");
        }
    }

    // ── System Configuration ─────────────────────────────────────────────

    public async Task<List<SystemConfigDto>> GetSystemConfigAsync()
    {
        // Return read-only configuration values
        var configs = new List<SystemConfigDto>
        {
            new("max_upload_size_mb", "100", "Maximum file upload size in megabytes", DateTime.UtcNow),
            new("max_message_length", "10000", "Maximum character length for messages", DateTime.UtcNow),
            new("rate_limit_per_minute", "60", "API rate limit per minute", DateTime.UtcNow),
            new("session_timeout_minutes", "60", "Session timeout in minutes", DateTime.UtcNow),
        };

        return await Task.FromResult(configs);
    }

    public async Task<SystemConfigDto?> UpdateSystemConfigAsync(UpdateSystemConfigRequest request)
    {
        // In a production system, this would persist to a configuration store
        return await Task.FromResult(new SystemConfigDto(
            request.Key, request.Value, "Updated by admin", DateTime.UtcNow
        ));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<int> GetActiveConnectionCountAsync()
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active'";
            var result = await cmd.ExecuteScalarAsync();
            return result is not null ? Convert.ToInt32(result) : 0;
        }
        catch
        {
            return 0;
        }
    }
}