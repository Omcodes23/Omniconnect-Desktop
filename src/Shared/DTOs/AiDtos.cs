namespace OmniConnect.Shared.DTOs;

// ── Chat ────────────────────────────────────────────────────────────────
// `Model` is the id from ModelCatalog (e.g. "qwen2.5-1.5b-instruct-q4km").
// OmniConnect runs models locally; there is no remote provider field any more.
// Older clients that still send a "provider" property are harmless — the
// ASP.NET JSON binder simply ignores unknown members.
public record AiChatRequest(
    string? Model,
    string Message,
    Guid? ConversationId,
    List<AiMessageDto>? History
);

public record AiChatResponse(
    Guid ConversationId,
    string Reply,
    int TokensUsed,
    List<AiMessageDto> History
);

public record AiMessageDto(
    string Role,   // "user" | "assistant" | "system"
    string Content,
    DateTime Timestamp
);

// ── Local model management ──────────────────────────────────────────────
// Progress is surfaced by polling the model list while a download runs, so no
// extra streaming endpoint or hub is required.
public record AiModelDto(
    string Id,
    string Name,
    string Description,
    string Family,
    string Quantization,
    long SizeBytes,
    string SizeLabel,
    bool IsDownloaded,
    bool IsDownloading,
    double DownloadProgress,   // 0..1, only meaningful while IsDownloading
    string Status                // "not-downloaded" | "downloading" | "downloaded" | "error"
);

public record AiModelListResponse(
    List<AiModelDto> Models,
    string? ActiveModelId
);

public record AiModelDownloadRequest(
    string ModelId
);

public record AiModelDownloadResponse(
    string ModelId,
    string Status
);

// ── Companion mode (AI as a chat node) ──────────────────────────────────
public record AiBotToggleRequest(
    bool Enabled,
    string? ModelId,
    string? DeviceId = null,
    string? DeviceName = null
);

public record AiBotStatusResponse(
    bool Enabled,
    string? ModelId,
    string DisplayName,
    string Status,   // "online" | "offline" | "loading" | "no-model"
    string? ModelName = null,
    string? OwnerDeviceId = null,
    string? OwnerDeviceName = null
);

// ── Translation ─────────────────────────────────────────────────────────
public record AiTranslateRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage
);

public record AiTranslateResponse(
    string TranslatedText,
    string DetectedLanguage,
    string Provider
);

// ── Summarization ───────────────────────────────────────────────────────
public record AiSummarizeRequest(
    string Text,
    int? MaxLength
);

public record AiSummarizeResponse(
    string Summary,
    int OriginalLength,
    int SummaryLength
);

// ── OCR ─────────────────────────────────────────────────────────────────
public record AiOcrRequest(
    string ImageBase64
);

public record AiOcrResponse(
    string ExtractedText,
    float Confidence,
    string Provider
);

// ── Smart Reply ─────────────────────────────────────────────────────────
public record AiSmartReplyRequest(
    string ContextMessage,
    List<string>? ConversationHistory
);

public record AiSmartReplyResponse(
    List<string> Suggestions
);

// ── Embeddings ───────────────────────────────────────────────────────────
public record AiEmbeddingRequest(
    string Text
);

public record AiEmbeddingResponse(
    float[] Embedding,
    int Dimensions,
    string Provider
);

// ── Conversation Management ─────────────────────────────────────────────
public record AiConversationDto(
    Guid Id,
    string Provider,
    string ModelName,
    string Title,
    int TokenCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record AiConversationListResponse(
    List<AiConversationDto> Conversations,
    int TotalCount
);

public record AiConversationDetailResponse(
    AiConversationDto Conversation,
    List<AiMessageDto> Messages
);
