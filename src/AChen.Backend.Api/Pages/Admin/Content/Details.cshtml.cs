using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AChen.Backend.Api.Pages.Admin.Content;

[Authorize(AuthenticationSchemes = ContentAdminAuthentication.Scheme)]
public sealed class DetailsModel(ContentReleaseService service) : PageModel
{
    public ContentReleaseResponse Release { get; private set; } = null!;
    public ActiveContentReleaseResponse? Active { get; private set; }
    public bool IsActive => Active?.ReleaseId == Release.Id;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(id, cancellationToken);
            return Page();
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostPublishAsync(
        Guid id,
        Guid? expectedCurrentReleaseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await service.GetAsync(id, cancellationToken);
            await service.SetActiveAsync(
                "development",
                release.Platform,
                release.AppVersion,
                new SetActiveContentReleaseRequest(id, expectedCurrentReleaseId),
                "admin-web",
                cancellationToken);
            TempData["Message"] = "当前版本已切换。";
        }
        catch (ApiException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            TempData["Message"] = "未完成版本已删除。";
            return RedirectToPage("/Admin/Content/Index");
        }
        catch (ApiException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToPage(new { id });
        }
    }

    public string FormatBytes(long bytes) => ContentAdminFormatting.FormatBytes(bytes);

    private async Task LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Release = await service.GetAsync(id, cancellationToken);
        Active = await service.GetActiveAsync(
            "development",
            Release.Platform,
            Release.AppVersion,
            cancellationToken);
    }
}
