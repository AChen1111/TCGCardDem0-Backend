namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed record CreateContentReleaseRequest(
    string Platform,
    string AppVersion,
    string ContentVersion,
    string? Notes);

public sealed record SetActiveContentReleaseRequest(
    Guid ReleaseId,
    Guid? ExpectedCurrentReleaseId);

public sealed record ContentReleaseFileResponse(
    string RelativePath,
    string Kind,
    long Size,
    string Sha256);

public sealed record ContentReleaseResponse(
    Guid Id,
    string Platform,
    string AppVersion,
    string ContentVersion,
    string State,
    string? Notes,
    int FileCount,
    long TotalBytes,
    string? ArtifactSha256,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReadyAt,
    IReadOnlyList<ContentReleaseFileResponse> Files);

public sealed record ContentReleasePageResponse(
    IReadOnlyList<ContentReleaseResponse> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages);

public sealed record ActiveContentReleaseResponse(
    string Channel,
    string Platform,
    string AppVersion,
    Guid ReleaseId,
    string ContentVersion,
    DateTimeOffset UpdatedAt);

public sealed record ContentPublicationResponse(
    Guid Id,
    string Channel,
    string Platform,
    string AppVersion,
    Guid? PreviousReleaseId,
    Guid ReleaseId,
    string ContentVersion,
    string Source,
    DateTimeOffset CreatedAt);

public sealed record ContentPublicationPageResponse(
    IReadOnlyList<ContentPublicationResponse> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages);

public sealed record LatestContentManifestResponse(
    int SchemaVersion,
    Guid ReleaseId,
    string Channel,
    string Platform,
    string AppVersion,
    string ContentVersion,
    DateTimeOffset PublishedAt,
    HotUpdateArtifactResponse HotUpdate,
    AddressablesArtifactResponse Addressables);

public sealed record HotUpdateArtifactResponse(string Path, long Size, string Sha256);

public sealed record AddressablesArtifactResponse(
    string BasePath,
    string CatalogPath,
    string CatalogHashPath);

public sealed record ReleasePackageManifest(
    int SchemaVersion,
    string Platform,
    string AppVersion,
    string ContentVersion,
    string HotUpdatePath,
    string CatalogPath,
    string CatalogHashPath,
    IReadOnlyList<ReleasePackageFile> Files);

public sealed record ReleasePackageFile(string Path, long Size, string Sha256);
