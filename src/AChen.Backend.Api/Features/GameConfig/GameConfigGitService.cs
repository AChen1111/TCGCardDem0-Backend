using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AChen.Backend.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.GameConfig;

public sealed partial class GameConfigGitService
{
    private const string CsvFileName = "game-config.csv";
    private const string ManifestFileName = "manifest.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly GameConfigGitOptions options;
    private readonly GameConfigService gameConfigService;
    private readonly GameConfigCsvSerializer csvSerializer;
    private readonly ILogger<GameConfigGitService> logger;
    private readonly string repositoryRoot;

    public GameConfigGitService(
        IOptions<GameConfigGitOptions> options,
        IHostEnvironment environment,
        GameConfigService gameConfigService,
        GameConfigCsvSerializer csvSerializer,
        ILogger<GameConfigGitService> logger)
    {
        this.options = options.Value;
        this.gameConfigService = gameConfigService;
        this.csvSerializer = csvSerializer;
        this.logger = logger;
        repositoryRoot = Path.GetFullPath(this.options.RepositoryRoot, environment.ContentRootPath);
        ValidateOptions();
    }

    public async Task<GameConfigGitStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return await GetStatusCoreAsync(cancellationToken);
        }
        catch (GameConfigGitException exception)
        {
            return GameConfigGitStatus.Unavailable(options.Branch, exception.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<GameConfigGitStatus> SaveSnapshotAsync(
        string? note,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await SaveSnapshotCoreAsync(note, cancellationToken);
            return await GetStatusCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<GameConfigGitStatus> SaveAndPushAsync(
        string? note,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            RequireRemote();
            await SaveSnapshotCoreAsync(note, cancellationToken);
            await RunGitAsync(
                "push",
                ["push", "--no-verify", "--set-upstream", options.RemoteName, $"HEAD:refs/heads/{options.Branch}"],
                cancellationToken);
            logger.LogInformation("Pushed game configuration snapshots to branch {Branch}.", options.Branch);
            return await GetStatusCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<GameConfigGitStatus> PullAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            RequireRemote();
            var worktree = await RunGitAsync(
                "status",
                ["status", "--porcelain", "--untracked-files=all"],
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(worktree.Output))
            {
                throw new GameConfigGitException(
                    "GIT_WORKTREE_NOT_CLEAN",
                    "配置仓库有尚未提交的文件，无法拉取。请先保存快照。");
            }

            await RunGitAsync(
                "fetch",
                ["fetch", options.RemoteName, options.Branch],
                cancellationToken);
            var head = await RunGitAsync("head", ["rev-parse", "--verify", "HEAD"], cancellationToken, true);
            if (head.ExitCode == 0)
            {
                await RunGitAsync("merge", ["merge", "--ff-only", "FETCH_HEAD"], cancellationToken);
            }
            else
            {
                await RunGitAsync("checkout", ["checkout", "-B", options.Branch, "FETCH_HEAD"], cancellationToken);
            }

            logger.LogInformation("Pulled game configuration snapshots from branch {Branch}.", options.Branch);
            return await GetStatusCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ApplyCommitToDraftAsync(
        string commitHash,
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        if (!CommitHashRegex().IsMatch(commitHash ?? ""))
        {
            throw new GameConfigGitException("GIT_COMMIT_INVALID", "Git 提交标识无效。");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var snapshot = await RunGitAsync(
                "read snapshot",
                ["show", $"{commitHash}:{CsvFileName}"],
                cancellationToken);
            GameConfigDraftData data;
            try
            {
                data = csvSerializer.Deserialize(Encoding.UTF8.GetBytes(snapshot.Output));
            }
            catch (GameConfigCsvException exception)
            {
                throw new GameConfigGitException(
                    "GIT_SNAPSHOT_INVALID",
                    $"该 Git 快照无法载入：{exception.Message}");
            }

            await gameConfigService.ReplaceDraftAsync(data, expectedEditRevision, cancellationToken);
            logger.LogInformation("Loaded game configuration Git commit {CommitHash} into the draft.", commitHash);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task SaveSnapshotCoreAsync(string? note, CancellationToken cancellationToken)
    {
        var admin = await gameConfigService.GetAdminAsync(cancellationToken);
        var data = await gameConfigService.GetDraftDataAsync(cancellationToken);
        var csv = csvSerializer.Serialize(data);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            draftRevision = admin.DraftRevision,
            editRevision = admin.EditRevision,
            exportedAt = DateTimeOffset.UtcNow,
            avatarCount = data.Avatars.Count,
            cardPackCount = data.CardPacks.Count
        }, new JsonSerializerOptions { WriteIndented = true });
        await WriteAtomicallyAsync(Path.Combine(repositoryRoot, CsvFileName), csv, cancellationToken);
        await WriteAtomicallyAsync(Path.Combine(repositoryRoot, ManifestFileName), manifest, cancellationToken);
        await RunGitAsync("stage", ["add", "--", CsvFileName, ManifestFileName], cancellationToken);

        var staged = await RunGitAsync("diff", ["diff", "--cached", "--quiet"], cancellationToken, true);
        if (staged.ExitCode == 1)
        {
            var safeNote = SanitizeNote(note);
            var message = $"config: snapshot draft r{admin.DraftRevision}.{admin.EditRevision}";
            if (!string.IsNullOrWhiteSpace(safeNote))
            {
                message += $" - {safeNote}";
            }

            await RunGitAsync(
                "commit",
                ["commit", "--no-gpg-sign", "--no-verify", "-m", message],
                cancellationToken);
            logger.LogInformation(
                "Saved game configuration draft {DraftRevision}.{EditRevision} as a Git snapshot.",
                admin.DraftRevision,
                admin.EditRevision);
        }
        else if (staged.ExitCode != 0)
        {
            throw Failed("diff");
        }
    }

    private async Task<GameConfigGitStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        var head = await RunGitAsync("head", ["rev-parse", "--verify", "HEAD"], cancellationToken, true);
        var history = new List<GameConfigGitCommit>();
        if (head.ExitCode == 0)
        {
            var log = await RunGitAsync(
                "history",
                [
                    "log",
                    $"--max-count={Math.Clamp(options.HistoryLimit, 1, 100)}",
                    "--format=%H%x1f%h%x1f%cI%x1f%s"
                ],
                cancellationToken);
            foreach (var line in log.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.TrimEnd('\r').Split('\u001f');
                if (fields.Length == 4 && DateTimeOffset.TryParse(
                        fields[2],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var createdAt))
                {
                    history.Add(new GameConfigGitCommit(fields[0], fields[1], createdAt, fields[3]));
                }
            }
        }

        var currentData = csvSerializer.Serialize(await gameConfigService.GetDraftDataAsync(cancellationToken));
        var committed = head.ExitCode == 0
            ? await RunGitAsync("read snapshot", ["show", $"HEAD:{CsvFileName}"], cancellationToken, true)
            : new GitResult(1, "");
        var currentText = NormalizeCsv(Encoding.UTF8.GetString(currentData));
        var committedText = NormalizeCsv(committed.Output);
        var hasUncommittedDraft = committed.ExitCode != 0 ||
            !string.Equals(currentText, committedText, StringComparison.Ordinal);

        return new GameConfigGitStatus(
            true,
            !string.IsNullOrWhiteSpace(options.RemoteUrl),
            hasUncommittedDraft,
            options.Branch,
            head.ExitCode == 0 ? head.Output.Trim() : null,
            GetRemoteDisplay(),
            null,
            history);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(repositoryRoot);
        var gitDirectory = Path.Combine(repositoryRoot, ".git");
        var ownershipMarker = Path.Combine(gitDirectory, "achen-game-config");
        if (Directory.Exists(gitDirectory) && !File.Exists(ownershipMarker))
        {
            throw new GameConfigGitException(
                "GIT_REPOSITORY_NOT_MANAGED",
                "目标目录是一个现有 Git 仓库，但不是由配置后台创建的专用仓库。",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (!Directory.Exists(gitDirectory))
        {
            await RunGitAsync("initialize", ["init"], cancellationToken);
            await File.WriteAllTextAsync(ownershipMarker, "AChen GameConfig repository\n", cancellationToken);
            await RunGitAsync(
                "set branch",
                ["symbolic-ref", "HEAD", $"refs/heads/{options.Branch}"],
                cancellationToken);
            await RunGitAsync("set identity", ["config", "user.name", "AChen Config Server"], cancellationToken);
            await RunGitAsync("set identity", ["config", "user.email", "config@achen.local"], cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(options.RemoteUrl))
        {
            var existing = await RunGitAsync(
                "read remote",
                ["remote", "get-url", options.RemoteName],
                cancellationToken,
                true);
            await RunGitAsync(
                "configure remote",
                existing.ExitCode == 0
                    ? ["remote", "set-url", options.RemoteName, options.RemoteUrl]
                    : ["remote", "add", options.RemoteName, options.RemoteUrl],
                cancellationToken);
        }
    }

    private async Task<GitResult> RunGitAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo) ?? throw new Win32Exception("Could not start git.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0 && !allowFailure)
            {
                logger.LogWarning("Game configuration Git operation {Operation} failed with exit code {ExitCode}.", operation, process.ExitCode);
                throw Failed(operation);
            }

            return new GitResult(process.ExitCode, output);
        }
        catch (Win32Exception)
        {
            throw new GameConfigGitException(
                "GIT_NOT_AVAILABLE",
                "服务器未安装 Git，配置版本界面暂不可用。",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void ValidateOptions()
    {
        if (!BranchRegex().IsMatch(options.Branch) || options.Branch.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GameConfigGit:Branch is invalid.");
        }

        if (!RemoteNameRegex().IsMatch(options.RemoteName))
        {
            throw new InvalidOperationException("GameConfigGit:RemoteName is invalid.");
        }

        if (Uri.TryCreate(options.RemoteUrl, UriKind.Absolute, out var remote) &&
            remote.Scheme is "http" or "https" && !string.IsNullOrEmpty(remote.UserInfo))
        {
            throw new InvalidOperationException(
                "GameConfigGit:RemoteUrl must not contain credentials. Configure a server credential helper instead.");
        }
    }

    private void RequireRemote()
    {
        if (string.IsNullOrWhiteSpace(options.RemoteUrl))
        {
            throw new GameConfigGitException(
                "GIT_REMOTE_NOT_CONFIGURED",
                "尚未配置 Git 远端。请在服务器设置 GameConfigGit:RemoteUrl。");
        }
    }

    private string? GetRemoteDisplay()
    {
        if (string.IsNullOrWhiteSpace(options.RemoteUrl))
        {
            return null;
        }

        if (Uri.TryCreate(options.RemoteUrl, UriKind.Absolute, out var remote))
        {
            if (remote.IsFile)
            {
                return "本机文件仓库";
            }

            return remote.IsDefaultPort ? remote.Host : $"{remote.Host}:{remote.Port}";
        }

        var separator = options.RemoteUrl.IndexOf(':');
        var at = options.RemoteUrl.IndexOf('@');
        return separator > 0
            ? options.RemoteUrl[(at >= 0 && at < separator ? at + 1 : 0)..separator]
            : "已配置远端";
    }

    private static string SanitizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return "";
        }

        var cleaned = new string(note
            .Where(value => !char.IsControl(value))
            .Take(120)
            .ToArray());
        return cleaned.Trim();
    }

    private static string NormalizeCsv(string value) =>
        value.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);

    private static GameConfigGitException Failed(string operation) => new(
        "GIT_COMMAND_FAILED",
        $"Git {operation} 操作失败，请检查服务器日志与远端权限。",
        StatusCodes.Status503ServiceUnavailable);

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitHashRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex BranchRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RemoteNameRegex();

    private sealed record GitResult(int ExitCode, string Output);
}

public sealed class GameConfigGitException(
    string code,
    string message,
    int statusCode = StatusCodes.Status422UnprocessableEntity)
    : ApiException(statusCode, code, message);
