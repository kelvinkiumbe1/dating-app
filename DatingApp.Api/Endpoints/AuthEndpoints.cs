using System.Security.Claims;
using ConsoleMatch.Core;
using DatingApp.Api.Auth;
using DatingApp.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class AuthEndpoints
{
    public const int MinimumAge = 18;

    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Tighter per-IP bucket: these are the credential-guessing endpoints.
        var group = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (
            RegisterRequest req, AppDbContext db, TokenService tokens, PasswordHasher<User> hasher,
            AdminOptions admins) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
                return Results.BadRequest(new { error = "A valid email is required." });

            if (PasswordProblem(req.Password) is { } problem)
                return Results.BadRequest(new { error = problem });

            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return Results.BadRequest(new { error = "Display name is required." });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var user = new User { DateOfBirth = req.DateOfBirth };
            if (req.DateOfBirth == default || user.AgeOn(today) < MinimumAge)
                return Results.BadRequest(new { error = $"You must be at least {MinimumAge} to sign up." });

            if (!Geo.IsValidLatitude(req.Latitude) || !Geo.IsValidLongitude(req.Longitude))
                return Results.BadRequest(new { error = "Latitude/longitude out of range." });

            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "An account with that email already exists." });

            user.Email = email;
            user.DisplayName = req.DisplayName.Trim();
            user.Gender = req.Gender.Trim().ToLowerInvariant();
            user.Bio = req.Bio?.Trim() ?? "";
            user.Interests = string.Join(",", Compatibility.ParseList(req.Interests));
            user.Latitude = req.Latitude;
            user.Longitude = req.Longitude;
            user.SeekingGenders = string.Join(",", Compatibility.ParseList(req.SeekingGenders));
            user.MinAge = Math.Max(MinimumAge, req.MinAge ?? MinimumAge);
            user.MaxAge = Math.Clamp(req.MaxAge ?? 99, user.MinAge, 120);
            user.MaxDistanceKm = Math.Clamp(req.MaxDistanceKm ?? 50, 1, 20_000);
            user.PasswordHash = hasher.HashPassword(user, req.Password);
            user.IsAdmin = admins.IsAdminEmail(email);

            db.Users.Add(user);
            return Results.Ok(await tokens.IssueSessionAsync(db, user));
        });

        group.MapPost("/login", async (
            LoginRequest req, AppDbContext db, TokenService tokens, PasswordHasher<User> hasher) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            // Same response whether the email is unknown or the password is wrong,
            // so the endpoint can't be used to enumerate registered accounts.
            if (user is null)
                return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "");
            if (result == PasswordVerificationResult.Failed)
                return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

            // Checked only after the password, so a ban never confirms an account to a guesser.
            if (user.IsBanned)
                return Results.Json(new { error = "This account has been suspended." }, statusCode: 403);

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = hasher.HashPassword(user, req.Password!);

            return Results.Ok(await tokens.IssueSessionAsync(db, user));
        });

        // Changing the password signs out every other session, then hands this one fresh tokens.
        group.MapPost("/change-password", async (
            ChangePasswordRequest req, ClaimsPrincipal principal, AppDbContext db, TokenService tokens,
            PasswordHasher<User> hasher) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId());
            if (user is null) return Results.NotFound();

            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword ?? "")
                == PasswordVerificationResult.Failed)
                return Results.BadRequest(new { error = "Current password is incorrect." });

            if (PasswordProblem(req.NewPassword) is { } problem)
                return Results.BadRequest(new { error = problem });

            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            user.TokenVersion++;
            await TokenService.RevokeAllAsync(db, user.Id);
            return Results.Ok(await tokens.IssueSessionAsync(db, user));
        }).RequireAuthorization();

        // Refresh and logout present a 256-bit random token, not a guessable password, so they
        // sit outside the tight per-IP auth bucket; the global limiter still applies.
        var session = app.MapGroup("/auth").WithTags("Auth");

        session.MapPost("/refresh", async (RefreshRequest req, AppDbContext db, TokenService tokens) =>
        {
            var hash = TokenService.Hash(req.RefreshToken ?? "");
            var stored = await db.RefreshTokens.Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            var invalid = Results.Json(new { error = "Session expired. Please sign in again." }, statusCode: 401);
            if (stored?.User is null) return invalid;

            var now = DateTimeOffset.UtcNow;
            if (stored.RevokedAt is { } revokedAt)
            {
                // A rotated token presented again outside the grace window means two parties
                // hold the same session: one of them stole it. Kill the whole chain.
                if (!stored.Rotated || now - revokedAt > tokens.RotationGrace)
                {
                    foreach (var t in await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync())
                        t.RevokedAt ??= now;
                    await db.SaveChangesAsync();
                }
                return invalid;
            }

            if (stored.ExpiresAt <= now || stored.User.IsBanned) return invalid;

            stored.RevokedAt = now;
            stored.Rotated = true;
            return Results.Ok(await tokens.IssueSessionAsync(db, stored.User, stored.FamilyId));
        });

        // Ends this device's session. Idempotent, and silent about unknown tokens.
        session.MapPost("/logout", async (RefreshRequest req, AppDbContext db) =>
        {
            var hash = TokenService.Hash(req.RefreshToken ?? "");
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (stored is not null)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var t in await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync())
                    t.RevokedAt ??= now;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { loggedOut = true });
        });

        // Ends every session, including access tokens already handed out.
        session.MapPost("/logout-all", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId());
            if (user is null) return Results.NotFound();

            user.TokenVersion++;
            await TokenService.RevokeAllAsync(db, user.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { loggedOut = true });
        }).RequireAuthorization();
    }

    private static string? PasswordProblem(string? password) =>
        (password ?? "").Length < 8 ? "Password must be at least 8 characters." : null;
}
