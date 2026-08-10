using System.Security.Claims;
using DatingApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Endpoints;

public static class PhotoEndpoints
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPhotosPerUser = 6;

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static void MapPhotoEndpoints(this WebApplication app, string uploadRoot)
    {
        Directory.CreateDirectory(uploadRoot);

        app.MapPost("/me/photos", async (
            IFormFile file, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var user = await db.Users.Include(u => u.Photos).FirstOrDefaultAsync(u => u.Id == meId);
            if (user is null) return Results.NotFound();

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded." });
            if (file.Length > MaxBytes)
                return Results.BadRequest(new { error = $"Photo exceeds {MaxBytes / 1024 / 1024}MB." });
            if (!AllowedTypes.TryGetValue(file.ContentType ?? "", out var extension))
                return Results.BadRequest(new { error = "Only JPEG, PNG, and WebP images are accepted." });
            if (user.Photos.Count >= MaxPhotosPerUser)
                return Results.BadRequest(new { error = $"You can have at most {MaxPhotosPerUser} photos." });

            var photo = new Photo
            {
                UserId = meId,
                ContentType = file.ContentType!,
                IsPrimary = user.Photos.Count == 0,
            };
            // Filename is server-generated — the client's name never touches the filesystem.
            photo.FileName = $"{photo.Id}{extension}";

            await using (var stream = File.Create(Path.Combine(uploadRoot, photo.FileName)))
                await file.CopyToAsync(stream);

            db.Photos.Add(photo);
            await db.SaveChangesAsync();

            return Results.Ok(new { photo.Id, url = $"/photos/{photo.Id}", photo.IsPrimary });
        }).RequireAuthorization().DisableAntiforgery().WithTags("Photos");

        app.MapGet("/photos/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return Results.NotFound();

            var path = Path.Combine(uploadRoot, photo.FileName);
            return File.Exists(path)
                ? Results.File(path, photo.ContentType)
                : Results.NotFound();
        }).WithTags("Photos");

        app.MapDelete("/me/photos/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var meId = principal.UserId();
            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id && p.UserId == meId);
            if (photo is null) return Results.NotFound();

            db.Photos.Remove(photo);
            await db.SaveChangesAsync();

            var path = Path.Combine(uploadRoot, photo.FileName);
            if (File.Exists(path)) File.Delete(path);

            // Promote another photo so the profile always has a primary if any remain.
            if (photo.IsPrimary)
            {
                // Ordered client-side for the same reason as message threads: SQLite
                // cannot ORDER BY DateTimeOffset, and a user has at most six photos.
                var next = (await db.Photos.Where(p => p.UserId == meId).ToListAsync())
                    .OrderBy(p => p.UploadedAt).FirstOrDefault();
                if (next is not null)
                {
                    next.IsPrimary = true;
                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok(new { deleted = true });
        }).RequireAuthorization().WithTags("Photos");
    }
}
