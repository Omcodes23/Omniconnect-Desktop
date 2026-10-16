using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Desktop.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly ApiService _api;

    public LoginViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRegisterMode;

    public event Action<AuthResponse>? LoginSucceeded;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            AuthResponse response;
            if (IsRegisterMode)
            {
                response = await _api.RegisterAsync(Username, Email, Password, DisplayName);
            }
            else
            {
                response = await _api.LoginAsync(Email, Password);
            }
            LoginSucceeded?.Invoke(response);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
    }
}
