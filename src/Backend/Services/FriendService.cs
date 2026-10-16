using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class FriendService
{
    private readonly AppDbContext _db;

    public FriendService(AppDbContext db) => _db = db;

    public async Task<FriendRequestDto> SendRequestAsync(Guid requesterId, Guid addresseeId)
    {
        if (requesterId == addresseeId)
            throw new InvalidOperationException("Cannot friend yourself.");

        var existing = await _db.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == requesterId && f.AddresseeId == addresseeId) ||
                (f.RequesterId == addresseeId && f.AddresseeId == requesterId));

        if (existing != null)
            throw new InvalidOperationException("Friend request already exists.");

        var friendship = new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = addresseeId,
        };

        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        return await GetDtoAsync(friendship.Id);
    }

    public async Task<FriendRequestDto> AcceptRequestAsync(Guid friendshipId, Guid userId)
    {
        var friendship = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.AddresseeId == userId)
            ?? throw new InvalidOperationException("Friend request not found.");

        friendship.IsAccepted = true;
        friendship.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return MapToDto(friendship);
    }

    public async Task<FriendRequestDto> EnsureFriendshipAsync(Guid userId1, Guid userId2)
    {
        if (userId1 == userId2)
            throw new InvalidOperationException("Cannot friend yourself.");

        var existing = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == userId1 && f.AddresseeId == userId2) ||
                (f.RequesterId == userId2 && f.AddresseeId == userId1));

        if (existing != null)
        {
            if (!existing.IsAccepted)
            {
                existing.IsAccepted = true;
                existing.AcceptedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            return MapToDto(existing);
        }

        var friendship = new Friendship
        {
            RequesterId = userId1,
            AddresseeId = userId2,
            IsAccepted = true,
            AcceptedAt = DateTime.UtcNow
        };

        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        return await GetDtoAsync(friendship.Id);
    }

    public async Task<List<FriendRequestDto>> GetFriendsAsync(Guid userId)
    {
        return await _db.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId) && f.IsAccepted)
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Select(f => MapToDto(f))
            .ToListAsync();
    }

    public async Task<List<FriendRequestDto>> GetPendingRequestsAsync(Guid userId)
    {
        return await _db.Friendships
            .Where(f => f.AddresseeId == userId && !f.IsAccepted)
            .Include(f => f.Requester)
            .Select(f => MapToDto(f))
            .ToListAsync();
    }

    private async Task<FriendRequestDto> GetDtoAsync(Guid id)
    {
        var f = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstAsync(f => f.Id == id);
        return MapToDto(f);
    }

    private static FriendRequestDto MapToDto(Friendship f) => new(
        f.Id, f.RequesterId, f.Requester.Username, f.Requester.AvatarUrl,
        f.AddresseeId, f.Addressee.Username, f.Addressee.AvatarUrl,
        f.IsAccepted, f.CreatedAt, f.AcceptedAt
    );
}
