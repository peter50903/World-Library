using System.Collections.ObjectModel;

namespace WorldSimulation.Core.Rng;

/// <summary>Position of one stream: SplitMix64 state and the number of draws made.</summary>
public sealed record RngStreamState(string StreamId, ulong State, ulong DrawCounter);

/// <summary>
/// Immutable in-memory capture of an <see cref="RngRegistry"/> (protocol version, both resolved root seeds,
/// and every stream's state). Not a save format: no file or JSON representation.
/// </summary>
public sealed class RngSnapshot
{
    public const int CurrentVersion = 1;

    public RngSnapshot(int version, ulong worldSeed, ulong historySeed, IEnumerable<RngStreamState> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        Version = version;
        WorldSeed = worldSeed;
        HistorySeed = historySeed;
        Streams = new ReadOnlyCollection<RngStreamState>(streams.ToList());
    }

    public int Version { get; }
    public ulong WorldSeed { get; }

    /// <summary>The resolved HistorySeed (explicit or derived).</summary>
    public ulong HistorySeed { get; }

    public IReadOnlyList<RngStreamState> Streams { get; }
}
