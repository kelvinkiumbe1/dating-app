using System.Security.Claims;
using ConsoleMatch.Core;
using DatingApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class MatchEndpoints
{
    public const int MaxMessageLength = 2000;

    public static void MapMatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/matches").WithTags("Matches").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var me = await db.Users.FirstOrDefaultAsync(u => u.Id == meId);
            if (me is null) return Results.NotFound();

            var matches = await db.Matches
                .Where(m => m.IsActive && (m.UserAId == meId || m.UserBId == meId))
                .ToListAsync();

            var otherIds = matches.Select(m => m.OtherUser(meId)).ToList();
            var others = await db.Users.Include(u => u.Photos)
                .Where(u => otherIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            var matchIds = matches.Select(m => m.Id).ToList();
            var messages = await db.Messages
                .Where(msg => matchIds.Contains(msg.MatchId))
                .ToListAsync();

            var myInterests = Compatibility.ParseList(me.Interests);

            var result = matches
                .Select(m =>
                {
                    var other = others.GetValueOrDefault(m.OtherUser(meId));
                    var thread = messages.Where(x => x.MatchId == m.Id).OrderBy(x => x.SentAt).ToList();
                    var last = thread.LastOrDefault();

                    return new MatchResponse(
                        m.Id,
                        other?.Id ?? Guid.Empty,
                        other?.DisplayName ?? "(deleted user)",
                        other?.Age ?? 0,
                        other is null ? 0 : Compatibility.Score(myInterests, Compatibility.ParseList(other.Interests)),
                        m.CreatedAt,
                        last?.Body,
                        last?.SentAt,
                        thread.Count(x => x.SenderId != meId && x.ReadAt is null),
                        other?.PhotoUrls() ?? new List<string>());
                })
                .OrderByDescending(m => m.LastMessageAt ?? m.MatchedAt)
                .ToList();

            return Results.Ok(result);
        });

        group.MapGet("/{matchId:guid}/messages", async (
            Guid matchId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match is null || !match.Includes(meId)) return Results.NotFound();

            // Ordered client-side: SQLite cannot ORDER BY a DateTimeOffset column,
            // and one match's thread is a small bounded set.
            var thread = (await db.Messages.Where(m => m.MatchId == matchId).ToListAsync())
                .OrderBy(m => m.SentAt).ToList();

            // Reading the thread marks the other side's messages as read.
            var now = DateTimeOffset.UtcNow;
            var unread = thread.Where(m => m.SenderId != meId && m.ReadAt is null).ToList();
            if (unread.Count > 0)
            {
                foreach (var m in unread) m.ReadAt = now;
                await db.SaveChangesAsync();
            }

            return Results.Ok(thread
                .Select(m => new MessageResponse(m.Id, m.SenderId, m.Body, m.SentAt, m.ReadAt))
                .ToList());
        });

        group.MapPost("/{matchId:guid}/messages", async (
            Guid matchId, SendMessageRequest req, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();

            var body = (req.Body ?? "").Trim();
            if (body.Length == 0)
                return Results.BadRequest(new { error = "Message cannot be empty." });
            if (body.Length > MaxMessageLength)
                return Results.BadRequest(new { error = $"Message exceeds {MaxMessageLength} characters." });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match is null || !match.Includes(meId)) return Results.NotFound();

            // An unmatch or block deactivates the match; the thread becomes read-only.
            if (!match.IsActive)
                return Results.BadRequest(new { error = "This match is no longer active." });

            var other = match.OtherUser(meId);
            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == meId && b.BlockedId == other) ||
                (b.BlockerId == other && b.BlockedId == meId));
            if (blocked) return Results.BadRequest(new { error = "You cannot message this user." });

            var message = new Message { MatchId = matchId, SenderId = meId, Body = body };
            db.Messages.Add(message);
            await db.SaveChangesAsync();

            return Results.Ok(new MessageResponse(
                message.Id, message.SenderId, message.Body, message.SentAt, message.ReadAt));
        });

        // Unmatch: deactivates the match for both sides and keeps the swipes on record
        // so neither person resurfaces in the other's deck.
        group.MapDelete("/{matchId:guid}", async (
            Guid matchId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match is null || !match.Includes(meId)) return Results.NotFound();

            match.IsActive = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { unmatched = true });
        });
    }
}
