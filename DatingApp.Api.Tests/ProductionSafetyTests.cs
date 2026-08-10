using System.Net;
using System.Net.Http.Json;
using DatingApp.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DatingApp.Api.Data;

namespace DatingApp.Api.Tests;

/// The app is meant to fail loudly rather than start misconfigured in Production.
/// These prove the guards actually fire instead of being aspirational comments.
public class ProductionSafetyTests
{
    private class ProductionFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            configure(builder);
        }
    }

    [Fact]
    public void ProductionRefusesToStartWithoutAJwtSigningKey()
    {
        using var factory = new ProductionFactory(b =>
        {
            b.UseSetting("Storage:S3:Bucket", "b");
            b.UseSetting("Storage:S3:AccessKey", "k");
            b.UseSetting("Storage:S3:SecretKey", "s");
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:Key", error.Message);
    }

    [Fact]
    public void ProductionRefusesToStartWithoutObjectStorage()
    {
        // Local disk would silently lose every uploaded photo on the next deploy.
        using var factory = new ProductionFactory(b =>
            b.UseSetting("Jwt:Key", "a-key-long-enough-for-hmac-sha256-signing-in-tests"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Storage:S3", error.Message);
    }

    [Fact]
    public void AnUnknownDatabaseProviderIsRejectedRatherThanGuessed()
    {
        using var factory = new ProductionFactory(b =>
        {
            b.UseSetting("Jwt:Key", "a-key-long-enough-for-hmac-sha256-signing-in-tests");
            b.UseSetting("Storage:S3:Bucket", "b");
            b.UseSetting("Storage:S3:AccessKey", "k");
            b.UseSetting("Storage:S3:SecretKey", "s");
            b.UseSetting("Database:Provider", "mysql");
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Unknown Database:Provider", error.Message);
    }

    [Fact]
    public void PostgresProviderWithoutAConnectionStringIsRejected()
    {
        using var factory = new ProductionFactory(b =>
        {
            b.UseSetting("Jwt:Key", "a-key-long-enough-for-hmac-sha256-signing-in-tests");
            b.UseSetting("Storage:S3:Bucket", "b");
            b.UseSetting("Storage:S3:AccessKey", "k");
            b.UseSetting("Storage:S3:SecretKey", "s");
            b.UseSetting("Database:Provider", "postgres");
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:Default", error.Message);
    }
}

/// Rate limiting is configured with production defaults; this factory dials the auth
/// bucket down far enough to observe it rejecting requests.
public class RateLimitTests
{
    private class TightLimitFactory : WebApplicationFactory<Program>
    {
        private SqliteConnection _connection = null!;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:Key", "a-key-long-enough-for-hmac-sha256-signing-in-tests");
            builder.UseSetting("RateLimiting:AuthPerMinute", "3");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection?.Dispose();
        }
    }

    [Fact]
    public async Task RepeatedLoginAttemptsAreRateLimited()
    {
        using var factory = new TightLimitFactory();
        var client = factory.CreateClient();
        var attempt = new LoginRequest("nobody@example.com", "guessing");

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            codes.Add((await client.PostAsJsonAsync("/auth/login", attempt)).StatusCode);

        // First few are ordinary failed logins; once the bucket empties, 429.
        Assert.Contains(HttpStatusCode.Unauthorized, codes);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }
}
