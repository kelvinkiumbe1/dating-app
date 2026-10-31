using System.Security.Claims;
using DatingApp.Api.Data;
using DatingApp.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class SafetyEndpoints
{
    private static readonly string[] ValidReasons =
    {
        "harassment", "spam", "fake_profile", "inappropriate_photos",
        "underage", "scam", "offline_behavior", "other",
    };

    public static void MapSafetyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/safety").WithTags("Safety").RequireAuthorization();

        // Blocking is mutual in effect: neither user can see, swipe, or message the other,
        // and any existing match is deactivated immediately.
        group.MapPost("/block/{userId:guid}", async (
            Guid userId, ClaimsPrincipal principal, AppDbContext db, RealtimeNotifier realtime) =>
        {
            var meId = principal.UserId();
            if (userId == meId) return Results.BadRequest(new { error = "You cannot block yourself." });
            if (!await db.Users.AnyAsync(u => u.Id == userId)) return Results.NotFound();

            if (!await db.Blocks.AnyAsync(b => b.BlockerId == meId && b.BlockedId == userId))
                db.Blocks.Add(new Block { BlockerId = meId, BlockedId = userId });

            var (a, b2) = Match.Order(meId, userId);
            var match = await db.Matches.FirstOrDefaultAsync(m => m.UserAId == a && m.UserBId == b2);
            var endedMatch = match is { IsActive: true } ? match : null;
            if (match is not null) match.IsActive = false;

            // Record a pass in both directions so neither reappears in the other's deck.
            foreach (var (swiper, target) in new[] { (meId, userId), (userId, meId) })
            {
                var existing = await db.Swipes
                    .FirstOrDefaultAsync(s => s.SwiperId == swiper && s.TargetId == target);
                if (existing is null)
                    db.Swipes.Add(new Swipe { SwiperId = swiper, TargetId = target, IsLike = false });
                else
                    existing.IsLike = false;
            }

            await db.SaveChangesAsync();
            if (endedMatch is not null) await realtime.Unmatched(endedMatch);
            return Results.Ok(new { blocked = true });
        });

        group.MapDelete("/block/{userId:guid}", async (
            Guid userId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var block = await db.Blocks
                .FirstOrDefaultAsync(b => b.BlockerId == meId && b.BlockedId == userId);
            if (block is null) return Results.NotFound();

            db.Blocks.Remove(block);
            await db.SaveChangesAsync();
            return Results.Ok(new { unblocked = true });
        });

        group.MapGet("/blocks", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var ids = await db.Blocks.Where(b => b.BlockerId == meId)
                .Select(b => b.BlockedId).ToListAsync();
            var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
            return Results.Ok(users.Select(u => new { u.Id, u.DisplayName }).ToList());
        });

        group.MapPost("/report", async (
            ReportRequest req, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            if (req.UserId == meId) return Results.BadRequest(new { error = "You cannot report yourself." });
            if (!await db.Users.AnyAsync(u => u.Id == req.UserId)) return Results.NotFound();

            var reason = (req.Reason ?? "").Trim().ToLowerInvariant();
            if (!ValidReasons.Contains(reason))
                return Results.BadRequest(new { error = $"Reason must be one of: {string.Join(", ", ValidReasons)}." });

            db.Reports.Add(new Report
            {
                ReporterId = meId,
                ReportedId = req.UserId,
                Reason = reason,
                Details = req.Details?.Trim(),
            });
            await db.SaveChangesAsync();

            // Reports queue for human review; they do not auto-suspend the reported account.
            return Results.Ok(new { reported = true, queuedForReview = true });
        });
    }
}
