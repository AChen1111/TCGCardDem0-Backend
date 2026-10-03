using AChen.Duel.Core;
using System.Threading.Channels;

namespace AChen.Backend.Api.Features.Duels;

public sealed record DuelRoomPlayer(Guid UserId, int Seat, bool Ready, bool HasDeck);
public sealed record DuelRoomView(Guid Id, string Code, string Status, int LocalSeat,
    long Sequence, IReadOnlyList<DuelRoomPlayer> Players, DuelDeckRequest? Deck, DuelSeatSnapshot? Duel,
    IReadOnlyList<double> RemainingSeconds, bool Paused)
{
    public int ProtocolVersion { get; init; } = DuelRulePackage.CurrentProtocolVersion;
    public string RuleVersion { get; init; } = DuelRulePackage.CurrentRuleVersion;
    public string RulePackageHash { get; init; } = "";
}
public sealed record DuelDeckRequest(string[] MainDeck, string[] ExtraDeck);
public sealed record DuelInputRequest(Guid RequestId, SeatInput Input);
public sealed record DuelCommandReceipt(Guid RequestId, bool Accepted, string Error, long Sequence, long Revision);
public sealed record DuelNamePage(long Revision, int Offset, int TotalCount, IReadOnlyList<CardNameDefinition> Items);
public sealed record DuelRoomNotice(DuelRoomView Room, DuelCommandReceipt? Result = null)
{
    public IReadOnlyList<ProjectedDuelEvent> Events { get; init; } = Array.Empty<ProjectedDuelEvent>();
}

public sealed class DuelRoomConnection
{
    readonly CancellationTokenSource stopped = new();
    bool isStopped;
    internal Channel<DuelRoomNotice> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<DuelRoomNotice>(
        new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    public Guid Id { get; } = Guid.NewGuid();
    public ChannelReader<DuelRoomNotice> Updates => Channel.Reader;
    internal CancellationToken Stopped => stopped.Token;
    internal void Stop()
    {
        if (isStopped) return;
        isStopped = true;
        Channel.Writer.TryComplete();
        stopped.Cancel();
    }
}
