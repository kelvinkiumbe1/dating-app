namespace ConsoleMatch.Core;

/// Shared scoring so the console client and the API rank candidates identically.
public static class Compatibility
{
    /// Jaccard index over interest sets, as a percentage. Case-insensitive.
    /// Returns 0 when either side has listed nothing, rather than dividing by zero.
    public static int Score(IEnumerable<string> a, IEnumerable<string> b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        if (left.Count == 0 || right.Count == 0) return 0;

        var union = left.Union(right).Count();
        if (union == 0) return 0;

        return (int)Math.Round(100.0 * left.Intersect(right).Count() / union);
    }

    public static HashSet<string> Normalize(IEnumerable<string> interests) =>
        interests
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim().ToLowerInvariant())
            .ToHashSet();

    /// Parses a comma-separated interest list into deduplicated, lowercased entries.
    public static List<string> ParseList(string? input) =>
        (input ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => i.ToLowerInvariant())
            .Distinct()
            .ToList();
}
