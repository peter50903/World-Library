namespace WorldSimulation.Core.Entity;

/// <summary>
/// Permanent entity identifier. Valid values are 1..ulong.MaxValue; 0 is the invalid sentinel,
/// which is also what <c>default(EntityId)</c> holds. Obtain instances via <see cref="From"/> or an allocator.
/// </summary>
public readonly record struct EntityId : IComparable<EntityId>
{
    private EntityId(ulong value)
    {
        Value = value;
    }

    public ulong Value { get; }

    /// <summary>False for <c>default(EntityId)</c> (the 0 sentinel).</summary>
    public bool IsValid => Value != 0;

    /// <exception cref="ArgumentOutOfRangeException">value is 0.</exception>
    public static EntityId From(ulong value)
        => value == 0
            ? throw new ArgumentOutOfRangeException(nameof(value), value, "EntityId 0 is the invalid sentinel.")
            : new EntityId(value);

    public static bool TryFrom(ulong value, out EntityId id)
    {
        id = value == 0 ? default : new EntityId(value);
        return value != 0;
    }

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
