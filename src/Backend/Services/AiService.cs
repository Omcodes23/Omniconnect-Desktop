using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

/// <summary>
/// All AI features, running entirely on-device.
///
/// There is exactly one inference backend: the embedded llama.cpp runtime
/// (<see cref="LocalInferenceEngine"/>). No remote providers, no local server,
/// no spawned processes. The only external interaction in the whole AI stack is
/// the one-time model download handled by <see cref="ModelManager"/>.
///
/// Conversation history is still persisted in the shared database so the
/// desktop client (and older clients) keep working exactly as before.
/// </summary>
public class AiService
{
    /// <summary>Value reported in every response's <c>Provider</c> field.</summary>
    public const string LocalProvider = "local";

    private readonly AppDbContext _db;
    private readonly LocalInferenceEngine _engine;
    private readonly ModelManager _models;
    private readonly ILogger<AiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public AiService(AppDbContext db, LocalInferenceEngine engine, ModelManager models, ILogger<AiService> logger)
    {
        _db = db;
        _engine = engine;
        _models = models;
        _logger = logger;
    }

    // ── AI Chat ──────────────────────────────────────────────────────────

    public async Task<AiChatResponse> ChatAsync(Guid userId, AiChatRequest request, CancellationToken ct = default)
    {
        var conversation = await GetOrCreateConversationAsync(userId, request);
        var history = GetHistory(conversation.MessagesJson, request.History);

        history.Add(new AiMessageDto("user", request.Message, DateTime.UtcNow));

        // No model downloaded yet: answer with guidance rather than failing, so
        // every client (desktop, mobile, companion bot) shows a usable message.
        var modelId = ResolveModelId(request.Model);
        if ((_models.GetModelPath(modelId) ?? _models.GetActiveModelPath()) is null)
        {
            var hint = NoModelSelected().Message;
            history.Add(new AiMessageDto("assistant", hint, DateTime.UtcNow));
            conversation.MessagesJson = JsonSerializer.Serialize(history, JsonOptions);
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return new AiChatResponse(conversation.Id, hint, 0, history);
        }

        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => (Role: NormalizeRole(m.Role), Content: m.Content))
            .ToList();

        var reply = await RunAsync(request.Model, messages, maxTokens: 512, temperature: 0.7f, ct: ct);
        var tokensUsed = ApproximateTokens(reply);

        history.Add(new AiMessageDto("assistant", reply, DateTime.UtcNow));

        conversation.MessagesJson = JsonSerializer.Serialize(history, JsonOptions);
        conversation.TokenCount += tokensUsed;
        conversation.ModelName = ResolveModelId(request.Model);
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new AiChatResponse(conversation.Id, reply, tokensUsed, history);
    }

    // ── Translation ──────────────────────────────────────────────────────

    public async Task<AiTranslateResponse> TranslateAsync(AiTranslateRequest request, CancellationToken ct = default)
    {
        var messages = new List<(string, string)>
        {
            ("system", "You are a professional translator. Return only the translated text, with no explanations or extra commentary."),
            ("user", $"Translate the following text from {request.SourceLanguage} to {request.TargetLanguage}.\n\nText: {request.Text}"),
        };

        var translated = await RunAsync(null, messages, maxTokens: 1024, temperature: 0.2f, ct: ct);
        return new AiTranslateResponse(translated.Trim(), request.SourceLanguage, LocalProvider);
    }

    // ── Summarization ────────────────────────────────────────────────────

    public async Task<AiSummarizeResponse> SummarizeAsync(AiSummarizeRequest request, CancellationToken ct = default)
    {
        var lengthInstruction = request.MaxLength.HasValue
            ? $"Summarize in approximately {request.MaxLength} words."
            : "Provide a concise summary.";

        var messages = new List<(string, string)>
        {
            ("system", "You are a summarization assistant. Provide clear, concise summaries."),
            ("user", $"{lengthInstruction}\n\nText: {request.Text}"),
        };

        var summary = (await RunAsync(null, messages, maxTokens: 512, temperature: 0.3f, ct: ct)).Trim();
        return new AiSummarizeResponse(summary, request.Text.Length, summary.Length);
    }

    // ── OCR ──────────────────────────────────────────────────────────────
    // The local text models are not vision models, so this deliberately does
    // not pretend to read pixels. It is kept as a stable endpoint that returns
    // a clear, honest message instead of failing or fabricating text.

    public Task<AiOcrResponse> PerformOcrAsync(AiOcrRequest request)
    {
        const string message =
            "OCR is unavailable with the current local text model. " +
            "Install a vision-capable model to extract text from images.";

        _logger.LogInformation("OCR requested but no vision model is available");
        return Task.FromResult(new AiOcrResponse(message, 0f, LocalProvider));
    }

    // ── Smart Reply ─────────────────────────────────────────────────────

    public async Task<AiSmartReplyResponse> GenerateSmartRepliesAsync(AiSmartReplyRequest request, CancellationToken ct = default)
    {
        var historyText = request.ConversationHistory is { Count: > 0 }
            ? string.Join("\n", request.ConversationHistory)
            : "";

        var fullContext = string.IsNullOrEmpty(historyText)
            ? $"Last message: {request.ContextMessage}"
            : $"Conversation history:\n{historyText}\n\nLast message: {request.ContextMessage}";

        var messages = new List<(string, string)>
        {
            ("system", "You are a smart reply assistant. Generate concise, contextually appropriate responses."),
            ("user", $"{fullContext}\n\nGenerate 3 short reply suggestions. Return only a JSON array of strings, nothing else."),
        };

        var reply = await RunAsync(null, messages, maxTokens: 256, temperature: 0.8f, ct: ct);

        List<string> suggestions;
        try
        {
            suggestions = JsonSerializer.Deserialize<List<string>>(reply, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            suggestions = reply.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('-', '*', '[', '"').TrimEnd('"', ',', ']').Trim())
                .Where(s => s.Length > 0)
                .Take(3)
                .ToList();
        }

        if (suggestions.Count == 0)
        {
            suggestions = ["I'll get back to you on that.", "Sounds good!", "Let me check."];
        }

        return new AiSmartReplyResponse(suggestions.Take(3).ToList());
    }

    // ── Embeddings ─────────────────────────────────────────────────────

    public async Task<AiEmbeddingResponse> GenerateEmbeddingAsync(AiEmbeddingRequest request, CancellationToken ct = default)
    {
        var path = _models.GetActiveModelPath()
            ?? throw NoModelSelected();

        await _engine.EnsureLoadedAsync(path, ct: ct);
        var embedding = await _engine.EmbedAsync(request.Text, ct);
        return new AiEmbeddingResponse(embedding, embedding.Length, LocalProvider);
    }

    // ── Conversation Management ─────────────────────────────────────────

    public async Task<AiConversationListResponse> GetConversationsAsync(Guid userId, int skip = 0, int take = 20)
    {
        var query = _db.Set<AiConversation>()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt);

        var total = await query.CountAsync();
        var conversations = await query
            .Skip(skip)
            .Take(take)
            .Select(c => new AiConversationDto(c.Id, c.Provider, c.ModelName, c.Title, c.TokenCount, c.CreatedAt, c.UpdatedAt))
            .ToListAsync();

        return new AiConversationListResponse(conversations, total);
    }

    public async Task<AiConversationDetailResponse?> GetConversationDetailAsync(Guid userId, Guid conversationId)
    {
        var conversation = await _db.Set<AiConversation>()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

        if (conversation is null) return null;

        var messages = JsonSerializer.Deserialize<List<AiMessageDto>>(conversation.MessagesJson, JsonOptions) ?? [];
        var dto = new AiConversationDto(
            conversation.Id, conversation.Provider, conversation.ModelName,
            conversation.Title, conversation.TokenCount,
            conversation.CreatedAt, conversation.UpdatedAt);

        return new AiConversationDetailResponse(dto, messages);
    }

    public async Task<bool> DeleteConversationAsync(Guid userId, Guid conversationId)
    {
        var conversation = await _db.Set<AiConversation>()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

        if (conversation is null) return false;

        _db.Set<AiConversation>().Remove(conversation);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Local engine plumbing ────────────────────────────────────────────

    /// <summary>
    /// Ensure the selected model is loaded, then run a completion.
    /// Shared by chat, translation, summarization and smart replies.
    /// </summary>
    private async Task<string> RunAsync(
        string? modelId,
        List<(string Role, string Content)> messages,
        int maxTokens,
        float temperature,
        CancellationToken ct = default)
    {
        var id = ResolveModelId(modelId);
        var path = _models.GetModelPath(id) ?? _models.GetActiveModelPath();
        if (path is null)
        {
            throw NoModelSelected();
        }

        await _engine.EnsureLoadedAsync(path, ct: ct);
        return await _engine.ChatAsync(messages, maxTokens, temperature, ct: ct);
    }

    private string ResolveModelId(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && ModelCatalog.Find(requested!) is not null)
        {
            return requested!;
        }
        return _models.ActiveModelId;
    }

    private static InvalidOperationException NoModelSelected() =>
        new("No local model is ready. Open AI chat, select a model and wait for its download to finish.");

    private static string NormalizeRole(string role) => role?.ToLowerInvariant() switch
    {
        "assistant" => "assistant",
        "system" => "system",
        _ => "user",
    };

    private static int ApproximateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<AiConversation> GetOrCreateConversationAsync(Guid userId, AiChatRequest request)
    {
        AiConversation conversation;

        if (request.ConversationId.HasValue)
        {
            conversation = await _db.Set<AiConversation>()
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId.Value && c.UserId == userId)
                ?? throw new InvalidOperationException("Conversation not found.");
        }
        else
        {
            conversation = new AiConversation
            {
                UserId = userId,
                Provider = LocalProvider,
                ModelName = ResolveModelId(request.Model),
                Title = request.Message.Length > 80
                    ? request.Message[..80] + "..."
                    : request.Message,
                MessagesJson = "[]",
            };

            _db.Set<AiConversation>().Add(conversation);
            await _db.SaveChangesAsync();
        }

        return conversation;
    }

    private static List<AiMessageDto> GetHistory(string existingJson, List<AiMessageDto>? requestHistory)
    {
        if (requestHistory is { Count: > 0 })
            return [.. requestHistory];

        try
        {
            return JsonSerializer.Deserialize<List<AiMessageDto>>(existingJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
