using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class MatchingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public MatchingTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task TwoRealUsersMatchOnlyWhenBothSwipeRight()
    {
        var alice = await _api.RegisterAsync(Email(), "Alice", gender: "f", seeking: "m");
        var bob = await _api.RegisterAsync(Email(), "Bob", gender: "m", seeking: "f");

        // Alice likes Bob first — no match yet, because Bob hasn't answered.
        var first = await alice.Client.PostAsJsonAsync("/swipes", new SwipeRequest(bob.Id, true));
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<SwipeResponse>();
        Assert.False(firstResult!.IsMatch);
        Assert.Null(firstResult.MatchId);

        // Bob likes Alice back — now it's a match.
        var second = await bob.Client.PostAsJsonAsync("/swipes", new SwipeRequest(alice.Id, true));
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<SwipeResponse>();
        Assert.True(secondResult!.IsMatch);
        Assert.NotNull(secondResult.MatchId);

        // And both sides see it.
        var aliceMatches = await alice.Client.GetFromJsonAsync<List<MatchResponse>>("/matches");
        var bobMatches = await bob.Client.GetFromJsonAsync<List<MatchResponse>>("/matches");
        Assert.Contains(aliceMatches!, m => m.UserId == bob.Id);
        Assert.Contains(bobMatches!, m => m.UserId == alice.Id);
    }

    [Fact]
    public async Task PassingMeansNoMatchEvenIfTheOtherPersonLikesYou()
    {
        var carol = await _api.RegisterAsync(Email(), "Carol", gender: "f", seeking: "m");
        var dave = await _api.RegisterAsync(Email(), "Dave", gender: "m", seeking: "f");

        await carol.Client.PostAsJsonAsync("/swipes", new SwipeRequest(dave.Id, true));
        var response = await dave.Client.PostAsJsonAsync("/swipes", new SwipeRequest(carol.Id, false));

        var result = await response.Content.ReadFromJsonAsync<SwipeResponse>();
        Assert.False(result!.IsMatch);

        var matches = await dave.Client.GetFromJsonAsync<List<MatchResponse>>("/matches");
        Assert.DoesNotContain(matches!, m => m.UserId == carol.Id);
    }

    [Fact]
    public async Task SwipingTwiceOnTheSamePersonIsRejected()
    {
        var eve = await _api.RegisterAsync(Email(), "Eve");
        var frank = await _api.RegisterAsync(Email(), "Frank");

        await eve.Client.PostAsJsonAsync("/swipes", new SwipeRequest(frank.Id, true));
        var again = await eve.Client.PostAsJsonAsync("/swipes", new SwipeRequest(frank.Id, false));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task YouCannotSwipeOnYourself()
    {
        var user = await _api.RegisterAsync(Email(), "Solo");
        var response = await user.Client.PostAsJsonAsync("/swipes", new SwipeRequest(user.Id, true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PendingAdmirersShowUpUnderLikes()
    {
        var grace = await _api.RegisterAsync(Email(), "Grace", gender: "f", seeking: "m");
        var henry = await _api.RegisterAsync(Email(), "Henry", gender: "m", seeking: "f");

        await henry.Client.PostAsJsonAsync("/swipes", new SwipeRequest(grace.Id, true));

        var likes = await grace.Client.GetFromJsonAsync<List<ProfileResponse>>("/likes");
        Assert.Contains(likes!, p => p.Id == henry.Id);

        // Once Grace answers, they stop being "pending".
        await grace.Client.PostAsJsonAsync("/swipes", new SwipeRequest(henry.Id, true));
        var after = await grace.Client.GetFromJsonAsync<List<ProfileResponse>>("/likes");
        Assert.DoesNotContain(after!, p => p.Id == henry.Id);
    }
}
