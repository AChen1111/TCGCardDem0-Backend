using System.Security.Cryptography;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AChen.Backend.Api.Pages.Admin.Content;

[Authorize(AuthenticationSchemes = ContentAdminAuthentication.Scheme)]
[RequestSizeLimit(2L * 1024 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
public sealed class NewModel(ContentReleaseService service) : PageModel
{
    [BindProperty]
    public string Platform { get; set; } = "StandaloneWindows64";

    [BindProperty]
    public string AppVersion { get; set; } = "0.1.0";

    [BindProperty]
    public string ContentVersion { get; set; } = "";

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public IFormFile? Artifact { get; set; }

    public IReadOnlyList<string> Platforms => ContentDeliveryValidation.SupportedPlatforms;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Artifact is null || Artifact.Length == 0)
        {
            ModelState.AddModelError(nameof(Artifact), "请选择发布 ZIP。");
            return Page();
        }

        ContentReleaseResponse? release = null;
        try
        {
            release = await service.CreateAsync(
                new CreateContentReleaseRequest(Platform, AppVersion, ContentVersion, Notes),
                cancellationToken);
            string hash;
            await using (var hashStream = Artifact.OpenReadStream())
            using (var sha256 = SHA256.Create())
            {
                hash = Convert.ToHexString(await sha256.ComputeHashAsync(hashStream, cancellationToken))
                    .ToLowerInvariant();
            }

            await using var uploadStream = Artifact.OpenReadStream();
            await service.UploadAsync(
                release.Id,
                uploadStream,
                Artifact.Length,
                hash,
                cancellationToken);
            TempData["Message"] = "发布包上传并校验成功。";
            return RedirectToPage("/Admin/Content/Details", new { id = release.Id });
        }
        catch (ApiException exception)
        {
            if (release is not null)
            {
                TempData["Error"] = exception.Message;
                return RedirectToPage("/Admin/Content/Details", new { id = release.Id });
            }

            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
