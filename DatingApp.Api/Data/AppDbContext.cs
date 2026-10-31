using Microsoft.EntityFrameworkCore;

namespace DatingApp.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Swipe> Swipes => Set<Swipe>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Ignore(u => u.Age);           // computed, not stored
            e.HasMany(u => u.Photos).WithOne(p => p.User!)
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // One swipe per (swiper, target): re-swiping the same person is not allowed.
        b.Entity<Swipe>().HasIndex(s => new { s.SwiperId, s.TargetId }).IsUnique();

        // Ordered pair means a match can exist only once per couple.
        b.Entity<Match>(e =>
        {
            e.HasIndex(m => new { m.UserAId, m.UserBId }).IsUnique();
            e.HasMany(m => m.Messages).WithOne(msg => msg.Match!)
                .HasForeignKey(msg => msg.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Message>().HasIndex(m => new { m.MatchId, m.SentAt });

        b.Entity<Block>().HasIndex(x => new { x.BlockerId, x.BlockedId }).IsUnique();

        b.Entity<Report>().HasIndex(r => new { r.Reviewed, r.CreatedAt });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.FamilyId);
            e.HasOne(t => t.User).WithMany()
                .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
