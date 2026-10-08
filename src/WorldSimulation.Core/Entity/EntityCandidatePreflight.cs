namespace WorldSimulation.Core.Entity;

/// <summary>
/// Pure compatibility preflight for a provisional entity-creation draft.
/// It does NOT commit entities, reserve IDs, update a live allocator or protect against
/// changes after the caller captured current. A future Commit Engine must revalidate
/// inside one atomic transaction before admitting any provisional identity.
/// </summary>
public static class EntityCandidatePreflight
{
    /// <summary>
    /// Validates all allocator snapshots before checking for a stale baseline.
    /// For an unchanged baseline, simulates the proposed identities IN ORDER on a
    /// detached allocator. Only an exact candidate allocator position is compatible.
    /// A true result does not turn provisional IDs into committed world entities.
    /// </summary>
    public static bool IsCompatible(
        EntityIdAllocatorSnapshot current,
        EntityIdAllocatorSnapshot baseline,
        EntityIdAllocatorSnapshot candidate,
        IReadOnlyList<EntityIdentity> proposedIdentities)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(proposedIdentities);

        // Validate all three snapshots, even when another one is already stale.
        // Restore creates detached in-memory allocators and never changes the world.
        _ = EntityIdAllocator.Restore(current);
        _ = EntityIdAllocator.Restore(baseline);
        _ = EntityIdAllocator.Restore(candidate);

        if (current != baseline)
        {
            return false;
        }

        var working = EntityIdAllocator.Restore(baseline);
        foreach (var proposal in proposedIdentities)
        {
            if (proposal is null ||
                !proposal.Id.IsValid ||
                string.IsNullOrWhiteSpace(proposal.TypeId))
            {
                throw new ArgumentException("Proposed identities must contain valid, non-null identities.", nameof(proposedIdentities));
            }

            EntityId expected;
            try
            {
                expected = working.Allocate();
            }
            catch (InvalidOperationException)
            {
                // Too many proposed IDs for the remaining global allocator capacity.
                return false;
            }

            if (proposal.Id != expected)
            {
                // Wrong order, skipped value, duplicate or ID already allocated.
                return false;
            }
        }

        // This also validates Exhausted=true after the one allowed MaxValue issue,
        // and requires an empty proposal to leave the allocator exactly unchanged.
        return working.Capture() == candidate;
    }
}
