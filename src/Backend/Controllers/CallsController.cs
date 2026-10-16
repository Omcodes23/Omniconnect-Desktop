using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallsController : ControllerBase
{
    private readonly CallService _callService;

    public CallsController(CallService callService) => _callService = callService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("initiate")]
    public async Task<ActionResult<CallDto>> Initiate([FromBody] InitiateCallRequest request)
    {
        var call = await _callService.InitiateAsync(GetUserId(), request);
        return Ok(call);
    }

    [HttpPost("{callId}/accept")]
    [HttpPost("{callId}/answer")]
    public async Task<ActionResult<CallDto>> Accept(Guid callId)
    {
        return Ok(await _callService.UpdateStatusAsync(callId, Shared.Models.CallStatus.Accepted));
    }

    [HttpPost("{callId}/reject")]
    public async Task<ActionResult<CallDto>> Reject(Guid callId)
    {
        return Ok(await _callService.UpdateStatusAsync(callId, Shared.Models.CallStatus.Rejected));
    }

    [HttpPost("{callId}/end")]
    public async Task<ActionResult<CallDto>> End(Guid callId)
    {
        return Ok(await _callService.UpdateStatusAsync(callId, Shared.Models.CallStatus.Ended));
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<CallDto>>> GetHistory()
    {
        return Ok(await _callService.GetCallHistoryAsync(GetUserId()));
    }
}