using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmniConnect.Shared.Converters;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private string? _token;
    private readonly DirectMessageListener _directListener = new();

    public ApiService(string baseUrl = "http://localhost:5000")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    /// <summary>Starts the ephemeral direct-message listener for LAN chat.</summary>
    public void StartDirectListener(int port = 5001) => _directListener.Start(port);

    /// <summary>Stops the ephemeral direct-message listener and clears queues.</summary>
    public void StopDirectListener() => _directListener.Stop();

    /// <summary>Port the direct listener is bound to.</summary>
    public int DirectListenerPort => _directListener.Port;

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // ── Auth ─────────────────────────────────────────────────────────
    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
        SetToken(result.Token);
        return result;
    }

    public async Task<AuthResponse> RegisterAsync(string username, string email, string password, string displayName)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, email, password, displayName), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
        SetToken(result.Token);
        return result;
    }

    // ── Users ────────────────────────────────────────────────────────
    public async Task<UserProfileDto> GetMyProfileAsync()
    {
        var response = await _http.GetAsync("/api/users/me");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
    }

    public async Task<List<UserDto>> SearchUsersAsync(string query)
    {
        var response = await _http.GetAsync($"/api/users/search?q={Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UserDto>>(JsonDefaults.Options) ?? new();
    }

    // ── Friends ──────────────────────────────────────────────────────
    public async Task<List<FriendRequestDto>> GetFriendsAsync()
    {
        var response = await _http.GetAsync("/api/friends");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<FriendRequestDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task<List<FriendRequestDto>> GetPendingRequestsAsync()
    {
        var response = await _http.GetAsync("/api/friends/pending");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<FriendRequestDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task SendFriendRequestAsync(Guid userId)
    {
        var response = await _http.PostAsync($"/api/friends/request/{userId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task AcceptFriendRequestAsync(Guid friendshipId)
    {
        var response = await _http.PostAsync($"/api/friends/accept/{friendshipId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FriendRequestDto?> EnsureFriendAsync(Guid userId)
    {
        var response = await _http.PostAsync($"/api/friends/ensure/{userId}", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<FriendRequestDto>(JsonDefaults.Options);
    }

    // ── Messages ─────────────────────────────────────────────────────
    public async Task<MessageDto?> SendMessageAsync(SendMessageRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/messages", request, JsonDefaults.Options);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<MessageDto>(JsonDefaults.Options);
    }

    public async Task<List<MessageDto>> GetChannelMessagesAsync(Guid communityId, Guid channelId, int skip = 0, int take = 50)
    {
        var response = await _http.GetAsync(
            $"/api/messages/channel/{communityId}/{channelId}?skip={skip}&take={take}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MessageDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task<List<MessageDto>> GetConversationAsync(Guid userId, int skip = 0, int take = 50)
    {
        var response = await _http.GetAsync($"/api/messages/conversation/{userId}?skip={skip}&take={take}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MessageDto>>(JsonDefaults.Options) ?? new();
    }

    // ── Direct (offline LAN) messages ──────────────────────────────────
    /// <summary>Send a direct message to a peer's listener over LAN (no backend, no DB).</summary>
    public async Task<bool> SendDirectMessageAsync(string peerIp, int peerPort, string fromPeerId, string fromPeerName, string toPeerId, string content, string type = "Text")
    {
        var msg = new DirectMessageDto(fromPeerId, fromPeerName, toPeerId, content, type,
            ClientMessageId: Guid.NewGuid().ToString("N"), SentAt: DateTime.UtcNow);
        return await DirectMessageListener.SendAsync(peerIp, peerPort, msg);
    }

    /// <summary>Drain ephemeral direct messages from a peer's listener (no backend, no DB).</summary>
    public async Task<List<(string FromPeerId, string FromPeerName, string Content, string Type, DateTime SentAt)>>
        FetchDirectMessagesAsync(string peerIp, int peerPort, string peerId)
    {
        var entries = await DirectMessageListener.FetchAsync(peerIp, peerPort, peerId);
        return entries.Select(e => (e.FromPeerId, e.FromPeerName, e.Content, e.Type, e.SentAt)).ToList();
    }

    /// <summary>Quickly check whether a peer's direct listener is reachable.</summary>
    public async Task<bool> IsDirectPeerReachableAsync(string peerIp, int peerPort)
    {
        return await DirectMessageListener.IsReachableAsync(peerIp, peerPort);
    }

    // ── Communities ──────────────────────────────────────────────────
    public async Task<List<CommunityDto>> GetMyCommunitiesAsync()
    {
        var response = await _http.GetAsync("/api/communities");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CommunityDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task<CommunityDto> CreateCommunityAsync(string name, string? description, bool isPublic)
    {
        var response = await _http.PostAsJsonAsync("/api/communities",
            new CreateCommunityRequest(name, description, isPublic), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CommunityDto>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
    }

    public async Task<List<ChannelDto>> GetChannelsAsync(Guid communityId)
    {
        var response = await _http.GetAsync($"/api/communities/{communityId}/channels");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ChannelDto>>(JsonDefaults.Options) ?? new();
    }

    // ── Notifications ────────────────────────────────────────────────
    public async Task<List<NotificationDto>> GetNotificationsAsync()
    {
        var response = await _http.GetAsync("/api/notifications");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<NotificationDto>>(JsonDefaults.Options) ?? new();
    }

    // ── Devices ──────────────────────────────────────────────────────
    public async Task RegisterDeviceAsync(string deviceId, string deviceName, string deviceModel, Guid? userId = null, string? userName = null, string? ip = null, int port = 0)
    {
        var dto = new DeviceDiscoveryDto(deviceId, deviceName, deviceModel,
            Shared.Enums.DeviceRole.Client, new HashSet<string>(), ip, port, userId, userName);
        var response = await _http.PostAsJsonAsync("/api/devices/register", dto, JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<RouteInfoDto>> GetRoutesAsync()
    {
        var response = await _http.GetAsync("/api/routing/routes");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<RouteInfoDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task<List<DeviceDiscoveryDto>> GetDiscoveredDevicesAsync()
    {
        var response = await _http.GetAsync("/api/devices");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new();
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options);
        if (payload.ValueKind == JsonValueKind.Array)
            return payload.Deserialize<List<DeviceDiscoveryDto>>(JsonDefaults.Options) ?? new();

        // Object form: { devices: [...], selfDeviceId: "..." }
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("devices", out var devices))
        {
            var rawList = devices.Deserialize<List<DeviceDiscoveryDto>>(JsonDefaults.Options) ?? new();
            var result = new List<DeviceDiscoveryDto>();
            foreach (var item in rawList)
            {
                if (item.DeviceId == null || item.DeviceId.StartsWith("device-OMEGA")) continue;
                var transports = new HashSet<string>(item.AvailableTransports ?? new());
                transports.Add("LAN");
                transports.Add("Internet");

                result.Add(item with { AvailableTransports = transports });
            }
            return result;
        }

        return new();
    }

    /// <summary>
    /// Device id the backend node broadcasts as, used to hide the local machine
    /// from the devices list. Empty when the server does not report one.
    /// </summary>
    public async Task<string> GetServerSelfDeviceIdAsync()
    {
        try
        {
            var response = await _http.GetAsync("/api/devices");
            if (!response.IsSuccessStatusCode) return string.Empty;

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options);
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("selfDeviceId", out var id))
            {
                return id.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Discovery API unavailable — nothing to hide.
        }

        return string.Empty;
    }

    public async Task<DeviceInfoDto?> GetMyDeviceInfoAsync()
    {
        var response = await _http.GetAsync("/api/devices/my-info");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceInfoDto>(JsonDefaults.Options);
    }

    // ── Files ─────────────────────────────────────────────────────────
    public async Task<FileUploadResponse> UploadFileAsync(string fileName, Stream fileStream, string mimeType, IProgress<double>? progress = null)
    {
        using var content = new MultipartFormDataContent();

        if (progress != null)
        {
            var totalBytes = fileStream.Length;
            var buffer = new byte[81920];
            var memoryStream = new MemoryStream();
            int bytesRead;
            long totalRead = 0;
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await memoryStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                progress.Report((double)totalRead / totalBytes * 100);
            }
            memoryStream.Position = 0;
            var fileContent = new ByteArrayContent(memoryStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            content.Add(fileContent, "file", fileName);
        }
        else
        {
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            content.Add(fileContent, "file", fileName);
        }

        var response = await _http.PostAsync("/api/files/upload", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileUploadResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
    }

    /// <summary>Downloads a file from the given URL (typically /api/files/download/{filename}).</summary>
    public async Task<byte[]> DownloadFileAsync(string mediaUrl)
    {
        // Handle relative URLs by prepending base address
        var url = mediaUrl.StartsWith("http") ? mediaUrl : $"{_http.BaseAddress}{mediaUrl}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // ── Profile ──────────────────────────────────────────────────────
    public async Task<UserProfileDto> UpdateProfileAsync(string? displayName, string? bio, string? avatarUrl)
    {
        var response = await _http.PutAsJsonAsync("/api/users/me",
            new UpdateProfileRequest(displayName, bio, avatarUrl), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(oldPassword, newPassword), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
    }

    // ── Calls ────────────────────────────────────────────────────────
    public async Task<CallDto> InitiateCallAsync(Guid calleeId, string type)
    {
        var response = await _http.PostAsJsonAsync("/api/calls/initiate",
            new InitiateCallRequest(calleeId, type), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CallDto>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid response");
    }

    public async Task<List<CallDto>> GetCallHistoryAsync()
    {
        var response = await _http.GetAsync("/api/calls/history");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CallDto>>(JsonDefaults.Options) ?? new();
    }

    public async Task AnswerCallAsync(Guid callId)
    {
        var response = await _http.PostAsync($"/api/calls/{callId}/accept", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RejectCallAsync(Guid callId)
    {
        var response = await _http.PostAsync($"/api/calls/{callId}/reject", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task EndCallAsync(Guid callId)
    {
        var response = await _http.PostAsync($"/api/calls/{callId}/end", null);
        response.EnsureSuccessStatusCode();
    }
}
