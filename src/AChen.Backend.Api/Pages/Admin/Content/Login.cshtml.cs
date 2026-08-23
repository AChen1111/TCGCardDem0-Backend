using System.Security.Claims;
using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AChen.Backend.Api.Pages.Admin.Content;

[AllowAnonymous]
[EnableRateLimiting("admin-login")]
public sealed class LoginModel(ContentPublisherCredentials credentials) : PageModel
{
    [BindProperty]
    public string PublishKey { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Admin/Content/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!credentials.Validate(PublishKey))
        {
            ModelState.AddModelError(string.Empty, "发布密钥无效。");
            PublishKey = "";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "content-admin"),
            new Claim(ClaimTypes.Name, "Content administrator"),
            new Claim(ContentPublisherAuthentication.FingerprintClaim, credentials.GetFingerprint())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ContentAdminAuthentication.Scheme));
        await HttpContext.SignInAsync(
            ContentAdminAuthentication.Scheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
        PublishKey = "";
        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl!)
            : RedirectToPage("/Admin/Content/Index");
    }
}
