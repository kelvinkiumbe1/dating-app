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
            RegisterRequest req, AppDbContext db, TokenService tokens, PasswordHasher<User> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
                return Results.BadRequest(new { error = "A valid email is required." });

            if (req.Password.Length < 8)
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });

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

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(new AuthResponse(tokens.Issue(user), user.Id, user.DisplayName));
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

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, req.Password!);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new AuthResponse(tokens.Issue(user), user.Id, user.DisplayName));
        });
    }
}
