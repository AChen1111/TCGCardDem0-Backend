using AChen.Backend.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(value => value.Id);
            user.Property(value => value.Username).HasMaxLength(24).IsRequired();
            user.Property(value => value.NormalizedUsername).HasMaxLength(24).IsRequired();
            user.Property(value => value.Email).HasMaxLength(254).IsRequired();
            user.Property(value => value.NormalizedEmail).HasMaxLength(254).IsRequired();
            user.Property(value => value.PasswordHash).IsRequired();
            user.HasIndex(value => value.NormalizedUsername).IsUnique();
            user.HasIndex(value => value.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<RefreshSession>(session =>
        {
            session.HasKey(value => value.Id);
            session.Property(value => value.TokenHash).HasMaxLength(64).IsRequired();
            session.Property(value => value.ReplacedByTokenHash).HasMaxLength(64);
            session.HasIndex(value => value.TokenHash).IsUnique();
            session.HasOne(value => value.User)
                .WithMany(value => value.RefreshSessions)
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
