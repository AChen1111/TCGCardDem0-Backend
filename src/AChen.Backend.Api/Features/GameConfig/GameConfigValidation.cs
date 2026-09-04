namespace AChen.Backend.Api.Features.GameConfig;

public static class GameConfigValidation
{
    public static Dictionary<string, string[]> Validate(AvatarDefinitionInput input)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateId(input.Id, errors);
        ValidateText(input.Name, 64, "name", "名称", errors);
        ValidateText(input.ResourceKey, 128, "resourceKey", "资源键", errors);
        ValidateExpectedEditRevision(input.ExpectedEditRevision, errors);
        return errors;
    }

    public static Dictionary<string, string[]> Validate(CardPackDefinitionInput input)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateId(input.Id, errors);
        ValidateText(input.Title, 64, "title", "标题", errors);
        ValidateText(input.CoverResourceKey, 128, "coverResourceKey", "封面资源键", errors);
        if (input.PriceGold < 0)
        {
            errors["priceGold"] = ["金币价格不能为负数"];
        }

        if (input.StartsAt is not null && input.EndsAt is not null && input.EndsAt <= input.StartsAt)
        {
            errors["endsAt"] = ["结束时间必须晚于开始时间"];
        }

        ValidateExpectedEditRevision(input.ExpectedEditRevision, errors);
        return errors;
    }

    private static void ValidateId(int id, Dictionary<string, string[]> errors)
    {
        if (id <= 0)
        {
            errors["id"] = ["ID 必须大于 0"];
        }
    }

    private static void ValidateText(
        string? value,
        int maxLength,
        string field,
        string label,
        Dictionary<string, string[]> errors)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            errors[field] = [$"{label}需为 1-{maxLength} 个字符且不能包含控制字符"];
        }
    }

    private static void ValidateExpectedEditRevision(
        long expectedEditRevision,
        Dictionary<string, string[]> errors)
    {
        if (expectedEditRevision < 0)
        {
            errors["expectedEditRevision"] = ["预期编辑版本号不能为负数"];
        }
    }
}
