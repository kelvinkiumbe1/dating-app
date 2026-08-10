using System.Security.Claims;
using DatingApp.Api.Data;
using DatingApp.Api.Storage;
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

    /// Magic-byte prefixes, checked because a client can claim any Content-Type it likes.
    private static readonly (string ContentType, byte[] Magic)[] Signatures =
    {
        ("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        ("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }),   // "RIFF" + WEBP at offset 8
    };

    public static void MapPhotoEndpoints(this WebApplication app)
    {
        app.MapPost("/me/photos", async (
            IFormFile file, ClaimsPrincipal principal, AppDbContext db, IPhotoStorage storage) =>
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

            // Buffered in full (already capped at 5MB) so the signature check and the
            // upload read the same bytes without relying on the stream being seekable.
            using var buffer = new MemoryStream();
            await using (var upload = file.OpenReadStream())
                await upload.CopyToAsync(buffer);
            buffer.Position = 0;

            if (!LooksLikeDeclaredImage(buffer, file.ContentType!))
                return Results.BadRequest(new { error = "File contents do not match the declared image type." });
            buffer.Position = 0;

            var photo = new Photo
            {
                UserId = meId,
                ContentType = file.ContentType!,
                IsPrimary = user.Photos.Count == 0,
            };
            // Key is server-generated — a client-supplied filename never reaches storage.
            photo.FileName = $"{photo.Id}{extension}";

            await storage.SaveAsync(photo.FileName, buffer, photo.ContentType);

            db.Photos.Add(photo);
            await db.SaveChangesAsync();

            return Results.Ok(new { photo.Id, url = $"/photos/{photo.Id}", photo.IsPrimary });
        }).RequireAuthorization().DisableAntiforgery().WithTags("Photos");

        app.MapGet("/photos/{id:guid}", async (
            Guid id, AppDbContext db, IPhotoStorage storage, HttpContext http) =>
        {
            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return Results.NotFound();

            var stream = await storage.OpenAsync(photo.FileName);
            if (stream is null) return Results.NotFound();

            // Photo bytes never change for a given id, so let clients cache aggressively.
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(stream, photo.ContentType);
        }).WithTags("Photos");

        app.MapDelete("/me/photos/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, IPhotoStorage storage) =>
        {
            var meId = principal.UserId();
            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id && p.UserId == meId);
            if (photo is null) return Results.NotFound();

            db.Photos.Remove(photo);
            await db.SaveChangesAsync();
            await storage.DeleteAsync(photo.FileName);

            // Promote another photo so the profile always has a primary if any remain.
            if (photo.IsPrimary)
            {
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

    private static bool LooksLikeDeclaredImage(MemoryStream buffer, string contentType)
    {
        var expected = Signatures.FirstOrDefault(s => s.ContentType == contentType).Magic;
        if (expected is null) return false;

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        return bytes.Length >= expected.Length && bytes[..expected.Length].SequenceEqual(expected);
    }
}
