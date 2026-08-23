namespace AChen.Backend.Api.Features.GameConfig;

public static class GameConfigValidation
{
    public static Dictionary<string, string[]> Validate(AvatarDefinitionInput input)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateId(input.Id, errors);
        ValidateText(input.Name, 64, "name", "Name", errors);
        ValidateText(input.ResourceKey, 128, "resourceKey", "ResourceKey", errors);
        ValidateExpectedEditRevision(input.ExpectedEditRevision, errors);
        return errors;
    }

    public static Dictionary<string, string[]> Validate(CardPackDefinitionInput input)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateId(input.Id, errors);
        ValidateText(input.Title, 64, "title", "Title", errors);
        ValidateText(input.CoverResourceKey, 128, "coverResourceKey", "CoverResourceKey", errors);
        if (input.PriceGold < 0)
        {
            errors["priceGold"] = ["PriceGold cannot be negative."];
        }

        if (input.StartsAt is not null && input.EndsAt is not null && input.EndsAt <= input.StartsAt)
        {
            errors["endsAt"] = ["EndsAt must be later than StartsAt."];
        }

        ValidateExpectedEditRevision(input.ExpectedEditRevision, errors);
        return errors;
    }

    private static void ValidateId(int id, Dictionary<string, string[]> errors)
    {
        if (id <= 0)
        {
            errors["id"] = ["Id must be greater than zero."];
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
            errors[field] = [$"{label} must contain 1-{maxLength} characters and cannot contain control characters."];
        }
    }

    private static void ValidateExpectedEditRevision(
        long expectedEditRevision,
        Dictionary<string, string[]> errors)
    {
        if (expectedEditRevision < 0)
        {
            errors["expectedEditRevision"] = ["ExpectedEditRevision cannot be negative."];
        }
    }
}
