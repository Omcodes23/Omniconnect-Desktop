using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Backend.Hubs;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class CallService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public CallService(AppDbContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<CallDto> InitiateAsync(Guid callerId, InitiateCallRequest request)
    {
        var caller = await _db.Users.FindAsync(callerId);
        var callee = await _db.Users.FindAsync(request.CalleeId)
            ?? throw new InvalidOperationException("Callee not found");

        var call = new CallRecord
        {
            CallerId = callerId,
            CalleeId = request.CalleeId,
            Type = request.Type.Equals("Video", StringComparison.OrdinalIgnoreCase) ? CallType.Video : CallType.Audio,
            Status = CallStatus.Initiated,
            StartedAt = DateTime.UtcNow
        };
        _db.Set<CallRecord>().Add(call);
        await _db.SaveChangesAsync();

        var dto = await GetCallDtoAsync(call.Id);

        // Signal callee in real-time
        await _hub.Clients.Group($"user_{request.CalleeId}").SendAsync("IncomingCall", dto);

        return dto;
    }

    public async Task<CallDto> UpdateStatusAsync(Guid callId, CallStatus status)
    {
        var call = await _db.Set<CallRecord>().FindAsync(callId)
            ?? throw new InvalidOperationException("Call not found");
        call.Status = status;
        if (status is CallStatus.Ended or CallStatus.Rejected or CallStatus.Missed)
            call.EndedAt = DateTime.UtcNow;
        if (status == CallStatus.Accepted)
            call.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = await GetCallDtoAsync(callId);

        var eventName = status switch
        {
            CallStatus.Accepted => "CallAccepted",
            CallStatus.Rejected => "CallRejected",
            CallStatus.Ended => "CallEnded",
            _ => "CallStatusChanged"
        };

        await _hub.Clients.Group($"user_{dto.CallerId}").SendAsync(eventName, dto);
        await _hub.Clients.Group($"user_{dto.CalleeId}").SendAsync(eventName, dto);

        return dto;
    }

    public async Task<List<CallDto>> GetCallHistoryAsync(Guid userId)
    {
        var calls = await _db.Set<CallRecord>()
            .Where(c => c.CallerId == userId || c.CalleeId == userId)
            .OrderByDescending(c => c.StartedAt)
            .Take(50)
            .ToListAsync();

        var userIds = calls.Select(c => c.CallerId).Concat(calls.Select(c => c.CalleeId)).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

        return calls.Select(c =>
        {
            var caller = users.GetValueOrDefault(c.CallerId);
            var callee = users.GetValueOrDefault(c.CalleeId);
            return new CallDto(
                c.Id,
                c.CallerId,
                caller?.DisplayName ?? caller?.Username ?? "User",
                caller?.AvatarUrl,
                c.CalleeId,
                callee?.DisplayName ?? callee?.Username ?? "User",
                callee?.AvatarUrl,
                c.Type.ToString(),
                c.Status.ToString(),
                c.StartedAt,
                c.EndedAt,
                c.Duration);
        }).ToList();
    }

    private async Task<CallDto> GetCallDtoAsync(Guid callId)
    {
        var c = await _db.Set<CallRecord>().FirstAsync(cr => cr.Id == callId);
        var caller = await _db.Users.FindAsync(c.CallerId);
        var callee = await _db.Users.FindAsync(c.CalleeId);

        return new CallDto(
            c.Id,
            c.CallerId,
            caller?.DisplayName ?? caller?.Username ?? "User",
            caller?.AvatarUrl,
            c.CalleeId,
            callee?.DisplayName ?? callee?.Username ?? "User",
            callee?.AvatarUrl,
            c.Type.ToString(),
            c.Status.ToString(),
            c.StartedAt,
            c.EndedAt,
            c.Duration);
    }
}