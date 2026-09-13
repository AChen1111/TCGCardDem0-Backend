using System.Security.Cryptography;

namespace AChen.Backend.Api.Features.Gacha;

public interface IGachaRandom
{
    int Next(int exclusiveUpperBound);
}

public sealed class CryptoGachaRandom : IGachaRandom
{
    public int Next(int exclusiveUpperBound) => RandomNumberGenerator.GetInt32(exclusiveUpperBound);
}
