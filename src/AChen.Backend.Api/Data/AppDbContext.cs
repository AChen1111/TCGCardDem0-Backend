using System.Text.Json;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.GameConfig;
using AChen.Backend.Api.Features.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AChen.Backend.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<GameConfigVersion> GameConfigVersions => Set<GameConfigVersion>();
    public DbSet<AvatarDefinition> AvatarDefinitions => Set<AvatarDefinition>();
    public DbSet<CardPackDefinition> CardPackDefinitions => Set<CardPackDefinition>();
    public DbSet<ContentRelease> ContentReleases => Set<ContentRelease>();
    public DbSet<ContentReleaseFile> ContentReleaseFiles => Set<ContentReleaseFile>();
    public DbSet<ActiveContentRelease> ActiveContentReleases => Set<ActiveContentRelease>();
    public DbSet<ContentPublication> ContentPublications => Set<ContentPublication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(value => value.Id);
            user.Property(value => value.Username).HasMaxLength(24).IsRequired();
            user.Property(value => value.NormalizedUsername).HasMaxLength(24).IsRequired();
            user.Property(value => value.PasswordHash).IsRequired();
            user.HasIndex(value => value.NormalizedUsername).IsUnique();
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

        modelBuilder.Entity<PlayerProfile>(profile =>
        {
            profile.HasKey(value => value.UserId);
            profile.Property(value => value.Nickname).HasMaxLength(24).IsRequired();
            profile.Property(value => value.OwnedAvatarIds)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                    value => JsonSerializer.Deserialize<List<int>>(value, JsonSerializerOptions.Default) ?? new List<int>(),
                    new ValueComparer<List<int>>(
                        (left, right) => left != null && right != null && left.SequenceEqual(right),
                        value => value.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                        value => value.ToList()))
                .HasColumnType("TEXT");
            profile.Property(value => value.Revision).IsConcurrencyToken();
            profile.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            profile.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            profile.ToTable(table => table.HasCheckConstraint(
                "CK_PlayerProfiles_Gold_NonNegative",
                "Gold >= 0"));
            profile.HasOne(value => value.User)
                .WithOne(value => value.PlayerProfile)
                .HasForeignKey<PlayerProfile>(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameConfigVersion>(version =>
        {
            version.HasKey(value => value.Revision);
            version.Property(value => value.Revision).ValueGeneratedNever();
            version.Property(value => value.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            version.Property(value => value.EditRevision).IsConcurrencyToken();
            version.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            version.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            version.Property(value => value.PublishedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            version.HasIndex(value => value.State)
                .IsUnique()
                .HasFilter("\"State\" = 'Draft'");
            version.ToTable(table => table.HasCheckConstraint(
                "CK_GameConfigVersions_EditRevision_NonNegative",
                "EditRevision >= 0"));
        });

        modelBuilder.Entity<AvatarDefinition>(avatar =>
        {
            avatar.HasKey(value => new { value.Revision, value.Id });
            avatar.Property(value => value.Name).HasMaxLength(64).IsRequired();
            avatar.Property(value => value.ResourceKey).HasMaxLength(128).IsRequired();
            avatar.HasIndex(value => new { value.Revision, value.ResourceKey }).IsUnique();
            avatar.HasIndex(value => new { value.Revision, value.SortOrder, value.Id });
            avatar.ToTable(table => table.HasCheckConstraint(
                "CK_AvatarDefinitions_Id_Positive",
                "Id > 0"));
            avatar.HasOne(value => value.Version)
                .WithMany(value => value.Avatars)
                .HasForeignKey(value => value.Revision)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardPackDefinition>(cardPack =>
        {
            cardPack.HasKey(value => new { value.Revision, value.Id });
            cardPack.Property(value => value.Title).HasMaxLength(64).IsRequired();
            cardPack.Property(value => value.CoverResourceKey).HasMaxLength(128).IsRequired();
            cardPack.Property(value => value.StartsAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            cardPack.Property(value => value.EndsAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            cardPack.HasIndex(value => new { value.Revision, value.SortOrder, value.Id });
            cardPack.ToTable(table =>
            {
                table.HasCheckConstraint("CK_CardPackDefinitions_Id_Positive", "Id > 0");
                table.HasCheckConstraint("CK_CardPackDefinitions_PriceGold_NonNegative", "PriceGold >= 0");
            });
            cardPack.HasOne(value => value.Version)
                .WithMany(value => value.CardPacks)
                .HasForeignKey(value => value.Revision)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContentRelease>(release =>
        {
            release.HasKey(value => value.Id);
            release.Property(value => value.Platform).HasMaxLength(32).IsRequired();
            release.Property(value => value.AppVersion).HasMaxLength(64).IsRequired();
            release.Property(value => value.ContentVersion).HasMaxLength(64).IsRequired();
            release.Property(value => value.State).HasConversion<string>().HasMaxLength(24).IsRequired();
            release.Property(value => value.Notes).HasMaxLength(2000);
            release.Property(value => value.ArtifactSha256).HasMaxLength(64);
            release.Property(value => value.HotUpdatePath).HasMaxLength(512);
            release.Property(value => value.CatalogPath).HasMaxLength(512);
            release.Property(value => value.CatalogHashPath).HasMaxLength(512);
            release.Property(value => value.FailureCode).HasMaxLength(64);
            release.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            release.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            release.Property(value => value.ReadyAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            release.HasIndex(value => new { value.Platform, value.AppVersion, value.ContentVersion }).IsUnique();
            release.HasIndex(value => value.CreatedAt);
        });

        modelBuilder.Entity<ContentReleaseFile>(file =>
        {
            file.HasKey(value => new { value.ReleaseId, value.RelativePath });
            file.Property(value => value.RelativePath).HasMaxLength(512).IsRequired();
            file.Property(value => value.Kind).HasMaxLength(32).IsRequired();
            file.Property(value => value.Sha256).HasMaxLength(64).IsRequired();
            file.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            file.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            file.HasOne(value => value.Release)
                .WithMany(value => value.Files)
                .HasForeignKey(value => value.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActiveContentRelease>(active =>
        {
            active.HasKey(value => new { value.Channel, value.Platform, value.AppVersion });
            active.Property(value => value.Channel).HasMaxLength(32).IsRequired();
            active.Property(value => value.Platform).HasMaxLength(32).IsRequired();
            active.Property(value => value.AppVersion).HasMaxLength(64).IsRequired();
            active.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            active.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            active.HasIndex(value => value.ReleaseId);
            active.HasOne(value => value.Release)
                .WithMany()
                .HasForeignKey(value => value.ReleaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContentPublication>(publication =>
        {
            publication.HasKey(value => value.Id);
            publication.Property(value => value.Channel).HasMaxLength(32).IsRequired();
            publication.Property(value => value.Platform).HasMaxLength(32).IsRequired();
            publication.Property(value => value.AppVersion).HasMaxLength(64).IsRequired();
            publication.Property(value => value.Source).HasMaxLength(32).IsRequired();
            publication.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            publication.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            publication.HasIndex(value => new
            {
                value.Channel,
                value.Platform,
                value.AppVersion,
                value.CreatedAt
            });
            publication.HasOne(value => value.Release)
                .WithMany()
                .HasForeignKey(value => value.ReleaseId)
                .OnDelete(DeleteBehavior.Restrict);
            publication.HasOne(value => value.PreviousRelease)
                .WithMany()
                .HasForeignKey(value => value.PreviousReleaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
