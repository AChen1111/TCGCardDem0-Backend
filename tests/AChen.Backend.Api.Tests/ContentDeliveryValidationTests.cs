using AChen.Backend.Api.Features.ContentDelivery;
namespace AChen.Backend.Api.Tests;
public sealed class ContentDeliveryValidationTests
{
    [Fact]
    public void Hash_validation_rejects_invalid_inputs()
    {
        Assert.True(ContentDeliveryValidation.IsSha256(new string('a', 64)));
        Assert.False(ContentDeliveryValidation.IsSha256(null));
        Assert.False(ContentDeliveryValidation.IsSha256(new string('g', 64)));
        Assert.False(ContentDeliveryValidation.IsSha256(new string('a', 63)));
    }
}
