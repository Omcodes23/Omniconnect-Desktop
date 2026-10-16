using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AdminService _adminService;

    public AdminController(AdminService adminService) => _adminService = adminService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Get system health status including uptime, memory, active connections, and database status.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<SystemHealthDto>> GetHealth()
    {
        var result = await _adminService.GetSystemHealthAsync();
        return Ok(result);
    }

    /// <summary>
    /// Get database statistics including total users, messages, communities, and database size.
    /// </summary>
    [HttpGet("database-stats")]
    public async Task<ActionResult<DatabaseStatsDto>> GetDatabaseStats()
    {
        var result = await _adminService.GetDatabaseStatsAsync();
        return Ok(result);
    }

    /// <summary>
    /// List all users with optional search, sorting, and pagination.
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult<AdminUserListResponse>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool? ascending = null)
    {
        var request = new AdminUserListRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            Ascending = ascending,
        };

        var result = await _adminService.GetUsersAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Perform an administrative action on a user (ban, unban, verify_email, reset_password, delete).
    /// </summary>
    [HttpPost("users/action")]
    public async Task<ActionResult<AdminUserActionResponse>> UserAction([FromBody] AdminUserActionRequest request)
    {
        var result = await _adminService.PerformUserActionAsync(request);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// Moderate content: flag, remove, or approve.
    /// </summary>
    [HttpPost("moderate")]
    public async Task<ActionResult<ModerateContentResponse>> Moderate([FromBody] ModerateContentRequest request)
    {
        var result = await _adminService.ModerateContentAsync(request);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// Get system configuration settings (read-only).
    /// </summary>
    [HttpGet("config")]
    public async Task<ActionResult<List<SystemConfigDto>>> GetConfig()
    {
        var result = await _adminService.GetSystemConfigAsync();
        return Ok(result);
    }

    /// <summary>
    /// Update a system configuration value.
    /// </summary>
    [HttpPut("config")]
    public async Task<ActionResult<SystemConfigDto>> UpdateConfig([FromBody] UpdateSystemConfigRequest request)
    {
        var result = await _adminService.UpdateSystemConfigAsync(request);
        return Ok(result);
    }
}