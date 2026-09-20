using AChen.Backend.Api.Features.Players;

namespace AChen.Backend.Api.Features.Social;

public sealed class Gift
{
    public Guid Id { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid? SourceUserId { get; set; }
    public long Gold { get; set; }
    public string? TitleKey { get; set; }
    public List<OwnedCard> Cards { get; set; } = [];
    public bool Claimed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
}
