using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.Rng;

/// <summary>
/// Entity-local indexed RNG protocol v1. Every result is a pure function of the two resolved
/// world roots, a fixed RNG stream, a permanent EntityId, an opaque local namespace and a draw index.
/// It never reads or advances the parent registry's mutable stream state.
/// </summary>
public sealed class EntityLocalRandom
{
    private const string ProtocolPrefix = "WSIM-ENTITY-LOCAL-v1";
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    private EntityLocalRandom(ulong seed) => Seed = seed;

    /// <summary>Immutable local seed derived from an initial parent stream seed, never its current state.</summary>
    public ulong Seed { get; }

    /// <summary>
    /// Derivation preimage:
    /// UTF8("WSIM-ENTITY-LOCAL-v1") || 0x00 || UInt64LE(parentStreamSeed) ||
    /// UInt32LE(namespace UTF8 byte length) || namespace UTF8 bytes || UInt64LE(entityId).
    /// The first eight SHA256 bytes interpreted as UInt64LE become the local seed.
    /// </summary>
    public static EntityLocalRandom Create(
        RngRegistry registry,
        EntityId entityId,
        string streamId,
        string localNamespace)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!entityId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId), "EntityId 0 is invalid.");
        }

        if (string.IsNullOrWhiteSpace(localNamespace))
        {
            throw new ArgumentException("The local namespace must not be blank.", nameof(localNamespace));
        }

        ulong parentSeed;
        if (streamId is not null && RngStreams.WorldStreams.Contains(streamId, StringComparer.Ordinal))
        {
            parentSeed = SeedDerivation.WorldStream(registry.WorldSeed, streamId);
        }
        else if (streamId is not null && RngStreams.HistoryStreams.Contains(streamId, StringComparer.Ordinal))
        {
            parentSeed = SeedDerivation.HistoryStream(registry.HistorySeed, streamId);
        }
        else
        {
            throw new ArgumentException($"Unknown RNG stream '{streamId}'.", nameof(streamId));
        }

        var prefix = Encoding.UTF8.GetBytes(ProtocolPrefix);
        var namespaceBytes = Encoding.UTF8.GetBytes(localNamespace);
        var data = new byte[prefix.Length + 1 + 8 + 4 + namespaceBytes.Length + 8];
        var offset = 0;

        prefix.CopyTo(data, offset);
        offset += prefix.Length;
        data[offset++] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), parentSeed);
        offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), checked((uint)namespaceBytes.Length));
        offset += 4;
        namespaceBytes.CopyTo(data, offset);
        offset += namespaceBytes.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), entityId.Value);

        var digest = SHA256.HashData(data);
        return new EntityLocalRandom(BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(0, 8)));
    }

    /// <summary>
    /// Pure zero-based SplitMix64 output at the specified index (including ulong.MaxValue).
    /// Arithmetic is unsigned modulo 2^64. This has no mutable counter and performs no world writes.
    /// </summary>
    public ulong GetUInt64(ulong drawIndex)
    {
        var state = unchecked(Seed + Gamma * unchecked(drawIndex + 1UL));
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
