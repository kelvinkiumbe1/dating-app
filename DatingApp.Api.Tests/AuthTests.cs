using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public AuthTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    private static RegisterRequest Valid(string email, int age = 30) => new(
        Email: email,
        Password: "correct-horse-battery",
        DisplayName: "Tester",
        DateOfBirth: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-age).AddDays(-1)),
        Gender: "f",
        Bio: "hi",
        Interests: "coffee",
        Latitude: 51.5,
        Longitude: -0.12);

    [Fact]
    public async Task ProtectedEndpointsRejectAnonymousCallers()
    {
        var client = _api.CreateClient();
        foreach (var path in new[] { "/me", "/discover", "/matches", "/likes", "/safety/blocks" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ARegisteredUserCanLoginAndReadTheirOwnProfile()
    {
        var email = Email();
        var user = await _api.RegisterAsync(email, "Zoe");

        var login = await _api.CreateClient()
            .PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse-battery"));
        login.EnsureSuccessStatusCode();

        var profile = await user.Client.GetFromJsonAsync<MyProfileResponse>("/me");
        Assert.Equal("Zoe", profile!.DisplayName);
        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmailBothReturnTheSameUnauthorized()
    {
        var email = Email();
        await _api.RegisterAsync(email, "Yolanda");
        var client = _api.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "nope"));
        var unknownEmail = await client.PostAsJsonAsync("/auth/login", new LoginRequest(Email(), "nope"));

        // Identical responses mean the endpoint can't be used to discover who has an account.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasswordsAreNeverStoredOrReturnedInPlaintext()
    {
        var email = Email();
        var user = await _api.RegisterAsync(email, "Hashed");

        var body = await (await user.Client.GetAsync("/me")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("correct-horse-battery", body);
        Assert.DoesNotContain("PasswordHash", body);
    }

    [Fact]
    public async Task DuplicateEmailIsRejected()
    {
        var email = Email();
        await _api.RegisterAsync(email, "First");

        var second = await _api.CreateClient().PostAsJsonAsync("/auth/register", Valid(email));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UnderageSignupIsRejected()
    {
        var response = await _api.CreateClient()
            .PostAsJsonAsync("/auth/register", Valid(Email(), age: 16));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShortPasswordIsRejected()
    {
        var request = Valid(Email()) with { Password = "short" };
        var response = await _api.CreateClient().PostAsJsonAsync("/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingMyAccountRemovesItEntirely()
    {
        var email = Email();
        var user = await _api.RegisterAsync(email, "Ghost");

        var deleted = await user.Client.DeleteAsync("/me");
        deleted.EnsureSuccessStatusCode();

        // The old token is rejected outright, and the credentials no longer work.
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/me")).StatusCode);

        var login = await _api.CreateClient()
            .PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
