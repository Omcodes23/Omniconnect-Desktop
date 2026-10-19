using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using OmniConnect.Backend.Data;
using OmniConnect.Backend.Hubs;
using OmniConnect.Shared.Models;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Live state of companion mode (the AI exposed as a chat node).
/// The model is always a local GGUF from <see cref="OmniConnect.Shared.Models.ModelCatalog"/>;
/// when <see cref="ModelId"/> is null the currently selected model is used.
/// </summary>
public interface IAiBotState
{
    bool IsEnabled { get; set; }
    string? ModelId { get; set; }
    string? OwnerDeviceId { get; set; }
    string? OwnerDeviceName { get; set; }
}

public class AiBotState : IAiBotState
{
    public bool IsEnabled { get; set; }
    public string? ModelId { get; set; }
    public string? OwnerDeviceId { get; set; }
    public string? OwnerDeviceName { get; set; }
}

/// <summary>
/// Background service that powers the AI Assistant as a live chat node.
/// Creates a system AI user that can be messaged like any other contact.
/// </summary>
public class AiBotService : BackgroundService
{
    /// <summary>Display name of the AI chat node.</summary>
    public const string DisplayName = "AI Assistant";

    public static readonly Guid AiUserId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private const string AiUsername = "ai_assistant";
    private const string AiDisplayName = DisplayName;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IAiBotState _state;
    private readonly ILogger<AiBotService> _logger;

    // Only the polling task touches these fields.
    private DateTime _cursorSentAt;
    private Guid _cursorId;

    public AiBotService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub> hubContext,
        IAiBotState state,
        ILogger<AiBotService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI Bot Service starting...");

        // Ensure the AI user exists. The database may not be reachable yet at
        // startup (bad host, VPN down, project paused), so retry in the
        // background instead of letting the exception tear down the service.
        if (!await TryEnsureAiUserAsync(stoppingToken))
        {
            _ = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                        if (await TryEnsureAiUserAsync(stoppingToken)) return;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not reach database to create AI user; will retry.");
                    }
                }
            }, stoppingToken);
        }

        // Periodically check for new messages sent to the AI user. The cursor
        // starts at the newest existing message so a restart never re-answers
        // history, then advances chronologically as messages are handled.
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeCursorAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not initialize AI message cursor; starting from now.");
                _cursorSentAt = DateTime.UtcNow;
                _cursorId = Guid.Empty;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessMessagesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI Bot processing error");
                }

                try
                {
                    await Task.Delay(2000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, stoppingToken);

        _logger.LogInformation("AI Bot Service running. AI User ID: {Id}", AiUserId);
    }

    private async Task<bool> TryEnsureAiUserAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aiUser = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Username == AiUsername, stoppingToken);

            if (aiUser == null)
            {
                aiUser = new User
                {
                    Id = AiUserId,
                    Username = AiUsername,
                    Email = "ai@omniconnect.local",
                    DisplayName = AiDisplayName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    IsEmailVerified = true,
                    Bio = "AI Assistant — I run entirely on this device.",
                };
                db.Users.Add(aiUser);
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("AI Assistant user created");
            }

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure AI Assistant user exists.");
            return false;
        }
    }

    private async Task InitializeCursorAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = await db.Messages
            .Where(m => m.ReceiverId == AiUserId)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(stoppingToken);

        if (latest is null)
        {
            _cursorSentAt = DateTime.UtcNow;
            _cursorId = Guid.Empty;
        }
        else
        {
            _cursorSentAt = latest.SentAt;
            _cursorId = latest.Id;
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiService>();

        var afterAt = _cursorSentAt;
        var afterId = _cursorId;

        // Find messages addressed to the AI user that are newer than the cursor.
        // SentAt is the primary key of the ordering; Id only breaks exact ties.
        var messages = await db.Messages
            .Where(m => m.ReceiverId == AiUserId &&
                (m.SentAt > afterAt || (m.SentAt == afterAt && m.Id.CompareTo(afterId) > 0)))
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Take(5)
            .ToListAsync(stoppingToken);

        foreach (var msg in messages)
        {
            try
            {
                if (!_state.IsEnabled)
                {
                    // Send auto-reply that AI is disabled
                    var reply = new Message
                    {
                        SenderId = AiUserId,
                        ReceiverId = msg.SenderId,
                        Content = "🤖 AI Assistant is currently disabled. The owner can enable it in AI Settings.",
                        Type = MessageType.Text,
                        SentAt = DateTime.UtcNow,
                        Status = MessageStatus.Sent,
                    };
                    db.Messages.Add(reply);
                    await db.SaveChangesAsync(stoppingToken);

                    await _hubContext.Clients.Group($"user_{msg.SenderId}")
                        .SendAsync("MessageReceived", MapToDto(reply, reply.SenderId), stoppingToken);
                    continue;
                }

                // Generate AI response
                var request = new Shared.DTOs.AiChatRequest(
                    _state.ModelId,
                    msg.Content,
                    null,
                    null
                );
                var result = await aiService.ChatAsync(AiUserId, request);

                var aiReply = new Message
                {
                    SenderId = AiUserId,
                    ReceiverId = msg.SenderId,
                    Content = result.Reply,
                    Type = MessageType.Text,
                    SentAt = DateTime.UtcNow,
                    Status = MessageStatus.Sent,
                    ReplyToId = msg.Id,
                };
                db.Messages.Add(aiReply);
                await db.SaveChangesAsync(stoppingToken);

                // Send real-time notification
                await _hubContext.Clients.Group($"user_{msg.SenderId}")
                    .SendAsync("MessageReceived", MapToDto(aiReply, aiReply.SenderId), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI response generation failed");
                try
                {
                    var errorReply = new Message
                    {
                        SenderId = AiUserId,
                        ReceiverId = msg.SenderId,
                        Content = $"⚠️ Sorry, I encountered an error: {ex.Message}",
                        Type = MessageType.Text,
                        SentAt = DateTime.UtcNow,
                        Status = MessageStatus.Failed,
                    };
                    db.Messages.Add(errorReply);
                    await db.SaveChangesAsync(stoppingToken);

                    await _hubContext.Clients.Group($"user_{msg.SenderId}")
                        .SendAsync("MessageReceived", MapToDto(errorReply, errorReply.SenderId), stoppingToken);
                }
                catch (Exception notifyEx)
                {
                    _logger.LogError(notifyEx, "Could not send AI error reply");
                }
            }
            finally
            {
                // Advance exactly once per handled message, whatever the outcome.
                _cursorSentAt = msg.SentAt;
                _cursorId = msg.Id;
            }
        }
    }

    private static Shared.DTOs.MessageDto MapToDto(Message m, Guid senderId) => new(
        m.Id, senderId, DisplayName, null,
        m.ReceiverId, null, null,
        (Shared.Enums.MessageType)m.Type, m.Content,
        null, null, null, (Shared.Enums.MessageStatus)m.Status,
        m.ReplyToId, m.SentAt, m.EditedAt
    );
}
