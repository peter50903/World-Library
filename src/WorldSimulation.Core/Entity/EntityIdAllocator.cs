namespace WorldSimulation.Core.Entity;

/// <summary>Immutable in-memory capture of an <see cref="EntityIdAllocator"/>. Not a save format.</summary>
public sealed record EntityIdAllocatorSnapshot(int Version, ulong NextValue, bool Exhausted)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Entity ID Allocator v1: one global, monotonic sequence shared by all entity types. IDs are never reused,
/// recycled or reset; no RNG, GUID or clock is involved. Allocating an ID does not create or commit an entity.
/// </summary>
public sealed class EntityIdAllocator
{
    private ulong _nextValue = 1;
    private bool _exhausted;

    /// <summary>The value the next <see cref="Allocate"/> would issue (stays ulong.MaxValue once exhausted).</summary>
    public ulong NextValue => _nextValue;

    public bool IsExhausted => _exhausted;

    /// <summary>Issues the next ID. ulong.MaxValue may be issued once; afterwards this throws and changes nothing.</summary>
    /// <exception cref="InvalidOperationException">The ID space is exhausted.</exception>
    public EntityId Allocate()
    {
        if (_exhausted)
        {
            throw new InvalidOperationException("The EntityId space is exhausted.");
        }

        var id = EntityId.From(_nextValue);
        if (_nextValue == ulong.MaxValue)
        {
            _exhausted = true;
        }
        else
        {
            _nextValue++;
        }

        return id;
    }

    public EntityIdAllocatorSnapshot Capture()
        => new(EntityIdAllocatorSnapshot.CurrentVersion, _nextValue, _exhausted);

    /// <summary>
    /// Creates a new allocator continuing from the snapshot, independent of the original. Validates everything first.
    /// A snapshot carries no world state: it must not be written back into a still-running world.
    /// </summary>
    /// <exception cref="ArgumentException">Unsupported version, NextValue 0, or Exhausted without NextValue = ulong.MaxValue.</exception>
    public static EntityIdAllocator Restore(EntityIdAllocatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Version != EntityIdAllocatorSnapshot.CurrentVersion)
        {
            throw new ArgumentException($"Unsupported allocator snapshot version {snapshot.Version}.", nameof(snapshot));
        }

        if (snapshot.NextValue == 0)
        {
            throw new ArgumentException("NextValue 0 is invalid.", nameof(snapshot));
        }

        if (snapshot.Exhausted && snapshot.NextValue != ulong.MaxValue)
        {
            throw new ArgumentException("An exhausted snapshot must have NextValue = ulong.MaxValue.", nameof(snapshot));
        }

        return new EntityIdAllocator { _nextValue = snapshot.NextValue, _exhausted = snapshot.Exhausted };
    }
}
