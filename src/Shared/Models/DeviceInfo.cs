using System.ComponentModel.DataAnnotations.Schema;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.Models;

public class DeviceInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    [NotMapped] public string? UserName { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string PlatformVersion { get; set; } = string.Empty;
    public DeviceRole Roles { get; set; } = DeviceRole.Client;
    [NotMapped] public HashSet<string> AvailableTransports { get; set; } = new();
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    [NotMapped] public ConnectionStatus Status { get; set; } = ConnectionStatus.Offline;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}