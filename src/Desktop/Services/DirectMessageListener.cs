using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.Services;

/// <summary>
/// Lightweight in-process HTTP listener that lets this desktop accept direct
/// LAN messages from peers without going through the backend server.
///
/// All messages are ephemeral: kept in memory only, never written to the DB,
/// and cleared when the listener stops or a peer's message queue is drained.
/// </summary>
public sealed class DirectMessageListener : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, List<DirectMessageEntry>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private Task? _pump;
    private int _port = 5001;
    private bool _disposed;

    public int Port => _port;

    /// <summary>Start listening on the given port (default 5001).</summary>
    public void Start(int port = 5001)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectMessageListener));
        _port = port;
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add($"http://+:{_port}/");
        _listener.Start();
        _pump = PumpAsync(_cts.Token);
    }

    /// <summary>Stop listening and clear all ephemeral queues.</summary>
    public void Stop()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _queues.Clear();
    }

    /// <summary>Send a direct message to a peer's listener.</summary>
    public static async Task<bool> SendAsync(string peerIp, int peerPort, DirectMessageDto message, TimeSpan? timeout = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
            var url = $"http://{peerIp}:{peerPort}/api/direct/message";
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fetch (and drain) ephemeral messages for a peer from a listener.</summary>
    public static async Task<List<DirectMessageEntry>> FetchAsync(string peerIp, int peerPort, string peerId, TimeSpan? timeout = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
            var url = $"http://{peerIp}:{peerPort}/api/direct/messages/{Uri.EscapeDataString(peerId)}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<DirectMessageEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Quick health check against a peer's listener.</summary>
    public static async Task<bool> IsReachableAsync(string peerIp, int peerPort, TimeSpan? timeout = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"http://{peerIp}:{peerPort}/api/direct/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    var req = ctx.Request;
                    var resp = ctx.Response;

                    if (req.Url == null) { resp.StatusCode = 400; resp.Close(); return; }

                    var path = req.Url.AbsolutePath;

                    // ── POST /api/direct/message ── store ephemeral message
                    if (path.Equals("/api/direct/message", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                        var body = await reader.ReadToEndAsync();
                        var msg = JsonSerializer.Deserialize<DirectMessageDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (msg == null || string.IsNullOrWhiteSpace(msg.ToPeerId))
                        {
                            resp.StatusCode = 400; resp.Close(); return;
                        }

                        var entry = new DirectMessageEntry
                        {
                            FromPeerId = msg.FromPeerId ?? string.Empty,
                            FromPeerName = msg.FromPeerName ?? "Peer",
                            Content = msg.Content,
                            Type = msg.Type,
                            ClientMessageId = msg.ClientMessageId,
                            SentAt = msg.SentAt ?? DateTime.UtcNow,
                        };

                        _queues.GetOrAdd(msg.ToPeerId, _ => new()).Add(entry);
                        resp.StatusCode = 200;
                        var ack = Encoding.UTF8.GetBytes("{\"ok\":true}");
                        resp.ContentType = "application/json";
                        resp.ContentLength64 = ack.Length;
                        await resp.OutputStream.WriteAsync(ack, ct);
                        resp.Close();
                        return;
                    }

                    // ── GET /api/direct/messages/{peerId} ── drain ephemeral queue
                    if (path.StartsWith("/api/direct/messages/", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
                    {
                        var peerId = path["/api/direct/messages/".Length..];
                        var queue = _queues.GetOrAdd(peerId, _ => new());
                        var snapshot = queue.ToList();
                        queue.Clear();

                        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        var bytes = Encoding.UTF8.GetBytes(json);
                        resp.ContentType = "application/json";
                        resp.ContentLength64 = bytes.Length;
                        await resp.OutputStream.WriteAsync(bytes, ct);
                        resp.Close();
                        return;
                    }

                    // ── GET /api/direct/health ──
                    if (path.Equals("/api/direct/health", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                        resp.ContentType = "application/json";
                        resp.ContentLength64 = bytes.Length;
                        await resp.OutputStream.WriteAsync(bytes, ct);
                        resp.Close();
                        return;
                    }

                    resp.StatusCode = 404;
                    resp.Close();
                }
                catch
                {
                    try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                }
            }, ct);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
        _listener.Close();
    }
}

/// <summary>DTO for sending a direct message to a peer listener.</summary>
public record DirectMessageDto(
    string FromPeerId,
    string FromPeerName,
    string ToPeerId,
    string Content,
    string Type = "Text",
    string? ClientMessageId = null,
    DateTime? SentAt = null
);

/// <summary>Ephemeral in-memory message entry (never written to DB).</summary>
public sealed class DirectMessageEntry
{
    public string FromPeerId { get; set; } = string.Empty;
    public string FromPeerName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "Text";
    public string? ClientMessageId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
