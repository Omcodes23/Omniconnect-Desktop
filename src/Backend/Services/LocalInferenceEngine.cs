using System.Runtime.InteropServices;
using System.Text;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Runs a GGUF language model in-process through the embedded llama.cpp shim.
///
/// This is the only inference path in the application: there is no Ollama, no
/// <c>llama-server</c>, no spawned process and no outbound API call. The native
/// runtime is compiled into the app (see <c>native/omni_llama</c>) and the model
/// file is read straight from disk.
///
/// Registered as a singleton so the model stays resident (loading a GGUF takes
/// seconds and hundreds of megabytes of RAM, so we never want to do it
/// per-request). All mutations of the native handle are serialized.
/// </summary>
public sealed class LocalInferenceEngine : IDisposable
{
    /// <summary>Human-readable message shown whenever the native library is absent.</summary>
    public const string MissingRuntimeMessage =
        "Local AI runtime not installed. Build native/omni_llama (see native/README.md) " +
        "and place omni_llama.dll next to the application.";

    private readonly ILogger<LocalInferenceEngine> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _handleLock = new();

    private IntPtr _handle = IntPtr.Zero;
    private string? _loadedPath;
    private int _contextSize;

    public LocalInferenceEngine(ILogger<LocalInferenceEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>Path of the currently loaded GGUF, or null when nothing is loaded.</summary>
    public string? LoadedModelPath
    {
        get { lock (_handleLock) { return _loadedPath; } }
    }

    /// <summary>Context window (tokens) of the loaded model, or 0 when not loaded.</summary>
    public int ContextSize
    {
        get { lock (_handleLock) { return _contextSize; } }
    }

    /// <summary>True when a model is resident and ready to serve requests.</summary>
    public bool IsReady
    {
        get { lock (_handleLock) { return _handle != IntPtr.Zero; } }
    }

    /// <summary>
    /// Ensure <paramref name="ggufPath"/> is the loaded model, loading it if
    /// necessary. Reloading only happens when the path actually changes.
    /// </summary>
    public async Task<bool> EnsureLoadedAsync(string ggufPath, int nCtx = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ggufPath) || !File.Exists(ggufPath))
        {
            _logger.LogWarning("Model file not found: {Path}", ggufPath);
            return false;
        }

        lock (_handleLock)
        {
            if (_handle != IntPtr.Zero && string.Equals(_loadedPath, ggufPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have loaded it while we waited.
            lock (_handleLock)
            {
                if (_handle != IntPtr.Zero && string.Equals(_loadedPath, ggufPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            UnloadCore();

            _logger.LogInformation("Loading local model {Path} (n_ctx={Ctx})", ggufPath, nCtx);

            var err = new byte[512];
            IntPtr handle;
            try
            {
                handle = await Task.Run(() => LlamaNative.Load(ggufPath, nCtx, 0, err, err.Length), ct)
                    .ConfigureAwait(false);
            }
            catch (DllNotFoundException ex)
            {
                _logger.LogError(ex, "Embedded llama.cpp library is missing");
                throw new InvalidOperationException(MissingRuntimeMessage, ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                _logger.LogError(ex, "Embedded llama.cpp library is out of date");
                throw new InvalidOperationException(
                    "The embedded AI runtime is out of date. Rebuild native/omni_llama (see native/README.md).", ex);
            }

            if (handle == IntPtr.Zero)
            {
                var message = Decode(err);
                _logger.LogError("Failed to load model {Path}: {Message}", ggufPath, message);
                throw new InvalidOperationException($"Could not load the local model: {message}");
            }

            lock (_handleLock)
            {
                _handle = handle;
                _loadedPath = ggufPath;
                _contextSize = LlamaNative.ContextSize(handle);
            }

            _logger.LogInformation("Local model ready (context {Ctx} tokens)", _contextSize);
            return true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Run a chat completion and return the full reply text.
    /// <paramref name="onToken"/> (optional) receives incrementally decoded text
    /// as tokens arrive; it is invoked on the calling thread while the native
    /// call blocks.
    /// </summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens = 512,
        float temperature = 0.7f,
        float topP = 0.9f,
        int topK = 40,
        Action<string>? onToken = null,
        CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        var handle = RequireHandle();
        maxTokens = Math.Clamp(maxTokens, 1, 8192);

        // oc_chat blocks entirely inside native code, so passing the token to
        // Task.Run would only stop us *waiting* — it cannot stop generation.
        // Bridge the token to the native cancel flag, which is safe to set from
        // any thread and is cleared automatically by the next generation.
        using var cancellation = ct.Register(static state =>
        {
            try { ((LocalInferenceEngine)state!).Cancel(); }
            catch { /* cancellation must never fault the token source */ }
        }, this);

        // Marshal the message list into unmanaged UTF-8 C strings.
        var native = new LlamaNative.OcMessage[messages.Count];
        var allocated = new IntPtr[messages.Count * 2];
        try
        {
            for (int i = 0; i < messages.Count; i++)
            {
                var role = Marshal.StringToCoTaskMemUTF8(messages[i].Role ?? "user");
                var content = Marshal.StringToCoTaskMemUTF8(messages[i].Content ?? string.Empty);
                allocated[i * 2] = role;
                allocated[i * 2 + 1] = content;
                native[i] = new LlamaNative.OcMessage { Role = role, Content = content };
            }

            // Token pieces are usually a handful of bytes; leave generous room,
            // and always keep the authoritative return buffer large enough that
            // a full reply fits even without relying on the streaming callback.
            int outLen = Math.Clamp(maxTokens * 64 + 4096, 16 * 1024, 8 * 1024 * 1024);
            var outBuf = new byte[outLen];

            LlamaNative.TokenCallback? callback = null;
            if (onToken is not null)
            {
                var decoder = Encoding.UTF8.GetDecoder();
                callback = (piece, pieceLen, _) =>
                {
                    if (pieceLen <= 0) return;
                    try
                    {
                        var bytes = new byte[pieceLen];
                        Marshal.Copy(piece, bytes, 0, pieceLen);
                        int charCount = decoder.GetCharCount(bytes, 0, pieceLen, flush: false);
                        if (charCount <= 0) return;
                        var chars = new char[charCount];
                        decoder.GetChars(bytes, 0, pieceLen, chars, 0, flush: false);
                        onToken(new string(chars));
                    }
                    catch (Exception ex)
                    {
                        // Never let a UI-side callback fault tear down the native call.
                        _logger.LogDebug(ex, "Token callback threw; ignoring");
                    }
                };
            }

            int written = await Task.Run(
                () => LlamaNative.Chat(
                    handle, native, native.Length, maxTokens,
                    temperature, topP, topK, 0u,
                    outBuf, outBuf.Length, callback, IntPtr.Zero),
                ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                // Either the client went away or shutdown was requested; surface
                // it as a cancellation rather than returning a partial reply.
                throw new OperationCanceledException(ct);
            }

            if (written < 0)
            {
                throw new InvalidOperationException($"Local inference failed (code {written}).");
            }

            return Encoding.UTF8.GetString(outBuf, 0, Math.Min(written, outBuf.Length));
        }
        finally
        {
            foreach (var ptr in allocated)
            {
                if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
            }
        }
    }

    /// <summary>
    /// Produce a mean-pooled embedding for <paramref name="text"/>.
    /// Returns an empty array when the model has no embedding dimension.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<float>();

        var handle = RequireHandle();
        int dim = LlamaNative.EmbedDim(handle);
        if (dim <= 0) return Array.Empty<float>();

        var outBuf = new float[dim];
        int written = await Task.Run(() => LlamaNative.Embed(handle, text, outBuf, dim), ct)
            .ConfigureAwait(false);

        if (written <= 0) return Array.Empty<float>();
        if (written < dim)
        {
            var trimmed = new float[written];
            Array.Copy(outBuf, trimmed, written);
            return trimmed;
        }
        return outBuf;
    }

    /// <summary>
    /// Ask any in-flight generation to stop. Safe to call from any thread; the
    /// flag is cleared automatically at the start of the next generation.
    /// </summary>
    public void Cancel()
    {
        IntPtr handle;
        lock (_handleLock) { handle = _handle; }
        if (handle != IntPtr.Zero)
        {
            try { LlamaNative.Cancel(handle, 1); }
            catch (Exception ex) { _logger.LogDebug(ex, "Cancel failed; ignoring"); }
        }
    }

    /// <summary>Release the loaded model (if any). The next call reloads it.</summary>
    public void Unload()
    {
        _loadLock.Wait();
        try { UnloadCore(); }
        finally { _loadLock.Release(); }
    }

    private void UnloadCore()
    {
        IntPtr handle;
        lock (_handleLock)
        {
            handle = _handle;
            _handle = IntPtr.Zero;
            _loadedPath = null;
            _contextSize = 0;
        }

        if (handle != IntPtr.Zero)
        {
            try { LlamaNative.Free(handle); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to free model handle"); }
        }
    }

    private IntPtr RequireHandle()
    {
        lock (_handleLock)
        {
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("No local model is loaded.");
            }
            return _handle;
        }
    }

    private static string Decode(byte[] buffer)
    {
        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0) end = buffer.Length;
        return end == 0 ? "unknown error" : Encoding.UTF8.GetString(buffer, 0, end);
    }

    public void Dispose()
    {
        try { Unload(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Dispose: unload failed; ignoring"); }
        _loadLock.Dispose();
    }
}
