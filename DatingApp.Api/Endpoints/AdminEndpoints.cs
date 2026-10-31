using System.Security.Claims;
using DatingApp.Api.Auth;
using DatingApp.Api.Data;
using DatingApp.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

/// The human-review queue for the reports /safety/report collects. Admins are named in
/// configuration (see AdminOptions); the "admin" role claim is attached per request from
/// the database, so demotion takes effect immediately.
public static class AdminEndpoints
{
    public const string Policy = "admin";

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policy);

        // Open reports oldest first, so nothing waits indefinitely behind newer arrivals.
        group.MapGet("/reports", async (AppDbContext db, string? status) =>
        {
            var query = db.Reports.AsQueryable();
            if (status != "all") query = query.Where(r => !r.Reviewed);

            var reports = (await query.ToListAsync())
                .OrderBy(r => r.Reviewed).ThenBy(r => r.CreatedAt)
                .Take(200)
                .ToList();

            var userIds = reports.SelectMany(r => new[] { r.ReporterId, r.ReportedId }).Distinct().ToList();
            var users = await db.Users.Include(u => u.Photos)
                .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

            var counts = await ReportCountsAsync(db, reports.Select(r => r.ReportedId).Distinct().ToList());

            return Results.Ok(reports.Select(r => new AdminReportResponse(
                r.Id, r.Reason, r.Details, r.CreatedAt, r.Reviewed, r.ReviewedAt, r.Resolution, r.ModeratorNote,
                r.ReporterId,
                users.GetValueOrDefault(r.ReporterId)?.DisplayName ?? "(deleted user)",
                users.GetValueOrDefault(r.ReportedId) is { } reported
                    ? Summarize(reported, counts.GetValueOrDefault(reported.Id))
                    : null)).ToList());
        });

        group.MapPost("/reports/{id:guid}/resolve", async (
            Guid id, ResolveReportRequest req, ClaimsPrincipal principal, AppDbContext db,
            RealtimeNotifier realtime) =>
        {
            var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id);
            if (report is null) return Results.NotFound();
            if (report.Reviewed) return Results.Conflict(new { error = "This report was already resolved." });

            var action = (req.Action ?? "").Trim().ToLowerInvariant();
            if (action is not ("dismiss" or "ban"))
                return Results.BadRequest(new { error = "Action must be 'dismiss' or 'ban'." });

            var moderatorId = principal.UserId();
            var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

            if (action == "dismiss")
            {
                Close(report, moderatorId, "dismissed", note);
                await db.SaveChangesAsync();
                return Results.Ok(new { resolved = true, resolution = "dismissed" });
            }

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == report.ReportedId);
            if (target is null)
            {
                // The account deleted itself after being reported; nothing left to ban.
                Close(report, moderatorId, "dismissed", note ?? "Account no longer exists.");
                await db.SaveChangesAsync();
                return Results.Ok(new { resolved = true, resolution = "dismissed" });
            }

            if (await BanAsync(db, realtime, target, note ?? $"Reported: {report.Reason}", moderatorId) is { } error)
                return error;

            return Results.Ok(new { resolved = true, resolution = "banned" });
        });

        group.MapPost("/users/{id:guid}/ban", async (
            Guid id, BanRequest req, ClaimsPrincipal principal, AppDbContext db, RealtimeNotifier realtime) =>
        {
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            return await BanAsync(db, realtime, target, req.Reason?.Trim(), principal.UserId())
                ?? Results.Ok(new { banned = true });
        });

        // Lifts the ban but does not restore matches: the other side may have moved on, and
        // reviving conversations with a previously banned account is not a default to pick.
        group.MapPost("/users/{id:guid}/unban", async (Guid id, AppDbContext db) =>
        {
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            target.IsBanned = false;
            target.BannedAt = null;
            target.BanReason = null;
            await db.SaveChangesAsync();
            return Results.Ok(new { banned = false });
        });

        group.MapGet("/users/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var user = await db.Users.Include(u => u.Photos).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var counts = await ReportCountsAsync(db, [id]);
            return Results.Ok(Summarize(user, counts.GetValueOrDefault(id)));
        });
    }

    /// Bans immediately: blocks sign-in and refresh, invalidates live access tokens through
    /// TokenVersion, ends every match, and closes all open reports against the account.
    private static async Task<IResult?> BanAsync(
        AppDbContext db, RealtimeNotifier realtime, User target, string? reason, Guid moderatorId)
    {
        if (target.Id == moderatorId)
            return Results.BadRequest(new { error = "You cannot ban yourself." });
        if (target.IsAdmin)
            return Results.BadRequest(new { error = "Admins cannot be banned. Remove them from Admin:Emails first." });

        target.IsBanned = true;
        target.BannedAt = DateTimeOffset.UtcNow;
        target.BanReason = reason;
        target.TokenVersion++;
        await TokenService.RevokeAllAsync(db, target.Id);

        var matches = await db.Matches
            .Where(m => m.IsActive && (m.UserAId == target.Id || m.UserBId == target.Id)).ToListAsync();
        foreach (var m in matches) m.IsActive = false;

        foreach (var open in await db.Reports.Where(r => r.ReportedId == target.Id && !r.Reviewed).ToListAsync())
            Close(open, moderatorId, "banned", reason);

        await db.SaveChangesAsync();
        foreach (var m in matches) await realtime.Unmatched(m);
        return null;
    }

    private static void Close(Report report, Guid moderatorId, string resolution, string? note)
    {
        report.Reviewed = true;
        report.ReviewedAt = DateTimeOffset.UtcNow;
        report.ReviewedById = moderatorId;
        report.Resolution = resolution;
        report.ModeratorNote = note;
    }

    private static async Task<Dictionary<Guid, (int Open, int Total)>> ReportCountsAsync(
        AppDbContext db, List<Guid> userIds) =>
        (await db.Reports.Where(r => userIds.Contains(r.ReportedId))
            .Select(r => new { r.ReportedId, r.Reviewed }).ToListAsync())
        .GroupBy(r => r.ReportedId)
        .ToDictionary(g => g.Key, g => (g.Count(r => !r.Reviewed), g.Count()));

    private static AdminUserSummary Summarize(User u, (int Open, int Total) counts) => new(
        u.Id, u.DisplayName, u.Email, u.Age, u.Bio, u.PhotoUrls(), u.IsBanned, u.BanReason,
        counts.Open, counts.Total);
}
