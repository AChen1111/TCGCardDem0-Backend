using System.Text.RegularExpressions;

namespace AChen.Backend.Api.Features.Auth;

public static partial class AuthValidation
{
    public static Dictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim() ?? "";

        if (!UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["用户名需为 3-24 位英文、数字或下划线"];
        }

        if (request.Password is null || request.Password.Length is < 8 or > 128)
        {
            errors["password"] = ["密码长度需为 8-128 位"];
        }
        else if (IsWeakPassword(request.Password))
        {
            errors["password"] = ["密码过弱"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim() ?? "";

        if (!UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["用户名需为 3-24 位英文、数字或下划线"];
        }

        if (request.Password is null || request.Password.Length is < 8 or > 128)
        {
            errors["password"] = ["密码长度需为 8-128 位"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateRefreshToken(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            return new Dictionary<string, string[]>
            {
                ["refreshToken"] = ["刷新令牌不能为空且长度不能超过 512 个字符"]
            };
        }

        return [];
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    private static bool IsWeakPassword(string password)
    {
        var hasLetter = false;
        var hasNumberOrSymbol = false;

        foreach (var character in password)
        {
            if (char.IsLetter(character))
            {
                hasLetter = true;
            }
            else
            {
                hasNumberOrSymbol = true;
            }
        }

        return !hasLetter || !hasNumberOrSymbol;
    }
}
