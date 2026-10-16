using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Desktop.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    private readonly ApiService _api;
    private UserProfileDto? _profile;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _bio = string.Empty;
    [ObservableProperty] private string _avatarUrl = string.Empty;
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _showPasswordForm;
    [ObservableProperty] private string _oldPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isStatusError;

    // ── Device Info Properties ────────────────────────────────────────────
    [ObservableProperty] private string _deviceName = string.Empty;
    [ObservableProperty] private string _deviceModel = string.Empty;
    [ObservableProperty] private string _devicePlatform = string.Empty;
    [ObservableProperty] private string _deviceIpAddress = string.Empty;
    [ObservableProperty] private bool _hasDeviceInfo;

    // Delegate set by the view for native file picker access
    public Func<Task<byte[]?>>? PickAvatarFileAsync { get; set; }

    public ProfileViewModel(ApiService api)
    {
        _api = api;
    }

    public async Task LoadProfileAsync()
    {
        try
        {
            _profile = await _api.GetMyProfileAsync();
            DisplayName = _profile.DisplayName;
            Username = _profile.Username;
            Email = _profile.Email;
            Bio = _profile.Bio ?? string.Empty;
            AvatarUrl = _profile.AvatarUrl ?? string.Empty;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load profile: {ex.Message}";
            IsStatusError = true;
        }

        // Also load device info
        await LoadDeviceInfoAsync();
    }

    private async Task LoadDeviceInfoAsync()
    {
        try
        {
            var deviceInfo = await _api.GetMyDeviceInfoAsync();
            if (deviceInfo != null)
            {
                DeviceName = deviceInfo.DeviceName;
                DeviceModel = deviceInfo.DeviceModel;
                DevicePlatform = deviceInfo.Platform;
                DeviceIpAddress = deviceInfo.IpAddress ?? "Unknown";
                HasDeviceInfo = true;
            }
        }
        catch
        {
            HasDeviceInfo = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsDialogOpen = false;
    }

    [RelayCommand]
    private void TogglePasswordForm()
    {
        ShowPasswordForm = !ShowPasswordForm;
        StatusMessage = string.Empty;
        IsStatusError = false;
    }

    [RelayCommand]
    private async Task EditAvatarAsync()
    {
        if (PickAvatarFileAsync == null) return;

        IsSaving = true;
        StatusMessage = "Uploading avatar...";
        IsStatusError = false;

        try
        {
            var imageData = await PickAvatarFileAsync();
            if (imageData == null)
            {
                IsSaving = false;
                return;
            }

            // Upload avatar to get a URL
            using var stream = new MemoryStream(imageData);
            var result = await _api.UploadFileAsync("avatar.png", stream, "image/png");

            // Update profile with new avatar URL
            _profile = await _api.UpdateProfileAsync(DisplayName, Bio,
                result.PublicUrl ?? result.StoragePath);
            AvatarUrl = _profile.AvatarUrl ?? string.Empty;
            StatusMessage = "Avatar updated successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to update avatar: {ex.Message}";
            IsStatusError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        IsSaving = true;
        StatusMessage = string.Empty;
        IsStatusError = false;

        try
        {
            _profile = await _api.UpdateProfileAsync(DisplayName, Bio,
                string.IsNullOrEmpty(AvatarUrl) ? null : AvatarUrl);
            StatusMessage = "Profile updated successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to update profile: {ex.Message}";
            IsStatusError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(OldPassword) ||
            string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = "Please fill in all password fields.";
            IsStatusError = true;
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            StatusMessage = "New passwords do not match.";
            IsStatusError = true;
            return;
        }

        if (NewPassword.Length < 6)
        {
            StatusMessage = "Password must be at least 6 characters.";
            IsStatusError = true;
            return;
        }

        IsSaving = true;
        StatusMessage = string.Empty;
        IsStatusError = false;

        try
        {
            await _api.ChangePasswordAsync(OldPassword, NewPassword);
            StatusMessage = "Password changed successfully!";
            OldPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            ShowPasswordForm = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to change password: {ex.Message}";
            IsStatusError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
