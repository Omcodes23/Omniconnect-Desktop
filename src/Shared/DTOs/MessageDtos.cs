using OmniConnect.Shared.Enums;

namespace OmniConnect.Shared.DTOs;

public record MessageDto(
    Guid Id,
    Guid SenderId,
    string SenderName,
    string? SenderAvatar,
    Guid? ReceiverId,
    Guid? CommunityId,
    Guid? ChannelId,
    MessageType Type,
    string Content,
    string? MediaUrl,
    long? FileSize,
    string? FileName,
    MessageStatus Status,
    Guid? ReplyToId,
    DateTime SentAt,
    DateTime? EditedAt
);

public record SendMessageRequest(
    Guid? ReceiverId,
    Guid? CommunityId,
    Guid? ChannelId,
    MessageType Type,
    string Content,
    string? MediaUrl,
    long? FileSize,
    string? FileName,
    Guid? ReplyToId,
    string ClientMessageId
);

public record MessageAck(string ClientMessageId, Guid ServerMessageId, MessageStatus Status, DateTime Timestamp);
