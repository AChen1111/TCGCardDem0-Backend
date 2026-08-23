namespace AChen.Backend.Api.Features.ContentDelivery;

public enum ContentReleaseState
{
    AwaitingUpload,
    Ready,
    Failed
}

public sealed class ContentRelease
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Platform { get; init; }
    public required string AppVersion { get; init; }
    public required string ContentVersion { get; init; }
    public ContentReleaseState State { get; set; } = ContentReleaseState.AwaitingUpload;
    public string? Notes { get; init; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string? ArtifactSha256 { get; set; }
    public string? HotUpdatePath { get; set; }
    public string? CatalogPath { get; set; }
    public string? CatalogHashPath { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public List<ContentReleaseFile> Files { get; init; } = [];
}

public sealed class ContentReleaseFile
{
    public Guid ReleaseId { get; init; }
    public required string RelativePath { get; init; }
    public required string Kind { get; init; }
    public long Size { get; init; }
    public required string Sha256 { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public ContentRelease Release { get; init; } = null!;
}

public sealed class ActiveContentRelease
{
    public required string Channel { get; init; }
    public required string Platform { get; init; }
    public required string AppVersion { get; init; }
    public Guid ReleaseId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ContentRelease Release { get; set; } = null!;
}

public sealed class ContentPublication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Channel { get; init; }
    public required string Platform { get; init; }
    public required string AppVersion { get; init; }
    public Guid? PreviousReleaseId { get; init; }
    public Guid ReleaseId { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public ContentRelease? PreviousRelease { get; init; }
    public ContentRelease Release { get; init; } = null!;
}
