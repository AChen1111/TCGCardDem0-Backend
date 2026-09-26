using System.Text.RegularExpressions;
namespace AChen.Backend.Api.Features.ContentDelivery;
public static class ContentDeliveryValidation
{
    public static bool IsSha256(string? value) => value != null && Regex.IsMatch(value, "\\A[0-9a-fA-F]{64}\\z");
}
