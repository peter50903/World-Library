using System.Collections.ObjectModel;

namespace WorldSimulation.Core.Entity;

/// <summary>
/// An isolated, discardable proposal for new entity identities. Provisional IDs are
/// allocated only by a private copy of the global allocator: they are NOT existing
/// world entities, committed IDs, or visible through IEntityIdentityLookup.
/// A future atomic Commit Engine must detect competing drafts before adoption.
/// </summary>
public sealed class EntityCreationDraft
{
    private readonly EntityIdAllocator _working;
    private readonly List<EntityIdentity> _proposals = new();

    private EntityCreationDraft(EntityIdAllocatorSnapshot baseSnapshot, EntityIdAllocator working)
    {
        BaseSnapshot = baseSnapshot;
        _working = working;
    }

    /// <summary>The immutable allocator position at draft creation, not a Save format.</summary>
    public EntityIdAllocatorSnapshot BaseSnapshot { get; }

    /// <summary>
    /// Captures the live allocator's position once and continues privately from it.
    /// Never calls Allocate on the original allocator.
    /// </summary>
    public static EntityCreationDraft Begin(EntityIdAllocator liveAllocator)
    {
        ArgumentNullException.ThrowIfNull(liveAllocator);
        var baseline = liveAllocator.Capture();
        return new EntityCreationDraft(baseline, EntityIdAllocator.Restore(baseline));
    }

    /// <summary>
    /// Creates a candidate identity, not a canonical world entity. Validate the opaque
    /// case-exact TypeId before allocating so invalid input consumes no candidate ID.
    /// Exhaustion leaves the candidate position and proposals unchanged.
    /// </summary>
    public EntityIdentity Propose(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("TypeId must not be blank.", nameof(typeId));
        }

        var identity = new EntityIdentity(_working.Allocate(), typeId);
        _proposals.Add(identity);
        return identity;
    }

    /// <summary>Captures only the proposed allocator position, without modifying live state.</summary>
    public EntityIdAllocatorSnapshot CaptureProposed() => _working.Capture();

    /// <summary>
    /// Detached, immutable view of the draft's proposals in original order.
    /// Previously captured views never change when more proposals are added.
    /// </summary>
    public IReadOnlyList<EntityIdentity> ProposedIdentities()
        => new ReadOnlyCollection<EntityIdentity>(_proposals.ToList());
}
