using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;
using Microsoft.AspNetCore.Hosting;

namespace DatingApp.Api.Tests;

public class SessionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public SessionTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    private async Task<HttpResponseMessage> Refresh(string token) =>
        await _api.CreateClient().PostAsJsonAsync("/auth/refresh", new RefreshRequest(token));

    [Fact]
    public async Task RegisteringReturnsAShortLivedAccessTokenAndARefreshToken()
    {
        var user = await _api.RegisterAsync(Email(), "Fresh");

        Assert.False(string.IsNullOrWhiteSpace(user.RefreshToken));
        Assert.NotEqual(user.Token, user.RefreshToken);
    }

    [Fact]
    public async Task ARefreshTokenBuysANewWorkingSessionAndIsRotated()
    {
        var user = await _api.RegisterAsync(Email(), "Rotator");

        var response = await Refresh(user.RefreshToken);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotEqual(user.RefreshToken, session!.RefreshToken);
        var me = await _api.ClientWithToken(session.Token).GetAsync("/me");
        me.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ARotatedTokenCannotBeUsedAgain()
    {
        var user = await _api.RegisterAsync(Email(), "Once");
        (await Refresh(user.RefreshToken)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(user.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ReplayInsideTheGraceWindowDoesNotKillTheNewSession()
    {
        // Two tabs refreshing at the same moment: the loser gets a 401 but the winner's
        // session must survive, otherwise every multi-tab user gets randomly signed out.
        var user = await _api.RegisterAsync(Email(), "TwoTabs");
        var winner = await (await Refresh(user.RefreshToken)).Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(user.RefreshToken)).StatusCode);
        (await Refresh(winner!.RefreshToken)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GarbageRefreshTokensAreRejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh("not-a-real-token")).StatusCode);
    }

    [Fact]
    public async Task LoggingOutRevokesThatRefreshToken()
    {
        var user = await _api.RegisterAsync(Email(), "Leaver");

        var logout = await _api.CreateClient().PostAsJsonAsync("/auth/logout", new RefreshRequest(user.RefreshToken));
        logout.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(user.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task LogoutAllInvalidatesAccessTokensAlreadyHandedOut()
    {
        var user = await _api.RegisterAsync(Email(), "Everywhere");
        var login = await _api.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, ApiFactory.Password));
        var otherDevice = await login.Content.ReadFromJsonAsync<AuthResponse>();

        (await user.Client.PostAsync("/auth/logout-all", null)).EnsureSuccessStatusCode();

        // A JWT is normally valid until it expires; the TokenVersion check is what recalls it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientWithToken(otherDevice!.Token).GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(otherDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(user.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ChangingThePasswordSignsOutOtherSessionsAndKeepsThisOne()
    {
        var user = await _api.RegisterAsync(Email(), "Changer");

        var wrong = await user.Client.PostAsJsonAsync("/auth/change-password",
            new ChangePasswordRequest("not-my-password", "a-brand-new-password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var changed = await user.Client.PostAsJsonAsync("/auth/change-password",
            new ChangePasswordRequest(ApiFactory.Password, "a-brand-new-password"));
        changed.EnsureSuccessStatusCode();
        var session = await changed.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/me")).StatusCode);
        (await _api.ClientWithToken(session!.Token).GetAsync("/me")).EnsureSuccessStatusCode();

        var client = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, ApiFactory.Password))).StatusCode);
        (await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, "a-brand-new-password")))
            .EnsureSuccessStatusCode();
    }
}

/// With no grace window, any replay of a rotated token counts as theft.
public class NoGraceApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jwt:RefreshReuseGraceSeconds", "0");
    }
}

public class RefreshTheftTests : IClassFixture<NoGraceApiFactory>
{
    private readonly NoGraceApiFactory _api;
    public RefreshTheftTests(NoGraceApiFactory api) => _api = api;

    [Fact]
    public async Task ReplayingARotatedTokenRevokesTheWholeChain()
    {
        // Attacker and victim both hold token #1. The victim refreshes first; when the
        // attacker's replay arrives, the token the victim now holds must die too, because
        // the server cannot tell which party is legitimate.
        var user = await _api.RegisterAsync($"u{Guid.NewGuid():N}@example.com", "Victim");
        var client = _api.CreateClient();

        var victim = await (await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(user.RefreshToken)))
            .Content.ReadFromJsonAsync<AuthResponse>();

        var replay = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(user.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var victimNext = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(victim!.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, victimNext.StatusCode);
    }
}
