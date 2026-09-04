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
            errors["platform"] = ["平台必须是 StandaloneWindows64、Android 或 iOS"];
        }

        if (string.IsNullOrWhiteSpace(request.AppVersion) ||
            request.AppVersion.Length > 64 ||
            !SafeVersionPattern().IsMatch(request.AppVersion))
        {
            errors["appVersion"] = ["应用版本需为 1-64 位字母、数字、点、加号或连字符"];
        }

        if (string.IsNullOrWhiteSpace(request.ContentVersion) ||
            request.ContentVersion.Length > 64 ||
            !SemanticVersionPattern().IsMatch(request.ContentVersion))
        {
            errors["contentVersion"] = ["内容版本必须是有效的语义化版本，例如 1.2.3 或 1.2.3-beta.1"];
        }

        if (request.Notes?.Length > 2_000)
        {
            errors["notes"] = ["备注不能超过 2000 个字符"];
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
