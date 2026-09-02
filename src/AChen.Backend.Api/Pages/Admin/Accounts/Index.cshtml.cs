using AChen.Backend.Api.Features.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AChen.Backend.Api.Pages.Admin.Accounts;

[AllowAnonymous]
[EnableRateLimiting("content-management")]
public sealed class IndexModel(AccountManagementService service) : PageModel
{
    public ManagedAccountPage Accounts { get; private set; } = new([], 1, 20, 0, 1);
    public string? Search { get; private set; }

    public async Task OnGetAsync(
        int page = 1,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Search = search?.Trim();
        Accounts = await service.ListAsync(page, 20, Search, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        Guid id,
        int page = 1,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var deleted = await service.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            TempData["Message"] = "账号已删除，关联的玩家资料和登录会话也已清除。";
        }
        else
        {
            TempData["Error"] = "账号不存在或已经被删除。";
        }

        return RedirectToPage(new { page, search });
    }
}
