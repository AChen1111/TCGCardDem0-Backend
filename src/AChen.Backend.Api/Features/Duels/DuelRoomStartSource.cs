using System.Security.Cryptography;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Features.Duels;

public interface IDuelRoomStartSource
{
    DuelStartRecord Create(string[][] mainDecks, string[][] extraDecks);
}

public sealed class DuelRoomStartSource : IDuelRoomStartSource
{
    public DuelStartRecord Create(string[][] mainDecks, string[][] extraDecks) => new()
    {
        MainDecks = mainDecks, ExtraDecks = extraDecks,
        Seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))),
        FirstPlayer = RandomNumberGenerator.GetInt32(2)
    };
}
