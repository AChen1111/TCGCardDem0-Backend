using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AChen.Backend.Api.Pages.Admin.Content;

[Authorize(AuthenticationSchemes = ContentAdminAuthentication.Scheme)]
public sealed class PublicationsModel(ContentReleaseService service) : PageModel
{
    public ContentPublicationPageResponse Publications { get; private set; } = new([], 1, 50, 0, 0);

    public async Task OnGetAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        Publications = await service.ListPublicationsAsync(
            page,
            50,
            "development",
            null,
            null,
            cancellationToken);
    }
}
