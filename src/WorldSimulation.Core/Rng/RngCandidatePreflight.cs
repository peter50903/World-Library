namespace WorldSimulation.Core.Rng;

/// <summary>
/// Pure snapshot-only preflight for a candidate RNG resolution. This is NOT an atomic
/// Commit Engine: a compatible snapshot does not lock live state or authorize any write.
/// A future Commit Engine must recheck the actual live state inside one transaction.
/// </summary>
public static class RngCandidatePreflight
{
    /// <summary>
    /// Returns true when the RNG candidate is based on the current complete RNG state
    /// and none of its ten stream draw counters has moved backward. All three snapshots
    /// must first be valid RNG protocol v1 snapshots; malformed snapshots throw rather
    /// than being misrepresented as an ordinary version conflict.
    /// No input is mutated or consumed.
    /// </summary>
    public static bool IsCompatible(RngSnapshot current, RngSnapshot baseline, RngSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        // Validate EVERY snapshot before comparison (including when another one differs).
        // Restore constructs detached registries; it never changes live RNG or these snapshots.
        _ = RngRegistry.Restore(current);
        _ = RngRegistry.Restore(baseline);
        _ = RngRegistry.Restore(candidate);

        if (!SameRecipe(current, baseline) || !SameRecipe(candidate, baseline))
        {
            return false;
        }

        // RngRegistry.Restore guaranteed exact official stream IDs with no duplicates.
        // Compare by fixed ID, not an external snapshot's incidental order.
        var live = current.Streams.ToDictionary(s => s.StreamId, StringComparer.Ordinal);
        var basis = baseline.Streams.ToDictionary(s => s.StreamId, StringComparer.Ordinal);
        var proposed = candidate.Streams.ToDictionary(s => s.StreamId, StringComparer.Ordinal);

        foreach (var streamId in RngStreams.All)
        {
            var expected = basis[streamId];
            var actual = live[streamId];
            var next = proposed[streamId];

            if (actual.DrawCounter != expected.DrawCounter ||
                actual.State != expected.State ||
                next.DrawCounter < expected.DrawCounter)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameRecipe(RngSnapshot left, RngSnapshot right)
        => left.Version == right.Version &&
           left.WorldSeed == right.WorldSeed &&
           left.HistorySeed == right.HistorySeed;
}
