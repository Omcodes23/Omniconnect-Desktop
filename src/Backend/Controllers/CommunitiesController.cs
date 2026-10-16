using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CommunitiesController : ControllerBase
{
    private readonly CommunityService _communityService;

    public CommunitiesController(CommunityService communityService) => _communityService = communityService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name)!;

    [HttpPost]
    public async Task<ActionResult<CommunityDto>> Create([FromBody] CreateCommunityRequest request)
    {
        var community = await _communityService.CreateAsync(request, GetUserId(), GetUsername());
        return Ok(community);
    }

    [HttpGet]
    public async Task<ActionResult<List<CommunityDto>>> GetMyCommunities()
    {
        var communities = await _communityService.GetMyCommunitiesAsync(GetUserId());
        return Ok(communities);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CommunityDto>> GetById(Guid id)
    {
        var community = await _communityService.GetByIdAsync(id);
        if (community == null) return NotFound();
        return Ok(community);
    }

    [HttpPost("{communityId}/channels")]
    public async Task<ActionResult<ChannelDto>> CreateChannel(Guid communityId, [FromBody] CreateChannelRequest request)
    {
        var channel = await _communityService.CreateChannelAsync(communityId, request);
        return Ok(channel);
    }

    [HttpGet("{communityId}/channels")]
    public async Task<ActionResult<List<ChannelDto>>> GetChannels(Guid communityId)
    {
        var channels = await _communityService.GetChannelsAsync(communityId);
        return Ok(channels);
    }
}
