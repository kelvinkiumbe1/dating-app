using System.Security.Claims;
using ConsoleMatch.Core;
using DatingApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        // The deck: real users, filtered by both sides' preferences, ranked by compatibility.
        app.MapGet("/discover", async (ClaimsPrincipal principal, AppDbContext db, int? limit) =>
        {
            var meId = principal.UserId();
            var me = await db.Users.FirstOrDefaultAsync(u => u.Id == meId);
            if (me is null) return Results.NotFound();

            var alreadySwiped = await db.Swipes.Where(s => s.SwiperId == meId)
                .Select(s => s.TargetId).ToListAsync();

            var blockedEitherWay = await db.Blocks
                .Where(b => b.BlockerId == meId || b.BlockedId == meId)
                .Select(b => b.BlockerId == meId ? b.BlockedId : b.BlockerId)
                .ToListAsync();

            var excluded = alreadySwiped.Concat(blockedEitherWay).Append(meId).ToHashSet();

            var pool = await db.Users.Include(u => u.Photos)
                .Where(u => u.IsDiscoverable && !excluded.Contains(u.Id))
                .ToListAsync();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var myInterests = Compatibility.ParseList(me.Interests);

            // Distance and age are computed in memory: haversine has no SQL translation,
            // and age depends on today's date rather than a stored column.
            var deck = pool
                .Select(u => new
                {
                    User = u,
                    Age = u.AgeOn(today),
                    DistanceKm = Geo.DistanceKm(me.Latitude, me.Longitude, u.Latitude, u.Longitude),
                })
                .Where(x =>
                    // My preferences must accept them...
                    x.Age >= me.MinAge && x.Age <= me.MaxAge &&
                    x.DistanceKm <= me.MaxDistanceKm &&
                    Shared.GenderMatches(me.SeekingGenders, x.User.Gender) &&
                    // ...and theirs must accept me. Showing people who filtered me out
                    // would manufacture likes that can never become matches.
                    me.AgeOn(today) >= x.User.MinAge && me.AgeOn(today) <= x.User.MaxAge &&
                    x.DistanceKm <= x.User.MaxDistanceKm &&
                    Shared.GenderMatches(x.User.SeekingGenders, me.Gender))
                .Select(x => new CandidateResponse(
                    x.User.Id, x.User.DisplayName, x.Age, x.User.Gender, x.User.Bio,
                    Compatibility.ParseList(x.User.Interests),
                    Compatibility.Score(myInterests, Compatibility.ParseList(x.User.Interests)),
                    (int)Math.Round(x.DistanceKm),
                    x.User.PhotoUrls()))
                .OrderByDescending(c => c.CompatibilityPercent)
                .ThenBy(c => c.DistanceKm)
                .Take(Math.Clamp(limit ?? 20, 1, 100))
                .ToList();

            return Results.Ok(deck);
        }).RequireAuthorization().WithTags("Discovery");

        // A swipe. A match is created only when the other person already liked you.
        app.MapPost("/swipes", async (SwipeRequest req, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            if (req.TargetId == meId)
                return Results.BadRequest(new { error = "You cannot swipe on yourself." });

            if (!await db.Users.AnyAsync(u => u.Id == req.TargetId))
                return Results.NotFound(new { error = "No such user." });

            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == meId && b.BlockedId == req.TargetId) ||
                (b.BlockerId == req.TargetId && b.BlockedId == meId));
            if (blocked) return Results.BadRequest(new { error = "You cannot swipe on this user." });

            if (await db.Swipes.AnyAsync(s => s.SwiperId == meId && s.TargetId == req.TargetId))
                return Results.Conflict(new { error = "You already swiped on this user." });

            db.Swipes.Add(new Swipe { SwiperId = meId, TargetId = req.TargetId, IsLike = req.IsLike });

            if (!req.IsLike)
            {
                await db.SaveChangesAsync();
                return Results.Ok(new SwipeResponse(false, null, "Passed."));
            }

            var theyLikedMe = await db.Swipes.AnyAsync(s =>
                s.SwiperId == req.TargetId && s.TargetId == meId && s.IsLike);

            if (!theyLikedMe)
            {
                await db.SaveChangesAsync();
                return Results.Ok(new SwipeResponse(false, null, "Liked. Waiting to see if they like you back."));
            }

            var (a, b2) = Match.Order(meId, req.TargetId);
            var match = await db.Matches.FirstOrDefaultAsync(m => m.UserAId == a && m.UserBId == b2);
            if (match is null)
            {
                match = new Match { UserAId = a, UserBId = b2 };
                db.Matches.Add(match);
            }
            else
            {
                match.IsActive = true;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new SwipeResponse(true, match.Id, "It's a match!"));
        }).RequireAuthorization().WithTags("Discovery");

        // Who liked you and is still waiting on your swipe.
        app.MapGet("/likes", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();

            var mySwipes = await db.Swipes.Where(s => s.SwiperId == meId)
                .Select(s => s.TargetId).ToListAsync();

            var blocked = await db.Blocks
                .Where(b => b.BlockerId == meId || b.BlockedId == meId)
                .Select(b => b.BlockerId == meId ? b.BlockedId : b.BlockerId)
                .ToListAsync();

            var admirerIds = await db.Swipes
                .Where(s => s.TargetId == meId && s.IsLike
                            && !mySwipes.Contains(s.SwiperId)
                            && !blocked.Contains(s.SwiperId))
                .Select(s => s.SwiperId).ToListAsync();

            var admirers = await db.Users.Include(u => u.Photos)
                .Where(u => admirerIds.Contains(u.Id)).ToListAsync();

            return Results.Ok(admirers.Select(u => u.ToPublicProfile()).ToList());
        }).RequireAuthorization().WithTags("Discovery");
    }
}
