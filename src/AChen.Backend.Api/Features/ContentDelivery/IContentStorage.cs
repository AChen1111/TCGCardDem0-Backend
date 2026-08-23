namespace AChen.Backend.Api.Features.ContentDelivery;

public interface IContentStorage
{
    Task<ValidatedContentPackage> StoreReleaseAsync(
        ContentRelease release,
        Stream archive,
        long? contentLength,
        string expectedArchiveSha256,
        CancellationToken cancellationToken);

    Task<ContentFileRead?> OpenReadAsync(
        Guid releaseId,
        string relativePath,
        CancellationToken cancellationToken);

    Task DeleteReleaseAsync(Guid releaseId, CancellationToken cancellationToken);

    Task DeleteStagingAsync(Guid releaseId, CancellationToken cancellationToken);

    Task<int> CleanStagingAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    Task<bool> CheckReadyAsync(CancellationToken cancellationToken);
}

public sealed record ValidatedContentPackage(
    ReleasePackageManifest Manifest,
    string ArtifactSha256,
    long ArtifactSize,
    long TotalBytes,
    IReadOnlyList<ValidatedContentFile> Files);

public sealed record ValidatedContentFile(
    string RelativePath,
    string Kind,
    long Size,
    string Sha256);

public sealed record ContentFileRead(
    Stream Stream,
    long Length,
    DateTimeOffset LastModified);
