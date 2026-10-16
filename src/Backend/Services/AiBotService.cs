using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using OmniConnect.Backend.Data;
using OmniConnect.Backend.Hubs;
using OmniConnect.Shared.Models;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Backend.Services;

public interface IAiBotState
{
    bool IsEnabled { get; set; }
    string Provider { get; set; }
    string Model { get; set; }
}

public class AiBotState : IAiBotState
{
    public bool IsEnabled { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// Background service that powers the AI Assistant as a live chat node.
/// Creates a system AI user that can be messaged like any other contact.
/// </summary>
public class AiBotService : BackgroundService
{
    private static readonly Guid AiUserId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private const string AiUsername = "ai_assistant";
    private const string AiDisplayName = "AI Assistant";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IAiBotState _state;
    private readonly ILogger<AiBotService> _logger;

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

        // Periodically check for new messages sent to the AI user
        _ = Task.Run(async () =>
        {
            Guid lastProcessedId = Guid.Empty;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessMessagesAsync(lastProcessedId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI Bot processing error");
                }
                await Task.Delay(2000, stoppingToken);
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
                    Bio = "AI Assistant — I can help with anything! Powered by " + _state.Provider,
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

    private async Task ProcessMessagesAsync(Guid lastProcessedId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiService>();

        // Find unprocessed messages addressed to the AI user
        var messages = await db.Messages
            .Where(m => m.ReceiverId == AiUserId && m.Id.CompareTo(lastProcessedId) > 0)
            .OrderBy(m => m.SentAt)
            .Take(5)
            .ToListAsync();

        foreach (var msg in messages)
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
                lastProcessedId = msg.Id;
                await db.SaveChangesAsync();

                await _hubContext.Clients.Group($"user_{msg.SenderId}")
                    .SendAsync("MessageReceived", MapToDto(reply, reply.SenderId));
                continue;
            }

            try
            {
                // Generate AI response
                var request = new Shared.DTOs.AiChatRequest(
                    _state.Provider,
                    _state.Model,
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
                lastProcessedId = msg.Id;
                await db.SaveChangesAsync();

                // Send real-time notification
                await _hubContext.Clients.Group($"user_{msg.SenderId}")
                    .SendAsync("MessageReceived", MapToDto(aiReply, aiReply.SenderId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI response generation failed");
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
                lastProcessedId = msg.Id;
                await db.SaveChangesAsync();

                await _hubContext.Clients.Group($"user_{msg.SenderId}")
                    .SendAsync("MessageReceived", MapToDto(errorReply, errorReply.SenderId));
            }
        }
    }

    private static Shared.DTOs.MessageDto MapToDto(Message m, Guid senderId) => new(
        m.Id, senderId, "AI Assistant", null,
        m.ReceiverId, null, null,
        (Shared.Enums.MessageType)m.Type, m.Content,
        null, null, null, (Shared.Enums.MessageStatus)m.Status,
        m.ReplyToId, m.SentAt, m.EditedAt
    );
}
