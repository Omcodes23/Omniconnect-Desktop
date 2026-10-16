using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly FriendService _friendService;

    public FriendsController(FriendService friendService) => _friendService = friendService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("request/{userId}")]
    public async Task<ActionResult<FriendRequestDto>> SendRequest(Guid userId)
    {
        try
        {
            var result = await _friendService.SendRequestAsync(GetUserId(), userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("accept/{friendshipId}")]
    public async Task<ActionResult<FriendRequestDto>> AcceptRequest(Guid friendshipId)
    {
        try
        {
            var result = await _friendService.AcceptRequestAsync(friendshipId, GetUserId());
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("ensure/{userId}")]
    public async Task<ActionResult<FriendRequestDto>> EnsureFriendship(Guid userId)
    {
        try
        {
            var result = await _friendService.EnsureFriendshipAsync(GetUserId(), userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendRequestDto>>> GetFriends()
    {
        var friends = await _friendService.GetFriendsAsync(GetUserId());
        return Ok(friends);
    }

    [HttpGet("pending")]
    public async Task<ActionResult<List<FriendRequestDto>>> GetPendingRequests()
    {
        var requests = await _friendService.GetPendingRequestsAsync(GetUserId());
        return Ok(requests);
    }
}
