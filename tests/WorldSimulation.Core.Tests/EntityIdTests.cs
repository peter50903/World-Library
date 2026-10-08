using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using WorldSimulation.Core.Entity;
using Xunit;

namespace WorldSimulation.Core.Tests;

public class EntityIdTests
{
    [Fact]
    public void NewAllocator_StartsAtOne()
    {
        var allocator = new EntityIdAllocator();
        Assert.Equal(1UL, allocator.NextValue);
        Assert.False(allocator.IsExhausted);
        Assert.Equal(1UL, allocator.Allocate().Value);
    }

    [Fact]
    public void Allocate_IsMonotonic_NeverReusesAndNeverIssuesZero()
    {
        var allocator = new EntityIdAllocator();
        var issued = Enumerable.Range(0, 1000).Select(_ => allocator.Allocate().Value).ToList();

        Assert.Equal(Enumerable.Range(1, 1000).Select(i => (ulong)i), issued);
        Assert.Equal(1001UL, allocator.NextValue);
    }

    [Fact]
    public void AllEntityTypes_ShareOneIdSpace()
    {
        var allocator = new EntityIdAllocator();
        var types = new[] { "TYPE_A", "TYPE_B", "TYPE_A", "TYPE_C", "TYPE_B", "TYPE_A" };
        var identities = types.Select(t => new EntityIdentity(allocator.Allocate(), t)).ToList();

        Assert.Equal(new ulong[] { 1, 2, 3, 4, 5, 6 }, identities.Select(i => i.Id.Value));
        Assert.Equal(6, identities.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void ZeroIsInvalid_IncludingDefaultValue()
    {
        Assert.False(default(EntityId).IsValid);
        Assert.Equal(0UL, default(EntityId).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => EntityId.From(0));
        Assert.False(EntityId.TryFrom(0, out var zero));
        Assert.False(zero.IsValid);
        Assert.True(EntityId.TryFrom(7, out var seven));
        Assert.Equal(7UL, seven.Value);
        Assert.Equal(EntityId.From(ulong.MaxValue), EntityId.From(ulong.MaxValue));
        Assert.True(EntityId.From(1).CompareTo(EntityId.From(2)) < 0);

        Assert.Throws<ArgumentException>(() => new EntityIdentity(default, "TYPE_A"));
    }

    [Fact]
    public void MaxValue_IsIssuedOnce_ThenRejected_WithoutChangingState()
    {
        var allocator = EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false));

        Assert.Equal(ulong.MaxValue, allocator.Allocate().Value);
        Assert.True(allocator.IsExhausted);
        Assert.Equal(ulong.MaxValue, allocator.NextValue);

        var before = allocator.Capture();
        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
        Assert.Equal(before, allocator.Capture());
        Assert.Equal(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true), allocator.Capture());
    }

    [Fact]
    public void NearMax_CountsDownToExhaustionWithoutWrapping()
    {
        var allocator = EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, ulong.MaxValue - 1, false));
        Assert.Equal(ulong.MaxValue - 1, allocator.Allocate().Value);
        Assert.False(allocator.IsExhausted);
        Assert.Equal(ulong.MaxValue, allocator.Allocate().Value);
        Assert.True(allocator.IsExhausted);
        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
    }

    [Fact]
    public void Snapshot_InitialMidAndExhausted_ContinueIdentically()
    {
        var fresh = new EntityIdAllocator();
        var freshRestored = EntityIdAllocator.Restore(fresh.Capture());
        Assert.Equal(fresh.Capture(), new EntityIdAllocatorSnapshot(1, 1, false));
        Assert.Equal(fresh.Allocate(), freshRestored.Allocate());

        var mid = new EntityIdAllocator();
        for (var i = 0; i < 17; i++) mid.Allocate();
        var midRestored = EntityIdAllocator.Restore(mid.Capture());
        Assert.Equal(
            Enumerable.Range(0, 5).Select(_ => mid.Allocate()).ToList(),
            Enumerable.Range(0, 5).Select(_ => midRestored.Allocate()).ToList());

        var exhausted = EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true));
        Assert.True(exhausted.IsExhausted);
        Assert.Throws<InvalidOperationException>(() => exhausted.Allocate());
        var exhaustedAgain = EntityIdAllocator.Restore(exhausted.Capture());
        Assert.True(exhaustedAgain.IsExhausted);
        Assert.Throws<InvalidOperationException>(() => exhaustedAgain.Allocate());
    }

    [Fact]
    public void Snapshot_DoesNotChangeLiveAllocator_AndRestoredIsIndependent()
    {
        var allocator = new EntityIdAllocator();
        allocator.Allocate();
        allocator.Allocate();

        var snapshot = allocator.Capture();
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 3, false), snapshot);
        Assert.Equal(3UL, allocator.NextValue);

        allocator.Allocate();
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 3, false), snapshot);

        var restored = EntityIdAllocator.Restore(snapshot);
        Assert.Equal(3UL, restored.Allocate().Value);
        Assert.Equal(4UL, restored.Allocate().Value);
        Assert.Equal(4UL, allocator.NextValue);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 3, false), snapshot);
    }

    [Fact]
    public void Restore_RejectsNullWrongVersionZeroAndInconsistentExhausted()
    {
        Assert.Throws<ArgumentNullException>(() => EntityIdAllocator.Restore(null!));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(2, 5, false)));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(0, 5, false)));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, 0, false)));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, 0, true)));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, 5, true)));
        Assert.Throws<ArgumentException>(() => EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, ulong.MaxValue - 1, true)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EntityIdentity_RejectsEmptyTypeId(string? typeId)
    {
        Assert.Throws<ArgumentException>(() => new EntityIdentity(EntityId.From(1), typeId!));
    }

    [Fact]
    public void EntityIdentity_KeepsTypeIdExactAndOpaque()
    {
        var a = new EntityIdentity(EntityId.From(5), "Some_Type");
        var b = new EntityIdentity(EntityId.From(5), "some_type");

        Assert.Equal("Some_Type", a.TypeId);
        Assert.NotEqual(a, b);
        Assert.Equal(a, new EntityIdentity(EntityId.From(5), "Some_Type"));
    }

    [Fact]
    public void LookupContract_IsReadOnly_AndUsableThroughAFake()
    {
        var members = typeof(IEntityIdentityLookup).GetMembers(BindingFlags.Public | BindingFlags.Instance);
        var method = Assert.Single(members.OfType<MethodInfo>());
        Assert.Equal("TryResolve", method.Name);
        Assert.Empty(members.OfType<PropertyInfo>());

        Assert.All(typeof(EntityIdentity).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(EntityId).GetProperties(), p => Assert.Null(p.SetMethod));

        var known = new EntityIdentity(EntityId.From(3), "TYPE_A");
        IEntityIdentityLookup lookup = new FakeLookup(known);
        Assert.True(lookup.TryResolve(EntityId.From(3), out var found));
        Assert.Equal(known, found);
        Assert.False(lookup.TryResolve(EntityId.From(4), out var missing));
        Assert.Null(missing);
        Assert.False(lookup.TryResolve(default, out _));
    }

    private sealed class FakeLookup : IEntityIdentityLookup
    {
        private readonly EntityIdentity _known;

        public FakeLookup(EntityIdentity known) => _known = known;

        public bool TryResolve(EntityId id, [NotNullWhen(true)] out EntityIdentity? identity)
        {
            identity = id == _known.Id ? _known : null;
            return identity is not null;
        }
    }
}
