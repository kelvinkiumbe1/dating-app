using System.Net.Http.Headers;
using System.Net.Http.Json;
using DatingApp.Api;
using DatingApp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DatingApp.Api.Tests;

/// Boots the real API against a private throwaway SQLite database, so every test
/// exercises the actual HTTP pipeline: routing, auth, model binding, and EF.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "moderator@example.com";
    public const string Password = "correct-horse-battery";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"datingapp-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-to-be-valid-hmac-sha256");
        builder.UseEnvironment("Development");

        // Every test shares one loopback address, so production per-IP limits would
        // reject the suite. Raised here rather than disabled, so the limiter still runs.
        builder.UseSetting("RateLimiting:AuthPerMinute", "100000");
        builder.UseSetting("RateLimiting:GlobalPerMinute", "100000");

        builder.UseSetting("Admin:Emails:0", AdminEmail);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            // A file rather than one shared in-memory connection: requests run concurrently
            // (SignalR long polling alongside REST calls, each authenticating against the
            // database), and a SqliteConnection is not safe to share across threads. One
            // connection per context mirrors production. Unpooled so the file can be deleted.
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Pooling=False"));
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_dbPath + suffix);
    }

    /// Registers a user and returns a client already carrying their bearer token.
    public async Task<TestUser> RegisterAsync(
        string email,
        string displayName,
        string gender = "f",
        string seeking = "",
        string interests = "coffee,coding,music",
        int age = 30,
        double lat = 51.5074,
        double lon = -0.1278,
        int maxDistanceKm = 50,
        int minAge = 18,
        int maxAge = 99)
    {
        var client = CreateClient();
        var request = new RegisterRequest(
            Email: email,
            Password: Password,
            DisplayName: displayName,
            DateOfBirth: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-age).AddDays(-1)),
            Gender: gender,
            Bio: $"{displayName}'s bio",
            Interests: interests,
            Latitude: lat,
            Longitude: lon,
            SeekingGenders: seeking,
            MinAge: minAge,
            MaxAge: maxAge,
            MaxDistanceKm: maxDistanceKm);

        var response = await client.PostAsJsonAsync("/auth/register", request);
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("register returned no body");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return new TestUser(auth.UserId, displayName, auth.Token, client, auth.RefreshToken, email);
    }

    /// A client that sends the given bearer token, for checking what an old token can still do.
    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public record TestUser(
    Guid Id, string DisplayName, string Token, HttpClient Client, string RefreshToken, string Email);
