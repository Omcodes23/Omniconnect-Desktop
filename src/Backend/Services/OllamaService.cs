using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Service that connects to a local Ollama instance (http://localhost:11434)
/// for listing, downloading, and running local LLM models.
/// </summary>
public class OllamaService
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(HttpClient http, ILogger<OllamaService> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri("http://localhost:11434");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>List all models available on the Ollama server.</summary>
    public async Task<List<OllamaModel>> ListModelsAsync()
    {
        try
        {
            var response = await _http.GetAsync("/api/tags");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaListResponse>();
            return result?.Models ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list Ollama models — is Ollama running on localhost:11434?");
            return new();
        }
    }

    /// <summary>Pull/download a model from Ollama's registry.</summary>
    public async Task<bool> PullModelAsync(string modelName)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/pull", new { name = modelName, stream = false });
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Ollama model {Model} pulled successfully", modelName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull Ollama model {Model}", modelName);
            return false;
        }
    }

    /// <summary>Generate a response from Ollama.</summary>
    public async Task<string> GenerateAsync(string model, string prompt)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/generate", new
            {
                model,
                prompt,
                stream = false
            });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
            return result?.Response ?? "No response from Ollama.";
        }
        catch (HttpRequestException)
        {
            return "⚠️ Cannot connect to Ollama. Make sure Ollama is running (`ollama serve`).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama generation failed");
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>Get detailed info about a specific model.</summary>
    public async Task<OllamaModelDetail?> GetModelInfoAsync(string modelName)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/show", new { name = modelName });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OllamaModelDetail>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Search for available models on Ollama's library.</summary>
    public async Task<List<string>> SearchLibraryAsync(string query)
    {
        // Ollama doesn't have a search API — return common models
        var commonModels = new List<string>
        {
            "llama3.2:1b", "llama3.2:3b", "llama3.1:8b", "llama3.1:70b",
            "mistral:7b", "mixtral:8x7b",
            "phi3:mini", "phi3:medium",
            "gemma2:2b", "gemma2:9b",
            "qwen2:0.5b", "qwen2:1.5b", "qwen2:7b",
            "codellama:7b", "codellama:13b",
            "llava:7b", "llava:13b",
            "nomic-embed-text", "mxbai-embed-large",
            "stablelm2:1.6b", "stablelm2:12b",
            "deepseek-coder:6.7b",
            "sqlcoder:7b",
            "dolphin-mixtral:8x7b",
        };

        if (string.IsNullOrEmpty(query)) return commonModels;

        return commonModels
            .Where(m => m.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public class OllamaModel
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("modified_at")] public DateTime ModifiedAt { get; set; }

    public string SizeFormatted => Size switch
    {
        > 1_000_000_000 => $"{Size / 1_000_000_000.0:F1}GB",
        > 1_000_000 => $"{Size / 1_000_000.0:F1}MB",
        _ => $"{Size / 1000}KB"
    };
}

public class OllamaListResponse
{
    [JsonPropertyName("models")] public List<OllamaModel> Models { get; set; } = new();
}

public class OllamaGenerateResponse
{
    [JsonPropertyName("response")] public string Response { get; set; } = string.Empty;
    [JsonPropertyName("done")] public bool Done { get; set; }
}

public class OllamaModelDetail
{
    [JsonPropertyName("modelfile")] public string? ModelFile { get; set; }
    [JsonPropertyName("parameters")] public string? Parameters { get; set; }
    [JsonPropertyName("template")] public string? Template { get; set; }
    [JsonPropertyName("details")] public OllamaModelDetails? Details { get; set; }
}

public class OllamaModelDetails
{
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("families")] public List<string>? Families { get; set; }
    [JsonPropertyName("parameter_size")] public string? ParameterSize { get; set; }
    [JsonPropertyName("quantization_level")] public string? QuantizationLevel { get; set; }
}
