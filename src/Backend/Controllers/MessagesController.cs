using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OmniConnect.Backend.Hubs;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly MessageService _messageService;
    private readonly IHubContext<ChatHub> _hubContext;

    public MessagesController(MessageService messageService, IHubContext<ChatHub> hubContext)
    {
        _messageService = messageService;
        _hubContext = hubContext;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<MessageDto>> SendMessage([FromBody] SendMessageRequest request)
    {
        var userId = GetUserId();
        var message = await _messageService.SendMessageAsync(userId, request);

        if (request.CommunityId.HasValue && request.ChannelId.HasValue)
        {
            var groupName = $"channel_{request.CommunityId}_{request.ChannelId}";
            await _hubContext.Clients.Group(groupName).SendAsync("MessageReceived", message);
        }
        else if (request.ReceiverId.HasValue)
        {
            var receiverId = request.ReceiverId.Value.ToString();
            await _hubContext.Clients.Group($"user_{receiverId}").SendAsync("MessageReceived", message);
            await _hubContext.Clients.Group($"user_{userId}").SendAsync("MessageReceived", message);
        }

        return Ok(message);
    }

    [HttpGet("channel/{communityId}/{channelId}")]
    public async Task<ActionResult<List<MessageDto>>> GetChannelMessages(
        Guid communityId, Guid channelId,
        [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var messages = await _messageService.GetMessagesAsync(communityId, channelId, skip, take);
        return Ok(messages);
    }

    [HttpGet("conversation/{userId}")]
    public async Task<ActionResult<List<MessageDto>>> GetConversation(
        Guid userId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var messages = await _messageService.GetConversationAsync(GetUserId(), userId, skip, take);
        return Ok(messages);
    }
}
