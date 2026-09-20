namespace AChen.Backend.Api.Features.Social;

public enum FriendRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

public sealed class FriendRequest
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public FriendRequestStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
