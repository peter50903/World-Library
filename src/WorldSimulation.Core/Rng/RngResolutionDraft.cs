namespace WorldSimulation.Core.Rng;

/// <summary>
/// An isolated, discardable RNG trial for a future resolution. It starts from all ten
/// live stream positions but NEVER advances the live RngRegistry. The immutable base
/// and proposed snapshots are for a future Commit Engine to validate atomically.
/// This component itself cannot commit or apply an RNG change.
/// </summary>
public sealed class RngResolutionDraft
{
    private readonly RngRegistry _working;

    private RngResolutionDraft(RngSnapshot baseSnapshot, RngRegistry working)
    {
        BaseSnapshot = baseSnapshot;
        _working = working;
    }

    /// <summary>
    /// The immutable complete RNG snapshot at draft creation, including both resolved
    /// root seeds and the original states and counters of all ten streams.
    /// </summary>
    public RngSnapshot BaseSnapshot { get; }

    /// <summary>
    /// Captures the live registry once and restores an independent in-memory worker
    /// from that exact capture. It does not reset existing draw counters.
    /// </summary>
    public static RngResolutionDraft Begin(RngRegistry liveRegistry)
    {
        ArgumentNullException.ThrowIfNull(liveRegistry);

        var baseSnapshot = liveRegistry.Capture();
        return new RngResolutionDraft(baseSnapshot, RngRegistry.Restore(baseSnapshot));
    }

    /// <summary>
    /// Draws only on the draft's private registry. Existing RNG v1 stream-id,
    /// counter exhaustion and deterministic SplitMix64 semantics apply unchanged.
    /// </summary>
    public ulong NextUInt64(string streamId) => _working.NextUInt64(streamId);

    /// <summary>
    /// Captures a detached candidate result for future Commit validation. This
    /// NEVER applies the candidate to live world RNG state or to history.
    /// </summary>
    public RngSnapshot CaptureProposed() => _working.Capture();
}
