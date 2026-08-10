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

public record AuthResponse(string Token, Guid UserId, string DisplayName);

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
    List<string> PhotoUrls);

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
