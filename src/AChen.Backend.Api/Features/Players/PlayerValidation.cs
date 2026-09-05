namespace AChen.Backend.Api.Features.Players;

public static class PlayerValidation
{
    public static Dictionary<string, string[]> Validate(UpdatePlayerProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var nickname = request.Nickname?.Trim() ?? "";
        if (nickname.Length is < 2 or > 24 || nickname.Any(char.IsControl))
        {
            errors["nickname"] = ["昵称需为 2-24 个字符且不能包含控制字符"];
        }

        if (request.AvatarId is < 0)
        {
            errors["avatarId"] = ["头像 ID 不能为负数"];
        }

        if (request.BackgroundId is <= 0)
        {
            errors["backgroundId"] = ["背景 ID 必须大于 0"];
        }

        if (request.ExpectedRevision < 0)
        {
            errors["expectedRevision"] = ["预期版本号不能为负数"];
        }

        return errors;
    }
}
