namespace AChen.Backend.Api.Features.GameConfig;

public sealed class GameConfigGitOptions
{
    public const string SectionName = "GameConfigGit";

    public string RepositoryRoot { get; set; } = "Data/game-config-git";
    public string Branch { get; set; } = "main";
    public string RemoteName { get; set; } = "origin";
    public string? RemoteUrl { get; set; }
    public int HistoryLimit { get; set; } = 30;
}

public sealed record GameConfigGitCommit(
    string Hash,
    string ShortHash,
    DateTimeOffset CreatedAt,
    string Subject);

public sealed record GameConfigGitStatus(
    bool IsAvailable,
    bool HasRemote,
    bool HasUncommittedDraft,
    string Branch,
    string? HeadHash,
    string? RemoteDisplay,
    string? Error,
    IReadOnlyList<GameConfigGitCommit> History)
{
    public static GameConfigGitStatus Unavailable(string branch, string error) =>
        new(false, false, false, branch, null, null, error, []);
}
