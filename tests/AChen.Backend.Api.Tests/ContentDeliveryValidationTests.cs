using AChen.Backend.Api.Features.ContentDelivery;

namespace AChen.Backend.Api.Tests;

public sealed class ContentDeliveryValidationTests
{
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("1.0.0-beta.1+build.9")]
    public void Strict_semver_accepts_valid_content_versions(string contentVersion)
    {
        var errors = ContentDeliveryValidation.Validate(new CreateContentReleaseRequest(
            "StandaloneWindows64",
            "0.1.0",
            contentVersion,
            null));

        Assert.DoesNotContain("contentVersion", errors.Keys);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-01")]
    [InlineData("v1.0.0")]
    public void Strict_semver_rejects_invalid_content_versions(string contentVersion)
    {
        var errors = ContentDeliveryValidation.Validate(new CreateContentReleaseRequest(
            "StandaloneWindows64",
            "0.1.0",
            contentVersion,
            null));

        Assert.Contains("contentVersion", errors.Keys);
    }

    [Fact]
    public void Sha256_validation_requires_exact_hex_digest()
    {
        Assert.True(ContentDeliveryValidation.IsSha256(new string('a', 64)));
        Assert.False(ContentDeliveryValidation.IsSha256(new string('a', 63)));
        Assert.False(ContentDeliveryValidation.IsSha256(new string('z', 64)));
    }
}
