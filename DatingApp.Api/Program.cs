using DatingApp.Api.Auth;
using DatingApp.Api.Data;
using DatingApp.Api.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "datingapp.db")}";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddSingleton<PasswordHasher<User>>();

// In production the signing key must come from configuration or the environment.
// The development fallback is per-process, so restarting invalidates old tokens.
var jwt = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwt);
if (string.IsNullOrWhiteSpace(jwt.Key))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set it via configuration or the JWT__KEY environment variable.");
    jwt.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}

var tokenService = new TokenService(jwt);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(tokenService);

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
            // Keep "sub" as-is instead of remapping it to a legacy claim type.
            NameClaimType = "sub",
        };
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Meta");

app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapDiscoveryEndpoints();
app.MapMatchEndpoints();
app.MapSafetyEndpoints();
app.MapPhotoEndpoints(Path.Combine(app.Environment.ContentRootPath, "uploads"));

app.Run();

// Marker so the integration test host can locate this assembly's entry point.
public partial class Program;
