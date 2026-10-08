using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WorldSimulation.Core.Rng;

/// <summary>
/// RNG v1 seed derivation. SHA256 over
/// UTF8("WSIM-RNG-v1") || 0x00 || UTF8(purpose) || 0x00 || UInt64LE(parentSeed) || UTF8(namespace);
/// the first 8 digest bytes read as a little-endian ulong.
/// </summary>
internal static class SeedDerivation
{
    private const string Prefix = "WSIM-RNG-v1";

    internal static ulong HistorySeed(ulong worldSeed) => Derive("HISTORY_SEED", worldSeed, string.Empty);

    internal static ulong WorldStream(ulong worldSeed, string streamId) => Derive("WORLD_STREAM", worldSeed, streamId);

    internal static ulong HistoryStream(ulong historySeed, string streamId) => Derive("HISTORY_STREAM", historySeed, streamId);

    internal static ulong Derive(string purpose, ulong parentSeed, string ns)
    {
        var prefix = Encoding.UTF8.GetBytes(Prefix);
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var nsBytes = Encoding.UTF8.GetBytes(ns);

        var input = new byte[prefix.Length + 1 + purposeBytes.Length + 1 + 8 + nsBytes.Length];
        var offset = 0;
        prefix.CopyTo(input, offset); offset += prefix.Length;
        input[offset++] = 0x00;
        purposeBytes.CopyTo(input, offset); offset += purposeBytes.Length;
        input[offset++] = 0x00;
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(offset, 8), parentSeed); offset += 8;
        nsBytes.CopyTo(input, offset);

        var digest = SHA256.HashData(input);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(0, 8));
    }
}
