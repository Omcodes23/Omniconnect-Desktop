namespace OmniConnect.Shared.DTOs;

// ── Chat ────────────────────────────────────────────────────────────────
public record AiChatRequest(
    string Provider,
    string Model,
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

// ── Translation ─────────────────────────────────────────────────────────
public record AiTranslateRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string? Provider
);

public record AiTranslateResponse(
    string TranslatedText,
    string DetectedLanguage,
    string Provider
);

// ── Summarization ───────────────────────────────────────────────────────
public record AiSummarizeRequest(
    string Text,
    int? MaxLength,
    string? Provider
);

public record AiSummarizeResponse(
    string Summary,
    int OriginalLength,
    int SummaryLength
);

// ── OCR ─────────────────────────────────────────────────────────────────
public record AiOcrRequest(
    string ImageBase64,
    string? Provider
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
    string Text,
    string? Provider
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