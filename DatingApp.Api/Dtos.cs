namespace DatingApp.Api;

public record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    DateOnly DateOfBirth,
    string Gender,
    string Bio,
    string Interests,
    double Latitude,
    double Longitude,
    string? SeekingGenders = null,
    int? MinAge = null,
    int? MaxAge = null,
    int? MaxDistanceKm = null);

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string Token,
    Guid UserId,
    string DisplayName,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ProfileResponse(
    Guid Id,
    string DisplayName,
    int Age,
    string Gender,
    string Bio,
    List<string> Interests,
    List<string> PhotoUrls);

public record MyProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    int Age,
    DateOnly DateOfBirth,
    string Gender,
    string Bio,
    List<string> Interests,
    double Latitude,
    double Longitude,
    List<string> SeekingGenders,
    int MinAge,
    int MaxAge,
    int MaxDistanceKm,
    bool IsDiscoverable,
    List<string> PhotoUrls,
    bool IsAdmin);

public record UpdateProfileRequest(
    string? DisplayName,
    string? Bio,
    string? Interests,
    double? Latitude,
    double? Longitude,
    string? SeekingGenders,
    int? MinAge,
    int? MaxAge,
    int? MaxDistanceKm,
    bool? IsDiscoverable);

public record CandidateResponse(
    Guid Id,
    string DisplayName,
    int Age,
    string Gender,
    string Bio,
    List<string> Interests,
    int CompatibilityPercent,
    int DistanceKm,
    List<string> PhotoUrls);

public record SwipeRequest(Guid TargetId, bool IsLike);

public record SwipeResponse(bool IsMatch, Guid? MatchId, string Message);

public record MatchResponse(
    Guid MatchId,
    Guid UserId,
    string DisplayName,
    int Age,
    int CompatibilityPercent,
    DateTimeOffset MatchedAt,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    List<string> PhotoUrls);

public record SendMessageRequest(string Body);

public record MessageResponse(
    Guid Id,
    Guid SenderId,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);

public record ReportRequest(Guid UserId, string Reason, string? Details);

// ── Moderation ──────────────────────────────────────────────────────────────

public record AdminUserSummary(
    Guid Id,
    string DisplayName,
    string Email,
    int Age,
    string Bio,
    List<string> PhotoUrls,
    bool IsBanned,
    string? BanReason,
    int OpenReportCount,
    int TotalReportCount);

public record AdminReportResponse(
    Guid Id,
    string Reason,
    string? Details,
    DateTimeOffset CreatedAt,
    bool Reviewed,
    DateTimeOffset? ReviewedAt,
    string? Resolution,
    string? ModeratorNote,
    Guid ReporterId,
    string ReporterName,
    AdminUserSummary? Reported);

/// Action is "dismiss" or "ban".
public record ResolveReportRequest(string Action, string? Note);

public record BanRequest(string? Reason);

// ── Realtime events (pushed over /hubs/chat) ────────────────────────────────

public record MessageEvent(Guid MatchId, MessageResponse Message);

public record ReadEvent(Guid MatchId, Guid ReaderId, DateTimeOffset ReadAt);

public record MatchEvent(Guid MatchId, Guid UserId, string DisplayName, List<string> PhotoUrls);

public record UnmatchedEvent(Guid MatchId);

public record TypingEvent(Guid MatchId, Guid UserId);
