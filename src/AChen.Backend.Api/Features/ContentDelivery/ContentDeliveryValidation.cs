using System.Text.RegularExpressions;

namespace AChen.Backend.Api.Features.ContentDelivery;

public static partial class ContentDeliveryValidation
{
    public static readonly string[] SupportedPlatforms = ["StandaloneWindows64", "Android", "iOS"];

    public static Dictionary<string, string[]> Validate(CreateContentReleaseRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!SupportedPlatforms.Contains(request.Platform, StringComparer.Ordinal))
        {
            errors["platform"] = ["Platform must be StandaloneWindows64, Android, or iOS."];
        }

        if (string.IsNullOrWhiteSpace(request.AppVersion) ||
            request.AppVersion.Length > 64 ||
            !SafeVersionPattern().IsMatch(request.AppVersion))
        {
            errors["appVersion"] = ["App version must contain 1-64 letters, numbers, dots, plus signs, or hyphens."];
        }

        if (string.IsNullOrWhiteSpace(request.ContentVersion) ||
            request.ContentVersion.Length > 64 ||
            !SemanticVersionPattern().IsMatch(request.ContentVersion))
        {
            errors["contentVersion"] = ["Content version must be a valid Semantic Version, for example 1.2.3 or 1.2.3-beta.1."];
        }

        if (request.Notes?.Length > 2_000)
        {
            errors["notes"] = ["Notes must contain at most 2000 characters."];
        }

        return errors;
    }

    public static bool IsSha256(string? value) =>
        value is not null && Sha256Pattern().IsMatch(value);

    public static bool IsSupportedPlatform(string platform) =>
        SupportedPlatforms.Contains(platform, StringComparer.Ordinal);

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeVersionPattern();

    [GeneratedRegex("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-((?:0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\\+([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
