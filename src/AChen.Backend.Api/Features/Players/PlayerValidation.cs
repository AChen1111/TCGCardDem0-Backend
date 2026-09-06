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

        if (request.BackgroundId is < 0)
        {
            errors["backgroundId"] = ["背景 ID 不能为负数"];
        }

        if (request.ExpectedRevision < 0)
        {
            errors["expectedRevision"] = ["预期版本号不能为负数"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(PurchaseShopItemRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var catalogType = request.CatalogType?.Trim() ?? "";
        if (catalogType is not (ShopCatalogTypes.Avatar or ShopCatalogTypes.Wallpaper))
        {
            errors["catalogType"] = ["商品类型只能是 avatar 或 wallpaper"];
        }

        if (request.ItemId < 0)
        {
            errors["itemId"] = ["商品 ID 不能为负数"];
        }

        if (request.ExpectedRevision < 0)
        {
            errors["expectedRevision"] = ["预期版本号不能为负数"];
        }

        return errors;
    }
}
