using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Owns the on-disk GGUF model files: what is downloaded, what is downloading,
/// and which one is selected. This is the only component that touches the
/// network (a one-time HTTPS download from the HuggingFace CDN, no API key).
///
/// Replaces the old <c>OllamaService</c>: instead of asking a local server to
/// pull a model, we download the file ourselves straight to
/// <c>%LOCALAPPDATA%\OmniConnect\models</c> and hand the path to the embedded
/// inference engine.
/// </summary>
public sealed class ModelManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<ModelManager> _logger;
    private readonly HttpClient _http;
    private readonly string _modelsDir;
    private readonly string _activeStateFile;

    private readonly ConcurrentDictionary<string, DownloadState> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeLock = new();
    private string? _activeModelId;

    public ModelManager(ILogger<ModelManager> logger)
    {
        _logger = logger;

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        _modelsDir = Path.Combine(root, "OmniConnect", "models");
        Directory.CreateDirectory(_modelsDir);
        _activeStateFile = Path.Combine(_modelsDir, "active-model.json");

        _http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan, // large files: no per-request timeout
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OmniConnect/1.0");
        // Let the server use multiple ranges / keep-alive normally.

        _activeModelId = LoadActiveFromDisk();
        _logger.LogInformation("Model directory: {Dir}", _modelsDir);
    }

    /// <summary>Absolute folder where GGUF files live.</summary>
    public string ModelsDirectory => _modelsDir;

    /// <summary>
    /// The currently selected model id, falling back to the catalog default when
    /// nothing valid has been chosen yet.
    /// </summary>
    public string ActiveModelId
    {
        get
        {
            lock (_activeLock)
            {
                if (!string.IsNullOrEmpty(_activeModelId) && ModelCatalog.Find(_activeModelId!) is not null)
                {
                    return _activeModelId!;
                }
                return ModelCatalog.Default.Id;
            }
        }
    }

    /// <summary>Path to a catalogued model file (whether or not it exists yet).</summary>
    public string PathFor(ModelCatalogEntry entry) => Path.Combine(_modelsDir, entry.FileName);

    /// <summary>
    /// Path to the selected model if it is downloaded, otherwise null. The
    /// companion-mode bot and AI features use this to decide whether they can
    /// run without asking the user anything.
    /// </summary>
    public string? GetActiveModelPath()
    {
        var entry = ModelCatalog.Find(ActiveModelId);
        if (entry is null) return null;
        var path = PathFor(entry);
        return IsComplete(path, entry) ? path : null;
    }

    /// <summary>Path to a specific model if downloaded, otherwise null.</summary>
    public string? GetModelPath(string modelId)
    {
        var entry = ModelCatalog.Find(modelId);
        if (entry is null) return null;
        var path = PathFor(entry);
        return IsComplete(path, entry) ? path : null;
    }

    /// <summary>Select the active model. Ignored if the id is not in the catalog.</summary>
    public bool SetActiveModel(string modelId)
    {
        if (ModelCatalog.Find(modelId) is null) return false;

        lock (_activeLock) { _activeModelId = modelId; }
        TrySaveActiveToDisk(modelId);
        _logger.LogInformation("Active local model set to {Model}", modelId);
        return true;
    }

    /// <summary>Snapshot of the catalog with per-model download state.</summary>
    public Task<AiModelListResponse> ListAsync()
    {
        var models = new List<AiModelDto>(ModelCatalog.All.Count);

        foreach (var entry in ModelCatalog.All)
        {
            var path = PathFor(entry);
            bool complete = IsComplete(path, entry);
            _downloads.TryGetValue(entry.Id, out var state);

            string status;
            bool downloading = false;
            double progress = complete ? 1d : 0d;

            if (state is { Running: true })
            {
                status = "downloading";
                downloading = true;
                long total = state.TotalBytes > 0 ? state.TotalBytes : entry.SizeBytes;
                progress = total > 0 ? Math.Clamp((double)state.BytesReceived / total, 0d, 1d) : 0d;
            }
            else if (state is { Error: not null } && !complete)
            {
                status = "error";
            }
            else if (complete)
            {
                status = "downloaded";
            }
            else
            {
                status = "not-downloaded";
            }

            models.Add(new AiModelDto(
                entry.Id,
                entry.DisplayName,
                entry.Description,
                entry.Family,
                entry.Quantization,
                entry.SizeBytes,
                ModelCatalog.FormatSize(entry.SizeBytes),
                complete,
                downloading,
                progress,
                status));
        }

        return Task.FromResult(new AiModelListResponse(models, ActiveModelId));
    }

    /// <summary>
    /// Kick off a background download. Idempotent: calling it while the same
    /// model is already downloading is a no-op. Progress is observed by polling
    /// <see cref="ListAsync"/>.
    /// </summary>
    public AiModelDownloadResponse StartDownload(string modelId)
    {
        var entry = ModelCatalog.Find(modelId)
            ?? throw new ArgumentException($"Unknown model '{modelId}'.", nameof(modelId));

        var path = PathFor(entry);
        if (IsComplete(path, entry))
        {
            return new AiModelDownloadResponse(entry.Id, "downloaded");
        }

        var state = _downloads.GetOrAdd(entry.Id, _ => new DownloadState());
        lock (state.Gate)
        {
            if (state.Running)
            {
                return new AiModelDownloadResponse(entry.Id, "downloading");
            }
            state.Running = true;
            state.Error = null;
            state.BytesReceived = existingPartialLength(entry);
            state.TotalBytes = entry.SizeBytes;
        }

        _ = Task.Run(() => DownloadAsync(entry, state));
        return new AiModelDownloadResponse(entry.Id, "downloading");
    }

    /// <summary>Delete a downloaded model file (cancelling it first if needed).</summary>
    public async Task<bool> DeleteAsync(string modelId)
    {
        var entry = ModelCatalog.Find(modelId);
        if (entry is null) return false;

        if (_downloads.TryGetValue(entry.Id, out var state))
        {
            lock (state.Gate) { state.CancelRequested = true; }
            // Give the download loop a moment to observe the flag and close its file.
            for (int i = 0; i < 20 && state.Running; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            _downloads.TryRemove(entry.Id, out _);
        }

        bool removed = false;
        foreach (var candidate in new[] { PathFor(entry), PathFor(entry) + ".part" })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                    removed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {Path}", candidate);
            }
        }

        if (removed) _logger.LogInformation("Deleted local model {Model}", entry.Id);
        return removed;
    }

    // ── Internals ────────────────────────────────────────────────────────

    private long existingPartialLength(ModelCatalogEntry entry)
    {
        var part = PathFor(entry) + ".part";
        try { return File.Exists(part) ? new FileInfo(part).Length : 0; }
        catch { return 0; }
    }

    private async Task DownloadAsync(ModelCatalogEntry entry, DownloadState state)
    {
        var finalPath = PathFor(entry);
        var partPath = finalPath + ".part";
        _logger.LogInformation("Downloading model {Model} from {Url}", entry.Id, entry.DownloadUrl);

        try
        {
            long offset = existingPartialLength(entry);

            using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
                .ConfigureAwait(false);

            // If the server ignored our Range request, start over rather than corrupt the file.
            if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                _logger.LogInformation("Server did not honour resume for {Model}; restarting", entry.Id);
                offset = 0;
                TryDeleteFile(partPath);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Download failed with HTTP {(int)response.StatusCode}.");
            }

            long contentLength = response.Content.Headers.ContentLength ?? (entry.SizeBytes - offset);
            long total = offset + contentLength;

            lock (state.Gate)
            {
                state.BytesReceived = offset;
                state.TotalBytes = total > 0 ? total : entry.SizeBytes;
            }

            await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var dest = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.None,
                         bufferSize: 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                long sinceReport = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    bool cancelled;
                    lock (state.Gate) { cancelled = state.CancelRequested; }
                    if (cancelled)
                    {
                        throw new OperationCanceledException("Download cancelled by user.");
                    }

                    await dest.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

                    lock (state.Gate) { state.BytesReceived += read; }
                    sinceReport += read;
                    if (sinceReport >= 4L * 1024 * 1024)
                    {
                        sinceReport = 0;
                        _logger.LogDebug("Model {Model}: {Mb:F1} MB / {Total:F1} MB", entry.Id,
                            state.BytesReceived / 1048576d, state.TotalBytes / 1048576d);
                    }
                }
                await dest.FlushAsync().ConfigureAwait(false);
            }

            // Promote the .part file only once complete, so a half-written file
            // is never mistaken for a usable model.
            if (File.Exists(finalPath)) TryDeleteFile(finalPath);
            File.Move(partPath, finalPath);

            if (!IsComplete(finalPath, entry))
            {
                throw new InvalidOperationException("Downloaded file failed verification.");
            }

            lock (state.Gate)
            {
                state.Running = false;
                state.Error = null;
                state.BytesReceived = state.TotalBytes;
            }

            _logger.LogInformation("Model {Model} downloaded to {Path}", entry.Id, finalPath);
        }
        catch (Exception ex)
        {
            bool cancelled;
            lock (state.Gate)
            {
                cancelled = state.CancelRequested;
                state.Running = false;
                state.Error = ex.Message;
            }
            if (cancelled)
            {
                _logger.LogInformation("Download of {Model} cancelled", entry.Id);
            }
            else
            {
                _logger.LogError(ex, "Download of {Model} failed", entry.Id);
            }
        }
    }

    /// <summary>
    /// A model file is usable when it exists, is not obviously truncated, and
    /// starts with the GGUF magic bytes. Hashes are not published for every
    /// catalog entry, so we rely on size + magic rather than a checksum.
    /// </summary>
    private static bool IsComplete(string path, ModelCatalogEntry entry)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            // Allow a small tolerance in case the published size is approximate.
            long floor = (long)(entry.SizeBytes * 0.95);
            if (info.Length < floor) return false;

            using var fs = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (fs.Read(magic) != 4) return false;
            return magic[0] == (byte)'G' && magic[1] == (byte)'G' && magic[2] == (byte)'U' && magic[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    private string? LoadActiveFromDisk()
    {
        try
        {
            if (!File.Exists(_activeStateFile)) return null;
            using var fs = File.OpenRead(_activeStateFile);
            var doc = JsonSerializer.Deserialize<ActiveModelState>(fs);
            return doc?.ModelId;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read active-model.json; using default");
            return null;
        }
    }

    private void TrySaveActiveToDisk(string modelId)
    {
        try
        {
            using var fs = File.Create(_activeStateFile);
            JsonSerializer.Serialize(fs, new ActiveModelState(modelId), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist active-model.json");
        }
    }

    private void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete {Path}", path); }
    }

    private sealed record ActiveModelState(string ModelId);

    private sealed class DownloadState
    {
        public readonly object Gate = new();
        public bool Running;
        public bool CancelRequested;
        public long BytesReceived;
        public long TotalBytes;
        public string? Error;
    }
}
