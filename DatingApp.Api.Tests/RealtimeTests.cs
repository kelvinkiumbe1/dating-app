using System.Net.Http.Json;
using DatingApp.Api;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace DatingApp.Api.Tests;

/// Drives the real SignalR hub over the test server, the same way the web client does.
public class RealtimeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public RealtimeTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private async Task<HubConnection> Connect(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                // The test server has no real sockets; long polling exercises the same
                // auth and routing, including the access_token query parameter.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static TaskCompletionSource<T> Expect<T>(HubConnection connection, string method)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<T>(method, e => tcs.TrySetResult(e));
        return tcs;
    }

    private async Task<(TestUser A, TestUser B, Guid MatchId)> Matched()
    {
        var a = await _api.RegisterAsync(Email(), "Ada", gender: "f", seeking: "m");
        var b = await _api.RegisterAsync(Email(), "Ben", gender: "m", seeking: "f");
        await a.Client.PostAsJsonAsync("/swipes", new SwipeRequest(b.Id, true));
        var swipe = await (await b.Client.PostAsJsonAsync("/swipes", new SwipeRequest(a.Id, true)))
            .Content.ReadFromJsonAsync<SwipeResponse>();
        return (a, b, swipe!.MatchId!.Value);
    }

    [Fact]
    public async Task AnonymousConnectionsAreRefused()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task AMessageIsPushedToTheRecipientAsItIsSent()
    {
        var (a, b, matchId) = await Matched();
        await using var bLive = await Connect(b.Token);
        var received = Expect<MessageEvent>(bLive, "message");

        await a.Client.PostAsJsonAsync($"/matches/{matchId}/messages", new SendMessageRequest("hey there"));

        var e = await received.Task.WaitAsync(Wait);
        Assert.Equal(matchId, e.MatchId);
        Assert.Equal("hey there", e.Message.Body);
        Assert.Equal(a.Id, e.Message.SenderId);
    }

    [Fact]
    public async Task TheOtherPersonHearsAboutANewMatchImmediately()
    {
        var a = await _api.RegisterAsync(Email(), "First", gender: "f", seeking: "m");
        var b = await _api.RegisterAsync(Email(), "Second", gender: "m", seeking: "f");
        await using var aLive = await Connect(a.Token);
        var matched = Expect<MatchEvent>(aLive, "match");

        await a.Client.PostAsJsonAsync("/swipes", new SwipeRequest(b.Id, true));
        await b.Client.PostAsJsonAsync("/swipes", new SwipeRequest(a.Id, true));

        var e = await matched.Task.WaitAsync(Wait);
        Assert.Equal(b.Id, e.UserId);
        Assert.Equal("Second", e.DisplayName);
    }

    [Fact]
    public async Task ReadReceiptsReachTheSender()
    {
        var (a, b, matchId) = await Matched();
        await a.Client.PostAsJsonAsync($"/matches/{matchId}/messages", new SendMessageRequest("read me"));

        await using var aLive = await Connect(a.Token);
        var read = Expect<ReadEvent>(aLive, "read");

        await b.Client.GetAsync($"/matches/{matchId}/messages");

        var e = await read.Task.WaitAsync(Wait);
        Assert.Equal(b.Id, e.ReaderId);
    }

    [Fact]
    public async Task TypingIsRelayedToTheMatchOnly()
    {
        var (a, b, matchId) = await Matched();
        var outsider = await _api.RegisterAsync(Email(), "Outsider");

        await using var aLive = await Connect(a.Token);
        await using var bLive = await Connect(b.Token);
        await using var outsiderLive = await Connect(outsider.Token);

        var typing = Expect<TypingEvent>(bLive, "typing");
        var leaked = Expect<TypingEvent>(outsiderLive, "typing");

        // The outsider claiming to type in someone else's match goes nowhere.
        await outsiderLive.InvokeAsync("Typing", matchId);
        await aLive.InvokeAsync("Typing", matchId);

        var e = await typing.Task.WaitAsync(Wait);
        Assert.Equal(a.Id, e.UserId);
        Assert.False(leaked.Task.IsCompleted);
    }

    [Fact]
    public async Task UnmatchingTellsBothSides()
    {
        var (a, b, matchId) = await Matched();
        await using var bLive = await Connect(b.Token);
        var unmatched = Expect<UnmatchedEvent>(bLive, "unmatched");

        await a.Client.DeleteAsync($"/matches/{matchId}");

        Assert.Equal(matchId, (await unmatched.Task.WaitAsync(Wait)).MatchId);
    }
}
