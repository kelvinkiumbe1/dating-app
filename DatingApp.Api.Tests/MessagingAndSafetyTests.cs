using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class MessagingAndSafetyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public MessagingAndSafetyTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    /// Registers two users and gets them matched.
    private async Task<(TestUser a, TestUser b, Guid matchId)> MatchedPairAsync()
    {
        var a = await _api.RegisterAsync(Email(), "PairA", gender: "f", seeking: "m");
        var b = await _api.RegisterAsync(Email(), "PairB", gender: "m", seeking: "f");

        await a.Client.PostAsJsonAsync("/swipes", new SwipeRequest(b.Id, true));
        var response = await b.Client.PostAsJsonAsync("/swipes", new SwipeRequest(a.Id, true));
        var result = await response.Content.ReadFromJsonAsync<SwipeResponse>();

        return (a, b, result!.MatchId!.Value);
    }

    [Fact]
    public async Task MatchedUsersCanMessageEachOtherAndSeeReadReceipts()
    {
        var (alice, bob, matchId) = await MatchedPairAsync();

        var sent = await alice.Client.PostAsJsonAsync(
            $"/matches/{matchId}/messages", new SendMessageRequest("hey, coffee sometime?"));
        sent.EnsureSuccessStatusCode();

        // Bob genuinely receives it — this is a real second user, not a simulation.
        var bobThread = await bob.Client.GetFromJsonAsync<List<MessageResponse>>($"/matches/{matchId}/messages");
        Assert.Single(bobThread!);
        Assert.Equal("hey, coffee sometime?", bobThread![0].Body);
        Assert.Equal(alice.Id, bobThread[0].SenderId);

        // Bob having read it is now visible to Alice.
        var aliceThread = await alice.Client.GetFromJsonAsync<List<MessageResponse>>($"/matches/{matchId}/messages");
        Assert.NotNull(aliceThread![0].ReadAt);
    }

    [Fact]
    public async Task UnreadCountsAppearOnTheMatchList()
    {
        var (alice, bob, matchId) = await MatchedPairAsync();

        await alice.Client.PostAsJsonAsync($"/matches/{matchId}/messages", new SendMessageRequest("one"));
        await alice.Client.PostAsJsonAsync($"/matches/{matchId}/messages", new SendMessageRequest("two"));

        var bobMatches = await bob.Client.GetFromJsonAsync<List<MatchResponse>>("/matches");
        var thread = bobMatches!.Single(m => m.MatchId == matchId);
        Assert.Equal(2, thread.UnreadCount);
        Assert.Equal("two", thread.LastMessage);
    }

    [Fact]
    public async Task StrangersCannotMessageEachOther()
    {
        var (_, _, matchId) = await MatchedPairAsync();
        var outsider = await _api.RegisterAsync(Email(), "Outsider");

        var read = await outsider.Client.GetAsync($"/matches/{matchId}/messages");
        var write = await outsider.Client.PostAsJsonAsync(
            $"/matches/{matchId}/messages", new SendMessageRequest("let me in"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task EmptyMessagesAreRejected()
    {
        var (alice, _, matchId) = await MatchedPairAsync();
        var response = await alice.Client.PostAsJsonAsync(
            $"/matches/{matchId}/messages", new SendMessageRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BlockingEndsTheMatchAndStopsMessaging()
    {
        var (alice, bob, matchId) = await MatchedPairAsync();
        await alice.Client.PostAsJsonAsync($"/matches/{matchId}/messages", new SendMessageRequest("hi"));

        var blocked = await alice.Client.PostAsync($"/safety/block/{bob.Id}", null);
        blocked.EnsureSuccessStatusCode();

        // The match disappears for both sides...
        Assert.Empty((await alice.Client.GetFromJsonAsync<List<MatchResponse>>("/matches"))!
            .Where(m => m.MatchId == matchId));
        Assert.Empty((await bob.Client.GetFromJsonAsync<List<MatchResponse>>("/matches"))!
            .Where(m => m.MatchId == matchId));

        // ...and the blocked user can no longer send anything.
        var attempt = await bob.Client.PostAsJsonAsync(
            $"/matches/{matchId}/messages", new SendMessageRequest("please respond"));
        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
    }

    [Fact]
    public async Task BlockedUsersDisappearFromEachOthersProfilesAndDeck()
    {
        var alice = await _api.RegisterAsync(Email(), "Blocker", gender: "f", seeking: "m");
        var bob = await _api.RegisterAsync(Email(), "Blocked", gender: "m", seeking: "f");

        await alice.Client.PostAsync($"/safety/block/{bob.Id}", null);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.GetAsync($"/users/{bob.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/users/{alice.Id}")).StatusCode);

        var aliceDeck = await alice.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        var bobDeck = await bob.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        Assert.DoesNotContain(aliceDeck!, c => c.Id == bob.Id);
        Assert.DoesNotContain(bobDeck!, c => c.Id == alice.Id);
    }

    [Fact]
    public async Task UnmatchingDeactivatesTheThread()
    {
        var (alice, bob, matchId) = await MatchedPairAsync();

        var unmatched = await alice.Client.DeleteAsync($"/matches/{matchId}");
        unmatched.EnsureSuccessStatusCode();

        var attempt = await bob.Client.PostAsJsonAsync(
            $"/matches/{matchId}/messages", new SendMessageRequest("hello?"));
        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
    }

    [Fact]
    public async Task ReportsAreAcceptedAndValidated()
    {
        var reporter = await _api.RegisterAsync(Email(), "Reporter");
        var target = await _api.RegisterAsync(Email(), "Target");

        var valid = await reporter.Client.PostAsJsonAsync(
            "/safety/report", new ReportRequest(target.Id, "harassment", "sent abusive messages"));
        valid.EnsureSuccessStatusCode();

        var invalid = await reporter.Client.PostAsJsonAsync(
            "/safety/report", new ReportRequest(target.Id, "because-i-said-so", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var self = await reporter.Client.PostAsJsonAsync(
            "/safety/report", new ReportRequest(reporter.Id, "spam", null));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }
}
