using System.Security.Claims;
using ConsoleMatch.Core;
using DatingApp.Api.Data;
using DatingApp.Api.Realtime;
using DatingApp.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me").WithTags("Profile").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal me, AppDbContext db) =>
        {
            var user = await db.Users.Include(u => u.Photos)
                .FirstOrDefaultAsync(u => u.Id == me.UserId());
            return user is null ? Results.NotFound() : Results.Ok(user.ToMyProfile());
        });

        group.MapPatch("/", async (UpdateProfileRequest req, ClaimsPrincipal me, AppDbContext db) =>
        {
            var user = await db.Users.Include(u => u.Photos)
                .FirstOrDefaultAsync(u => u.Id == me.UserId());
            if (user is null) return Results.NotFound();

            if (req.DisplayName is { } name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new { error = "Display name cannot be blank." });
                user.DisplayName = name.Trim();
            }

            if (req.Bio is { } bio) user.Bio = bio.Trim();
            if (req.Interests is { } interests)
                user.Interests = string.Join(",", Compatibility.ParseList(interests));
            if (req.SeekingGenders is { } seeking)
                user.SeekingGenders = string.Join(",", Compatibility.ParseList(seeking));

            if (req.Latitude is { } lat)
            {
                if (!Geo.IsValidLatitude(lat)) return Results.BadRequest(new { error = "Latitude out of range." });
                user.Latitude = lat;
            }

            if (req.Longitude is { } lon)
            {
                if (!Geo.IsValidLongitude(lon)) return Results.BadRequest(new { error = "Longitude out of range." });
                user.Longitude = lon;
            }

            if (req.MinAge is { } min) user.MinAge = Math.Max(AuthEndpoints.MinimumAge, min);
            if (req.MaxAge is { } max) user.MaxAge = Math.Clamp(max, user.MinAge, 120);
            if (user.MaxAge < user.MinAge) user.MaxAge = user.MinAge;
            if (req.MaxDistanceKm is { } dist) user.MaxDistanceKm = Math.Clamp(dist, 1, 20_000);
            if (req.IsDiscoverable is { } visible) user.IsDiscoverable = visible;

            await db.SaveChangesAsync();
            return Results.Ok(user.ToMyProfile());
        });

        // GDPR-style hard delete: the account and everything attributable to it.
        group.MapDelete("/", async (
            ClaimsPrincipal me, AppDbContext db, IPhotoStorage storage, RealtimeNotifier realtime) =>
        {
            var id = me.UserId();
            var user = await db.Users.Include(u => u.Photos).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();

            var photoKeys = user.Photos.Select(p => p.FileName).ToList();
            var activeMatches = await db.Matches
                .Where(m => m.IsActive && (m.UserAId == id || m.UserBId == id)).ToListAsync();

            var matchIds = await db.Matches
                .Where(m => m.UserAId == id || m.UserBId == id)
                .Select(m => m.Id).ToListAsync();

            db.Messages.RemoveRange(db.Messages.Where(m => matchIds.Contains(m.MatchId)));
            db.Matches.RemoveRange(db.Matches.Where(m => m.UserAId == id || m.UserBId == id));
            db.Swipes.RemoveRange(db.Swipes.Where(s => s.SwiperId == id || s.TargetId == id));
            db.Blocks.RemoveRange(db.Blocks.Where(x => x.BlockerId == id || x.BlockedId == id));
            db.Reports.RemoveRange(db.Reports.Where(r => r.ReporterId == id));
            db.Users.Remove(user);   // photo rows and refresh tokens cascade

            await db.SaveChangesAsync();

            // The rows are gone; the image bytes in storage have to be removed separately or
            // a "deleted" account would leave its photos behind.
            foreach (var key in photoKeys) await storage.DeleteAsync(key);
            foreach (var match in activeMatches) await realtime.Unmatched(match);

            return Results.Ok(new { deleted = true });
        });

        // Another user's public profile — hidden if either side has blocked the other.
        app.MapGet("/users/{id:guid}", async (Guid id, ClaimsPrincipal me, AppDbContext db) =>
        {
            var myId = me.UserId();
            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == myId && b.BlockedId == id) ||
                (b.BlockerId == id && b.BlockedId == myId));
            if (blocked) return Results.NotFound();

            var user = await db.Users.Include(u => u.Photos).FirstOrDefaultAsync(u => u.Id == id);
            return user is null || user.IsBanned ? Results.NotFound() : Results.Ok(user.ToPublicProfile());
        }).RequireAuthorization().WithTags("Profile");
    }
}
