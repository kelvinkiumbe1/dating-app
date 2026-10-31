namespace DatingApp.Api.Data;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Identity
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Profile
    public string DisplayName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string Bio { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Interests { get; set; } = "";     // comma-separated, lowercased

    // Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Discovery preferences
    public string SeekingGenders { get; set; } = "";  // comma-separated; empty = any
    public int MinAge { get; set; } = 18;
    public int MaxAge { get; set; } = 99;
    public int MaxDistanceKm { get; set; } = 50;

    public bool IsDiscoverable { get; set; } = true;

    // Security
    /// Embedded in every access token; bumping it invalidates all outstanding tokens at once
    /// (sign out everywhere, password change, ban).
    public int TokenVersion { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }

    public List<Photo> Photos { get; set; } = new();

    public int Age => AgeOn(DateOnly.FromDateTime(DateTime.UtcNow));

    public int AgeOn(DateOnly today)
    {
        var age = today.Year - DateOfBirth.Year;
        if (DateOfBirth > today.AddYears(-age)) age--;
        return age;
    }
}

public class Photo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string FileName { get; set; } = "";      // stored file on disk
    public string ContentType { get; set; } = "";
    public bool IsPrimary { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Swipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwiperId { get; set; }
    public Guid TargetId { get; set; }
    public bool IsLike { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// A mutual like. UserAId/UserBId are stored ordered so a pair can only match once.
public class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;      // false once either side unmatches/blocks

    public List<Message> Messages { get; set; } = new();

    public static (Guid a, Guid b) Order(Guid x, Guid y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);

    public Guid OtherUser(Guid me) => me == UserAId ? UserBId : UserAId;

    public bool Includes(Guid userId) => userId == UserAId || userId == UserBId;
}

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

public class Block
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public Guid ReportedId { get; set; }
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Reviewed { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public string? Resolution { get; set; }         // "dismissed" or "banned"
    public string? ModeratorNote { get; set; }
}

/// A long-lived credential exchanged for short-lived access tokens. Only the SHA-256 of the
/// token is stored, so a database leak does not hand out sessions. Each refresh rotates the
/// token; the rotated tokens share a FamilyId so replaying an old one can revoke the chain.
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool Rotated { get; set; }              // revoked by being exchanged, not by logout
}
