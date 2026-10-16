using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Backend.Hubs;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DeviceDiscoveryService _discoveryService;
    private readonly IHubContext<ChatHub> _hubContext;

    public DevicesController(
        AppDbContext db,
        DeviceDiscoveryService discoveryService,
        IHubContext<ChatHub> hubContext)
    {
        _db = db;
        _discoveryService = discoveryService;
        _hubContext = hubContext;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register([FromBody] DeviceDiscoveryDto dto)
    {
        // Resolve the user: prefer the DTO, else the auth token if present
        Guid? userId = dto.UserId ?? GetUserId();

        var clientIp = dto.IpAddress;
        if (string.IsNullOrWhiteSpace(clientIp) || clientIp == "127.0.0.1" || clientIp == "localhost")
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(remoteIp) && remoteIp != "::1")
            {
                clientIp = remoteIp.Replace("::ffff:", "");
            }
        }

        var platform = dto.DeviceModel?.Contains("Android") == true
            ? "Android"
            : (dto.DeviceModel?.Contains("iOS") == true ? "iOS" : "Desktop");

        var existing = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == dto.DeviceId);
        if (existing != null)
        {
            existing.DeviceName = dto.DeviceName;
            existing.DeviceModel = dto.DeviceModel;
            existing.Platform = platform;
            existing.IpAddress = clientIp ?? existing.IpAddress;
            existing.Port = dto.Port > 0 ? dto.Port : existing.Port;
            existing.UserId = userId ?? existing.UserId;
            existing.LastSeen = DateTime.UtcNow;
        }
        else
        {
            _db.Devices.Add(new DeviceInfo
            {
                DeviceId = dto.DeviceId,
                DeviceName = dto.DeviceName,
                DeviceModel = dto.DeviceModel,
                Platform = platform,
                IpAddress = clientIp,
                Port = dto.Port,
                UserId = userId,
                LastSeen = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();

        // Broadcast device presence to SignalR clients
        try
        {
            string? userName = dto.UserName;
            if (string.IsNullOrEmpty(userName) && userId.HasValue)
            {
                userName = await _db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync();
            }

            var broadcastDto = new DeviceDiscoveryDto(
                dto.DeviceId,
                dto.DeviceName,
                dto.DeviceModel ?? "",
                dto.Roles,
                dto.AvailableTransports?.Count > 0 ? dto.AvailableTransports : new HashSet<string> { "Internet", "LAN" },
                clientIp,
                dto.Port,
                userId,
                userName
            );

            await _hubContext.Clients.All.SendAsync("DeviceDiscovered", broadcastDto);
        }
        catch
        {
            // Logging or graceful fallback
        }

        return Ok(new { status = "registered", userId, ipAddress = clientIp });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<DeviceDiscoveryDto>>> GetDevices()
    {
        // 1. Get recent devices from Database (online in last 10 minutes)
        var dbDevices = await _db.Devices
            .Where(d => d.LastSeen > DateTime.UtcNow.AddMinutes(-10))
            .ToListAsync();

        var userIds = dbDevices.Where(d => d.UserId != null).Select(d => d.UserId!.Value).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var resultDict = new Dictionary<string, DeviceDiscoveryDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in dbDevices)
        {
            string? userName = null;
            if (d.UserId.HasValue && userNames.TryGetValue(d.UserId.Value, out var un))
            {
                userName = un;
            }

            resultDict[d.DeviceId] = new DeviceDiscoveryDto(
                d.DeviceId,
                d.DeviceName,
                d.DeviceModel ?? "",
                d.Roles,
                new HashSet<string> { "Internet", "LAN" },
                d.IpAddress,
                d.Port,
                d.UserId,
                userName
            );
        }

        // 2. Merge with live UDP-discovered devices in memory from DeviceDiscoveryService
        var udpDiscovered = _discoveryService.GetDiscoveredDevices();
        foreach (var dev in udpDiscovered)
        {
            var transports = dev.AvailableTransports?.Count > 0
                ? dev.AvailableTransports
                : new HashSet<string> { "LAN", "WiFiDirect" };

            resultDict[dev.DeviceId] = new DeviceDiscoveryDto(
                dev.DeviceId,
                dev.DeviceName,
                dev.DeviceModel ?? "",
                dev.Roles,
                transports,
                dev.IpAddress,
                dev.Port,
                dev.UserId,
                dev.UserName
            );
        }

        return Ok(new
        {
            devices = resultDict.Values.ToList(),
            // The server node itself. Clients hide this row so a user never
            // sees the machine they are currently signed in on.
            selfDeviceId = _discoveryService.SelfDeviceId,
        });
    }

    [HttpGet("my-info")]
    [AllowAnonymous]
    public ActionResult<object> GetMyInfo()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString()?.Replace("::ffff:", "") ?? "unknown";
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        return Ok(new
        {
            IpAddress = ip,
            UserAgent = userAgent,
            DeviceName = Environment.MachineName,
            Platform = Environment.OSVersion.ToString(),
            PlatformVersion = Environment.OSVersion.VersionString,
            Timestamp = DateTime.UtcNow,
        });
    }
}
