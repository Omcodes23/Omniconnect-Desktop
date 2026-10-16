using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/sync")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;

    public SyncController(SyncService syncService) => _syncService = syncService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Push queued offline operations to the server.
    /// Accepts a batch of operations with version tracking for conflict detection.
    /// </summary>
    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest request)
    {
        var result = await _syncService.PushAsync(GetUserId(), request);
        return Ok(result);
    }

    /// <summary>
    /// Pull synced operations since the last synchronization point.
    /// Used by clients to catch up after going online.
    /// </summary>
    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull(
        [FromQuery] Guid? lastSyncedEntityId,
        [FromQuery] long? lastSyncedTimestamp,
        [FromQuery] int take = 100)
    {
        var request = new SyncPullRequest(lastSyncedEntityId, lastSyncedTimestamp, take);
        var result = await _syncService.PullAsync(GetUserId(), request);
        return Ok(result);
    }

    /// <summary>
    /// Resolve a conflict detected during sync.
    /// Supports strategies: use_client, use_server, merge.
    /// </summary>
    [HttpPost("resolve")]
    public async Task<ActionResult<ConflictResolutionResponse>> ResolveConflict(
        [FromBody] ConflictResolutionRequest request)
    {
        try
        {
            var result = await _syncService.ResolveConflictAsync(GetUserId(), request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get the current sync queue status including pending and total counts.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SyncStatusDto>> GetStatus()
    {
        var result = await _syncService.GetSyncStatusAsync(GetUserId());
        return Ok(result);
    }
}