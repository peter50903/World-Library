using WorldSimulation.Core.Rng;
using Xunit;

namespace WorldSimulation.Core.Tests;

// Known vectors were computed independently of the C# code (Python hashlib.sha256 + a SplitMix64 reference
// that reproduces the published seed-0 outputs e220a8397b1dcdaf, 6e789e6aa1b965f4, 06c45d188009454f).
public class RngRegistryTests
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong DerivedHistory42 = 0x5101f03c36e33c06UL;
    private const ulong DerivedHistory0 = 0x43a0e157914282c2UL;

    private static List<ulong> Draw(RngRegistry registry, string stream, int count)
        => Enumerable.Range(0, count).Select(_ => registry.NextUInt64(stream)).ToList();

    private static RngSnapshot With(RngSnapshot s, IEnumerable<RngStreamState> streams, int? version = null)
        => new(version ?? s.Version, s.WorldSeed, s.HistorySeed, streams);

    [Fact]
    public void DerivedHistorySeed_MatchesKnownVectors()
    {
        Assert.Equal(DerivedHistory42, RngRegistry.Create(42).HistorySeed);
        Assert.Equal(DerivedHistory0, RngRegistry.Create(0).HistorySeed);
        Assert.Equal(42UL, RngRegistry.Create(42).WorldSeed);
    }

    [Theory]
    [InlineData("GEOGRAPHY", 0x93b1200127b9c725UL, 0xad444541d7f34de4UL)]
    [InlineData("RESOURCE", 0x2c1d23d7c1eb1e42UL, 0x70819685c4e374b2UL)]
    [InlineData("ECOLOGY_INIT", 0x804a8895d4c009d3UL, 0x06b5642396ded1cbUL)]
    [InlineData("CHARACTER", 0xa79bb01662b6eba4UL, 0x3b3ce953f026f220UL)]
    [InlineData("POP", 0x892c884aa4144c0cUL, 0xa70253095b3c617fUL)]
    [InlineData("EVENT", 0xdb70ce4c80e0f05cUL, 0xab5e0f11d357ac3aUL)]
    [InlineData("COMBAT", 0xed42f7a11d2e0b15UL, 0x118fbcc96a2a8ddfUL)]
    [InlineData("RELIGION", 0xed543c1165c2864dUL, 0x8ba398afedf5553dUL)]
    [InlineData("DUNGEON", 0xfa1a2db90ce96327UL, 0x945bb4bff7cf55daUL)]
    [InlineData("THREAT", 0x3a93dcf574e8ab65UL, 0xcaaa1d2a83f91ee3UL)]
    public void WorldSeed42_FirstTwoDraws_MatchKnownVectors(string stream, ulong first, ulong second)
    {
        var registry = RngRegistry.Create(42);
        Assert.Equal(first, registry.NextUInt64(stream));
        Assert.Equal(second, registry.NextUInt64(stream));
    }

    [Fact]
    public void WorldSeed0_WorldAndHistoryStreams_MatchKnownVectors()
    {
        var registry = RngRegistry.Create(0);
        Assert.Equal(
            new[] { 0x1678e023e2f301d2UL, 0xf5f722dbc763c1f2UL, 0xa0ed3c1939bd1e5cUL },
            Draw(registry, "GEOGRAPHY", 3));
        Assert.Equal(new[] { 0x5f9c20e3a2a531b6UL, 0x351b41e2c60145eaUL }, Draw(registry, "EVENT", 2));
    }

    [Fact]
    public void SameSeeds_TwoRegistries_ProduceIdenticalSequences()
    {
        var a = RngRegistry.Create(12345);
        var b = RngRegistry.Create(12345);
        foreach (var id in RngStreams.All)
        {
            Assert.Equal(Draw(a, id, 50), Draw(b, id, 50));
        }
    }

    [Fact]
    public void ExplicitHistorySeed_IsReported_AndWorldStreamsAreUnchanged()
    {
        var explicitSeed = RngRegistry.Create(42, DerivedHistory0);
        Assert.Equal(DerivedHistory0, explicitSeed.HistorySeed);

        // World streams ignore HistorySeed; history streams now follow it (vector from WorldSeed 0's derived seed).
        Assert.Equal(0x93b1200127b9c725UL, explicitSeed.NextUInt64("GEOGRAPHY"));
        Assert.Equal(
            new[] { 0x5f9c20e3a2a531b6UL, 0x351b41e2c60145eaUL },
            Draw(explicitSeed, "EVENT", 2));
    }

    [Fact]
    public void ExplicitHistorySeedEqualToDerived_BehavesLikeUnspecified()
    {
        var implicitSeed = RngRegistry.Create(42);
        var explicitSeed = RngRegistry.Create(42, DerivedHistory42);
        foreach (var id in RngStreams.All)
        {
            Assert.Equal(Draw(implicitSeed, id, 5), Draw(explicitSeed, id, 5));
        }
    }

    [Fact]
    public void ChangingWorldSeed_ChangesAllStreams()
    {
        var a = RngRegistry.Create(1);
        var b = RngRegistry.Create(2);
        foreach (var id in RngStreams.All)
        {
            Assert.NotEqual(a.NextUInt64(id), b.NextUInt64(id));
        }
    }

    [Fact]
    public void ChangingOnlyHistorySeed_KeepsWorldStreams_ChangesHistoryStreams()
    {
        var a = RngRegistry.Create(7, 100);
        var b = RngRegistry.Create(7, 101);
        foreach (var id in RngStreams.WorldStreams)
        {
            Assert.Equal(Draw(a, id, 5), Draw(b, id, 5));
        }
        foreach (var id in RngStreams.HistoryStreams)
        {
            Assert.NotEqual(a.NextUInt64(id), b.NextUInt64(id));
        }
    }

    [Fact]
    public void Streams_AreIndependent()
    {
        var registry = RngRegistry.Create(42);
        Draw(registry, "EVENT", 1000);

        Assert.Equal(1000UL, registry.GetDrawCounter("EVENT"));
        Assert.Equal(0UL, registry.GetDrawCounter("COMBAT"));
        Assert.Equal(0xed42f7a11d2e0b15UL, registry.NextUInt64("COMBAT"));
        Assert.Equal(0x93b1200127b9c725UL, registry.NextUInt64("GEOGRAPHY"));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("Event")]
    [InlineData("UNKNOWN")]
    [InlineData(" EVENT")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownStream_IsRejected(string? stream)
    {
        var registry = RngRegistry.Create(1);
        Assert.Throws<ArgumentException>(() => registry.NextUInt64(stream!));
        Assert.Throws<ArgumentException>(() => registry.GetDrawCounter(stream!));
    }

    [Fact]
    public void Snapshot_ZeroDraws_RestoresToIdenticalSequences()
    {
        var original = RngRegistry.Create(99);
        var restored = RngRegistry.Restore(original.Capture());
        foreach (var id in RngStreams.All)
        {
            Assert.Equal(Draw(original, id, 10), Draw(restored, id, 10));
        }
    }

    [Fact]
    public void Snapshot_MidStream_ContinuesExactlyWhereItStopped()
    {
        var original = RngRegistry.Create(99, 5);
        var step = 1;
        foreach (var id in RngStreams.All)
        {
            Draw(original, id, step++ * 3);
        }

        var snapshot = original.Capture();
        Assert.Equal(1, snapshot.Version);
        Assert.Equal(99UL, snapshot.WorldSeed);
        Assert.Equal(5UL, snapshot.HistorySeed);
        Assert.Equal(10, snapshot.Streams.Count);

        var restored = RngRegistry.Restore(snapshot);
        Assert.Equal(5UL, restored.HistorySeed);
        foreach (var id in RngStreams.All)
        {
            Assert.Equal(original.GetDrawCounter(id), restored.GetDrawCounter(id));
            Assert.Equal(Draw(original, id, 8), Draw(restored, id, 8));
        }
    }

    [Fact]
    public void Snapshot_IsDetachedFromLiveRegistry()
    {
        var registry = RngRegistry.Create(3);
        var before = registry.Capture();
        var beforeValues = before.Streams.ToList();

        Draw(registry, "POP", 4);
        Assert.Equal(beforeValues, before.Streams.ToList());

        var streams = Assert.IsAssignableFrom<IList<RngStreamState>>(before.Streams);
        Assert.Throws<NotSupportedException>(() => streams.Add(new RngStreamState("X", 0, 0)));
        Assert.Throws<NotSupportedException>(() => streams[0] = new RngStreamState("GEOGRAPHY", 1, 1));

        // A restored registry is independent of both the snapshot and the original.
        var restored = RngRegistry.Restore(before);
        Draw(restored, "POP", 10);
        Assert.Equal(4UL, registry.GetDrawCounter("POP"));
        Assert.Equal(beforeValues, before.Streams.ToList());
    }

    [Fact]
    public void Restore_Null_Throws() => Assert.Throws<ArgumentNullException>(() => RngRegistry.Restore(null!));

    [Fact]
    public void Restore_RejectsWrongVersion()
    {
        var s = RngRegistry.Create(1).Capture();
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, s.Streams, version: 2)));
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, s.Streams, version: 0)));
    }

    [Fact]
    public void Restore_RejectsMissingExtraDuplicateAndUnknownStreams()
    {
        var s = RngRegistry.Create(1).Capture();
        var all = s.Streams.ToList();

        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, all.Skip(1))));
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, all.Append(new RngStreamState("EXTRA", 0, 0)))));
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, all.Append(all[0]))));

        var duplicatedSameCount = all.Skip(1).Append(all[1]).ToList();
        Assert.Equal(10, duplicatedSameCount.Count);
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, duplicatedSameCount)));

        var renamed = all.Skip(1).Append(all[0] with { StreamId = "geography" });
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, renamed)));
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, Array.Empty<RngStreamState>())));
    }

    [Fact]
    public void Restore_RejectsInconsistentStateOrCounterOrSeed()
    {
        var registry = RngRegistry.Create(1);
        Draw(registry, "COMBAT", 3);
        var s = registry.Capture();
        var all = s.Streams.ToList();
        var i = all.FindIndex(x => x.StreamId == "COMBAT");

        var badState = all.ToList(); badState[i] = all[i] with { State = all[i].State + 1 };
        var badCounter = all.ToList(); badCounter[i] = all[i] with { DrawCounter = all[i].DrawCounter + 1 };
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, badState)));
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(With(s, badCounter)));

        var tamperedSeed = new RngSnapshot(s.Version, s.WorldSeed + 1, s.HistorySeed, all);
        Assert.Throws<ArgumentException>(() => RngRegistry.Restore(tamperedSeed));
    }

    [Fact]
    public void CounterAtMax_RejectsDraw_WithoutWrappingOrChangingState()
    {
        var s = RngRegistry.Create(1).Capture();
        var initial = SeedDerivation.WorldStream(1, "GEOGRAPHY");
        var atMax = new RngStreamState("GEOGRAPHY", unchecked(initial + Gamma * ulong.MaxValue), ulong.MaxValue);
        var streams = s.Streams.Select(x => x.StreamId == "GEOGRAPHY" ? atMax : x).ToList();

        var registry = RngRegistry.Restore(With(s, streams));
        Assert.Equal(ulong.MaxValue, registry.GetDrawCounter("GEOGRAPHY"));

        var before = registry.Capture().Streams.ToList();
        Assert.Throws<InvalidOperationException>(() => registry.NextUInt64("GEOGRAPHY"));
        Assert.Throws<InvalidOperationException>(() => registry.NextUInt64("GEOGRAPHY"));
        Assert.Equal(before, registry.Capture().Streams.ToList());

        // Other streams are unaffected.
        registry.NextUInt64("RESOURCE");
        Assert.Equal(1UL, registry.GetDrawCounter("RESOURCE"));
    }
}
