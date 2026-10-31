using System.Security.Claims;
using ConsoleMatch.Core;
using DatingApp.Api.Data;

namespace DatingApp.Api.Endpoints;

public static class Shared
{
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public static List<string> PhotoUrls(this User user) =>
        user.Photos
            .OrderByDescending(p => p.IsPrimary).ThenBy(p => p.UploadedAt)
            .Select(p => $"/photos/{p.Id}")
            .ToList();

    public static ProfileResponse ToPublicProfile(this User u) => new(
        u.Id, u.DisplayName, u.Age, u.Gender, u.Bio,
        Compatibility.ParseList(u.Interests), u.PhotoUrls());

    public static MyProfileResponse ToMyProfile(this User u) => new(
        u.Id, u.Email, u.DisplayName, u.Age, u.DateOfBirth, u.Gender, u.Bio,
        Compatibility.ParseList(u.Interests), u.Latitude, u.Longitude,
        Compatibility.ParseList(u.SeekingGenders), u.MinAge, u.MaxAge, u.MaxDistanceKm,
        u.IsDiscoverable, u.PhotoUrls(), u.IsAdmin);

    /// Empty preference list means "any gender".
    public static bool GenderMatches(string seekingCsv, string gender)
    {
        var seeking = Compatibility.ParseList(seekingCsv);
        return seeking.Count == 0 || seeking.Contains(gender.Trim().ToLowerInvariant());
    }
}
