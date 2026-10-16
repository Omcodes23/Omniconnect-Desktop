using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Enums;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class MessageService
{
    private readonly AppDbContext _db;

    public MessageService(AppDbContext db) => _db = db;

    public async Task<MessageDto> SendMessageAsync(Guid senderId, SendMessageRequest request)
    {
        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = request.ReceiverId,
            CommunityId = request.CommunityId,
            ChannelId = request.ChannelId,
            Type = request.Type,
            Content = request.Content,
            MediaUrl = request.MediaUrl,
            FileSize = request.FileSize,
            FileName = request.FileName,
            ReplyToId = request.ReplyToId,
            ClientMessageId = request.ClientMessageId,
            Status = MessageStatus.Sent,
            SentAt = DateTime.UtcNow,
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        return await GetMessageDtoAsync(message.Id);
    }

    public async Task<MessageDto?> GetMessageDtoAsync(Guid messageId)
    {
        return await _db.Messages
            .Where(m => m.Id == messageId)
            .Select(m => new MessageDto(
                m.Id, m.SenderId, m.Sender.Username, m.Sender.AvatarUrl,
                m.ReceiverId, m.CommunityId, m.ChannelId, m.Type, m.Content,
                m.MediaUrl, m.FileSize, m.FileName, m.Status, m.ReplyToId,
                m.SentAt, m.EditedAt
            ))
            .FirstOrDefaultAsync();
    }

    public async Task<List<MessageDto>> GetMessagesAsync(Guid? communityId, Guid? channelId, int skip = 0, int take = 50)
    {
        var query = _db.Messages.AsQueryable();

        if (communityId.HasValue)
            query = query.Where(m => m.CommunityId == communityId);

        if (channelId.HasValue)
            query = query.Where(m => m.ChannelId == channelId);

        return await query
            .OrderByDescending(m => m.SentAt)
            .Skip(skip)
            .Take(take)
            .Select(m => new MessageDto(
                m.Id, m.SenderId, m.Sender.Username, m.Sender.AvatarUrl,
                m.ReceiverId, m.CommunityId, m.ChannelId, m.Type, m.Content,
                m.MediaUrl, m.FileSize, m.FileName, m.Status, m.ReplyToId,
                m.SentAt, m.EditedAt
            ))
            .ToListAsync();
    }

    public async Task<List<MessageDto>> GetConversationAsync(Guid userId1, Guid userId2, int skip = 0, int take = 50)
    {
        return await _db.Messages
            .Where(m => (m.SenderId == userId1 && m.ReceiverId == userId2) ||
                        (m.SenderId == userId2 && m.ReceiverId == userId1))
            .OrderByDescending(m => m.SentAt)
            .Skip(skip)
            .Take(take)
            .Select(m => new MessageDto(
                m.Id, m.SenderId, m.Sender.Username, m.Sender.AvatarUrl,
                m.ReceiverId, m.CommunityId, m.ChannelId, m.Type, m.Content,
                m.MediaUrl, m.FileSize, m.FileName, m.Status, m.ReplyToId,
                m.SentAt, m.EditedAt
            ))
            .ToListAsync();
    }
}
