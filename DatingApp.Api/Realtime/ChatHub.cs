using DatingApp.Api.Data;
using DatingApp.Api.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Realtime;

/// Live delivery only. Every state change still goes through the REST endpoints, which
/// push the result here, so a client that misses an event loses nothing by reloading.
[Authorize]
public class ChatHub(AppDbContext db) : Hub
{
    /// The one client-to-server call: a typing indicator, relayed to the other side of an
    /// active match and nowhere else.
    public async Task Typing(Guid matchId)
    {
        var meId = Context.User!.UserId();
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId);
        if (match is null || !match.IsActive || !match.Includes(meId)) return;

        await Clients.User(match.OtherUser(meId).ToString())
            .SendAsync(RealtimeEvents.Typing, new TypingEvent(matchId, meId));
    }
}

/// SignalR's default maps users by ClaimTypes.NameIdentifier; this API keeps JWT "sub".
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value;
}

public static class RealtimeEvents
{
    public const string Message = "message";
    public const string Read = "read";
    public const string Match = "match";
    public const string Unmatched = "unmatched";
    public const string Typing = "typing";
}

/// Thin wrapper so endpoints say what happened rather than how it is transported.
public class RealtimeNotifier(IHubContext<ChatHub> hub)
{
    private IClientProxy To(params Guid[] users) =>
        hub.Clients.Users(users.Select(u => u.ToString()).ToList());

    /// Sent to both participants so the sender's other tabs and devices stay in sync.
    public Task MessageSent(Match match, MessageResponse message) =>
        To(match.UserAId, match.UserBId).SendAsync(RealtimeEvents.Message, new MessageEvent(match.Id, message));

    public Task ThreadRead(Match match, Guid readerId, DateTimeOffset readAt) =>
        To(match.OtherUser(readerId)).SendAsync(RealtimeEvents.Read, new ReadEvent(match.Id, readerId, readAt));

    public Task Matched(Guid recipient, Match match, User other) =>
        To(recipient).SendAsync(RealtimeEvents.Match,
            new MatchEvent(match.Id, other.Id, other.DisplayName, other.PhotoUrls()));

    public Task Unmatched(Match match) =>
        To(match.UserAId, match.UserBId).SendAsync(RealtimeEvents.Unmatched, new UnmatchedEvent(match.Id));
}
