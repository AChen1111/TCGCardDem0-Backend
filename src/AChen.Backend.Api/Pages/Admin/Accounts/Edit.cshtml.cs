using AChen.Backend.Api.Features.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AChen.Backend.Api.Pages.Admin.Accounts;

[AllowAnonymous]
[EnableRateLimiting("content-management")]
public sealed class EditModel(AccountManagementService service) : PageModel
{
    public ManagedAccountDetails Account { get; private set; } = null!;

    [BindProperty]
    public PlayerDataInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var account = await service.GetAsync(id, cancellationToken);
        if (account is null)
        {
            TempData["Error"] = "账号不存在或已经被删除。";
            return RedirectToPage("/Admin/Accounts/Index");
        }

        Account = account;
        Input = new PlayerDataInput
        {
            Nickname = account.Nickname,
            AvatarId = account.AvatarId,
            OwnedAvatarIds = string.Join(", ", account.OwnedAvatarIds),
            BackgroundId = account.BackgroundId,
            Gold = account.Gold
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownedAvatarIds = ParseOwnedAvatarIds(Input.OwnedAvatarIds);
        if (!ModelState.IsValid)
        {
            return await ReloadPageAsync(id, cancellationToken);
        }

        var result = await service.UpdatePlayerDataAsync(
            id,
            new UpdateManagedPlayerData(
                Input.Nickname,
                Input.AvatarId,
                ownedAvatarIds,
                Input.BackgroundId,
                Input.Gold),
            cancellationToken);
        if (!result.Found)
        {
            TempData["Error"] = "账号不存在或已经被删除。";
            return RedirectToPage("/Admin/Accounts/Index");
        }

        foreach (var (field, messages) in result.Errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(
                    string.IsNullOrEmpty(field) ? string.Empty : $"Input.{field}",
                    message);
            }
        }

        if (!result.Succeeded)
        {
            return await ReloadPageAsync(id, cancellationToken);
        }

        TempData["Message"] = "玩家数据已保存。";
        return RedirectToPage(new { id });
    }

    private IReadOnlyList<int> ParseOwnedAvatarIds(string? value)
    {
        var parts = (value ?? "").Split(
            [',', '，', ';', '；', ' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var avatarId))
            {
                ModelState.AddModelError("Input.OwnedAvatarIds", $"“{part}”不是有效的头像 ID。");
                continue;
            }

            values.Add(avatarId);
        }

        return values;
    }

    private async Task<IActionResult> ReloadPageAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await service.GetAsync(id, cancellationToken);
        if (account is null)
        {
            TempData["Error"] = "账号不存在或已经被删除。";
            return RedirectToPage("/Admin/Accounts/Index");
        }

        Account = account;
        return Page();
    }

    public sealed class PlayerDataInput
    {
        public string Nickname { get; set; } = "";
        public int? AvatarId { get; set; }
        public string OwnedAvatarIds { get; set; } = "";
        public int? BackgroundId { get; set; }
        public long Gold { get; set; }
    }
}
