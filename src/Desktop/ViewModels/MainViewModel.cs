using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Desktop.Services;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ApiService _api;
    private readonly SignalRService _signalR;
    private AuthResponse? _auth;
    private string _selfDeviceId = "";
    private string _serverDeviceId = "";
    private Avalonia.Threading.DispatcherTimer? _discoveryTimer;

    // ── Ephemeral peer-to-peer chat (LAN / Bluetooth, no backend, no DB) ──
    private string? _ephemeralPeerId;
    private string? _ephemeralPeerIp;
    private int _ephemeralPeerPort;
    private string _ephemeralPeerName = string.Empty;
    private Avalonia.Threading.DispatcherTimer? _directPollTimer;

    public MainViewModel(ApiService api, SignalRService signalR)
    {
        _api = api;
        _signalR = signalR;

        ProfileVm = new ProfileViewModel(api);
        NetworkGraphVm = new NetworkGraphViewModel(api);

        SetupSignalRHandlers();
    }

    // ── User Properties ──────────────────────────────────────────────
    [ObservableProperty] private string _userDisplayName = "User";
    [ObservableProperty] private string _userEmail = string.Empty;
    [ObservableProperty] private string _userAvatar = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isAdmin;

    // ── Collections ──────────────────────────────────────────────────
    public ObservableCollection<FriendItem> Friends { get; } = new();
    public ObservableCollection<InternetHostItem> InternetHosts { get; } = new();
    // Retained for the existing channel/message code while communities are hidden from the UI.
    public ObservableCollection<ChannelItem> Channels { get; } = new();
    public ObservableCollection<MessageDto> Messages { get; } = new();
    public ObservableCollection<UserDto> SearchResults { get; } = new();
    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    public ObservableCollection<DeviceItem> DiscoveredDevices { get; } = new();

    // Ephemeral peer-to-peer messages for the currently selected direct chat session.
    // Never written to the DB; cleared whenever a new peer session starts or the listener stops.
    public ObservableCollection<EphemeralMessageItem> EphemeralMessages { get; } = new();

    // ── Selected Items ───────────────────────────────────────────────
    [ObservableProperty] private CommunityItem? _selectedCommunity;
    [ObservableProperty] private ChannelItem? _selectedChannel;
    [ObservableProperty] private FriendItem? _selectedFriend;
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";

    /// True when a direct ephemeral LAN/Bluetooth session is active.
    public bool IsDirectChatActive => _ephemeralPeerId != null;

    /// True when showing the normal DB-backed chat (no direct session).
    public bool IsRegularChatActive => _ephemeralPeerId == null;

    // ── Panel Properties ─────────────────────────────────────────────
    [ObservableProperty] private bool _isAiPanelOpen;
    [ObservableProperty] private bool _isNotificationFlyoutOpen;
    [ObservableProperty] private int _unreadNotificationCount;
    [ObservableProperty] private bool _hasUnreadNotifications;

    // ── Sub-ViewModels ───────────────────────────────────────────────
    public CallViewModel CallVm { get; } = new();
    public AiViewModel AiVm { get; } = new();
    public AdminViewModel AdminVm { get; } = new();
    public ProfileViewModel ProfileVm { get; }
    public NetworkGraphViewModel NetworkGraphVm { get; }

    // ── File Upload Properties ────────────────────────────────────────
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private double _uploadProgress;

    // Delegate set by ChatView for the native file-save dialog.
    public Func<string, Task<string?>>? PickSaveFilePathAsync { get; set; }

    // ── Event Handlers ──────────────────────────────────────────────
    partial void OnSelectedCommunityChanged(CommunityItem? value)
    {
        if (value != null) LoadChannels(value.Id);
    }

    partial void OnSelectedChannelChanged(ChannelItem? value)
    {
        if (value != null && SelectedCommunity != null)
            LoadChannelMessages(SelectedCommunity.Id, value.Id);
    }

    partial void OnSelectedFriendChanged(FriendItem? value)
    {
        if (value != null) LoadConversation(value.Id);
    }

    // ── Initialization ───────────────────────────────────────────────
    public async Task InitializeAsync(AuthResponse auth)
    {
        _auth = auth;
        UserDisplayName = auth.User.DisplayName;
        UserEmail = auth.User.Email;
        IsAdmin = auth.User.Role >= UserRole.Admin;
        AdminVm.IsAdmin = IsAdmin;

        await _signalR.ConnectAsync(auth.Token);
        AiVm.SetToken(auth.Token);
        await AiVm.LoadConversationsAsync();

        // Register this desktop as a device so other clients can discover it
        _selfDeviceId = $"desktop-{Environment.MachineName}";
        try
        {
            await _api.RegisterDeviceAsync(
                deviceId: _selfDeviceId,
                deviceName: $"Desktop ({Environment.MachineName})",
                deviceModel: Environment.OSVersion.VersionString,
                userId: _auth.User.Id,
                userName: _auth.User.DisplayName);
        }
        catch
        {
            // Best-effort — don't block startup on registration
        }
        IsConnected = true;
        await LoadDataAsync();

        // Start periodic discovery and routing refresh timer (every 6 seconds)
        _discoveryTimer?.Stop();
        _discoveryTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _discoveryTimer.Tick += async (_, _) =>
        {
            await LoadDiscoveredDevicesAsync();
            await LoadInternetHostsAsync();
        };
        _discoveryTimer.Start();
    }

    private void SetupSignalRHandlers()
    {
        _signalR.OnMessageReceived += message =>
        {
            if (message == null) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var currentUserId = _auth?.User?.Id;
                var isForCurrentFriend = SelectedFriend != null &&
                    ((message.SenderId == SelectedFriend.Id && message.ReceiverId == currentUserId) ||
                     (message.SenderId == currentUserId && message.ReceiverId == SelectedFriend.Id));
                var isForCurrentChannel = SelectedChannel != null && message.ChannelId == SelectedChannel.Id;

                if (IsDirectChatActive)
                {
                    if (!EphemeralMessages.Any(m => m.Content == message.Content && m.FromPeerName == (message.SenderName ?? "Peer")))
                    {
                        EphemeralMessages.Add(new EphemeralMessageItem
                        {
                            FromPeerId = message.SenderId.ToString(),
                            FromPeerName = message.SenderName ?? "Peer",
                            Content = message.Content ?? "",
                            Type = "Text",
                            SentAt = message.SentAt,
                            IsMe = message.SenderId == currentUserId,
                            ClientMessageId = message.Id.ToString()
                        });
                    }
                }
                else if (isForCurrentFriend || isForCurrentChannel)
                {
                    if (!Messages.Any(m => m.Id == message.Id))
                    {
                        Messages.Add(message);
                    }
                }

                if (message.SenderId != _auth?.User?.Id)
                {
                    Notifications.Insert(0, new NotificationItem
                    {
                        Type = "message",
                        Title = message.SenderName ?? "New Message",
                        Body = (message.Content ?? "").Length > 80
                            ? (message.Content ?? "")[..80] + "..."
                            : (message.Content ?? ""),
                        CreatedAt = DateTime.Now,
                        ImageUrl = message.SenderAvatar
                    });
                    HasUnreadNotifications = true;
                    UnreadNotificationCount = Notifications.Count(n => !n.IsRead);
                }
            });
        };

        _signalR.OnUserOnline += userId =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var friend = Friends.FirstOrDefault(f => f.Id.ToString() == userId);
                if (friend != null) friend.Status = "Online";
            });
        };

        _signalR.OnUserOffline += userId =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var friend = Friends.FirstOrDefault(f => f.Id.ToString() == userId);
                if (friend != null) friend.Status = "Offline";
            });
        };

        _signalR.OnIncomingCall += call =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CallVm.StartIncomingCall(call);
            });
        };

        _signalR.OnCallAccepted += call =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CallVm.HandleCallAccepted(call);
            });
        };

        _signalR.OnCallRejected += call =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CallVm.HandleCallRejected(call);
            });
        };

        _signalR.OnCallEnded += call =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CallVm.HandleCallEnded(call);
            });
        };

        _signalR.OnDeviceDiscovered += dto =>
        {
            // Ignore this machine and the backend node it is signed in to.
            if (dto.DeviceId == _selfDeviceId) return;
            if (!string.IsNullOrEmpty(_serverDeviceId) && dto.DeviceId == _serverDeviceId) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var existing = DiscoveredDevices.FirstOrDefault(d => d.DeviceId == dto.DeviceId);
                if (existing != null)
                {
                    existing.DeviceName = dto.UserName ?? dto.DeviceName;
                    existing.DeviceModel = dto.DeviceModel;
                    existing.UserId = dto.UserId ?? existing.UserId;
                    existing.Status = "Online";
                }
                else
                {
                    var transport = dto.AvailableTransports?.FirstOrDefault() ?? "LAN";
                    DiscoveredDevices.Add(new DeviceItem
                    {
                        DeviceId = dto.DeviceId,
                        DeviceName = dto.UserName ?? dto.DeviceName,
                        DeviceModel = dto.DeviceModel,
                        Transport = transport,
                        TransportDetail = transport,
                        UserId = dto.UserId,
                        Status = "Online"
                    });
                }
            });
        };
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Load friends from API
            var friends = await _api.GetFriendsAsync();
            Friends.Clear();
            foreach (var f in friends)
            {
                var isRequester = f.RequesterId == _auth!.User.Id;
                Friends.Add(new FriendItem
                {
                    Id = isRequester ? f.AddresseeId : f.RequesterId,
                    Name = isRequester ? f.AddresseeName : f.RequesterName,
                    Avatar = isRequester ? f.AddresseeAvatar : f.RequesterAvatar,
                    Status = "Online"
                });
            }

            // Check if AI Assistant is in the friends list
            // The backend's AiBotService auto-creates this user — it will appear
            // as a friend once the backend is running and the service is active.
            // Messages sent to AI Assistant are auto-replied by the backend.
            var aiAssistant = Friends.FirstOrDefault(f =>
                f.Name.Contains("AI Assistant", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("AI", StringComparison.OrdinalIgnoreCase));

            // Load discovered devices first so the backend node can be excluded from hosts.
            await LoadDiscoveredDevicesAsync();

            // Internet hosts are authenticated users with a backend-registered device.
            await LoadInternetHostsAsync();

            // Load notifications from API
            var notifs = await _api.GetNotificationsAsync();
            Notifications.Clear();
            foreach (var n in notifs)
            {
                Notifications.Add(new NotificationItem
                {
                    Type = n.Type,
                    Title = n.Title,
                    Body = n.Body ?? "",
                    CreatedAt = n.CreatedAt,
                    IsRead = n.IsRead
                });
            }
            HasUnreadNotifications = Notifications.Any(n => !n.IsRead);
            UnreadNotificationCount = Notifications.Count(n => !n.IsRead);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load data: {ex.Message}";
        }
    }

    private async Task LoadDiscoveredDevicesAsync()
    {
        try
        {
            var devices = await _api.GetDiscoveredDevicesAsync();

            // The server node also registers as a device; hide it so the user
            // never sees the machine they are signed in on.
            var serverDeviceId = await _api.GetServerSelfDeviceIdAsync();
            if (!string.IsNullOrEmpty(serverDeviceId)) _serverDeviceId = serverDeviceId;

            // Load routing table (best transport per device)
            Dictionary<string, RouteInfoDto> routes = new();
            try
            {
                foreach (var r in await _api.GetRoutesAsync())
                    routes[r.DeviceId] = r;
            }
            catch
            {
                // Routing API unavailable — fall back to LAN label
            }

            DiscoveredDevices.Clear();
            foreach (var d in devices.Where(x =>
                !string.IsNullOrEmpty(x.DeviceId) &&
                x.DeviceId != _selfDeviceId &&
                x.DeviceId != serverDeviceId))
            {
                routes.TryGetValue(d.DeviceId, out var route);
                var localTransports = d.AvailableTransports?
                    .Where(t => t.Equals("LAN", StringComparison.OrdinalIgnoreCase) || t.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? new();
                var transportsStr = localTransports.Count > 0 ? string.Join(", ", localTransports) : (route?.ActiveTransport.ToString() ?? "LAN");

                DiscoveredDevices.Add(new DeviceItem
                {
                    DeviceId = d.DeviceId,
                    DeviceName = d.UserName ?? d.DeviceName,
                    DeviceModel = d.DeviceModel,
                    Transport = route?.ActiveTransport.ToString() ?? d.AvailableTransports?.FirstOrDefault() ?? "LAN",
                    TransportDetail = transportsStr,
                    IpAddress = d.IpAddress,
                    Port = d.Port,
                    UserId = d.UserId,
                    Status = "Online"
                });
            }
        }
        catch
        {
            // Discovery API not available — leave empty
        }
    }

    private async Task LoadInternetHostsAsync()
    {
        try
        {
            var devices = await _api.GetDiscoveredDevicesAsync();
            var hosts = devices
                .Where(device =>
                    device.UserId.HasValue &&
                    device.UserId != _auth?.User.Id &&
                    !string.IsNullOrWhiteSpace(device.DeviceId) &&
                    device.DeviceId != _selfDeviceId &&
                    device.DeviceId != _serverDeviceId)
                .GroupBy(device => device.UserId!.Value)
                .Select(group => group.First())
                .OrderBy(device => device.UserName ?? device.DeviceName)
                .Select(device => new InternetHostItem
                {
                    UserId = device.UserId!.Value,
                    DisplayName = string.IsNullOrWhiteSpace(device.UserName)
                        ? device.DeviceName
                        : device.UserName,
                    Status = "Online"
                })
                .ToList();

            InternetHosts.Clear();
            foreach (var host in hosts)
                InternetHosts.Add(host);
        }
        catch
        {
            // The Internet hosts list is optional while the discovery service is unavailable.
            InternetHosts.Clear();
        }
    }

    private async void LoadChannels(Guid communityId)
    {
        try
        {
            var channels = await _api.GetChannelsAsync(communityId);
            Channels.Clear();
            foreach (var c in channels)
            {
                Channels.Add(new ChannelItem
                {
                    Id = c.Id,
                    CommunityId = c.CommunityId,
                    Name = c.Name,
                    IsVoice = c.IsVoice
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load channels: {ex.Message}";
        }
    }

    private async void LoadChannelMessages(Guid communityId, Guid channelId)
    {
        try
        {
            var messages = await _api.GetChannelMessagesAsync(communityId, channelId);
            Messages.Clear();
            foreach (var m in messages.OrderBy(m => m.SentAt))
                Messages.Add(m);

            await _signalR.JoinChannel(communityId, channelId);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load messages: {ex.Message}";
        }
    }

    private async void LoadConversation(Guid userId)
    {
        try
        {
            var messages = await _api.GetConversationAsync(userId);
            Messages.Clear();
            foreach (var m in messages.OrderBy(m => m.SentAt))
                Messages.Add(m);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load conversation: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        // Direct ephemeral chat uses its own send path (no DB).
        if (_ephemeralPeerId != null)
        {
            await SendDirectMessageAsync(MessageText);
            MessageText = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(MessageText)) return;
        if (SelectedFriend == null && SelectedChannel == null)
        {
            StatusText = "Please select a contact or channel to chat with.";
            return;
        }

        var text = MessageText.Trim();
        var clientMsgId = Guid.NewGuid().ToString("N");

        try
        {
            var request = new SendMessageRequest(
                ReceiverId: SelectedFriend?.Id,
                CommunityId: SelectedCommunity?.Id,
                ChannelId: SelectedChannel?.Id,
                Type: MessageType.Text,
                Content: text,
                MediaUrl: null,
                FileSize: null,
                FileName: null,
                ReplyToId: null,
                ClientMessageId: clientMsgId
            );

            MessageText = string.Empty;

            if (_signalR.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
            {
                try
                {
                    await _signalR.SendMessage(request);
                }
                catch
                {
                    var sent = await _api.SendMessageAsync(request);
                    if (sent != null && !Messages.Any(m => m.Id == sent.Id))
                    {
                        Messages.Add(sent);
                    }
                }
            }
            else
            {
                var sent = await _api.SendMessageAsync(request);
                if (sent != null && !Messages.Any(m => m.Id == sent.Id))
                {
                    Messages.Add(sent);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to send message: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 2) return;
        try
        {
            var results = await _api.SearchUsersAsync(SearchQuery);
            SearchResults.Clear();
            foreach (var u in results)
                SearchResults.Add(u);
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddFriendAsync(Guid userId)
    {
        try
        {
            await _api.SendFriendRequestAsync(userId);
            StatusText = "Friend request sent!";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleNotifications()
    {
        IsNotificationFlyoutOpen = !IsNotificationFlyoutOpen;
    }

    [RelayCommand]
    private void MarkAllNotificationsRead()
    {
        foreach (var n in Notifications)
            n.IsRead = true;
        HasUnreadNotifications = false;
        UnreadNotificationCount = 0;
        IsNotificationFlyoutOpen = false;
    }

    [RelayCommand]
    private void DismissNotification(NotificationItem item)
    {
        Notifications.Remove(item);
        HasUnreadNotifications = Notifications.Any(n => !n.IsRead);
        UnreadNotificationCount = Notifications.Count(n => !n.IsRead);
    }

    [RelayCommand]
    private async Task OpenProfile()
    {
        ProfileVm.IsDialogOpen = true;
        await ProfileVm.LoadProfileAsync();
    }

    [RelayCommand]
    private void CloseProfile()
    {
        ProfileVm.IsDialogOpen = false;
    }

    [RelayCommand]
    private void ToggleAiPanel()
    {
        IsAiPanelOpen = !IsAiPanelOpen;
        AiVm.IsPanelOpen = IsAiPanelOpen;
    }

    [RelayCommand]
    private void ToggleNetworkGraph()
    {
        NetworkGraphVm.IsPanelOpen = !NetworkGraphVm.IsPanelOpen;
        if (NetworkGraphVm.IsPanelOpen)
        {
            NetworkGraphVm.StartAutoRefresh();
        }
        else
        {
            NetworkGraphVm.StopAutoRefresh();
        }
    }

    [RelayCommand]
    private void ToggleAdminPanel()
    {
        AdminVm.IsPanelOpen = !AdminVm.IsPanelOpen;
    }

    [RelayCommand]
    private void StartCall(FriendItem friend)
    {
        CallVm.StartOutgoingCall(friend.Id, friend.Name, null, "Audio");
    }

    [RelayCommand]
    private void StartVideoCall(FriendItem friend)
    {
        CallVm.StartOutgoingCall(friend.Id, friend.Name, null, "Video");
    }

    /// <summary>
    /// Called from ChatView code-behind after native file picker selection.
    /// Uploads the selected file bytes to the API and sends as a chat message.
    /// </summary>
    public async Task SendFileUploadAsync(string fileName, byte[] fileData)
    {
        if (fileData.Length == 0) return;

        IsUploading = true;
        UploadProgress = 0;
        StatusText = $"Uploading {fileName}...";

        try
        {
            var mimeType = MimeHelper.GetMimeType(fileName);
            var progress = new Progress<double>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => UploadProgress = p);
            });

            using var stream = new MemoryStream(fileData);
            var result = await _api.UploadFileAsync(fileName, stream, mimeType, progress);

            if (result != null)
            {
                var request = new SendMessageRequest(
                    ReceiverId: SelectedFriend?.Id,
                    CommunityId: SelectedCommunity?.Id,
                    ChannelId: SelectedChannel?.Id,
                    Type: MessageType.File,
                    Content: result.FileName,
                    MediaUrl: result.PublicUrl ?? result.StoragePath,
                    FileSize: result.FileSize,
                    FileName: result.FileName,
                    ReplyToId: null,
                    ClientMessageId: Guid.NewGuid().ToString("N")
                );

                try
                {
                    if (_signalR.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                    {
                        await _signalR.SendMessage(request);
                    }
                    else
                    {
                        var sent = await _api.SendMessageAsync(request);
                        if (sent != null) Messages.Add(sent);
                    }
                }
                catch
                {
                    var sent = await _api.SendMessageAsync(request);
                    if (sent != null) Messages.Add(sent);
                }

                StatusText = $"Uploaded: {result.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Upload failed: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            UploadProgress = 0;
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync(MessageDto message)
    {
        if (string.IsNullOrWhiteSpace(message.MediaUrl))
        {
            StatusText = "No download URL for this file.";
            return;
        }

        try
        {
            StatusText = $"Downloading: {message.FileName ?? "file"}";
            var bytes = await _api.DownloadFileAsync(message.MediaUrl);

            if (PickSaveFilePathAsync == null)
            {
                StatusText = "The save dialog is not ready.";
                return;
            }

            var savePath = await PickSaveFilePathAsync(message.FileName ?? "download");
            if (string.IsNullOrWhiteSpace(savePath))
            {
                StatusText = "Download cancelled.";
                return;
            }

            await System.IO.File.WriteAllBytesAsync(savePath, bytes);
            StatusText = $"Saved: {message.FileName ?? "file"}";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ChatWithInternetHost(InternetHostItem host)
    {
        OpenChatWithUser(host.UserId, host.DisplayName, "Internet");
    }

    [RelayCommand]
    private void ChatWithDevice(DeviceItem device)
    {
        // Prefer direct ephemeral LAN/Bluetooth session when peer is reachable (IP + port).
        // Falls back to DB-backed chat when the peer has a user id but no direct endpoint.
        if (device.CanDirectChat)
        {
            _ = OpenDirectChatSession(
                device.DeviceId,
                !string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceName : "Nearby Device",
                device.IpAddress!,
                device.Port);
            return;
        }

        if (device.UserId == null) return;

        OpenChatWithUser(
            device.UserId.Value,
            !string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceName : "Nearby User",
            "nearby device");
    }

    private void OpenChatWithUser(Guid userId, string displayName, string source)
    {
        var friend = Friends.FirstOrDefault(f => f.Id == userId);
        if (friend == null)
        {
            friend = new FriendItem
            {
                Id = userId,
                Name = displayName,
                Status = "Online"
            };
            Friends.Insert(0, friend);

            _ = Task.Run(async () =>
            {
                try { await _api.EnsureFriendAsync(userId); } catch { }
            });
        }

        SelectedFriend = friend;
        SelectedCommunity = null;
        SelectedChannel = null;
        StatusText = $"Chatting with {friend.Name} via {source}";
    }

    // ── Ephemeral peer-to-peer session (LAN / Bluetooth) ──────────────────

    /// <summary>
    /// Opens a brand-new ephemeral direct chat session with a discovered peer.
    /// Messages are never stored in the DB; starting a new session clears history.
    /// </summary>
    public async Task OpenDirectChatSession(string peerId, string peerName, string peerIp, int peerPort)
    {
        // End any previous ephemeral session — fresh session each time.
        StopDirectPolling();
        EphemeralMessages.Clear();
        _ephemeralPeerId = peerId;
        _ephemeralPeerName = peerName;
        _ephemeralPeerIp = peerIp;
        _ephemeralPeerPort = 5001; // direct listener port, not server 5000

        // Ensure listener is running so peer can reach us back.
        try { _api.StartDirectListener(); StatusText = $"Direct session with {peerName} (fresh, no DB)"; } catch (Exception ex) { StatusText = $"Listener start failed: {ex.Message}"; }

        OnPropertyChanged(nameof(IsDirectChatActive));
        OnPropertyChanged(nameof(IsRegularChatActive));
        _ephemeralPeerName = peerName;
        _ephemeralPeerIp = peerIp;
        _ephemeralPeerPort = peerPort;

        // Ensure our own direct listener is running so the peer can reach us.
        try { _api.StartDirectListener(); StatusText = "Direct listener started on port 5001"; } catch (Exception ex) { StatusText = $"Listener error: {ex.Message}"; }

        SelectedFriend = null;
        SelectedCommunity = null;
        SelectedChannel = null;
        StatusText = $"Direct chat with {peerName} (no DB, session-only)";

        StartDirectPolling();
    }

    /// <summary>Starts polling the peer's listener for ephemeral inbound messages.</summary>
    private void StartDirectPolling()
    {
        StopDirectPolling();
        _directPollTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _directPollTimer.Tick += async (_, _) => await PollDirectMessagesAsync();
        _directPollTimer.Start();
        _ = PollDirectMessagesAsync();
    }

    private void StopDirectPolling()
    {
        _directPollTimer?.Stop();
        _directPollTimer = null;
    }

    private async Task PollDirectMessagesAsync()
    {
        if (string.IsNullOrEmpty(_ephemeralPeerIp) || string.IsNullOrEmpty(_ephemeralPeerId)) return;

        try
        {
            var reachable = await _api.IsDirectPeerReachableAsync(_ephemeralPeerIp!, _ephemeralPeerPort);
            if (!reachable) return;

            var received = await _api.FetchDirectMessagesAsync(_ephemeralPeerIp!, _ephemeralPeerPort, _ephemeralPeerId!);
            foreach (var (fromId, fromName, content, type, sentAt) in received)
            {
                if (EphemeralMessages.Any(m => m.ClientMessageId == null || m.Content == content && m.SentAt == sentAt)) continue;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    EphemeralMessages.Add(new EphemeralMessageItem
                    {
                        FromPeerId = fromId,
                        FromPeerName = fromName,
                        Content = content,
                        Type = type,
                        SentAt = sentAt,
                        IsMe = string.Equals(fromId, _auth?.User?.Id.ToString() ?? _selfDeviceId, StringComparison.OrdinalIgnoreCase)
                    });
                });
            }
        }
        catch
        {
            // Swallow — polling is best-effort.
        }
    }

    /// <summary>Sends a direct ephemeral message to the current peer (no DB).</summary>
    public async Task<bool> SendDirectMessageAsync(string content, string type = "Text")
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (string.IsNullOrEmpty(_ephemeralPeerId) || string.IsNullOrEmpty(_ephemeralPeerIp))
        {
            StatusText = "Direct chat: peer IP/ID missing — session not open";
            return false;
        }

        var myId = _auth?.User?.Id.ToString() ?? _selfDeviceId;
        var myName = _auth?.User?.DisplayName ?? Environment.MachineName;

        var sent = await _api.SendDirectMessageAsync(
            _ephemeralPeerIp!, _ephemeralPeerPort, myId, myName, _ephemeralPeerId!, content, type);

        if (sent)
        {
            StatusText = $"Direct sent to {_ephemeralPeerName}";
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                EphemeralMessages.Add(new EphemeralMessageItem
                {
                    FromPeerId = myId,
                    FromPeerName = myName,
                    Content = content,
                    Type = type,
                    SentAt = DateTime.UtcNow,
                    IsMe = true
                });
            });
        }

        else
        {
            StatusText = $"Direct send failed (peer {_ephemeralPeerIp}:{_ephemeralPeerPort})";
        }
        return sent;
    }

    /// <summary>Clears the current ephemeral session without starting a new one.</summary>
    public void CloseDirectChatSession()
    {
        StopDirectPolling();
        EphemeralMessages.Clear();
        _ephemeralPeerId = null;
        _ephemeralPeerIp = null;
        _ephemeralPeerPort = 5001;
        _ephemeralPeerName = string.Empty;
        OnPropertyChanged(nameof(IsDirectChatActive));
        OnPropertyChanged(nameof(IsRegularChatActive));
        _ephemeralPeerIp = null;
        _ephemeralPeerName = string.Empty;
        StatusText = "Direct chat closed";
    }
}

public class EphemeralMessageItem
{
    public string FromPeerId { get; set; } = string.Empty;
    public string FromPeerName { get; set; } = string.Empty;

    /// <summary>Alias for FromPeerName so the shared chat ItemTemplate can bind SenderName.</summary>
    public string SenderName => FromPeerName;

    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "Text";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsMe { get; set; }
    public string? ClientMessageId { get; set; }
}

public class FriendItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string Status { get; set; } = "Offline";
}

public class CommunityItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MemberCount { get; set; }
}

public class InternetHostItem
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Online";

    public string Initial => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName[..1].ToUpperInvariant();
}

public class ChannelItem
{
    public Guid Id { get; set; }
    public Guid CommunityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsVoice { get; set; }
}

public class NotificationItem
{
    public string Type { get; set; } = "info";
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.Now - CreatedAt;
            if (diff.TotalMinutes < 1) return "now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }

    public string TypeIcon => Type switch
    {
        "message" => "💬",
        "friend_request" => "👤",
        "call" => "📞",
        "system" => "🔔",
        _ => "ℹ️"
    };
}

public class DeviceItem
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string Transport { get; set; } = "LAN";
    public string? TransportDetail { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    public Guid? UserId { get; set; }
    public string Status { get; set; } = "Offline";

    /// <summary>Can open a direct ephemeral LAN session (peer reachable, has IP+port).</summary>
    public bool CanDirectChat => !string.IsNullOrWhiteSpace(IpAddress) && Port > 0;

    public bool CanChat => UserId != null;

    public string TransportIcon => Transport switch
    {
        "Bluetooth" => "🔵",
        "WiFiDirect" => "🟢",
        "Internet" => "🌐",
        "Lan" => "🟠",
        "LAN" => "🟠",
        _ => "⚪"
    };

    public bool IsOnline => Status == "Online";
    public string StatusColor => IsOnline ? "#10b981" : "#ef4444";
}
