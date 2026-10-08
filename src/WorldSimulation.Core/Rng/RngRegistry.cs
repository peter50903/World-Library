namespace WorldSimulation.Core.Rng;

/// <summary>
/// Deterministic RNG Registry v1: two root seeds, 10 independent SplitMix64 streams, in-memory capture/restore.
/// Not transactional: draws are not tied to any Commit, and no distributions or game rules live here.
/// </summary>
public sealed class RngRegistry
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    private sealed class Stream
    {
        public ulong Initial;
        public ulong State;
        public ulong Counter;
    }

    private readonly Dictionary<string, Stream> _streams = new(StringComparer.Ordinal);

    private RngRegistry(ulong worldSeed, ulong historySeed)
    {
        WorldSeed = worldSeed;
        HistorySeed = historySeed;

        foreach (var id in RngStreams.WorldStreams)
        {
            AddStream(id, SeedDerivation.WorldStream(worldSeed, id));
        }

        foreach (var id in RngStreams.HistoryStreams)
        {
            AddStream(id, SeedDerivation.HistoryStream(historySeed, id));
        }
    }

    public ulong WorldSeed { get; }

    /// <summary>The resolved HistorySeed: the explicit one, or derived from WorldSeed when none was given.</summary>
    public ulong HistorySeed { get; }

    public static RngRegistry Create(ulong worldSeed, ulong? historySeed = null)
        => new(worldSeed, historySeed ?? SeedDerivation.HistorySeed(worldSeed));

    /// <summary>Next raw 64-bit value of the stream; consumes only that stream.</summary>
    /// <exception cref="ArgumentException">Unknown stream ID.</exception>
    /// <exception cref="InvalidOperationException">The stream's draw counter is exhausted; state is left unchanged.</exception>
    public ulong NextUInt64(string streamId)
    {
        var stream = Find(streamId);
        if (stream.Counter == ulong.MaxValue)
        {
            throw new InvalidOperationException($"Draw counter of stream '{streamId}' is exhausted.");
        }

        var state = unchecked(stream.State + Gamma);
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        var result = z ^ (z >> 31);

        stream.State = state;
        stream.Counter++;
        return result;
    }

    /// <summary>Number of draws made so far on the stream.</summary>
    public ulong GetDrawCounter(string streamId) => Find(streamId).Counter;

    public RngSnapshot Capture()
        => new(
            RngSnapshot.CurrentVersion,
            WorldSeed,
            HistorySeed,
            RngStreams.All.Select(id => new RngStreamState(id, _streams[id].State, _streams[id].Counter)));

    /// <summary>
    /// Creates a registry continuing exactly where the snapshot was taken. Rejects (without creating anything) a wrong
    /// version, a missing, extra, unknown or duplicate stream, or a State inconsistent with seeds and DrawCounter.
    /// </summary>
    /// <exception cref="ArgumentException">The snapshot is invalid.</exception>
    public static RngRegistry Restore(RngSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Version != RngSnapshot.CurrentVersion)
        {
            throw new ArgumentException($"Unsupported RNG snapshot version {snapshot.Version}.", nameof(snapshot));
        }

        var provided = new Dictionary<string, RngStreamState>(StringComparer.Ordinal);
        foreach (var state in snapshot.Streams)
        {
            if (state is null || !provided.TryAdd(state.StreamId ?? string.Empty, state))
            {
                throw new ArgumentException("Snapshot has a null or duplicate stream.", nameof(snapshot));
            }
        }

        if (provided.Count != RngStreams.All.Count || RngStreams.All.Any(id => !provided.ContainsKey(id)))
        {
            throw new ArgumentException("Snapshot must contain exactly the 10 RNG v1 streams.", nameof(snapshot));
        }

        var registry = new RngRegistry(snapshot.WorldSeed, snapshot.HistorySeed);
        foreach (var id in RngStreams.All)
        {
            var target = registry._streams[id];
            var saved = provided[id];
            if (saved.State != unchecked(target.Initial + Gamma * saved.DrawCounter))
            {
                throw new ArgumentException($"Stream '{id}' State is inconsistent with its seed and DrawCounter.", nameof(snapshot));
            }

            target.State = saved.State;
            target.Counter = saved.DrawCounter;
        }

        return registry;
    }

    private void AddStream(string id, ulong seed)
        => _streams.Add(id, new Stream { Initial = seed, State = seed, Counter = 0 });

    private Stream Find(string streamId)
    {
        if (streamId is null || !_streams.TryGetValue(streamId, out var stream))
        {
            throw new ArgumentException($"Unknown RNG stream '{streamId}'.", nameof(streamId));
        }

        return stream;
    }
}
