using System.ComponentModel.DataAnnotations;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AChen.Backend.Api.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class RegisterModel(AuthService authService) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [TempData]
    public string? RegisteredUsername { get; set; }

    public bool RegistrationCompleted => !string.IsNullOrWhiteSpace(RegisteredUsername);

    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var request = new RegisterRequest(Input.Username, Input.Email, Input.Password);
        AddDomainValidationErrors(AuthValidation.Validate(request));
        if (!ModelState.IsValid)
        {
            ClearPasswords();
            return Page();
        }

        try
        {
            var account = await authService.CreateAccountAsync(request, cancellationToken);
            RegisteredUsername = account.Username;
            ClearPasswords();
            return RedirectToPage();
        }
        catch (ApiException exception) when (exception.Code == "ACCOUNT_EXISTS")
        {
            ModelState.AddModelError(string.Empty, "该用户名或邮箱已被注册，请更换后重试。");
            ClearPasswords();
            return Page();
        }
    }

    private void AddDomainValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            var modelKey = field switch
            {
                "username" => nameof(Input) + "." + nameof(Input.Username),
                "email" => nameof(Input) + "." + nameof(Input.Email),
                "password" => nameof(Input) + "." + nameof(Input.Password),
                _ => string.Empty
            };

            if (ModelState.TryGetValue(modelKey, out var entry) && entry.Errors.Count > 0)
            {
                continue;
            }

            foreach (var message in messages)
            {
                ModelState.AddModelError(modelKey, TranslateValidationMessage(field, message));
            }
        }
    }

    private static string TranslateValidationMessage(string field, string fallback) => field switch
    {
        "username" => "用户名需为 3–24 位，只能包含英文字母、数字或下划线。",
        "email" => "请输入有效的邮箱地址，长度不能超过 254 个字符。",
        "password" => "密码长度需为 8–128 位。",
        _ => fallback
    };

    private void ClearPasswords()
    {
        Input.Password = "";
        Input.ConfirmPassword = "";
    }

    public sealed class RegisterInput
    {
        [Required(ErrorMessage = "请输入用户名。")]
        [RegularExpression("^[A-Za-z0-9_]{3,24}$", ErrorMessage = "用户名需为 3–24 位，只能包含英文字母、数字或下划线。")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "请输入邮箱地址。")]
        [StringLength(254, ErrorMessage = "邮箱长度不能超过 254 个字符。")]
        [EmailAddress(ErrorMessage = "请输入有效的邮箱地址。")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "请输入密码。")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "密码长度需为 8–128 位。")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "请再次输入密码。")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "两次输入的密码不一致。")]
        public string ConfirmPassword { get; set; } = "";
    }
}
