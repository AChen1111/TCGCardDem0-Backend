using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AChen.Backend.Api.Pages.Admin.Content;

[Authorize(AuthenticationSchemes = ContentAdminAuthentication.Scheme)]
public sealed class IndexModel(ContentReleaseService service) : PageModel
{
    public ContentReleasePageResponse Releases { get; private set; } = new([], 1, 20, 0, 0);
    public IReadOnlyList<string> Platforms => ContentDeliveryValidation.SupportedPlatforms;
    public IReadOnlyList<string> States => Enum.GetNames<ContentReleaseState>();
    public string? Platform { get; private set; }
    public string? AppVersion { get; private set; }
    public string? State { get; private set; }

    public async Task OnGetAsync(
        int page = 1,
        string? platform = null,
        string? appVersion = null,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        Platform = platform;
        AppVersion = appVersion;
        State = state;
        Releases = await service.ListAsync(page, 20, platform, appVersion, state, cancellationToken);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(ContentAdminAuthentication.Scheme);
        return RedirectToPage("/Admin/Content/Login");
    }

    public string FormatBytes(long bytes) => ContentAdminFormatting.FormatBytes(bytes);
}
