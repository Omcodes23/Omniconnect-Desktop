using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

public partial class AiViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private string? _token;

    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _selectedProvider = "OpenAI";
    [ObservableProperty] private string _selectedModel = "gpt-4o";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private AiConversationItem? _selectedConversation;

    // ── Ollama-specific properties ─────────────────────────────────────────
    [ObservableProperty] private string _ollamaSearchQuery = string.Empty;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;

    public ObservableCollection<AiConversationItem> Conversations { get; } = new();
    public ObservableCollection<AiMessageItem> Messages { get; } = new();
    public ObservableCollection<OllamaModelItem> OllamaLocalModels { get; } = new();
    public ObservableCollection<OllamaModelItem> OllamaSearchResults { get; } = new();

    public List<string> AvailableProviders { get; } = new() { "OpenAI", "Gemini", "Ollama", "Local" };

    private readonly Dictionary<string, List<string>> _modelsByProvider = new()
    {
        ["OpenAI"] = new() { "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo" },
        ["Gemini"] = new() { "gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash" },
        ["Ollama"] = new() { "llama3.2", "mistral", "phi4", "gemma3", "deepseek-r1" },
        ["Local"] = new() { "llama-3.1-8b", "mistral-7b", "phi-3" }
    };

    public List<string> AvailableModels => _modelsByProvider.TryGetValue(SelectedProvider, out var models) ? models : new();

    public bool IsOllamaSelected => SelectedProvider == "Ollama";

    public AiViewModel()
    {
        _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task LoadConversationsAsync()
    {
        if (_token == null) return;
        try
        {
            var response = await _http.GetAsync("/api/ai/conversations");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AiConversationListResponse>();
            if (result?.Conversations != null)
            {
                Conversations.Clear();
                foreach (var c in result.Conversations.OrderByDescending(c => c.UpdatedAt))
                {
                    Conversations.Add(new AiConversationItem
                    {
                        Id = c.Id,
                        Title = c.Title,
                        Provider = c.Provider,
                        ModelName = c.ModelName,
                        TokenCount = c.TokenCount,
                        UpdatedAt = c.UpdatedAt
                    });
                }
            }
        }
        catch
        {
            // Silently fail — conversations list will remain empty if the API is not ready
        }
    }

    partial void OnSelectedProviderChanged(string value)
    {
        OnPropertyChanged(nameof(AvailableModels));
        OnPropertyChanged(nameof(IsOllamaSelected));
        SelectedModel = AvailableModels.FirstOrDefault() ?? string.Empty;

        if (IsOllamaSelected)
        {
            _ = LoadOllamaModelsAsync();
        }
    }

    partial void OnSelectedConversationChanged(AiConversationItem? value)
    {
        if (value != null)
        {
            _ = SelectConversationAsync(value);
        }
    }

    [RelayCommand]
    private void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
    }

    [RelayCommand]
    private async Task SelectConversationAsync(AiConversationItem? conversation)
    {
        if (conversation == null) return;
        if (_token == null) return;

        IsLoading = true;
        SelectedConversation = conversation;

        try
        {
            var response = await _http.GetAsync($"/api/ai/conversations/{conversation.Id}");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AiConversationDetailResponse>();
            if (result != null)
            {
                Messages.Clear();
                foreach (var m in result.Messages)
                {
                    Messages.Add(new AiMessageItem
                    {
                        Role = m.Role,
                        Content = m.Content,
                        Timestamp = m.Timestamp
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Messages.Clear();
            Messages.Add(new AiMessageItem
            {
                Role = "assistant",
                Content = $"**Error loading conversation:** {ex.Message}",
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SendAiMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText) || IsLoading || _token == null) return;

        var userMessage = MessageText.Trim();
        MessageText = string.Empty;
        IsLoading = true;

        Messages.Add(new AiMessageItem
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.Now
        });

        try
        {
            // Build request with conversation history
            var history = Messages
                .Where(m => m != Messages.LastOrDefault())
                .TakeLast(20)
                .Select(m => new AiMessageDto(m.Role, m.Content, m.Timestamp))
                .ToList();

            var request = new AiChatRequest(
                SelectedProvider,
                SelectedModel,
                userMessage,
                SelectedConversation?.Id,
                history
            );

            var response = await _http.PostAsJsonAsync("/api/ai/chat", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>();
            if (result != null)
            {
                Messages.Add(new AiMessageItem
                {
                    Role = "assistant",
                    Content = result.Reply,
                    Timestamp = DateTime.Now
                });

                // Update conversation tracking
                if (SelectedConversation == null)
                {
                    SelectedConversation = new AiConversationItem
                    {
                        Id = result.ConversationId,
                        Title = userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage,
                        Provider = SelectedProvider,
                        ModelName = SelectedModel,
                    };
                    Conversations.Insert(0, SelectedConversation);
                }
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new AiMessageItem
            {
                Role = "assistant",
                Content = $"**Error:** {ex.Message}\n\nMake sure the backend is running and AI provider keys are configured in `appsettings.Development.json`.",
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NewConversationAsync()
    {
        Messages.Clear();
        SelectedConversation = null;

        if (_token == null) return;

        try
        {
            // POST to /api/ai/chat with empty message and no conversationId
            // to let the server create a new conversation container
            var request = new AiChatRequest(
                SelectedProvider,
                SelectedModel,
                " ",
                null,
                null
            );

            var response = await _http.PostAsJsonAsync("/api/ai/chat", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>();
            if (result != null)
            {
                SelectedConversation = new AiConversationItem
                {
                    Id = result.ConversationId,
                    Title = "New Conversation",
                    Provider = SelectedProvider,
                    ModelName = SelectedModel,
                };
                Conversations.Insert(0, SelectedConversation);
            }
        }
        catch (Exception ex)
        {
            // If server-side creation fails, just start locally
            DownloadStatus = $"Could not create conversation on server: {ex.Message}";
        }
    }

    // ── Ollama Integration ──────────────────────────────────────────────────

    private async Task LoadOllamaModelsAsync()
    {
        if (_token == null || !IsOllamaSelected) return;
        try
        {
            var response = await _http.GetAsync("/api/ollama/models");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaModelListResponse>();
                OllamaLocalModels.Clear();
                if (result?.Models != null)
                {
                    foreach (var m in result.Models)
                    {
                        OllamaLocalModels.Add(new OllamaModelItem
                        {
                            Name = m.Name,
                            Size = m.Size,
                            ModifiedAt = m.ModifiedAt
                        });
                    }
                }
            }
        }
        catch
        {
            // Ollama not available — silently ignore
        }
    }

    [RelayCommand]
    private async Task BrowseOllamaModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(OllamaSearchQuery) || _token == null) return;

        IsLoading = true;
        try
        {
            var response = await _http.PostAsJsonAsync("/api/ollama/search",
                new { query = OllamaSearchQuery });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaSearchResponse>();
            OllamaSearchResults.Clear();
            if (result?.Models != null)
            {
                foreach (var m in result.Models)
                {
                    OllamaSearchResults.Add(new OllamaModelItem
                    {
                        Name = m.Name,
                        Size = m.Size,
                        Description = m.Description ?? string.Empty
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadOllamaModelAsync(OllamaModelItem? model)
    {
        if (model == null || _token == null) return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = $"Downloading {model.Name}...";

        try
        {
            var response = await _http.PostAsJsonAsync("/api/ollama/pull",
                new { name = model.Name });
            response.EnsureSuccessStatusCode();

            // Try to read download progress if the endpoint returns it
            var result = await response.Content.ReadFromJsonAsync<OllamaPullResponse>();
            if (result != null)
            {
                DownloadProgress = result.Progress;
                DownloadStatus = result.Status ?? $"Downloaded {model.Name} successfully!";

                // Refresh the local model list
                await LoadOllamaModelsAsync();
            }
            else
            {
                DownloadStatus = $"Downloaded {model.Name} successfully!";
                DownloadProgress = 100;
                await LoadOllamaModelsAsync();
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}

public class AiConversationItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AiMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
}

// ── Ollama DTOs ──────────────────────────────────────────────────────────────

public class OllamaModelItem
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Description { get; set; }
    public DateTime? ModifiedAt { get; set; }

    public string FormattedSize
    {
        get
        {
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}

public record OllamaModelListResponse(List<OllamaModelDto> Models);
public record OllamaModelDto(string Name, long Size, DateTime? ModifiedAt);
public record OllamaSearchModelDto(string Name, long Size, string? Description);
public record OllamaSearchResponse(List<OllamaSearchModelDto> Models);
public record OllamaPullResponse(string? Status, double Progress);
