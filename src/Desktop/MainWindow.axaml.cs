using Avalonia.Controls;
using OmniConnect.Desktop.Services;
using OmniConnect.Desktop.ViewModels;
using OmniConnect.Desktop.Views;

namespace OmniConnect.Desktop;

public partial class MainWindow : Window
{
    private readonly ApiService _api;
    private readonly SignalRService _signalR;
    private LoginViewModel? _loginViewModel;
    private MainViewModel? _mainViewModel;

    public MainWindow()
    {
        InitializeComponent();

        var baseUrl = "http://localhost:5000";
        _api = new ApiService(baseUrl);
        _signalR = new SignalRService(baseUrl);

        SetupLoginView();
    }

    private void SetupLoginView()
    {
        _loginViewModel = new LoginViewModel(_api);
        _loginViewModel.LoginSucceeded += OnLoginSucceeded;
        LoginControl.DataContext = _loginViewModel;
    }

    private async void OnLoginSucceeded(Shared.DTOs.AuthResponse auth)
    {
        LoginControl.IsVisible = false;
        ChatControl.IsVisible = true;

        _mainViewModel = new MainViewModel(_api, _signalR);
        ChatControl.DataContext = _mainViewModel;

        // Set MainWindow DataContext so notification flyout bindings work
        DataContext = _mainViewModel;

        // Wire up sub-views
        CallOverlayControl.DataContext = _mainViewModel.CallVm;
        AdminPanelControl.DataContext = _mainViewModel.AdminVm;
        ProfileDialogControl.DataContext = _mainViewModel.ProfileVm;
        NetworkGraphControl.DataContext = _mainViewModel.NetworkGraphVm;

        // Listen for call state changes to show/hide overlay
        _mainViewModel.CallVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CallViewModel.IsCallActive))
            {
                CallOverlayControl.IsVisible = _mainViewModel.CallVm.IsCallActive;
            }
        };

        // Listen for admin panel toggles
        _mainViewModel.AdminVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AdminViewModel.IsPanelOpen))
            {
                AdminPanelControl.IsVisible = _mainViewModel.AdminVm.IsPanelOpen;
            }
        };

        // Listen for profile dialog toggles
        _mainViewModel.ProfileVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProfileViewModel.IsDialogOpen))
            {
                ProfileDialogControl.IsVisible = _mainViewModel.ProfileVm.IsDialogOpen;
            }
        };

        // Listen for network graph toggles
        _mainViewModel.NetworkGraphVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NetworkGraphViewModel.IsPanelOpen))
            {
                NetworkGraphControl.IsVisible = _mainViewModel.NetworkGraphVm.IsPanelOpen;
                if (!_mainViewModel.NetworkGraphVm.IsPanelOpen)
                {
                    _mainViewModel.NetworkGraphVm.StopAutoRefresh();
                }
            }
        };

        await _mainViewModel.InitializeAsync(auth);
        Title = $"OmniConnect - {auth.User.DisplayName}";
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _signalR.DisposeAsync().AsTask().ConfigureAwait(false);
    }
}
