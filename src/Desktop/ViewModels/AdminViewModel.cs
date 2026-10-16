using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

public partial class AdminViewModel : ViewModelBase
{
    [ObservableProperty] private string _systemStatus = "Healthy";
    [ObservableProperty] private double _uptimeHours;
    [ObservableProperty] private long _memoryUsageMb;
    [ObservableProperty] private int _activeConnections;
    [ObservableProperty] private int _activeUsers5Min;
    [ObservableProperty] private double _cpuUsagePercent;
    [ObservableProperty] private string _databaseStatus = "Connected";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private string _userSearchQuery = string.Empty;
    [ObservableProperty] private AdminUserItem? _selectedUser;

    public ObservableCollection<AdminUserItem> Users { get; } = new();
    public ObservableCollection<DatabaseStatItem> DatabaseStats { get; } = new();

    public AdminViewModel()
    {
        // Pre-populate with sample data for display
        LoadSampleData();
    }

    private void LoadSampleData()
    {
        DatabaseStats.Add(new DatabaseStatItem { Label = "Total Users", Value = "1,234" });
        DatabaseStats.Add(new DatabaseStatItem { Label = "Total Messages", Value = "45,678" });
        DatabaseStats.Add(new DatabaseStatItem { Label = "Total Communities", Value = "89" });
        DatabaseStats.Add(new DatabaseStatItem { Label = "Total Files", Value = "2,345" });
        DatabaseStats.Add(new DatabaseStatItem { Label = "Database Size", Value = "156 MB" });
    }

    [RelayCommand]
    private void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
    }

    [RelayCommand]
    private async Task RefreshSystemHealthAsync()
    {
        // Simulate loading system health data
        await Task.Delay(500);
        SystemStatus = "Healthy";
        UptimeHours = 72.5;
        MemoryUsageMb = 256;
        ActiveConnections = 42;
        ActiveUsers5Min = 18;
        CpuUsagePercent = 23.4;
        DatabaseStatus = "Connected";
    }

    [RelayCommand]
    private async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(UserSearchQuery)) return;

        await Task.Delay(300);
        // In production, this would call ApiService to search users
        Users.Clear();
        Users.Add(new AdminUserItem
        {
            Id = Guid.NewGuid(),
            Username = "johndoe",
            Email = "john@example.com",
            DisplayName = "John Doe",
            Role = "User",
            IsEmailVerified = true,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastSeenAt = DateTime.UtcNow.AddHours(-1),
            MessageCount = 142
        });
        Users.Add(new AdminUserItem
        {
            Id = Guid.NewGuid(),
            Username = "janesmith",
            Email = "jane@example.com",
            DisplayName = "Jane Smith",
            Role = "Admin",
            IsEmailVerified = true,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            LastSeenAt = DateTime.UtcNow.AddMinutes(-5),
            MessageCount = 891
        });
    }

    [RelayCommand]
    private async Task BanUserAsync(AdminUserItem? user)
    {
        if (user == null) return;
        await Task.Delay(200);
        user.IsBanned = true;
    }

    [RelayCommand]
    private async Task UnbanUserAsync(AdminUserItem? user)
    {
        if (user == null) return;
        await Task.Delay(200);
        user.IsBanned = false;
    }
}

public class AdminUserItem
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public bool IsEmailVerified { get; set; }
    public bool IsBanned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public long MessageCount { get; set; }

    public string StatusBadge => IsBanned ? "Banned" : IsEmailVerified ? "Verified" : "Pending";
    public string StatusColor => IsBanned ? "#ff6b6b" : IsEmailVerified ? "#51cf66" : "#ffd43b";
}

public class DatabaseStatItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}