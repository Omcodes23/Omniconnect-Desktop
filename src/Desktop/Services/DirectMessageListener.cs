using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.Services;

/// <summary>
/// Lightweight in-process HTTP server that lets this desktop accept direct
/// LAN messages from peers without going through the backend server.
///
/// Uses a raw <see cref="TcpListener"/> rather than <see cref="HttpListener"/>:
/// HttpListener needs a URL ACL (\"netsh http add urlacl\") for a non-elevated
/// process to bind a wildcard prefix, which made direct chat silently fail for
/// normal users. TcpListener needs no elevation and no machine configuration.
///
/// All messages are ephemeral: kept in memory only, never written to the DB,
/// and cleared when the listener stops or a message queue is drained.
/// </summary>
public sealed class DirectMessageListener : IDisposable
{
    private readonly ConcurrentDictionary<string, List<DirectMessageEntry>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private TcpListener? _listener;
    private int _port = 5001;
    private bool _disposed;

    public int Port => _port;

    /// <summary>True while the socket is bound and accepting connections.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _listener != null; }
    }

    /// <summary>Start listening on the given port (default 5001). Idempotent.</summary>
    public void Start(int port = 5001)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectMessageListener));

        lock (_gate)
        {
            if (_listener != null) return;

            var listener = new TcpListener(IPAddress.Any, port);
            // Allow a quick restart while old sockets sit in TIME_WAIT.
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start(64);

            _listener = listener;
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync(listener, _cts.Token);
        }

        Debug.WriteLine($"[DirectMessageListener] Listening on 0.0.0.0:{_port}");
    }

    /// <summary>Stop listening and clear all ephemeral queues.</summary>
    public void Stop()
    {
        TcpListener? listener;
        lock (_gate)
        {
            listener = _listener;
            _listener = null;
        }

        try { listener?.Stop(); } catch { }
        _queues.Clear();
    }

    /// <summary>Drain queued messages for [peerId] from local memory.</summary>
    public List<DirectMessageEntry> FetchQueued(string peerId)
    {
        var queue = _queues.GetOrAdd(peerId, _ => new List<DirectMessageEntry>());
        lock (queue)
        {
            var snapshot = queue.ToList();
            queue.Clear();
            return snapshot;
        }
    }

    /// <summary>Send a direct message to a peer's listener.</summary>
    public static async Task<bool> SendAsync(string peerIp, int peerPort, DirectMessageDto message, TimeSpan? timeout = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
            var url = $"http://{peerIp}:{peerPort}/api/direct/message";
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content);
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
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<DirectMessageEntry>();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<DirectMessageEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<DirectMessageEntry>();
        }
        catch
        {
            return new List<DirectMessageEntry>();
        }
    }

    /// <summary>Quick health check against a peer's listener.</summary>
    public static async Task<bool> IsReachableAsync(string peerIp, int peerPort, TimeSpan? timeout = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync($"http://{peerIp}:{peerPort}/api/direct/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Minimal HTTP/1.1 server ───────────────────────────────────────────

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch { continue; }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var request = await ReadRequestAsync(stream, ct);
                if (request is null)
                {
                    await WriteResponseAsync(stream, 400, "{\"error\":\"bad request\"}", ct);
                    return;
                }

                var (method, path, body) = request.Value;
                var query = path.IndexOf('?');
                if (query >= 0) path = path[..query];

                // ── POST /api/direct/message ── store ephemeral message
                if (path.Equals("/api/direct/message", StringComparison.OrdinalIgnoreCase) &&
                    method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = JsonSerializer.Deserialize<DirectMessageDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (msg == null || string.IsNullOrWhiteSpace(msg.ToPeerId))
                    {
                        await WriteResponseAsync(stream, 400, "{\"error\":\"toPeerId required\"}", ct);
                        return;
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

                    var queue = _queues.GetOrAdd(msg.ToPeerId, _ => new List<DirectMessageEntry>());
                    lock (queue) queue.Add(entry);

                    await WriteResponseAsync(stream, 200, "{\"ok\":true}", ct);
                    return;
                }

                // ── GET /api/direct/messages/{peerId} ── drain ephemeral queue
                if (path.StartsWith("/api/direct/messages/", StringComparison.OrdinalIgnoreCase) &&
                    method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    var peerId = Uri.UnescapeDataString(path["/api/direct/messages/".Length..]);
                    var snapshot = FetchQueued(peerId);
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await WriteResponseAsync(stream, 200, json, ct);
                    return;
                }

                // ── GET /api/direct/health ──
                if (path.Equals("/api/direct/health", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 200, "{\"ok\":true}", ct);
                    return;
                }

                await WriteResponseAsync(stream, 404, "{\"error\":\"not found\"}", ct);
            }
            catch
            {
                try { await WriteResponseAsync(stream, 500, "{\"error\":\"server error\"}", ct); } catch { }
            }
        }
    }

    private static async Task<(string Method, string Path, string Body)?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
            if (read <= 0) return null;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > 64 * 1024) return null;

            var bytes = buffer.GetBuffer();
            headerEnd = FindHeaderEnd(bytes, (int)buffer.Length);
        }

        var all = buffer.ToArray();
        var headerText = Encoding.UTF8.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines.Length > 0 ? lines[0].Split(' ', 3) : Array.Empty<string>();
        if (requestLine.Length < 2) return null;

        int contentLength = 0;
        bool expectContinue = false;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(value, out contentLength);
            else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                expectContinue = true;
        }

        if (expectContinue)
        {
            var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            await stream.WriteAsync(interim, ct);
            await stream.FlushAsync(ct);
        }

        var bodyBytes = all.AsSpan(headerEnd + 4).ToArray();
        if (bodyBytes.Length < contentLength)
        {
            var combined = new byte[contentLength];
            var copied = Math.Min(bodyBytes.Length, contentLength);
            Array.Copy(bodyBytes, combined, copied);

            int offset = copied;
            while (offset < contentLength)
            {
                int read = await stream.ReadAsync(combined.AsMemory(offset, contentLength - offset), ct);
                if (read <= 0) break;
                offset += read;
            }
            bodyBytes = combined;
        }

        var body = contentLength > 0
            ? Encoding.UTF8.GetString(bodyBytes, 0, Math.Min(contentLength, bodyBytes.Length))
            : string.Empty;

        return (requestLine[0], requestLine[1], body);
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (int i = 3; i < length; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                return i - 3;
        }
        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "Internal Server Error",
        };

        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(head, ct);
        if (bytes.Length > 0) await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Cancel();
        _cts.Dispose();
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
