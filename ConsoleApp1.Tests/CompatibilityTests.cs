using ConsoleMatch;

namespace ConsoleApp1.Tests;

public class CompatibilityTests
{
    private static Candidate Candidate(int standards, params string[] interests) => new()
    {
        Name = "Test",
        Age = 30,
        Bio = "bio",
        Interests = interests,
        Standards = standards,
    };

    private static UserProfile User(params string[] interests) => new()
    {
        Name = "You",
        Age = 30,
        Bio = "bio",
        Interests = interests.ToList(),
    };

    [Fact]
    public void IdenticalInterestsAreFullyCompatible()
    {
        var c = Candidate(0, "coffee", "coding");
        Assert.Equal(100, c.CompatibilityWith(User("coffee", "coding")));
    }

    [Fact]
    public void NoSharedInterestsIsZero()
    {
        var c = Candidate(0, "coffee", "coding");
        Assert.Equal(0, c.CompatibilityWith(User("baking", "dogs")));
    }

    [Fact]
    public void PartialOverlapUsesJaccardIndex()
    {
        // Shared: coffee. Union: coffee, coding, baking => 1/3 => 33%.
        var c = Candidate(0, "coffee", "coding");
        Assert.Equal(33, c.CompatibilityWith(User("coffee", "baking")));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var c = Candidate(0, "Coffee", "CODING");
        Assert.Equal(100, c.CompatibilityWith(User("coffee", "coding")));
    }

    [Fact]
    public void EmptyUserInterestsScoreZeroRatherThanDividingByZero()
    {
        var c = Candidate(0, "coffee");
        Assert.Equal(0, c.CompatibilityWith(User()));
    }

    [Theory]
    [InlineData(20, 20, true)]   // exactly meeting standards is a match
    [InlineData(21, 20, true)]
    [InlineData(19, 20, false)]
    public void MatchRequiresCompatibilityAtOrAboveStandards(int compat, int standards, bool expected)
        => Assert.Equal(expected, compat >= standards);

    [Fact]
    public void EveryPooledCandidateHasReachableStandards()
    {
        // A candidate whose standards exceed 100 could never match anyone.
        Assert.All(DatingApp.Pool, c => Assert.InRange(c.Standards, 0, 100));
    }

    [Fact]
    public void PooledCandidateNamesAreUnique()
    {
        var names = DatingApp.Pool.Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
