using DatingApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Auth;

/// Moderators are named in configuration (Admin:Emails:0, Admin:Emails:1, …) rather than
/// granted through the API, so there is no endpoint that can escalate an account to admin.
public class AdminOptions
{
    public string[] Emails { get; set; } = [];

    public bool IsAdminEmail(string email) =>
        Emails.Any(e => string.Equals(e.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    /// Brings the IsAdmin flags in line with configuration, so removing an email from the
    /// list demotes that account on the next deploy.
    public async Task SyncAsync(AppDbContext db)
    {
        var listed = Emails.Select(e => e.Trim().ToLowerInvariant()).ToList();
        var changed = await db.Users
            .Where(u => u.IsAdmin != listed.Contains(u.Email))
            .ToListAsync();

        foreach (var user in changed) user.IsAdmin = !user.IsAdmin;
        if (changed.Count > 0) await db.SaveChangesAsync();
    }
}
