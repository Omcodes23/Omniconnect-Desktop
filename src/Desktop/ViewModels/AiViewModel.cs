using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Desktop.ViewModels;

/// <summary>
/// AI chat panel, driven entirely by on-device models.
///
/// There is one UX: pick a model. If it is not downloaded yet, download it
/// (with progress), then chat. The backend exposes the curated catalogue and
/// the download manager; this view model never talks to any external service.
/// </summary>
public partial class AiViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private string? _token;
    private bool _polling;
    private string? _deviceId;
    private string? _deviceName;

    // ── Chat state ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private AiConversationItem? _selectedConversation;

    // ── Model state ────────────────────────────────────────────────────────
    [ObservableProperty] private LocalModelItem? _selectedModel;
    [ObservableProperty] private bool _isModelBusy;
    [ObservableProperty] private string _modelStatusText = "Loading models...";

    // ── Companion mode (AI as a chat node) ─────────────────────────────────
    [ObservableProperty] private bool _isCompanionEnabled;
    [ObservableProperty] private string _companionStatusText = "Companion mode off";

    public ObservableCollection<AiConversationItem> Conversations { get; } = new();
    public ObservableCollection<AiMessageItem> Messages { get; } = new();
    public ObservableCollection<LocalModelItem> Models { get; } = new();

    /// <summary>True once a model has finished downloading and can be chatted with.</summary>
    public bool HasReadyModel => SelectedModel?.IsDownloaded == true;

    public AiViewModel()
    {
        _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _ = InitializeAsync();
    }

    /// <summary>Loads models and companion status the first time the panel is used.</summary>
    private async Task InitializeAsync()
    {
        await RefreshModelsAsync();
        await RefreshCompanionStatusAsync();
    }

    partial void OnSelectedModelChanged(LocalModelItem? value)
    {
        OnPropertyChanged(nameof(HasReadyModel));
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
        if (IsPanelOpen)
        {
            _ = RefreshModelsAsync();
        }
    }

    // ── Model management ───────────────────────────────────────────────────

    public async Task RefreshModelsAsync()
    {
        if (_token == null) return;

        try
        {
            var response = await _http.GetAsync("/api/ai/models");
            if (!response.IsSuccessStatusCode) return;

            var result = await response.Content.ReadFromJsonAsync<AiModelListResponse>();
            if (result?.Models == null) return;

            bool anyDownloading = false;
            foreach (var dto in result.Models)
            {
                anyDownloading |= dto.IsDownloading;
                ApplyModel(dto);
            }

            // Keep the selected model pointing at the catalogue's active model.
            var active = SelectedModel != null
                ? Models.FirstOrDefault(m => m.Id == SelectedModel.Id)
                : null;
            active ??= Models.FirstOrDefault(m => m.Id == result.ActiveModelId) ?? Models.FirstOrDefault();

            if (!ReferenceEquals(active, SelectedModel))
            {
                SelectedModel = active;
            }

            RemoveStale(Models.Select(m => m.Id).ToHashSet());

            ModelStatusText = anyDownloading
                ? "Downloading..."
                : HasReadyModel ? $"Ready: {SelectedModel!.Name}" : "Select a model to get started";

            if (anyDownloading)
            {
                _ = PollWhileDownloadingAsync();
            }
        }
        catch
        {
            ModelStatusText = "Could not reach the backend.";
        }
    }

    private void ApplyModel(AiModelDto dto)
    {
        var item = Models.FirstOrDefault(m => m.Id == dto.Id);
        if (item == null)
        {
            item = new LocalModelItem { Id = dto.Id };
            Models.Add(item);
        }

        item.Name = dto.Name;
        item.Description = dto.Description;
        item.SizeLabel = dto.SizeLabel;
        item.SizeBytes = dto.SizeBytes;
        item.IsDownloaded = dto.IsDownloaded;
        item.IsDownloading = dto.IsDownloading;
        item.DownloadProgress = Math.Clamp(dto.DownloadProgress * 100.0, 0, 100);
        item.Status = dto.Status;
    }

    private void RemoveStale(HashSet<string> liveIds)
    {
        for (int i = Models.Count - 1; i >= 0; i--)
        {
            if (!liveIds.Contains(Models[i].Id))
            {
                Models.RemoveAt(i);
            }
        }
    }

    /// <summary>Polls the model list while a download runs so progress advances.</summary>
    private async Task PollWhileDownloadingAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            while (Models.Any(m => m.IsDownloading))
            {
                await Task.Delay(800);
                await RefreshModelsAsync();
            }
        }
        finally
        {
            _polling = false;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync(LocalModelItem? model)
    {
        if (model == null || _token == null) return;

        try
        {
            var response = await _http.PostAsJsonAsync("/api/ai/models/download",
                new AiModelDownloadRequest(model.Id));
            if (!response.IsSuccessStatusCode)
            {
                ModelStatusText = $"Could not start download ({response.StatusCode}).";
                return;
            }

            model.IsDownloading = true;
            model.Status = "downloading";
            ModelStatusText = $"Downloading {model.Name}...";
            OnPropertyChanged(nameof(HasReadyModel));
            _ = PollWhileDownloadingAsync();
        }
        catch (Exception ex)
        {
            ModelStatusText = $"Download failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UseModelAsync(LocalModelItem? model)
    {
        if (model == null || !model.IsDownloaded || _token == null) return;

        try
        {
            var response = await _http.PostAsJsonAsync("/api/ai/models/select",
                new AiModelDownloadRequest(model.Id));
            if (response.IsSuccessStatusCode)
            {
                SelectedModel = model;
                ModelStatusText = $"Ready: {model.Name}";
                OnPropertyChanged(nameof(HasReadyModel));
            }
        }
        catch
        {
            ModelStatusText = "Could not select the model.";
        }
    }

    [RelayCommand]
    private async Task DeleteModelAsync(LocalModelItem? model)
    {
        if (model == null || _token == null) return;

        try
        {
            var response = await _http.DeleteAsync($"/api/ai/models/{model.Id}");
            if (response.IsSuccessStatusCode)
            {
                await RefreshModelsAsync();
            }
        }
        catch
        {
            ModelStatusText = "Could not delete the model file.";
        }
    }

    // ── Companion mode ─────────────────────────────────────────────────────

    /// <summary>
    /// Identifies the device that owns companion mode so other clients can see
    /// which machine enabled the AI node. Called once the desktop registers
    /// itself with the backend.
    /// </summary>
    public void SetDeviceIdentity(string deviceId, string deviceName)
    {
        _deviceId = deviceId;
        _deviceName = deviceName;
    }

    private async Task RefreshCompanionStatusAsync()
    {
        if (_token == null) return;
        try
        {
            var status = await _http.GetFromJsonAsync<AiBotStatusResponse>("/api/ai/bot/status");
            if (status != null)
            {
                IsCompanionEnabled = status.Enabled;
                CompanionStatusText = DescribeCompanion(status);
            }
        }
        catch
        {
            // Companion status is non-critical; leave the default text.
        }
    }

    [RelayCommand]
    private async Task ToggleCompanionAsync()
    {
        if (_token == null) return;

        var enable = !IsCompanionEnabled;
        try
        {
            var response = await _http.PostAsJsonAsync("/api/ai/bot/toggle",
                new AiBotToggleRequest(enable, SelectedModel?.Id, _deviceId, _deviceName));

            if (!response.IsSuccessStatusCode)
            {
                CompanionStatusText = "Could not change companion mode.";
                return;
            }

            var status = await response.Content.ReadFromJsonAsync<AiBotStatusResponse>();
            if (status != null)
            {
                IsCompanionEnabled = status.Enabled;
                CompanionStatusText = DescribeCompanion(status);
            }
        }
        catch
        {
            CompanionStatusText = "Could not change companion mode.";
        }
    }

    private static string DescribeCompanion(AiBotStatusResponse status) => status.Status switch
    {
        "online" => string.IsNullOrWhiteSpace(status.OwnerDeviceName)
            ? $"Online — {status.ModelName ?? "local model"}. Others can message the AI in internet chat."
            : $"Online — {status.ModelName ?? "local model"} on {status.OwnerDeviceName}. Others can message the AI in internet chat.",
        "no-model" => "On, but no model is ready — download one first.",
        _ => "Companion mode off",
    };

    // ── Conversations ──────────────────────────────────────────────────────

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

        Messages.Add(new AiMessageItem
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.Now
        });

        // No model ready: tell the user how to get one instead of failing.
        if (!HasReadyModel)
        {
            Messages.Add(new AiMessageItem
            {
                Role = "assistant",
                Content = "No local model is ready. Pick a model below and wait for its download to finish, then try again.",
                Timestamp = DateTime.Now
            });
            return;
        }

        IsLoading = true;

        try
        {
            // Build request with conversation history
            var history = Messages
                .Where(m => m != Messages.LastOrDefault())
                .TakeLast(20)
                .Select(m => new AiMessageDto(m.Role, m.Content, m.Timestamp))
                .ToList();

            var request = new AiChatRequest(
                SelectedModel?.Id,
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
                        Provider = "local",
                        ModelName = SelectedModel?.Name ?? string.Empty,
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
                Content = $"**Error:** {ex.Message}\n\nMake sure the backend is running and a local model has finished downloading.",
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

        if (_token == null || !HasReadyModel) return;

        try
        {
            var request = new AiChatRequest(SelectedModel?.Id, " ", null, null);
            var response = await _http.PostAsJsonAsync("/api/ai/chat", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>();
            if (result != null)
            {
                SelectedConversation = new AiConversationItem
                {
                    Id = result.ConversationId,
                    Title = "New Conversation",
                    Provider = "local",
                    ModelName = SelectedModel?.Name ?? string.Empty,
                };
                Conversations.Insert(0, SelectedConversation);
            }
        }
        catch (Exception ex)
        {
            ModelStatusText = $"Could not create conversation on server: {ex.Message}";
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

/// <summary>One entry of the curated local model catalogue, with live download state.</summary>
public partial class LocalModelItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SizeLabel { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress; // 0..100
    [ObservableProperty] private string _status = "not-downloaded";

    public string ActionLabel => IsDownloaded ? "Use" : IsDownloading ? "Downloading" : "Download";

    partial void OnIsDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ShowDownloadButton));
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ShowDownloadButton));
    }

    public bool ShowDownloadButton => !IsDownloaded && !IsDownloading;
}
