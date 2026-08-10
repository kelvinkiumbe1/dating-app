namespace ConsoleMatch;

public class UserProfile
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Bio { get; set; } = "";
    public List<string> Interests { get; set; } = new();
}

public class Candidate
{
    public required string Name { get; init; }
    public required int Age { get; init; }
    public required string Bio { get; init; }
    public required string[] Interests { get; init; }

    // Minimum compatibility (0-100) this candidate needs to like you back.
    public required int Standards { get; init; }

    public int CompatibilityWith(UserProfile user)
    {
        if (user.Interests.Count == 0) return 0;
        var mine = Interests.Select(i => i.ToLowerInvariant()).ToHashSet();
        var theirs = user.Interests.Select(i => i.ToLowerInvariant()).ToHashSet();
        var shared = mine.Intersect(theirs).Count();
        var union = mine.Union(theirs).Count();
        return (int)Math.Round(100.0 * shared / union);
    }
}

public class ChatMessage
{
    public required string From { get; set; }
    public required string Text { get; set; }
}

public class AppState
{
    public UserProfile? User { get; set; }
    public List<string> Swiped { get; set; } = new();
    public List<string> Matches { get; set; } = new();
    public Dictionary<string, List<ChatMessage>> Chats { get; set; } = new();
}
