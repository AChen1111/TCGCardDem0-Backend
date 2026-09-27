using System.Text.Json;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Decks;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Features.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AChen.Backend.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CurrentContent> CurrentContents => Set<CurrentContent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PlayerDeck> PlayerDecks => Set<PlayerDeck>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Gift> Gifts => Set<Gift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerDeck>(deck =>
        {
            deck.HasKey(x => x.Id);
            deck.Property(x => x.Name).HasMaxLength(64).IsRequired();
            deck.Property(x => x.MainDeckJson).HasColumnName("MainDeck").IsRequired();
            deck.Property(x => x.ExtraDeckJson).HasColumnName("ExtraDeck").IsRequired();
            deck.Property(x => x.Revision).IsConcurrencyToken();
            deck.Property(x => x.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            deck.Property(x => x.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            deck.HasIndex(x => x.UserId);
            deck.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

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
            profile.Property(value => value.OwnedAvatarFrameIds)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                    value => JsonSerializer.Deserialize<List<int>>(value, JsonSerializerOptions.Default)!,
                    new ValueComparer<List<int>>(
                        (left, right) => left!.SequenceEqual(right!),
                        value => value.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                        value => value.ToList()))
                .HasColumnType("TEXT");
            profile.Property(value => value.OwnedBackgroundIds)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                    value => JsonSerializer.Deserialize<List<int>>(value, JsonSerializerOptions.Default) ?? new List<int>(),
                    new ValueComparer<List<int>>(
                        (left, right) => left != null && right != null && left.SequenceEqual(right),
                        value => value.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                        value => value.ToList()))
                .HasColumnType("TEXT");
            profile.Property(value => value.OwnedCards)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                    value => JsonSerializer.Deserialize<List<OwnedCard>>(value, JsonSerializerOptions.Default) ?? new List<OwnedCard>(),
                    new ValueComparer<List<OwnedCard>>(
                        (left, right) => left != null && right != null && left.SequenceEqual(right),
                        value => value.Aggregate(0, (hash, card) => HashCode.Combine(hash, card.CardId, card.Rarity, card.Count)),
                        value => value.ToList()))
                .HasColumnType("TEXT");
            profile.Property(value => value.Ur).HasDefaultValue(0L);
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

        modelBuilder.Entity<CurrentContent>(content =>
        {
            content.HasKey(x => x.Target);
            content.Property(x => x.Target).HasMaxLength(32);
            content.Property(x => x.ContentId).IsRequired();
            content.Property(x => x.ManifestJson).IsRequired();
        });

        modelBuilder.Entity<Friendship>(friendship =>
        {
            friendship.HasKey(value => new { value.UserIdA, value.UserIdB });
            friendship.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
        });

        modelBuilder.Entity<FriendRequest>(request =>
        {
            request.HasKey(value => value.Id);
            request.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            request.Property(value => value.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            request.HasIndex(value => new { value.ToUserId, value.Status });
            request.HasIndex(value => new { value.FromUserId, value.ToUserId });
        });

        modelBuilder.Entity<Gift>(gift =>
        {
            gift.HasKey(value => value.Id);
            gift.Property(value => value.Cards)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                    value => JsonSerializer.Deserialize<List<OwnedCard>>(value, JsonSerializerOptions.Default) ?? new List<OwnedCard>(),
                    new ValueComparer<List<OwnedCard>>(
                        (left, right) => left != null && right != null && left.SequenceEqual(right),
                        value => value.Aggregate(0, (hash, card) => HashCode.Combine(hash, card.CardId, card.Rarity, card.Count)),
                        value => value.ToList()))
                .HasColumnType("TEXT");
            gift.Property(value => value.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            gift.Property(value => value.ClaimedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            gift.HasIndex(value => new { value.TargetUserId, value.Claimed });
        });
    }
}
