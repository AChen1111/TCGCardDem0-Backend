using System.Text;
using System.Diagnostics;
using AChen.Backend.Api.Features.GameConfig;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class GameConfigCsvAndGitTests
{
    [Fact]
    public void Csv_round_trip_preserves_both_tables_and_escaped_values()
    {
        var serializer = new GameConfigCsvSerializer();
        var startsAt = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.FromHours(8));
        var source = new GameConfigDraftData(
            [new AvatarConfigResponse(1, "默认,头像", "Avatar_\"Default\"", 2, true)],
            [new CardPackConfigResponse(1001, "+基础卡包", "CardPack_Default", 1000, startsAt, null, 3, false)]);

        var csv = serializer.Serialize(source);
        var restored = serializer.Deserialize(csv);

        Assert.Equal("默认,头像", Assert.Single(restored.Avatars).Name);
        Assert.Equal("Avatar_\"Default\"", restored.Avatars[0].ResourceKey);
        Assert.Equal("+基础卡包", Assert.Single(restored.CardPacks).Title);
        Assert.Equal(startsAt, restored.CardPacks[0].StartsAt);
        Assert.False(restored.CardPacks[0].IsEnabled);
    }

    [Fact]
    public void Csv_rejects_unknown_headers()
    {
        var serializer = new GameConfigCsvSerializer();

        var exception = Assert.Throws<GameConfigCsvException>(() =>
            serializer.Deserialize(Encoding.UTF8.GetBytes("Id,Name\r\n1,Avatar\r\n")));

        Assert.Contains("表头", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replacing_draft_preserves_missing_published_ids_as_disabled()
    {
        using var factory = new ApiFactory();
        factory.CreateClient().Dispose();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();

        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(1, "Published", "Avatar_Published", 0, true, admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);

        await service.ReplaceDraftAsync(
            new GameConfigDraftData(
                [new AvatarConfigResponse(2, "Imported", "Avatar_Imported", 1, true)],
                []),
            admin.EditRevision,
            CancellationToken.None);

        var replaced = await service.GetAdminAsync(CancellationToken.None);
        Assert.Equal(2, replaced.Avatars.Count);
        Assert.False(replaced.Avatars.Single(value => value.Id == 1).IsEnabled);
        Assert.True(replaced.Avatars.Single(value => value.Id == 2).IsEnabled);
    }

    [Fact]
    public async Task Git_history_can_restore_a_snapshot_into_the_draft()
    {
        using var factory = new ApiFactory();
        factory.CreateClient().Dispose();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
        var git = scope.ServiceProvider.GetRequiredService<GameConfigGitService>();

        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(1, "Version A", "Avatar_A", 0, true, admin.EditRevision),
            CancellationToken.None);
        var firstStatus = await git.SaveSnapshotAsync("first", CancellationToken.None);
        Assert.False(firstStatus.HasUncommittedDraft);
        var firstCommit = Assert.Single(firstStatus.History);

        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(1, "Version B", "Avatar_A", 0, true, admin.EditRevision),
            CancellationToken.None);
        var secondStatus = await git.SaveSnapshotAsync("second", CancellationToken.None);
        Assert.Equal(2, secondStatus.History.Count);

        admin = await service.GetAdminAsync(CancellationToken.None);
        await git.ApplyCommitToDraftAsync(firstCommit.Hash, admin.EditRevision, CancellationToken.None);

        var restored = await service.GetAdminAsync(CancellationToken.None);
        Assert.Equal("Version A", Assert.Single(restored.Avatars).Name);
    }

    [Fact]
    public async Task Git_one_click_push_uploads_snapshot_to_configured_remote()
    {
        var remotePath = Path.Combine(
            Path.GetTempPath(),
            $"achen-game-config-remote-{Guid.NewGuid():N}.git");
        Directory.CreateDirectory(remotePath);
        try
        {
            RunGit(Directory.GetParent(remotePath)!.FullName, ["init", "--bare", remotePath]);
            using var factory = new ApiFactory(new Uri(remotePath + Path.DirectorySeparatorChar).AbsoluteUri);
            factory.CreateClient().Dispose();
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
            var git = scope.ServiceProvider.GetRequiredService<GameConfigGitService>();

            var admin = await service.GetAdminAsync(CancellationToken.None);
            await service.UpsertCardPackAsync(
                new CardPackDefinitionInput(
                    1001,
                    "Remote Pack",
                    "CardPack_Remote",
                    99,
                    null,
                    null,
                    0,
                    true,
                    admin.EditRevision),
                CancellationToken.None);

            var status = await git.SaveAndPushAsync("remote snapshot", CancellationToken.None);

            Assert.True(status.HasRemote);
            Assert.False(status.HasUncommittedDraft);
            var remoteHead = RunGit(
                Directory.GetParent(remotePath)!.FullName,
                ["--git-dir", remotePath, "rev-parse", "refs/heads/main"]);
            Assert.Equal(status.HeadHash, remoteHead.Trim());
        }
        finally
        {
            DeleteTemporaryDirectory(remotePath);
        }
    }

    private static string RunGit(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var target = Path.GetFullPath(path);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        Assert.StartsWith(temporaryRoot, target, StringComparison.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(target, true);
    }
}
