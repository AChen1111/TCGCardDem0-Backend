namespace AChen.Backend.Api.Features.Social;

public sealed class Friendship
{
    public Guid UserIdA { get; set; }
    public Guid UserIdB { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
