using System.Net.Http.Headers;
using System.Net.Http.Json;
using DatingApp.Api;
using DatingApp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DatingApp.Api.Tests;

/// Boots the real API against a private in-memory SQLite database, so every test
/// exercises the actual HTTP pipeline: routing, auth, model binding, and EF.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-to-be-valid-hmac-sha256");
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            // A single open connection keeps the in-memory database alive for the
            // lifetime of the factory rather than dying with the first context.
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _connection?.Dispose();
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
            Password: "correct-horse-battery",
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
        return new TestUser(auth.UserId, displayName, auth.Token, client);
    }
}

public record TestUser(Guid Id, string DisplayName, string Token, HttpClient Client);
