using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class ContentReleaseService(
    IContentReleaseRepository repository,
    IContentStorage storage,
    ContentReleaseLockProvider lockProvider,
    IOptions<ContentDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<ContentReleaseService> logger)
{
    private readonly ContentDeliveryOptions options = options.Value;

    public async Task<ContentReleaseResponse> CreateAsync(
        CreateContentReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            Platform = request.Platform?.Trim() ?? "",
            AppVersion = request.AppVersion?.Trim() ?? "",
            ContentVersion = request.ContentVersion?.Trim() ?? "",
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        var errors = ContentDeliveryValidation.Validate(normalized);
        if (errors.Count > 0)
        {
            throw new ContentValidationException(errors);
        }

        var duplicate = await repository.FindAsync(
            normalized.Platform,
            normalized.AppVersion,
            normalized.ContentVersion,
            cancellationToken);
        if (duplicate is not null)
        {
            throw Conflict("CONTENT_RELEASE_EXISTS", "A release with the same platform and versions already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var release = new ContentRelease
        {
            Platform = normalized.Platform,
            AppVersion = normalized.AppVersion,
            ContentVersion = normalized.ContentVersion,
            Notes = normalized.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        await repository.AddAsync(release, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw Conflict("CONTENT_RELEASE_EXISTS", "A release with the same platform and versions already exists.");
        }

        logger.LogInformation(
            "Created content release {ReleaseId} for {Platform} app {AppVersion} content {ContentVersion}.",
            release.Id,
            release.Platform,
            release.AppVersion,
            release.ContentVersion);
        return ToResponse(release);
    }

    public async Task<ContentReleaseResponse> UploadAsync(
        Guid releaseId,
        Stream archive,
        long? contentLength,
        string artifactSha256,
        CancellationToken cancellationToken)
    {
        if (!ContentDeliveryValidation.IsSha256(artifactSha256))
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["X-Artifact-Sha256"] = ["X-Artifact-Sha256 must contain a 64-character SHA-256 value."]
            });
        }

        artifactSha256 = artifactSha256.ToLowerInvariant();
        await using var releaseLock = await lockProvider.AcquireAsync(releaseId, cancellationToken);
        var release = await GetRequiredAsync(releaseId, includeFiles: true, cancellationToken);
        if (release.State == ContentReleaseState.Ready)
        {
            if (string.Equals(release.ArtifactSha256, artifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                return ToResponse(release);
            }

            throw Conflict(
                "RELEASE_ARTIFACT_CONFLICT",
                "This release is already ready with a different archive.");
        }

        try
        {
            var package = await storage.StoreReleaseAsync(
                release,
                archive,
                contentLength,
                artifactSha256,
                cancellationToken);
            release.Files.Clear();
            var now = timeProvider.GetUtcNow();
            foreach (var file in package.Files)
            {
                release.Files.Add(new ContentReleaseFile
                {
                    ReleaseId = release.Id,
                    RelativePath = file.RelativePath,
                    Kind = file.Kind,
                    Size = file.Size,
                    Sha256 = file.Sha256,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            release.ArtifactSha256 = package.ArtifactSha256;
            release.FileCount = package.Files.Count;
            release.TotalBytes = package.TotalBytes;
            release.HotUpdatePath = package.Manifest.HotUpdatePath;
            release.CatalogPath = package.Manifest.CatalogPath;
            release.CatalogHashPath = package.Manifest.CatalogHashPath;
            release.State = ContentReleaseState.Ready;
            release.FailureCode = null;
            release.ReadyAt = now;
            release.UpdatedAt = now;
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await storage.DeleteReleaseAsync(release.Id, CancellationToken.None);
                throw;
            }

            logger.LogInformation(
                "Validated content release {ReleaseId}: {FileCount} files, {TotalBytes} bytes.",
                release.Id,
                release.FileCount,
                release.TotalBytes);
            return ToResponse(release);
        }
        catch (ContentDeliveryException exception)
        {
            release.State = ContentReleaseState.Failed;
            release.FailureCode = exception.Code;
            release.UpdatedAt = timeProvider.GetUtcNow();
            await repository.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(
                "Rejected content release {ReleaseId} with {FailureCode}.",
                release.Id,
                exception.Code);
            throw;
        }
    }

    public async Task<ContentReleaseResponse> GetAsync(Guid releaseId, CancellationToken cancellationToken) =>
        ToResponse(await GetRequiredAsync(releaseId, includeFiles: true, cancellationToken));

    public async Task<ContentReleasePageResponse> ListAsync(
        int page,
        int pageSize,
        string? platform,
        string? appVersion,
        string? state,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        ContentReleaseState? parsedState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<ContentReleaseState>(state, ignoreCase: true, out var value))
            {
                throw new ContentValidationException(new Dictionary<string, string[]>
                {
                    ["state"] = ["State must be AwaitingUpload, Ready, or Failed."]
                });
            }

            parsedState = value;
        }

        if (!string.IsNullOrWhiteSpace(platform) && !ContentDeliveryValidation.IsSupportedPlatform(platform))
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["platform"] = ["Platform must be StandaloneWindows64, Android, or iOS."]
            });
        }

        var result = await repository.ListAsync(
            page,
            pageSize,
            NullIfWhiteSpace(platform),
            NullIfWhiteSpace(appVersion),
            parsedState,
            cancellationToken);
        return new ContentReleasePageResponse(
            result.Items.Select(ToResponse).ToList(),
            page,
            pageSize,
            result.Total,
            TotalPages(result.Total, pageSize));
    }

    public async Task DeleteAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        await using var releaseLock = await lockProvider.AcquireAsync(releaseId, cancellationToken);
        var release = await GetRequiredAsync(releaseId, includeFiles: false, cancellationToken);
        if (release.State == ContentReleaseState.Ready)
        {
            throw Conflict("CONTENT_RELEASE_IMMUTABLE", "Ready releases are permanent and cannot be deleted.");
        }

        await storage.DeleteStagingAsync(release.Id, cancellationToken);
        await storage.DeleteReleaseAsync(release.Id, cancellationToken);
        repository.Delete(release);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Deleted incomplete content release {ReleaseId}.", release.Id);
    }

    public async Task<ActiveContentReleaseResponse> SetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        SetActiveContentReleaseRequest request,
        string source,
        CancellationToken cancellationToken)
    {
        ValidateTarget(channel, platform, appVersion);
        var release = await GetRequiredAsync(request.ReleaseId, includeFiles: false, cancellationToken);
        if (release.State != ContentReleaseState.Ready)
        {
            throw Conflict("CONTENT_RELEASE_NOT_READY", "Only ready releases can be made active.");
        }

        if (!string.Equals(release.Platform, platform, StringComparison.Ordinal) ||
            !string.Equals(release.AppVersion, appVersion, StringComparison.Ordinal))
        {
            throw Conflict(
                "CONTENT_RELEASE_TARGET_MISMATCH",
                "The release platform and app version must match the active target.");
        }

        var now = timeProvider.GetUtcNow();
        var active = await repository.SetActiveAsync(
            channel,
            platform,
            appVersion,
            release,
            request.ExpectedCurrentReleaseId,
            source,
            now,
            cancellationToken);
        logger.LogInformation(
            "Set active content release for {Channel}/{Platform}/{AppVersion} to {ReleaseId} from {Source}.",
            channel,
            platform,
            appVersion,
            release.Id,
            source);
        return ToActiveResponse(active, release);
    }

    public async Task<ActiveContentReleaseResponse?> GetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        CancellationToken cancellationToken)
    {
        ValidateTarget(channel, platform, appVersion);
        var active = await repository.GetActiveAsync(
            channel,
            platform,
            appVersion,
            includeRelease: true,
            cancellationToken);
        return active is null ? null : ToActiveResponse(active, active.Release);
    }

    public async Task<LatestContentManifestResponse> GetLatestManifestAsync(
        string channel,
        string platform,
        string appVersion,
        CancellationToken cancellationToken)
    {
        ValidateTarget(channel, platform, appVersion);
        var active = await repository.GetActiveAsync(
            channel,
            platform,
            appVersion,
            includeRelease: true,
            cancellationToken) ?? throw NotFound(
                "CONTENT_RELEASE_NOT_FOUND",
                "No active content release exists for this channel, platform, and app version.");
        var release = active.Release;
        if (release.State != ContentReleaseState.Ready ||
            release.HotUpdatePath is null ||
            release.CatalogPath is null ||
            release.CatalogHashPath is null)
        {
            throw NotFound("CONTENT_RELEASE_NOT_FOUND", "The active content release is not available.");
        }

        var hotUpdate = release.Files.Single(value => value.RelativePath == release.HotUpdatePath);
        var releaseBase = $"/content/releases/{release.Id:D}";
        return new LatestContentManifestResponse(
            1,
            release.Id,
            active.Channel,
            active.Platform,
            active.AppVersion,
            release.ContentVersion,
            active.UpdatedAt,
            new HotUpdateArtifactResponse(
                BuildContentPath(releaseBase, hotUpdate.RelativePath),
                hotUpdate.Size,
                hotUpdate.Sha256),
            new AddressablesArtifactResponse(
                $"{releaseBase}/Addressables",
                BuildContentPath(releaseBase, release.CatalogPath),
                BuildContentPath(releaseBase, release.CatalogHashPath)));
    }

    public async Task<ContentPublicationPageResponse> ListPublicationsAsync(
        int page,
        int pageSize,
        string? channel,
        string? platform,
        string? appVersion,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        var result = await repository.ListPublicationsAsync(
            page,
            pageSize,
            NullIfWhiteSpace(channel),
            NullIfWhiteSpace(platform),
            NullIfWhiteSpace(appVersion),
            cancellationToken);
        return new ContentPublicationPageResponse(
            result.Items.Select(value => new ContentPublicationResponse(
                value.Id,
                value.Channel,
                value.Platform,
                value.AppVersion,
                value.PreviousReleaseId,
                value.ReleaseId,
                value.Release.ContentVersion,
                value.Source,
                value.CreatedAt)).ToList(),
            page,
            pageSize,
            result.Total,
            TotalPages(result.Total, pageSize));
    }

    public async Task<ContentFileDownload> OpenFileAsync(
        Guid releaseId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeDownloadPath(relativePath);
        var file = await repository.GetFileAsync(releaseId, normalized, cancellationToken);
        if (file is null || file.Release.State != ContentReleaseState.Ready)
        {
            throw NotFound("CONTENT_FILE_NOT_FOUND", "The requested content file does not exist.");
        }

        var stored = await storage.OpenReadAsync(releaseId, normalized, cancellationToken) ??
            throw NotFound("CONTENT_FILE_NOT_FOUND", "The requested content file does not exist.");
        return new ContentFileDownload(stored, file.Kind, file.Sha256, normalized);
    }

    private async Task<ContentRelease> GetRequiredAsync(
        Guid releaseId,
        bool includeFiles,
        CancellationToken cancellationToken) =>
        await repository.GetAsync(releaseId, includeFiles, cancellationToken) ??
        throw NotFound("CONTENT_RELEASE_NOT_FOUND", "The requested content release does not exist.");

    private void ValidateTarget(string channel, string platform, string appVersion)
    {
        if (!options.AllowedChannels.Contains(channel, StringComparer.Ordinal))
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["channel"] = ["The requested channel is not enabled."]
            });
        }

        if (!ContentDeliveryValidation.IsSupportedPlatform(platform))
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["platform"] = ["Platform must be StandaloneWindows64, Android, or iOS."]
            });
        }

        if (string.IsNullOrWhiteSpace(appVersion) || appVersion.Length > 64)
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["appVersion"] = ["App version is required and must contain at most 64 characters."]
            });
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Page must be at least 1 and pageSize must be between 1 and 100."]
            });
        }
    }

    private static ContentReleaseResponse ToResponse(ContentRelease release) => new(
        release.Id,
        release.Platform,
        release.AppVersion,
        release.ContentVersion,
        release.State.ToString(),
        release.Notes,
        release.FileCount,
        release.TotalBytes,
        release.ArtifactSha256,
        release.FailureCode,
        release.CreatedAt,
        release.UpdatedAt,
        release.ReadyAt,
        release.Files
            .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
            .Select(value => new ContentReleaseFileResponse(
                value.RelativePath,
                value.Kind,
                value.Size,
                value.Sha256))
            .ToList());

    private static ActiveContentReleaseResponse ToActiveResponse(
        ActiveContentRelease active,
        ContentRelease release) => new(
            active.Channel,
            active.Platform,
            active.AppVersion,
            active.ReleaseId,
            release.ContentVersion,
            active.UpdatedAt);

    private static string BuildContentPath(string releaseBase, string relativePath) =>
        releaseBase + "/" + string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));

    private static string NormalizeDownloadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 512 ||
            path.Contains('\\') ||
            path.Contains('\0') ||
            Path.IsPathRooted(path) ||
            path.StartsWith('/') ||
            path.Contains(':'))
        {
            throw NotFound("CONTENT_FILE_NOT_FOUND", "The requested content file does not exist.");
        }

        var segments = path.Split('/');
        if (segments.Any(value => value is "" or "." or ".."))
        {
            throw NotFound("CONTENT_FILE_NOT_FOUND", "The requested content file does not exist.");
        }

        return string.Join('/', segments);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int TotalPages(int total, int pageSize) =>
        total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

    private static ContentDeliveryException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    private static ContentDeliveryException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}

public sealed record ContentFileDownload(
    ContentFileRead StoredFile,
    string Kind,
    string Sha256,
    string RelativePath);

public sealed class ContentValidationException(Dictionary<string, string[]> errors)
    : ContentDeliveryException(
        StatusCodes.Status422UnprocessableEntity,
        "VALIDATION_ERROR",
        "Request validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
