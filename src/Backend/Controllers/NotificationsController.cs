using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> GetNotifications()
    {
        var notifications = await _db.Notifications
            .Where(n => n.UserId == GetUserId())
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.IsRead, n.CreatedAt))
            .ToListAsync();
        return Ok(notifications);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var count = await _db.Notifications
            .CountAsync(n => n.UserId == GetUserId() && !n.IsRead);
        return Ok(count);
    }

    [HttpPost("{id}/read")]
    public async Task<ActionResult> MarkAsRead(Guid id)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == GetUserId());
        if (notification == null) return NotFound();
        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllAsRead()
    {
        await _db.Notifications
            .Where(n => n.UserId == GetUserId() && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return NoContent();
    }
}
