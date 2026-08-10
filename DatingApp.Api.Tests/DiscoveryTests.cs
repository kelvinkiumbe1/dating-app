using System.Net.Http.Json;
using ConsoleMatch.Core;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class DiscoveryTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public DiscoveryTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    // London and Edinburgh are ~530km apart — comfortably outside a 50km radius.
    private const double LondonLat = 51.5074, LondonLon = -0.1278;
    private const double EdinburghLat = 55.9533, EdinburghLon = -3.1883;

    [Fact]
    public async Task PeopleOutsideMyDistanceRadiusAreNotShown()
    {
        var me = await _api.RegisterAsync(Email(), "Local", gender: "f", seeking: "m",
            lat: LondonLat, lon: LondonLon, maxDistanceKm: 50);
        var faraway = await _api.RegisterAsync(Email(), "Faraway", gender: "m", seeking: "f",
            lat: EdinburghLat, lon: EdinburghLon, maxDistanceKm: 50);

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        Assert.DoesNotContain(deck!, c => c.Id == faraway.Id);
    }

    [Fact]
    public async Task PeopleOutsideMyAgeRangeAreNotShown()
    {
        var me = await _api.RegisterAsync(Email(), "Picky", gender: "f", seeking: "m",
            age: 30, minAge: 28, maxAge: 34);
        var tooYoung = await _api.RegisterAsync(Email(), "TooYoung", gender: "m", seeking: "f", age: 20);
        var justRight = await _api.RegisterAsync(Email(), "JustRight", gender: "m", seeking: "f", age: 31);

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        Assert.DoesNotContain(deck!, c => c.Id == tooYoung.Id);
        Assert.Contains(deck!, c => c.Id == justRight.Id);
    }

    [Fact]
    public async Task GenderPreferencesAreRespectedInBothDirections()
    {
        var me = await _api.RegisterAsync(Email(), "SeeksMen", gender: "f", seeking: "m");
        var wrongGender = await _api.RegisterAsync(Email(), "AlsoWoman", gender: "f", seeking: "m");
        var notSeekingMe = await _api.RegisterAsync(Email(), "SeeksMenToo", gender: "m", seeking: "m");
        var mutual = await _api.RegisterAsync(Email(), "SeeksWomen", gender: "m", seeking: "f");

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");

        Assert.DoesNotContain(deck!, c => c.Id == wrongGender.Id);   // I'm not looking for women
        Assert.DoesNotContain(deck!, c => c.Id == notSeekingMe.Id);  // he isn't looking for women
        Assert.Contains(deck!, c => c.Id == mutual.Id);
    }

    [Fact]
    public async Task AlreadySwipedPeopleDoNotReappear()
    {
        var me = await _api.RegisterAsync(Email(), "Swiper", gender: "f", seeking: "m");
        var them = await _api.RegisterAsync(Email(), "Swiped", gender: "m", seeking: "f");

        Assert.Contains((await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover"))!,
            c => c.Id == them.Id);

        await me.Client.PostAsJsonAsync("/swipes", new SwipeRequest(them.Id, false));

        Assert.DoesNotContain((await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover"))!,
            c => c.Id == them.Id);
    }

    [Fact]
    public async Task HidingMyProfileRemovesMeFromEveryoneElsesDeck()
    {
        var hider = await _api.RegisterAsync(Email(), "Hider", gender: "f", seeking: "m");
        var seeker = await _api.RegisterAsync(Email(), "Seeker", gender: "m", seeking: "f");

        Assert.Contains((await seeker.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover"))!,
            c => c.Id == hider.Id);

        var hidden = await hider.Client.PatchAsJsonAsync("/me",
            new UpdateProfileRequest(null, null, null, null, null, null, null, null, null, IsDiscoverable: false));
        hidden.EnsureSuccessStatusCode();

        Assert.DoesNotContain((await seeker.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover"))!,
            c => c.Id == hider.Id);
    }

    [Fact]
    public async Task DeckReportsCompatibilityAndDistance()
    {
        var me = await _api.RegisterAsync(Email(), "Scorer", gender: "f", seeking: "m",
            interests: "coffee,coding", lat: LondonLat, lon: LondonLon);
        var them = await _api.RegisterAsync(Email(), "Scored", gender: "m", seeking: "f",
            interests: "coffee,coding", lat: LondonLat, lon: LondonLon);

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        var candidate = deck!.Single(c => c.Id == them.Id);

        Assert.Equal(100, candidate.CompatibilityPercent);   // identical interest sets
        Assert.Equal(0, candidate.DistanceKm);               // same coordinates
    }

    [Fact]
    public async Task DeckIsRankedByCompatibility()
    {
        var me = await _api.RegisterAsync(Email(), "Ranker", gender: "f", seeking: "m",
            interests: "coffee,coding,music");
        await _api.RegisterAsync(Email(), "NoOverlap", gender: "m", seeking: "f", interests: "fishing");
        await _api.RegisterAsync(Email(), "FullOverlap", gender: "m", seeking: "f",
            interests: "coffee,coding,music");

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        var scores = deck!.Select(c => c.CompatibilityPercent).ToList();

        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public void GeoDistanceMatchesKnownCitySeparation()
    {
        // London → Edinburgh is roughly 530km; allow slack for the spherical approximation.
        var km = Geo.DistanceKm(LondonLat, LondonLon, EdinburghLat, EdinburghLon);
        Assert.InRange(km, 500, 560);
    }
}
