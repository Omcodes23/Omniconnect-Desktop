using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OmniConnect.Shared.Converters;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.Services;

public class SignalRService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _hubUrl;

    public event Action<MessageDto>? OnMessageReceived;
    public event Action<string>? OnUserOnline;
    public event Action<string>? OnUserOffline;
    public event Action<string, Guid?, Guid?, bool>? OnUserTyping;
    public event Action<CallDto>? OnIncomingCall;
    public event Action<CallDto>? OnCallAccepted;
    public event Action<CallDto>? OnCallRejected;
    public event Action<CallDto>? OnCallEnded;
    public event Action<string, string>? OnCallSignalReceived;
    public event Action<string, string, string>? OnCallMediaReceived;
    public event Action<DeviceDiscoveryDto>? OnDeviceDiscovered;

    public SignalRService(string baseUrl = "http://localhost:5000")
    {
        _hubUrl = $"{baseUrl}/hubs/chat";
    }

    public async Task ConnectAsync(string token)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.Converters.Add(new FlexibleEnumConverterFactory());
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _connection.On<MessageDto>("MessageReceived", message =>
        {
            OnMessageReceived?.Invoke(message);
        });

        _connection.On<string>("UserOnline", userId =>
        {
            OnUserOnline?.Invoke(userId);
        });

        _connection.On<string>("UserOffline", userId =>
        {
            OnUserOffline?.Invoke(userId);
        });

        _connection.On<string, Guid?, Guid?, bool>("UserTyping", (userId, communityId, channelId, isTyping) =>
        {
            OnUserTyping?.Invoke(userId, communityId, channelId, isTyping);
        });

        _connection.On<CallDto>("IncomingCall", call =>
        {
            OnIncomingCall?.Invoke(call);
        });

        _connection.On<CallDto>("CallAccepted", call =>
        {
            OnCallAccepted?.Invoke(call);
        });

        _connection.On<CallDto>("CallRejected", call =>
        {
            OnCallRejected?.Invoke(call);
        });

        _connection.On<CallDto>("CallEnded", call =>
        {
            OnCallEnded?.Invoke(call);
        });

        _connection.On<string, string>("CallSignalReceived", (senderId, signalData) =>
        {
            OnCallSignalReceived?.Invoke(senderId, signalData);
        });

        _connection.On<string, string, string>("CallMediaReceived", (senderId, kind, payload) =>
        {
            OnCallMediaReceived?.Invoke(senderId, kind, payload);
        });

        _connection.On<DeviceDiscoveryDto>("DeviceDiscovered", device =>
        {
            OnDeviceDiscovered?.Invoke(device);
        });

        _connection.Closed += async (error) =>
        {
            Console.WriteLine($"SignalR connection closed: {error?.Message}");
            await Task.Delay(5000);
            try { await ConnectAsync(token); } catch { }
        };

        await _connection.StartAsync();
    }

    public async Task JoinChannel(Guid communityId, Guid channelId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("JoinChannel", communityId, channelId);
    }

    public async Task LeaveChannel(Guid communityId, Guid channelId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("LeaveChannel", communityId, channelId);
    }

    public async Task SendMessage(SendMessageRequest request)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("SendMessage", request);
    }

    public async Task SendTyping(Guid? communityId, Guid? channelId, bool isTyping)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("Typing", communityId, channelId, isTyping);
    }

    public async Task SendCallSignal(Guid targetUserId, string signalData)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("SendCallSignal", targetUserId, signalData);
    }

    public async Task SendCallMedia(Guid targetUserId, string kind, string payload)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("SendCallMedia", targetUserId, kind, payload);
    }

    public HubConnectionState? State => _connection?.State;

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }
    }
}
