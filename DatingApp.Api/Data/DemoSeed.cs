using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Data;

/// Development-only demo data, so the web client has a deck to swipe on. Two accounts are
/// meant to be signed into — alice@demo.test and sam@demo.test, password "demo-password" —
/// and about half the other profiles have already liked them, so liking back produces
/// matches straight away.
public static class DemoSeed
{
    public const string Password = "demo-password";

    // Nairobi CBD; every profile lands within ~15km so the default 50km radius covers them.
    private const double CenterLat = -1.2864, CenterLon = 36.8172;

    private static readonly string[] Interests =
    {
        "coffee", "hiking", "music", "football", "cooking", "travel", "photography", "reading",
        "gaming", "art", "dancing", "fitness", "movies", "coding", "running", "yoga", "fashion", "podcasts",
    };

    private static readonly (string Name, string Gender)[] People =
    {
        ("Wanjiru", "f"), ("Amani", "f"), ("Njeri", "f"), ("Zawadi", "f"), ("Achieng", "f"), ("Faith", "f"),
        ("Imani", "f"), ("Mercy", "f"), ("Nia", "f"), ("Grace", "f"), ("Halima", "f"), ("Shiro", "f"),
        ("Brian", "m"), ("Kevin", "m"), ("Otieno", "m"), ("Baraka", "m"), ("Juma", "m"), ("Kamau", "m"),
        ("Tevin", "m"), ("Mwangi", "m"), ("Daniel", "m"), ("Omondi", "m"), ("Eric", "m"), ("Kip", "m"),
    };

    private static readonly string[] Bios =
    {
        "Weekend hikes, weekday coffee. Looking for someone to explore the city with.",
        "Ask me about my playlist. Fair warning: it's long.",
        "Probably cooking something ambitious right now.",
        "Here for good conversation and bad puns.",
        "Will travel for good food. Have a passport, need a co-pilot.",
        "Gym in the morning, Netflix at night, balance is key.",
        "Bookworm with a soft spot for documentaries.",
        "If you can beat me at FIFA we should talk.",
    };

    public static async Task RunAsync(AppDbContext db, PasswordHasher<User> hasher)
    {
        if (await db.Users.AnyAsync(u => u.Email == "alice@demo.test"))
        {
            Console.WriteLine("Demo data already present; nothing to do.");
            return;
        }

        var random = new Random(42);   // deterministic, so every developer sees the same deck
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        User Make(string email, string name, string gender, string seeking, string? bio = null)
        {
            var user = new User
            {
                Email = email,
                DisplayName = name,
                Gender = gender,
                SeekingGenders = seeking,
                DateOfBirth = today.AddYears(-random.Next(22, 38)).AddDays(-random.Next(1, 360)),
                Bio = bio ?? Bios[random.Next(Bios.Length)],
                Interests = string.Join(",", Interests.OrderBy(_ => random.Next()).Take(random.Next(3, 7))),
                Latitude = CenterLat + (random.NextDouble() - 0.5) * 0.2,
                Longitude = CenterLon + (random.NextDouble() - 0.5) * 0.2,
            };
            user.PasswordHash = hasher.HashPassword(user, Password);
            db.Users.Add(user);
            return user;
        }

        var alice = Make("alice@demo.test", "Alice", "f", "m", "Demo account. Swipe right on the people who liked you to get instant matches.");
        var sam = Make("sam@demo.test", "Sam", "m", "f", "Demo account. Swipe right on the people who liked you to get instant matches.");

        foreach (var (name, gender) in People)
        {
            var user = Make($"{name.ToLowerInvariant()}@demo.test", name, gender, gender == "f" ? "m" : "f");

            // Half of each side has already liked the demo account of the opposite gender.
            var target = gender == "f" ? sam : alice;
            if (random.Next(2) == 0)
                db.Swipes.Add(new Swipe { SwiperId = user.Id, TargetId = target.Id, IsLike = true });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Seeded {People.Length + 2} demo users. Sign in as alice@demo.test or sam@demo.test / {Password}");
    }
}
