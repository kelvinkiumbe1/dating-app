using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class AdminTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public AdminTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    // One moderator per factory: the admin email is fixed by configuration.
    private TestUser? _admin;
    private static readonly SemaphoreSlim AdminLock = new(1, 1);

    private async Task<TestUser> Admin()
    {
        await AdminLock.WaitAsync();
        try
        {
            if (_admin is not null) return _admin;
            var client = _api.CreateClient();
            var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(ApiFactory.AdminEmail, ApiFactory.Password));
            if (login.IsSuccessStatusCode)
            {
                var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
                return _admin = new TestUser(auth!.UserId, "Mod", auth.Token,
                    _api.ClientWithToken(auth.Token), auth.RefreshToken, ApiFactory.AdminEmail);
            }
            return _admin = await _api.RegisterAsync(ApiFactory.AdminEmail, "Mod");
        }
        finally
        {
            AdminLock.Release();
        }
    }

    private static async Task Report(TestUser reporter, Guid target, string reason = "harassment") =>
        (await reporter.Client.PostAsJsonAsync("/safety/report", new ReportRequest(target, reason, "details")))
            .EnsureSuccessStatusCode();

    private static async Task<List<AdminReportResponse>> OpenReports(TestUser admin) =>
        (await admin.Client.GetFromJsonAsync<List<AdminReportResponse>>("/admin/reports"))!;

    [Fact]
    public async Task OrdinaryUsersCannotReachTheModerationQueue()
    {
        var user = await _api.RegisterAsync(Email(), "Nosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.Client.PostAsJsonAsync($"/admin/users/{user.Id}/ban", new BanRequest("x"))).StatusCode);
    }

    [Fact]
    public async Task TheConfiguredModeratorIsFlaggedAsAdmin()
    {
        var admin = await Admin();
        var me = await admin.Client.GetFromJsonAsync<MyProfileResponse>("/me");
        Assert.True(me!.IsAdmin);
    }

    [Fact]
    public async Task FiledReportsShowUpInTheQueueWithContext()
    {
        var admin = await Admin();
        var reporter = await _api.RegisterAsync(Email(), "Reporter");
        var target = await _api.RegisterAsync(Email(), "Target");

        await Report(reporter, target.Id, "spam");

        var report = (await OpenReports(admin)).Single(r => r.Reported?.Id == target.Id);
        Assert.Equal("spam", report.Reason);
        Assert.Equal("Reporter", report.ReporterName);
        Assert.Equal(1, report.Reported!.OpenReportCount);
    }

    [Fact]
    public async Task DismissingAReportClosesItWithoutTouchingTheAccount()
    {
        var admin = await Admin();
        var reporter = await _api.RegisterAsync(Email(), "Grumpy");
        var target = await _api.RegisterAsync(Email(), "Innocent");
        await Report(reporter, target.Id);

        var report = (await OpenReports(admin)).Single(r => r.Reported?.Id == target.Id);
        (await admin.Client.PostAsJsonAsync($"/admin/reports/{report.Id}/resolve",
            new ResolveReportRequest("dismiss", "No evidence"))).EnsureSuccessStatusCode();

        Assert.DoesNotContain(await OpenReports(admin), r => r.Id == report.Id);
        (await target.Client.GetAsync("/me")).EnsureSuccessStatusCode();

        var again = await admin.Client.PostAsJsonAsync($"/admin/reports/{report.Id}/resolve",
            new ResolveReportRequest("dismiss", null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task BanningFromAReportLocksTheAccountOutEverywhereAtOnce()
    {
        var admin = await Admin();
        var victim = await _api.RegisterAsync(Email(), "Victim", gender: "f", seeking: "m");
        var other = await _api.RegisterAsync(Email(), "Witness", gender: "f", seeking: "m");
        var abuser = await _api.RegisterAsync(Email(), "Abuser", gender: "m", seeking: "f");

        // An active match that the ban should end.
        await victim.Client.PostAsJsonAsync("/swipes", new SwipeRequest(abuser.Id, true));
        await abuser.Client.PostAsJsonAsync("/swipes", new SwipeRequest(victim.Id, true));

        await Report(victim, abuser.Id);
        await Report(other, abuser.Id, "scam");

        var report = (await OpenReports(admin)).First(r => r.Reported?.Id == abuser.Id);
        Assert.Equal(2, report.Reported!.OpenReportCount);

        (await admin.Client.PostAsJsonAsync($"/admin/reports/{report.Id}/resolve",
            new ResolveReportRequest("ban", "Harassment confirmed"))).EnsureSuccessStatusCode();

        // Existing access token, refresh token, and password all stop working.
        Assert.Equal(HttpStatusCode.Unauthorized, (await abuser.Client.GetAsync("/me")).StatusCode);
        var refresh = await _api.CreateClient().PostAsJsonAsync("/auth/refresh", new RefreshRequest(abuser.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var login = await _api.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(abuser.Email, ApiFactory.Password));
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        // The match is gone, the profile is hidden, and every open report against them closed.
        Assert.DoesNotContain(await victim.Client.GetFromJsonAsync<List<MatchResponse>>("/matches") ?? [],
            m => m.UserId == abuser.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/users/{abuser.Id}")).StatusCode);
        Assert.DoesNotContain(await other.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover") ?? [],
            c => c.Id == abuser.Id);
        Assert.DoesNotContain(await OpenReports(admin), r => r.Reported?.Id == abuser.Id);
    }

    [Fact]
    public async Task UnbanningRestoresSignIn()
    {
        var admin = await Admin();
        var user = await _api.RegisterAsync(Email(), "SecondChance");

        (await admin.Client.PostAsJsonAsync($"/admin/users/{user.Id}/ban", new BanRequest("test"))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsync($"/admin/users/{user.Id}/unban", null)).EnsureSuccessStatusCode();

        var login = await _api.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, ApiFactory.Password));
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminsCannotBeBannedSoModerationCannotLockItselfOut()
    {
        var admin = await Admin();
        var response = await admin.Client.PostAsJsonAsync($"/admin/users/{admin.Id}/ban", new BanRequest("oops"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownResolutionActionsAreRejected()
    {
        var admin = await Admin();
        var reporter = await _api.RegisterAsync(Email(), "R");
        var target = await _api.RegisterAsync(Email(), "T");
        await Report(reporter, target.Id);
        var report = (await OpenReports(admin)).Single(r => r.Reported?.Id == target.Id);

        var response = await admin.Client.PostAsJsonAsync($"/admin/reports/{report.Id}/resolve",
            new ResolveReportRequest("delete-everything", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
