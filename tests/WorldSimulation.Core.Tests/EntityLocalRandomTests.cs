using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class EntityLocalRandomTests
{
    private static EntityLocalRandom Local(
        ulong worldSeed = 42,
        ulong? historySeed = null,
        ulong entityId = 7,
        string streamId = RngStreams.Geography,
        string localNamespace = "TRAITS")
        => EntityLocalRandom.Create(
            RngRegistry.Create(worldSeed, historySeed),
            EntityId.From(entityId),
            streamId,
            localNamespace);

    // Golden vectors independently computed with Python hashlib.sha256 and a SplitMix64 reference,
    // not copied from the C# implementation under test.
    [Fact]
    public void Geography_KnownSeedAndIndexedVectors_AreStable()
    {
        var local = Local();
        Assert.Equal(0x31a83070d9802ac4UL, local.Seed);
        Assert.Equal(0x239f273726ebcedfUL, local.GetUInt64(0));
        Assert.Equal(0x1a2d248266175367UL, local.GetUInt64(1));
        Assert.Equal(0xabafb98722ca4ac9UL, local.GetUInt64(ulong.MaxValue));
    }

    [Fact]
    public void Character_KnownSeedAndIndexedVectors_AreStable()
    {
        var local = Local(streamId: RngStreams.Character);
        Assert.Equal(0xd95c7f82f08112a2UL, local.Seed);
        Assert.Equal(0x8173295e8fbe4ac2UL, local.GetUInt64(0));
        Assert.Equal(0xb2a6cba3d7f44f94UL, local.GetUInt64(1));
    }

    [Fact]
    public void SameInputs_ProduceSameValues_InAnyReadOrder()
    {
        var a = Local();
        var b = Local();
        var indices = new[] { 91UL, 0UL, 300UL, 1UL, ulong.MaxValue, 91UL };
        var aValues = indices.Select(a.GetUInt64).ToArray();
        var bValues = indices.Select(b.GetUInt64).ToArray();
        Assert.Equal(aValues, bValues);
        Assert.Equal(a.GetUInt64(91), a.GetUInt64(91));
        Assert.NotEqual(a.GetUInt64(90), a.GetUInt64(91));
    }

    [Fact]
    public void DifferentEntityIds_ProduceDistinctLocalSequences()
    {
        var first = Local(entityId: 7);
        var second = Local(entityId: 8);
        Assert.NotEqual(first.Seed, second.Seed);
        Assert.NotEqual(first.GetUInt64(0), second.GetUInt64(0));
    }

    [Fact]
    public void DifferentLocalNamespaces_AreIndependentAndCaseExact()
    {
        var first = Local(localNamespace: "TRAITS");
        var second = Local(localNamespace: "traits");
        var third = Local(localNamespace: "TRAITS ");
        Assert.NotEqual(first.Seed, second.Seed);
        Assert.NotEqual(first.Seed, third.Seed);
        Assert.NotEqual(second.Seed, third.Seed);
    }

    [Fact]
    public void UnicodeNamespace_UsesUtf8BytesWithoutNormalizing()
    {
        var composed = Local(localNamespace: "\u00e9");
        var decomposed = Local(localNamespace: "e\u0301");
        Assert.NotEqual(composed.Seed, decomposed.Seed);
        Assert.Equal(composed.Seed, Local(localNamespace: "\u00e9").Seed);
    }

    [Fact]
    public void AllTenOfficialStreams_UseDistinctLocalSeeds()
    {
        var ids = RngStreams.All;
        Assert.Equal(10, ids.Count);
        var seeds = ids.Select(id => Local(streamId: id).Seed).ToArray();
        Assert.Equal(ids.Count, seeds.Distinct().Count());
    }

    [Fact]
    public void ChangingOnlyHistorySeed_LeavesWorldLocalsIntact()
    {
        var firstWorld = Local(historySeed: 100, streamId: RngStreams.Geography);
        var secondWorld = Local(historySeed: 101, streamId: RngStreams.Geography);
        Assert.Equal(firstWorld.Seed, secondWorld.Seed);
        Assert.Equal(firstWorld.GetUInt64(35), secondWorld.GetUInt64(35));

        var firstHistory = Local(historySeed: 100, streamId: RngStreams.Character);
        var secondHistory = Local(historySeed: 101, streamId: RngStreams.Character);
        Assert.NotEqual(firstHistory.Seed, secondHistory.Seed);
        Assert.NotEqual(firstHistory.GetUInt64(35), secondHistory.GetUInt64(35));
    }

    [Fact]
    public void ChangingWorldSeed_ChangesWorldLocalSeed()
    {
        Assert.NotEqual(Local(worldSeed: 42).Seed, Local(worldSeed: 43).Seed);
    }

    [Fact]
    public void ImplicitAndExplicitEquivalentHistorySeed_ProduceSameHistoryLocal()
    {
        var implicitHistory = RngRegistry.Create(42).HistorySeed;
        Assert.Equal(
            Local(streamId: RngStreams.Event).Seed,
            Local(historySeed: implicitHistory, streamId: RngStreams.Event).Seed);
    }

    [Fact]
    public void GlobalStreamDraws_DoNotAffectLocalOutputsOrCounters()
    {
        var registry = RngRegistry.Create(42);
        var before = registry.Capture();
        var local = EntityLocalRandom.Create(registry, EntityId.From(7), RngStreams.Event, "TRAITS");
        var first = local.GetUInt64(0);
        var afterCreate = registry.Capture();

        Assert.Equal(before.Streams, afterCreate.Streams);
        Assert.Equal(0UL, registry.GetDrawCounter(RngStreams.Event));

        for (var i = 0; i < 1000; i++)
        {
            registry.NextUInt64(RngStreams.Event);
        }

        var recreated = EntityLocalRandom.Create(registry, EntityId.From(7), RngStreams.Event, "TRAITS");
        Assert.Equal(local.Seed, recreated.Seed);
        Assert.Equal(first, recreated.GetUInt64(0));
        Assert.Equal(1000UL, registry.GetDrawCounter(RngStreams.Event));
    }

    [Fact]
    public void LocalReads_NeverConsumeAnyParentStream()
    {
        var registry = RngRegistry.Create(42);
        var before = registry.Capture();
        var local = EntityLocalRandom.Create(registry, EntityId.From(9), RngStreams.Combat, "ACTION");
        for (ulong i = 0; i < 64; i++)
        {
            local.GetUInt64(i);
        }
        local.GetUInt64(ulong.MaxValue);
        var after = registry.Capture();
        Assert.Equal(before.Streams, after.Streams);
        Assert.All(RngStreams.All, id => Assert.Equal(0UL, registry.GetDrawCounter(id)));
    }

    [Fact]
    public void NullRegistry_AndDefaultEntityId_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EntityLocalRandom.Create(null!, EntityId.From(1), RngStreams.Event, "ITEM"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EntityLocalRandom.Create(RngRegistry.Create(42), default, RngStreams.Event, "ITEM"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void EmptyNamespace_IsRejected(string? localNamespace)
    {
        Assert.Throws<ArgumentException>(() =>
            EntityLocalRandom.Create(RngRegistry.Create(42), EntityId.From(1), RngStreams.Event, localNamespace!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("event")]
    [InlineData(" Event")]
    [InlineData("UNKNOWN")]
    public void UnknownOrIncorrectlyCasedStream_IsRejected(string? streamId)
    {
        Assert.Throws<ArgumentException>(() =>
            EntityLocalRandom.Create(RngRegistry.Create(42), EntityId.From(1), streamId!, "ITEM"));
    }
}
