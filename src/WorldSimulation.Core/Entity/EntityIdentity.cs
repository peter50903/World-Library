using System.Diagnostics.CodeAnalysis;

namespace WorldSimulation.Core.Entity;

/// <summary>
/// Read-only identity of an entity: its permanent ID and an opaque, case-exact, non-empty TypeId.
/// TypeId is not an enum or whitelist; the set of valid types belongs to formal data, not to this contract.
/// </summary>
public sealed record EntityIdentity
{
    public EntityIdentity(EntityId id, string typeId)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("EntityId must be valid (non-zero).", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("TypeId must not be empty.", nameof(typeId));
        }

        Id = id;
        TypeId = typeId;
    }

    public EntityId Id { get; }
    public string TypeId { get; }
}

/// <summary>
/// Read-only identity lookup, to be implemented later by the real Entity Registry / Commit Engine.
/// This module provides no implementation and no way to register, remove or retype entities.
/// </summary>
public interface IEntityIdentityLookup
{
    bool TryResolve(EntityId id, [NotNullWhen(true)] out EntityIdentity? identity);
}
