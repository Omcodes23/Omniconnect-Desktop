using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class CommunityService
{
    private readonly AppDbContext _db;

    public CommunityService(AppDbContext db) => _db = db;

    public async Task<CommunityDto> CreateAsync(CreateCommunityRequest request, Guid ownerId, string ownerName)
    {
        var community = new Community
        {
            Name = request.Name,
            Description = request.Description,
            IsPublic = request.IsPublic,
            OwnerId = ownerId,
        };

        _db.Communities.Add(community);

        // Add owner as a member
        _db.CommunityMembers.Add(new CommunityMember
        {
            CommunityId = community.Id,
            UserId = ownerId,
            Role = "Owner"
        });

        await _db.SaveChangesAsync();

        return MapToDto(community, ownerName, 1);
    }

    public async Task<List<CommunityDto>> GetMyCommunitiesAsync(Guid userId)
    {
        var memberCommunityIds = await _db.CommunityMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.CommunityId)
            .ToListAsync();

        return await _db.Communities
            .Where(c => c.OwnerId == userId || memberCommunityIds.Contains(c.Id))
            .Include(c => c.Owner)
            .Select(c => MapToDto(c, c.Owner.Username,
                _db.CommunityMembers.Count(m => m.CommunityId == c.Id)))
            .ToListAsync();
    }

    public async Task<CommunityDto?> GetByIdAsync(Guid id)
    {
        var community = await _db.Communities
            .Include(c => c.Owner)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (community == null) return null;

        var memberCount = await _db.CommunityMembers
            .CountAsync(m => m.CommunityId == id);

        return MapToDto(community, community.Owner.Username, memberCount);
    }

    public async Task<ChannelDto> CreateChannelAsync(Guid communityId, CreateChannelRequest request)
    {
        var channel = new Channel
        {
            CommunityId = communityId,
            Name = request.Name,
            Description = request.Description,
            IsVoice = request.IsVoice,
            SortOrder = await _db.Channels.Where(c => c.CommunityId == communityId)
                .Select(c => (int?)c.SortOrder).MaxAsync() + 1 ?? 0,
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        return new ChannelDto(channel.Id, channel.CommunityId, channel.Name,
            channel.Description, channel.IsVoice, channel.SortOrder);
    }

    public async Task<List<ChannelDto>> GetChannelsAsync(Guid communityId)
    {
        return await _db.Channels
            .Where(c => c.CommunityId == communityId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new ChannelDto(c.Id, c.CommunityId, c.Name,
                c.Description, c.IsVoice, c.SortOrder))
            .ToListAsync();
    }

    private static CommunityDto MapToDto(Community c, string ownerName, int memberCount) => new(
        c.Id, c.Name, c.Description, c.IconUrl, c.OwnerId,
        ownerName, c.IsPublic, memberCount, c.CreatedAt
    );
}
