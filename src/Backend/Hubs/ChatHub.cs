using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;
using System.Security.Claims;

namespace OmniConnect.Backend.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly MessageService _messageService;
    private static readonly Dictionary<string, string> _connections = new();

    public ChatHub(MessageService messageService) => _messageService = messageService;

    private Guid GetUserId() => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => Context.User!.FindFirstValue(ClaimTypes.Name)!;

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId().ToString();
        _connections[Context.ConnectionId] = userId;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        await Clients.Others.SendAsync("UserOnline", userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId().ToString();
        _connections.Remove(Context.ConnectionId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        await Clients.Others.SendAsync("UserOffline", userId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChannel(Guid communityId, Guid channelId)
    {
        var groupName = $"channel_{communityId}_{channelId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveChannel(Guid communityId, Guid channelId)
    {
        var groupName = $"channel_{communityId}_{channelId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task SendMessage(SendMessageRequest request)
    {
        var message = await _messageService.SendMessageAsync(GetUserId(), request);

        if (request.CommunityId.HasValue && request.ChannelId.HasValue)
        {
            var groupName = $"channel_{request.CommunityId}_{request.ChannelId}";
            await Clients.Group(groupName).SendAsync("MessageReceived", message);
        }
        else if (request.ReceiverId.HasValue)
        {
            var userId = GetUserId().ToString();
            var receiverId = request.ReceiverId.Value.ToString();

            await Clients.Group($"user_{receiverId}").SendAsync("MessageReceived", message);
            await Clients.Group($"user_{userId}").SendAsync("MessageReceived", message);
        }

        // Send acknowledgment
        await Clients.Caller.SendAsync("MessageAcknowledged", new MessageAck(
            request.ClientMessageId, message.Id, message.Status, message.SentAt
        ));
    }

    public async Task Typing(Guid? communityId, Guid? channelId, bool isTyping)
    {
        var userId = GetUserId().ToString();

        if (communityId.HasValue && channelId.HasValue)
        {
            var groupName = $"channel_{communityId}_{channelId}";
            await Clients.OthersInGroup(groupName).SendAsync("UserTyping", userId, communityId, channelId, isTyping);
        }
    }

    public async Task UpdatePresence(string status)
    {
        var userId = GetUserId().ToString();
        await Clients.Others.SendAsync("PresenceUpdated", userId, status);
    }

    public async Task SendCallSignal(Guid targetUserId, string signalData)
    {
        var userId = GetUserId().ToString();
        await Clients.Group($"user_{targetUserId}").SendAsync("CallSignalReceived", userId, signalData);
    }

    public static string? GetUserConnectionId(string userId)
    {
        return _connections.FirstOrDefault(x => x.Value == userId).Key;
    }
}
