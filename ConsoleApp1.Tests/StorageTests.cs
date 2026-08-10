using ConsoleMatch;

namespace ConsoleApp1.Tests;

public class StorageTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cm-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void MissingFileYieldsEmptyState()
    {
        var state = Storage.LoadFrom(_path);
        Assert.Null(state.User);
        Assert.Empty(state.Swiped);
        Assert.Empty(state.Matches);
    }

    [Fact]
    public void StateSurvivesARoundTrip()
    {
        var original = new AppState
        {
            User = new UserProfile { Name = "Kelvin", Age = 28, Bio = "debug by day", Interests = new() { "coffee", "coding" } },
            Swiped = new() { "Sam", "Riley" },
            Matches = new() { "Sam" },
            Chats = new() { ["Sam"] = new() { new ChatMessage { From = "you", Text = "hey" } } },
        };

        Storage.SaveTo(_path, original);
        var loaded = Storage.LoadFrom(_path);

        Assert.Equal("Kelvin", loaded.User!.Name);
        Assert.Equal(28, loaded.User.Age);
        Assert.Equal(new[] { "coffee", "coding" }, loaded.User.Interests);
        Assert.Equal(new[] { "Sam", "Riley" }, loaded.Swiped);
        Assert.Equal(new[] { "Sam" }, loaded.Matches);
        Assert.Equal("hey", loaded.Chats["Sam"].Single().Text);
    }

    [Fact]
    public void CorruptFileFallsBackToEmptyStateInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{ this is not json");
        var state = Storage.LoadFrom(_path);
        Assert.Null(state.User);
    }
}

public class ProfileParsingTests
{
    [Fact]
    public void InterestsAreSplitTrimmedAndLowercased()
        => Assert.Equal(new[] { "coffee", "hiking", "film" },
                        DatingApp.ParseInterests(" Coffee ,  HIKING,film "));

    [Fact]
    public void DuplicateInterestsAreCollapsed()
        => Assert.Equal(new[] { "coffee" }, DatingApp.ParseInterests("coffee, Coffee, COFFEE"));

    [Fact]
    public void EmptyInputYieldsNoInterests()
        => Assert.Empty(DatingApp.ParseInterests("   ,  , "));
}

public class WrapTests
{
    [Fact]
    public void ShortTextIsLeftAlone()
        => Assert.Equal(new[] { "hello there" }, DatingApp.Wrap("hello there", 40));

    [Fact]
    public void LongTextWrapsAtOrBelowTheGivenWidth()
    {
        var lines = DatingApp.Wrap("Coffee snob, trail runner, will judge your semicolon usage.", 20);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(l.Length <= 20, $"line too long: '{l}'"));
    }

    [Fact]
    public void EmptyTextYieldsOneBlankLineSoTheCardStillRenders()
        => Assert.Equal(new[] { "" }, DatingApp.Wrap("", 20));
}

public class ReplyEngineTests
{
    private static readonly Candidate Sam = DatingApp.Pool.First(c => c.Name == "Sam");
    private static readonly UserProfile Me = new()
    {
        Name = "Kelvin", Age = 28, Bio = "debug by day", Interests = new() { "coffee", "coding" },
    };

    [Fact]
    public async Task CannedEngineIsDeterministicWhenSeeded()
    {
        var a = await new CannedReplyEngine(seed: 42).ReplyAsync(Sam, Me, Array.Empty<ChatMessage>());
        var b = await new CannedReplyEngine(seed: 42).ReplyAsync(Sam, Me, Array.Empty<ChatMessage>());
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task CannedEngineAlwaysReturnsSomethingToSay()
    {
        var reply = await new CannedReplyEngine(seed: 1).ReplyAsync(Sam, Me, Array.Empty<ChatMessage>());
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public void SystemPromptCarriesBothProfilesAndTheCompatibilityScore()
    {
        var prompt = ClaudeReplyEngine.BuildSystemPrompt(Sam, Me);
        Assert.Contains("Sam", prompt);
        Assert.Contains("Kelvin", prompt);
        Assert.Contains("coffee", prompt);
        Assert.Contains($"{Sam.CompatibilityWith(Me)}% compatible", prompt);
    }
}
