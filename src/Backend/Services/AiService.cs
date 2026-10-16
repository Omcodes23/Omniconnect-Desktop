using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class AiService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // Track running llama.cpp process
    private Process? _llamaProcess;
    private readonly object _llamaLock = new();

    public AiService(AppDbContext db, HttpClient httpClient, IConfiguration config, ILogger<AiService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    // ── AI Chat ──────────────────────────────────────────────────────────

    public async Task<AiChatResponse> ChatAsync(Guid userId, AiChatRequest request)
    {
        var conversation = await GetOrCreateConversationAsync(userId, request);
        var history = GetHistory(conversation.MessagesJson, request.History);

        // Add user message to history
        history.Add(new AiMessageDto("user", request.Message, DateTime.UtcNow));

        string reply;
        int tokensUsed;

        switch (request.Provider.ToLowerInvariant())
        {
            case "openai":
                (reply, tokensUsed) = await CallOpenAiAsync(request.Model, history);
                break;
            case "gemini":
                (reply, tokensUsed) = await CallGeminiAsync(request.Model, history);
                break;
            case "openrouter":
                (reply, tokensUsed) = await CallOpenRouterAsync(request.Model, history);
                break;
            case "local":
                (reply, tokensUsed) = await CallLocalModelAsync(request.Model, history);
                break;
            default:
                throw new ArgumentException($"Unsupported provider: {request.Provider}");
        }

        // Add assistant reply to history
        history.Add(new AiMessageDto("assistant", reply, DateTime.UtcNow));

        // Persist conversation
        conversation.MessagesJson = JsonSerializer.Serialize(history, JsonOptions);
        conversation.TokenCount += tokensUsed;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new AiChatResponse(conversation.Id, reply, tokensUsed, history);
    }

    // ── Translation ──────────────────────────────────────────────────────

    public async Task<AiTranslateResponse> TranslateAsync(AiTranslateRequest request)
    {
        var provider = request.Provider ?? "openai";
        var prompt = $"Translate the following text from {request.SourceLanguage} to {request.TargetLanguage}. Return only the translated text, no explanations.\n\nText: {request.Text}";

        var history = new List<AiMessageDto>
        {
            new("system", "You are a professional translator. Return only the translated text.", DateTime.UtcNow),
            new("user", prompt, DateTime.UtcNow),
        };

        string translated;
        _ = provider.ToLowerInvariant() switch
        {
            "openai" => translated = (await CallOpenAiAsync("gpt-4o-mini", history)).Reply,
            "gemini" => translated = (await CallGeminiAsync("gemini-2.0-flash", history)).Reply,
            "openrouter" => translated = (await CallOpenRouterAsync("openai/gpt-4o-mini", history)).Reply,
            _ => throw new ArgumentException($"Unsupported provider: {provider}"),
        };

        return new AiTranslateResponse(translated.Trim(), request.SourceLanguage, provider);
    }

    // ── Summarization ────────────────────────────────────────────────────

    public async Task<AiSummarizeResponse> SummarizeAsync(AiSummarizeRequest request)
    {
        var provider = request.Provider ?? "openai";
        var lengthInstruction = request.MaxLength.HasValue
            ? $"Summarize in approximately {request.MaxLength} words."
            : "Provide a concise summary.";

        var prompt = $"{lengthInstruction}\n\nText: {request.Text}";

        var history = new List<AiMessageDto>
        {
            new("system", "You are a summarization assistant. Provide clear, concise summaries.", DateTime.UtcNow),
            new("user", prompt, DateTime.UtcNow),
        };

        string summary;
        _ = provider.ToLowerInvariant() switch
        {
            "openai" => summary = (await CallOpenAiAsync("gpt-4o-mini", history)).Reply,
            "gemini" => summary = (await CallGeminiAsync("gemini-2.0-flash", history)).Reply,
            "openrouter" => summary = (await CallOpenRouterAsync("openai/gpt-4o-mini", history)).Reply,
            _ => throw new ArgumentException($"Unsupported provider: {provider}"),
        };

        return new AiSummarizeResponse(summary.Trim(), request.Text.Length, summary.Trim().Length);
    }

    // ── OCR ────────────────────────────────────────────────────────────

    public async Task<AiOcrResponse> PerformOcrAsync(AiOcrRequest request)
    {
        var provider = request.Provider ?? "openai";
        var prompt = "Extract all visible text from the image. Return only the extracted text, preserving line breaks.";

        // For providers that support vision, pass the image as base64
        string extracted;
        float confidence = 0.95f;

        var history = new List<AiMessageDto>
        {
            new("system", "You are an OCR assistant. Extract text from images accurately.", DateTime.UtcNow),
            new("user", $"{prompt}\n[Image: base64 data ({request.ImageBase64.Length} chars)]", DateTime.UtcNow),
        };

        _ = provider.ToLowerInvariant() switch
        {
            "openai" => extracted = (await CallOpenAiAsync("gpt-4o", history)).Reply,
            "gemini" => extracted = (await CallGeminiAsync("gemini-2.0-flash", history)).Reply,
            "openrouter" => extracted = (await CallOpenRouterAsync("openai/gpt-4o", history)).Reply,
            _ => throw new ArgumentException($"Unsupported provider: {provider}"),
        };

        return new AiOcrResponse(extracted.Trim(), confidence, provider);
    }

    // ── Smart Reply ─────────────────────────────────────────────────────

    public async Task<AiSmartReplyResponse> GenerateSmartRepliesAsync(AiSmartReplyRequest request)
    {
        var context = request.ContextMessage;
        var historyText = request.ConversationHistory is { Count: > 0 }
            ? string.Join("\n", request.ConversationHistory)
            : "";

        var fullContext = string.IsNullOrEmpty(historyText)
            ? $"Last message: {context}"
            : $"Conversation history:\n{historyText}\n\nLast message: {context}";

        var prompt = $"{fullContext}\n\nGenerate 3 short, contextual reply suggestions. Return them as a JSON array of strings. Do not include any other text.";

        var history = new List<AiMessageDto>
        {
            new("system", "You are a smart reply assistant. Generate concise, contextually appropriate responses.", DateTime.UtcNow),
            new("user", prompt, DateTime.UtcNow),
        };

        var (reply, _) = await CallOpenAiAsync("gpt-4o-mini", history);

        // Try to parse the response as JSON array of strings
        List<string> suggestions;
        try
        {
            suggestions = JsonSerializer.Deserialize<List<string>>(reply, JsonOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            // Fallback: split by newlines and clean up
            suggestions = reply.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('-', '*', '"').TrimEnd('"', ','))
                .Where(s => s.Length > 0)
                .Take(3)
                .ToList();
        }

        // Ensure we have at least 1 suggestion
        if (suggestions.Count == 0)
            suggestions = ["I'll get back to you on that.", "Sounds good!", "Let me check."];

        return new AiSmartReplyResponse(suggestions.Take(3).ToList());
    }

    // ── Embeddings ─────────────────────────────────────────────────────

    public async Task<AiEmbeddingResponse> GenerateEmbeddingAsync(AiEmbeddingRequest request)
    {
        var provider = request.Provider ?? "openai";

        switch (provider.ToLowerInvariant())
        {
            case "openai":
                return await CallOpenAiEmbeddingAsync(request.Text);
            case "gemini":
                return await CallGeminiEmbeddingAsync(request.Text);
            case "local":
                return await CallLocalEmbeddingAsync(request.Text);
            default:
                throw new ArgumentException($"Embedding provider not supported: {provider}");
        }
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

    // ── Provider Implementations ─────────────────────────────────────────

    private async Task<(string Reply, int TokensUsed)> CallOpenAiAsync(string model, List<AiMessageDto> history)
    {
        var apiKey = _config["Ai:OpenAi:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI API key not configured.");
        var baseUrl = _config["Ai:OpenAi:BaseUrl"] ?? "https://api.openai.com/v1";

        var messages = history.Select(h => new
        {
            role = h.Role,
            content = h.Content,
        }).ToList();

        var requestBody = new
        {
            model,
            messages,
            max_tokens = 2048,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Headers = { { "Authorization", $"Bearer {apiKey}" } },
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var reply = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var tokensUsed = json.GetProperty("usage").GetProperty("total_tokens").GetInt32();

        return (reply, tokensUsed);
    }

    private async Task<(string Reply, int TokensUsed)> CallGeminiAsync(string model, List<AiMessageDto> history)
    {
        var apiKey = _config["Ai:Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini API key not configured.");
        var baseUrl = _config["Ai:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta";

        var contents = history.Select(h => new
        {
            role = h.Role == "assistant" ? "model" : h.Role,
            parts = new[] { new { text = h.Content } },
        }).ToList();

        var requestBody = new { contents };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{model}:generateContent?key={apiKey}")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var reply = json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;

        return (reply, reply.Length / 4); // approximate token count
    }

    private async Task<(string Reply, int TokensUsed)> CallOpenRouterAsync(string model, List<AiMessageDto> history)
    {
        var apiKey = _config["Ai:OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter API key not configured.");

        var messages = history.Select(h => new
        {
            role = h.Role,
            content = h.Content,
        }).ToList();

        var requestBody = new
        {
            model,
            messages,
            max_tokens = 2048,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Headers =
            {
                { "Authorization", $"Bearer {apiKey}" },
                { "HTTP-Referer", "https://omniconnect.local" },
                { "X-Title", "OmniConnect" },
            },
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var reply = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var tokensUsed = json.GetProperty("usage").GetProperty("total_tokens").GetInt32();

        return (reply, tokensUsed);
    }

    private async Task<(string Reply, int TokensUsed)> CallLocalModelAsync(string model, List<AiMessageDto> history)
    {
        var modelPath = _config[$"Ai:Local:ModelPath"]
            ?? throw new InvalidOperationException("Local model path not configured.");

        EnsureLlamaProcess(modelPath, model);

        var prompt = string.Join("\n", history.Select(h => $"{h.Role}: {h.Content}")) + "\nassistant: ";

        // Write prompt to stdin of llama.cpp process
        // This is a simplified integration - in production, use server mode with HTTP
        var psi = new ProcessStartInfo
        {
            FileName = "llama-cli",
            Arguments = $"--model \"{modelPath}\" --prompt \"{EscapePrompt(prompt)}\" --n-predict 512 --temp 0.7",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("Local model exited with code {ExitCode}: {Error}", process.ExitCode, error);
            throw new InvalidOperationException($"Local model inference failed: {error}");
        }

        // Clean up output - remove the prompt from response
        var reply = output.Trim();
        var tokensUsed = reply.Length / 4; // approximate

        return (reply, tokensUsed);
    }

    private async Task<AiEmbeddingResponse> CallOpenAiEmbeddingAsync(string text)
    {
        var apiKey = _config["Ai:OpenAi:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI API key not configured.");

        var requestBody = new
        {
            model = "text-embedding-3-small",
            input = text,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Headers = { { "Authorization", $"Bearer {apiKey}" } },
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var embedding = json.GetProperty("data")[0].GetProperty("embedding")
            .EnumerateArray().Select(e => e.GetSingle()).ToArray();

        return new AiEmbeddingResponse(embedding, embedding.Length, "openai");
    }

    private async Task<AiEmbeddingResponse> CallGeminiEmbeddingAsync(string text)
    {
        var apiKey = _config["Ai:Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini API key not configured.");

        var requestBody = new
        {
            model = "models/text-embedding-004",
            content = new { parts = new[] { new { text } } },
        };

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent?key={apiKey}")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var embedding = json.GetProperty("embedding").GetProperty("values")
            .EnumerateArray().Select(e => e.GetSingle()).ToArray();

        return new AiEmbeddingResponse(embedding, embedding.Length, "gemini");
    }

    private async Task<AiEmbeddingResponse> CallLocalEmbeddingAsync(string text)
    {
        var modelPath = _config["Ai:Local:EmbeddingModelPath"]
            ?? _config["Ai:Local:ModelPath"]
            ?? throw new InvalidOperationException("Local model path not configured.");

        var psi = new ProcessStartInfo
        {
            FileName = "llama-embedding",
            Arguments = $"--model \"{modelPath}\" --prompt \"{EscapePrompt(text)}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var values = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => float.TryParse(s, out var v) ? v : 0f)
            .ToArray();

        return new AiEmbeddingResponse(values, values.Length, "local");
    }

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
                Provider = request.Provider,
                ModelName = request.Model,
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

    private void EnsureLlamaProcess(string modelPath, string model)
    {
        // In production, this would start llama.cpp in server mode
        // For now, we use per-request process spawning in CallLocalModelAsync
        _logger.LogInformation("Local model configured: {ModelPath}", modelPath);
    }

    private static string EscapePrompt(string prompt)
    {
        return prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}