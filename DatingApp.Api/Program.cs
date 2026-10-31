using System.Threading.RateLimiting;
using DatingApp.Api.Auth;
using DatingApp.Api.Data;
using DatingApp.Api.Endpoints;
using DatingApp.Api.Realtime;
using DatingApp.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────
// Postgres in production; SQLite locally and in tests. The provider is explicit
// rather than sniffed from the connection string so a typo fails loudly.
var provider = (builder.Configuration["Database:Provider"] ?? "sqlite").ToLowerInvariant();
var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    switch (provider)
    {
        case "postgres" or "postgresql":
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "ConnectionStrings:Default is required when Database:Provider is postgres.");
            options.UseNpgsql(NormalizePostgresUrl(connectionString));
            break;

        case "sqlite":
            options.UseSqlite(connectionString
                ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "datingapp.db")}");
            break;

        default:
            throw new InvalidOperationException(
                $"Unknown Database:Provider '{provider}'. Use 'postgres' or 'sqlite'.");
    }
});

builder.Services.AddSingleton<PasswordHasher<User>>();

// ── Photo storage ───────────────────────────────────────────────────────────
// Container filesystems are ephemeral, so anything deployed needs object storage.
var s3 = new S3StorageOptions();
builder.Configuration.GetSection("Storage:S3").Bind(s3);

if (s3.IsConfigured)
{
    builder.Services.AddSingleton<IPhotoStorage>(new S3PhotoStorage(s3));
}
else
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Storage:S3 is not configured. Uploaded photos would be lost on restart. "
            + "Set Storage:S3 Bucket/AccessKey/SecretKey (and ServiceUrl for Cloudflare R2).");

    builder.Services.AddSingleton<IPhotoStorage>(
        new LocalPhotoStorage(Path.Combine(builder.Environment.ContentRootPath, "uploads")));
}

// ── Auth ────────────────────────────────────────────────────────────────────
var jwt = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwt);
if (string.IsNullOrWhiteSpace(jwt.Key))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set it via configuration or the JWT__KEY environment variable.");

    // Development-only fallback; per-process, so restarting invalidates old tokens.
    jwt.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}

var tokenService = new TokenService(jwt);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(tokenService);

var admins = new AdminOptions();
builder.Configuration.GetSection("Admin").Bind(admins);
builder.Services.AddSingleton(admins);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenService.SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",   // keep "sub" rather than remapping to a legacy claim type
        };
        options.MapInboundClaims = false;

        options.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on a WebSocket upgrade, so the SignalR client sends
            // the token as a query parameter. Accepted only on the hub path.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },

            // A valid signature is not enough: the account must still exist, not be banned,
            // and not have revoked its tokens since this one was issued. This one primary-key
            // lookup is what makes "sign out everywhere" and bans take effect immediately.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userId = principal.UserId();
                var account = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.TokenVersion, u.IsBanned, u.IsAdmin })
                    .FirstOrDefaultAsync();

                if (account is null || account.IsBanned
                    || principal.FindFirst(CustomClaims.TokenVersion)?.Value != account.TokenVersion.ToString())
                {
                    context.Fail("Token has been revoked.");
                    return;
                }

                if (account.IsAdmin)
                    ((System.Security.Claims.ClaimsIdentity)principal.Identity!).AddClaim(
                        new System.Security.Claims.Claim(CustomClaims.Role, CustomClaims.AdminRole));
            },
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminEndpoints.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(CustomClaims.Role, CustomClaims.AdminRole));

// ── Realtime ────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSingleton<RealtimeNotifier>();

// ── CORS ────────────────────────────────────────────────────────────────────
// Allowed origins are explicit: a credentialed API must never use AllowAnyOrigin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        // AllowCredentials is what lets the SignalR client negotiate cross-origin.
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// ── Rate limiting ───────────────────────────────────────────────────────────
// Limits are configurable so the integration tests can raise them; the defaults
// are the production values.
var authLimit = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 10);
var globalLimit = builder.Configuration.GetValue("RateLimiting:GlobalPerMinute", 300);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login and registration are the brute-force surface, so they get a tighter,
    // per-IP bucket than the rest of the API.
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));

    // Everything else: per-user when authenticated, per-IP otherwise.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.User.Identity?.IsAuthenticated == true
                ? $"user:{http.User.UserId()}"
                : $"ip:{http.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = globalLimit, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// ── Schema ──────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
        db.Database.Migrate();          // versioned migrations in production
    else
        db.Database.EnsureCreated();    // SQLite dev/test databases are disposable

    // `dotnet run --project DatingApp.Api -- --seed` fills a development database with
    // demo accounts. Refused outside Development so it can never touch real data.
    if (args.Contains("--seed"))
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("--seed only runs in Development.");
        await DemoSeed.RunAsync(db, scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>());
    }

    await admins.SyncAsync(db);
}

// Fly.io, Render, and Railway all terminate TLS at their proxy, so the scheme and
// client IP have to come from forwarded headers for HTTPS redirect and rate
// limiting to see the truth.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { },
    KnownProxies = { },
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers.XFrameOptions = "DENY";
    // The bundled web client uses no inline scripts and talks only to this origin.
    headers.ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; "
        + "connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

// The web client is built into wwwroot/app (see web/). Served under /app so its routes can
// never collide with API paths like /matches or /likes on a page refresh.
app.UseStaticFiles();
// Explicit, so routing (and the SPA fallback) runs after static files. Left implicit,
// minimal hosting routes first and the fallback would answer asset requests with index.html.
app.UseRouting();

app.UseCors();
// Authentication runs before the limiter so the global bucket can partition by user
// rather than lumping every signed-in user behind one NAT into a single IP bucket.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health").WithTags("Meta");
app.MapGet("/", () => Results.Redirect("/app/")).ExcludeFromDescription();
app.MapFallbackToFile("/app/{**path}", "app/index.html");

app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapDiscoveryEndpoints();
app.MapMatchEndpoints();
app.MapSafetyEndpoints();
app.MapPhotoEndpoints();
app.MapAdminEndpoints();

// Closing on expiry forces the client to reconnect with a fresh access token, so a revoked
// session loses its live connection within one token lifetime.
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);

app.Run();

/// Hosts hand out Postgres URLs (postgres://user:pass@host/db); Npgsql wants
/// key/value pairs. Accept either so a pasted connection string just works.
static string NormalizePostgresUrl(string value)
{
    if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return value;

    var uri = new Uri(value);
    var parts = uri.UserInfo.Split(':', 2);
    var database = uri.AbsolutePath.Trim('/');

    var isLocal = uri.Host is "localhost" or "127.0.0.1";

    return new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Username = Uri.UnescapeDataString(parts[0]),
        Password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "",
        Database = string.IsNullOrEmpty(database) ? "postgres" : database,
        // Managed providers (Neon, Supabase, Fly) all require TLS; local Postgres usually has none.
        SslMode = isLocal ? Npgsql.SslMode.Disable : Npgsql.SslMode.Require,
    }.ConnectionString;
}

// Marker so the integration test host can locate this assembly's entry point.
public partial class Program;
