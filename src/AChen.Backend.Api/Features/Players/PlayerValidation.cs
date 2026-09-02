namespace AChen.Backend.Api.Features.Players;

public static class PlayerValidation
{
    public static Dictionary<string, string[]> Validate(UpdatePlayerProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var nickname = request.Nickname?.Trim() ?? "";
        if (nickname.Length is < 2 or > 24 || nickname.Any(char.IsControl))
        {
            errors["nickname"] = ["Nickname must contain 2-24 characters and cannot contain control characters."];
        }

        if (request.AvatarId is < 0)
        {
            errors["avatarId"] = ["AvatarId cannot be negative."];
        }

        if (request.BackgroundId is <= 0)
        {
            errors["backgroundId"] = ["BackgroundId must be greater than zero."];
        }

        if (request.ExpectedRevision < 0)
        {
            errors["expectedRevision"] = ["ExpectedRevision cannot be negative."];
        }

        return errors;
    }
}
