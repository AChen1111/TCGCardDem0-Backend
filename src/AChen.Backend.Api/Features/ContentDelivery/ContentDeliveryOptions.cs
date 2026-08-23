namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class ContentDeliveryOptions
{
    public const string SectionName = "ContentDelivery";

    public string StorageRoot { get; init; } = "Data/content";
    public string PublishKey { get; init; } = "";
    public long MaxArchiveBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int MaxFileCount { get; init; } = 10_000;
    public string[] AllowedChannels { get; init; } = ["development"];
}
