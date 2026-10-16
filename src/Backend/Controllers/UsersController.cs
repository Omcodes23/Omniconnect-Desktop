using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db) => _db = db;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMe()
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null) return NotFound();

        return Ok(new UserProfileDto(
            user.Id, user.Username, user.DisplayName, user.Email,
            user.AvatarUrl, user.Bio, ConnectionStatus.Online,
            user.LastSeenAt, user.CreatedAt
        ));
    }

    [HttpPut("me")]
    public async Task<ActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null) return NotFound();

        if (request.DisplayName != null) user.DisplayName = request.DisplayName;
        if (request.Bio != null) user.Bio = request.Bio;
        if (request.AvatarUrl != null) user.AvatarUrl = request.AvatarUrl;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<UserDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<UserDto>());

        var users = await _db.Users
            .Where(u => u.Username.Contains(q) || u.DisplayName.Contains(q))
            .Take(20)
            .Select(u => new UserDto(
                u.Id, u.Username, u.DisplayName, u.AvatarUrl,
                u.Bio, ConnectionStatus.Online, u.LastSeenAt, u.CreatedAt
            ))
            .ToListAsync();

        return Ok(users);
    }
}
