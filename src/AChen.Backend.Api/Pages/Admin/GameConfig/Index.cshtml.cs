using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.GameConfig;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AChen.Backend.Api.Pages.Admin.GameConfig;

[Authorize(AuthenticationSchemes = ContentAdminAuthentication.Scheme)]
public sealed class IndexModel(
    GameConfigService service,
    GameConfigCsvSerializer csvSerializer,
    GameConfigGitService gitService) : PageModel
{
    private const long MaxCsvBytes = 5 * 1024 * 1024;

    public GameConfigAdminResponse Config { get; private set; } =
        new(1, 0, null, null, [], []);
    public GameConfigGitStatus GitStatus { get; private set; } =
        GameConfigGitStatus.Unavailable("main", "正在读取配置仓库状态。");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Config = await service.GetAdminAsync(cancellationToken);
        GitStatus = await gitService.GetStatusAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        var config = await service.GetAdminAsync(cancellationToken);
        var data = await service.GetDraftDataAsync(cancellationToken);
        return File(
            csvSerializer.Serialize(data),
            "text/csv; charset=utf-8",
            $"game-config-draft-r{config.DraftRevision}-{config.EditRevision}.csv");
    }

    public async Task<IActionResult> OnPostImportCsvAsync(
        IFormFile? csvFile,
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || csvFile is null || csvFile.Length is <= 0 or > MaxCsvBytes)
        {
            TempData["Error"] = "请选择不超过 5 MiB 的有效 CSV 文件。";
            return RedirectToPage();
        }

        try
        {
            await using var stream = new MemoryStream((int)csvFile.Length);
            await csvFile.CopyToAsync(stream, cancellationToken);
            var imported = csvSerializer.Deserialize(stream.ToArray());
            await service.ReplaceDraftAsync(imported, expectedEditRevision, cancellationToken);
            TempData["Message"] =
                $"CSV 已载入草稿：{imported.Avatars.Count} 个头像，{imported.CardPacks.Count} 个卡包。";
        }
        catch (GameConfigCsvException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (ApiException exception)
        {
            TempData["Error"] = DisplayMessage(exception);
        }

        return RedirectToPage();
    }

    public Task<IActionResult> OnPostGitSaveAsync(
        string? note,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => gitService.SaveSnapshotAsync(note, cancellationToken),
            "当前草稿已保存为本地 Git 快照。");

    public Task<IActionResult> OnPostGitPushAsync(
        string? note,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => gitService.SaveAndPushAsync(note, cancellationToken),
            "当前草稿已提交并推送到 Git 远端。");

    public Task<IActionResult> OnPostGitPullAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => gitService.PullAsync(cancellationToken),
            "Git 远端历史已拉取。需要应用时请选择一个版本载入草稿。");

    public Task<IActionResult> OnPostGitApplyAsync(
        string commitHash,
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => gitService.ApplyCommitToDraftAsync(
                commitHash,
                expectedEditRevision,
                cancellationToken),
            "所选 Git 版本已载入当前草稿，请检查后再发布。");

    public Task<IActionResult> OnPostSaveAvatarAsync(
        int id,
        string name,
        string resourceKey,
        int sortOrder,
        bool isEnabled,
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.UpsertAvatarAsync(
                new AvatarDefinitionInput(
                    id,
                    name,
                    resourceKey,
                    sortOrder,
                    isEnabled,
                    expectedEditRevision),
                cancellationToken),
            id > 0 ? "头像草稿已保存。" : "头像草稿已新增。");

    public Task<IActionResult> OnPostDeleteAvatarAsync(
        int id,
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.DeleteAvatarAsync(id, expectedEditRevision, cancellationToken),
            "头像草稿已删除。");

    public Task<IActionResult> OnPostSaveCardPackAsync(
        int id,
        string title,
        string coverResourceKey,
        long priceGold,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        int sortOrder,
        bool isEnabled,
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.UpsertCardPackAsync(
                new CardPackDefinitionInput(
                    id,
                    title,
                    coverResourceKey,
                    priceGold,
                    startsAt,
                    endsAt,
                    sortOrder,
                    isEnabled,
                    expectedEditRevision),
                cancellationToken),
            "卡包草稿已保存。");

    public Task<IActionResult> OnPostDeleteCardPackAsync(
        int id,
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.DeleteCardPackAsync(id, expectedEditRevision, cancellationToken),
            "卡包草稿已删除。");

    public async Task<IActionResult> OnPostPublishAsync(
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "表单数据格式无效，请刷新后重试。";
            return RedirectToPage();
        }

        try
        {
            var publication = await service.PublishAsync(expectedEditRevision, cancellationToken);
            TempData["Message"] =
                $"配置 Revision {publication.PublishedRevision} 已发布，新草稿为 Revision {publication.DraftRevision}。";
        }
        catch (ApiException exception)
        {
            TempData["Error"] = DisplayMessage(exception);
        }

        return RedirectToPage();
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, string message)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "表单数据格式无效，请检查数字和 ISO-8601 时间。";
            return RedirectToPage();
        }

        try
        {
            await action();
            TempData["Message"] = message;
        }
        catch (ApiException exception)
        {
            TempData["Error"] = DisplayMessage(exception);
        }

        return RedirectToPage();
    }

    private static string DisplayMessage(ApiException exception)
    {
        if (exception is not IApiValidationException validation)
        {
            return exception.Message;
        }

        return validation.Errors.Values.SelectMany(value => value).FirstOrDefault() ?? exception.Message;
    }
}
