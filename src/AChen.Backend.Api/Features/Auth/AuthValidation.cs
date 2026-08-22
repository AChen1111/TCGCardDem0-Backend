using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace AChen.Backend.Api.Features.Auth;

public static partial class AuthValidation
{
    public static Dictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";

        if (!UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["Username must contain 3-24 letters, numbers, or underscores."];
        }

        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["Email must be a valid address with at most 254 characters."];
        }

        if (request.Password is null || request.Password.Length is < 8 or > 128)
        {
            errors["password"] = ["Password must contain 8-128 characters."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var identifier = request.Identifier?.Trim() ?? "";

        if (identifier.Length is < 1 or > 254)
        {
            errors["identifier"] = ["Identifier must contain 1-254 characters."];
        }

        if (request.Password is null || request.Password.Length is < 8 or > 128)
        {
            errors["password"] = ["Password must contain 8-128 characters."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateRefreshToken(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            return new Dictionary<string, string[]>
            {
                ["refreshToken"] = ["Refresh token is required and must contain at most 512 characters."]
            };
        }

        return [];
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
