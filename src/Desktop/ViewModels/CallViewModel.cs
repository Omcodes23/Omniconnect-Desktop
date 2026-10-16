using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Desktop.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

public partial class CallViewModel : ViewModelBase
{
    private ApiService? _api;
    private SignalRService? _signalR;
    private System.Timers.Timer? _timer;

    [ObservableProperty] private string _callerName = string.Empty;
    [ObservableProperty] private string? _callerAvatar;
    [ObservableProperty] private string _calleeName = string.Empty;
    [ObservableProperty] private string? _calleeAvatar;
    [ObservableProperty] private string _callType = "Audio"; // "Audio" or "Video"
    [ObservableProperty] private string _callStatus = "Ringing"; // Ringing, Connected, Ended, Missed

    partial void OnCallerNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayInitial));
    }

    partial void OnCalleeNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayInitial));
    }

    partial void OnCallStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsRinging));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowAnswerReject));
        OnPropertyChanged(nameof(ShowEndCall));
    }

    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isSpeakerOn;
    [ObservableProperty] private bool _isCallActive;

    partial void OnIsCallActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEndCall));
    }

    [ObservableProperty] private bool _isIncoming;

    partial void OnIsIncomingChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayInitial));
        OnPropertyChanged(nameof(ShowAnswerReject));
        OnPropertyChanged(nameof(ShowEndCall));
    }

    [ObservableProperty] private string _callTimerDisplay = "00:00";
    [ObservableProperty] private bool _isCallHistoryLoading;
    [ObservableProperty] private bool _showCallHistory;

    private int _callSeconds;

    public Guid CallId { get; set; }
    public Guid CallerId { get; set; }
    public Guid CalleeId { get; set; }

    public string DisplayName => IsIncoming
        ? (string.IsNullOrWhiteSpace(CallerName) ? "User" : CallerName)
        : (string.IsNullOrWhiteSpace(CalleeName) ? "User" : CalleeName);

    public string DisplayInitial => DisplayName.Length > 0 ? DisplayName[0].ToString().ToUpper() : "?";

    public bool IsRinging => CallStatus == "Ringing";
    public bool IsConnected => CallStatus == "Connected";
    public bool ShowAnswerReject => IsIncoming && IsRinging;
    public bool ShowEndCall => IsCallActive && !ShowAnswerReject;

    public ObservableCollection<CallHistoryItem> CallHistory { get; } = new();

    public event Action? CallAccepted;
    public event Action? CallRejected;
    public event Action? CallEnded;
    public event Action<bool>? MuteToggled;
    public event Action<bool>? SpeakerToggled;

    public CallViewModel()
    {
    }

    public CallViewModel(ApiService api, SignalRService? signalR = null)
    {
        _api = api;
        _signalR = signalR;
    }

    public void SetServices(ApiService api, SignalRService signalR)
    {
        _api = api;
        _signalR = signalR;
    }

    public void StartIncomingCall(CallDto call)
    {
        CallId = call.Id;
        CallerId = call.CallerId;
        CallerName = call.CallerName;
        CallerAvatar = call.CallerAvatar;
        CalleeId = call.CalleeId;
        CalleeName = call.CalleeName;
        CalleeAvatar = call.CalleeAvatar;
        CallType = call.Type;
        CallStatus = "Ringing";
        IsIncoming = true;
        IsCallActive = true;
    }

    public void StartOutgoingCall(Guid calleeId, string calleeName, string? calleeAvatar, string type)
    {
        _ = StartOutgoingCallAsync(calleeId, calleeName, calleeAvatar, type);
    }

    public async Task StartOutgoingCallAsync(Guid calleeId, string calleeName, string? calleeAvatar, string type)
    {
        CalleeId = calleeId;
        CalleeName = calleeName;
        CalleeAvatar = calleeAvatar;
        CallType = type;
        CallStatus = "Ringing";
        IsIncoming = false;
        IsCallActive = true;
        StartTimer();

        if (_api != null)
        {
            try
            {
                var call = await _api.InitiateCallAsync(calleeId, type);
                CallId = call.Id;
            }
            catch
            {
                // Fallback / best-effort
            }
        }
    }

    public void HandleCallAccepted(CallDto call)
    {
        CallStatus = "Connected";
        CallAccepted?.Invoke();
    }

    public void HandleCallRejected(CallDto call)
    {
        CallStatus = "Ended";
        CallRejected?.Invoke();
        StopTimer();
        IsCallActive = false;
    }

    public void HandleCallEnded(CallDto call)
    {
        CallStatus = "Ended";
        CallEnded?.Invoke();
        StopTimer();
        IsCallActive = false;
    }

    [RelayCommand]
    private async Task AnswerCallAsync()
    {
        if (!IsIncoming) return;
        CallStatus = "Connected";
        CallAccepted?.Invoke();
        StartTimer();

        if (_api != null && CallId != Guid.Empty)
        {
            try { await _api.AnswerCallAsync(CallId); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task RejectCallAsync()
    {
        CallStatus = IsIncoming ? "Missed" : "Ended";
        CallRejected?.Invoke();
        StopTimer();
        IsCallActive = false;

        if (_api != null && CallId != Guid.Empty)
        {
            try { await _api.RejectCallAsync(CallId); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task EndCallAsync()
    {
        CallStatus = "Ended";
        CallEnded?.Invoke();
        StopTimer();
        IsCallActive = false;

        if (_api != null && CallId != Guid.Empty)
        {
            try { await _api.EndCallAsync(CallId); }
            catch { }
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        MuteToggled?.Invoke(IsMuted);
    }

    [RelayCommand]
    private void ToggleSpeaker()
    {
        IsSpeakerOn = !IsSpeakerOn;
        SpeakerToggled?.Invoke(IsSpeakerOn);
    }

    [RelayCommand]
    private void ToggleCallHistory()
    {
        ShowCallHistory = !ShowCallHistory;
        if (ShowCallHistory)
        {
            _ = LoadCallHistoryAsync();
        }
    }

    public async Task LoadCallHistoryAsync()
    {
        if (_api == null) return;

        IsCallHistoryLoading = true;
        try
        {
            var history = await _api.GetCallHistoryAsync();
            CallHistory.Clear();
            foreach (var call in history.OrderByDescending(c => c.StartedAt))
            {
                CallHistory.Add(new CallHistoryItem
                {
                    Id = call.Id,
                    Name = call.CallerId == CallerId ? call.CalleeName : call.CallerName,
                    Type = call.Type,
                    Status = call.Status,
                    StartedAt = call.StartedAt,
                    Duration = call.Duration,
                    IsIncoming = call.CalleeId == CalleeId || CalleeId == Guid.Empty,
                    DurationDisplay = call.Duration > 0
                        ? $"{(call.Duration >= 3600 ? $"{call.Duration / 3600}h " : "")}{(call.Duration % 3600) / 60:D2}:{call.Duration % 60:D2}"
                        : "No answer"
                });
            }
        }
        catch
        {
            // API not available
        }
        finally
        {
            IsCallHistoryLoading = false;
        }
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += (_, _) =>
        {
            _callSeconds++;
            var minutes = _callSeconds / 60;
            var seconds = _callSeconds % 60;
            var display = $"{minutes:D2}:{seconds:D2}";
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CallTimerDisplay = display);
        };
        _timer.AutoReset = true;
        _timer.Start();
        _callSeconds = 0;
        CallTimerDisplay = "00:00";
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }
    }
}

public class CallHistoryItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Audio";
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public int Duration { get; set; }
    public string DurationDisplay { get; set; } = string.Empty;
    public bool IsIncoming { get; set; }

    public string StatusIcon => Status switch
    {
        "Connected" => "✅",
        "Missed" => "❌",
        "Ended" => "\U0001F4E4",
        _ => "❓"
    };

    public string Direction => IsIncoming ? "Incoming" : "Outgoing";

    public string DateDisplay => StartedAt.ToLocalTime().ToString("MMM dd, HH:mm");
}