using System.Collections.ObjectModel;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.Commit;

/// <summary>
/// Detached, immutable pre-commit proposal combining provisional entity identities and
/// RNG positions. This is NOT a validated commit package, authoritative world state
/// or a durability boundary; no live state can be written through this object.
/// </summary>
public sealed class EntityRngResolutionCandidate
{
    public EntityRngResolutionCandidate(
        EntityIdAllocatorSnapshot entityBaseline,
        EntityIdAllocatorSnapshot entityProposed,
        IReadOnlyList<EntityIdentity> proposedIdentities,
        RngSnapshot rngBaseline,
        RngSnapshot rngProposed)
    {
        ArgumentNullException.ThrowIfNull(entityBaseline);
        ArgumentNullException.ThrowIfNull(entityProposed);
        ArgumentNullException.ThrowIfNull(proposedIdentities);
        ArgumentNullException.ThrowIfNull(rngBaseline);
        ArgumentNullException.ThrowIfNull(rngProposed);

        EntityBaseline = entityBaseline;
        EntityProposed = entityProposed;
        ProposedIdentities = new ReadOnlyCollection<EntityIdentity>(proposedIdentities.ToList());
        RngBaseline = rngBaseline;
        RngProposed = rngProposed;
    }

    public EntityIdAllocatorSnapshot EntityBaseline { get; }

    public EntityIdAllocatorSnapshot EntityProposed { get; }

    /// <summary>Detached, immutable sequence of provisional, NOT committed identities.</summary>
    public IReadOnlyList<EntityIdentity> ProposedIdentities { get; }

    public RngSnapshot RngBaseline { get; }

    public RngSnapshot RngProposed { get; }
}

/// <summary>
/// Read-only two-domain candidate check. Validates both domains even when the first
/// has a stale baseline, so malformed RNG data cannot be hidden by an Entity conflict.
/// Does not enforce one authoritative world version or make any writes. A real Commit
/// Engine must recheck both current snapshots inside one atomic world transaction.
/// </summary>
public static class EntityRngCandidatePreflight
{
    public static bool IsCompatible(
        EntityIdAllocatorSnapshot currentEntity,
        RngSnapshot currentRng,
        EntityRngResolutionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(currentEntity);
        ArgumentNullException.ThrowIfNull(currentRng);
        ArgumentNullException.ThrowIfNull(candidate);

        var entityCompatible = EntityCandidatePreflight.IsCompatible(
            currentEntity, candidate.EntityBaseline, candidate.EntityProposed,
            candidate.ProposedIdentities);

        // Deliberately evaluated even when entityCompatible == false.
        // Malformed snapshots must throw rather than be masked by a separate conflict.
        var rngCompatible = RngCandidatePreflight.IsCompatible(
            currentRng, candidate.RngBaseline, candidate.RngProposed);

        return entityCompatible && rngCompatible;
    }
}
