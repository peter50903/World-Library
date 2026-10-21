using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 04 章 a 節：人口生死改走歷史種子的 POP 流（實體局部亂數 v1）。</summary>
public sealed class DemoPopulationDrawsTests
{
    [Fact]
    public void FixedVector_World42_DerivedHistory_Person1_Week1_Conception()
    {
        // 原始值由協定獨立算出：HISTORY_SEED(42)→HISTORY_STREAM(POP)→ENTITY-LOCAL(人 1, TEMP-HUMAN-DEMOGRAPHY-v1)→索引 1×8＋4。
        var draws = new DemoPopulationDraws(RngRegistry.Create(42));
        Assert.Equal(11404040931146213669UL, draws.Raw(1, 1, DemoPopulationAction.Conception));
    }

    [Fact]
    public void Draws_AreOrderIndependent_Repeatable_AndDoNotTouchGlobalStreams()
    {
        var registry = RngRegistry.Create(7, 99);
        var before = registry.Capture();
        var cases = (from person in new long[] { 1, 2, 57, 1000 }
                     from week in new[] { 0, 1, 52, 520 }
                     from action in Enum.GetValues<DemoPopulationAction>()
                     select (person, week, action)).ToArray();
        var first = new DemoPopulationDraws(registry);
        var expected = cases.ToDictionary(c => c, c => first.Raw(c.person, c.week, c.action));
        for (var i = 0; i < 1000; i++)
        {
            var draws = new DemoPopulationDraws(registry);
            foreach (var c in Enumerable.Reverse(cases).Skip(i % cases.Length).Concat(cases.Take(i % cases.Length)))
                Assert.Equal(expected[c], draws.Raw(c.person, c.week, c.action));
        }
        Assert.Equal(before.Streams, registry.Capture().Streams);
        Assert.Equal(before.WorldSeed, registry.Capture().WorldSeed);
    }

    [Fact]
    public void Hit_UsesIntegerThresholds()
    {
        var draws = new DemoPopulationDraws(RngRegistry.Create(1));
        Assert.False(draws.Hit(1, 1, DemoPopulationAction.NaturalDeath, 0));
        Assert.False(draws.Hit(1, 1, DemoPopulationAction.NaturalDeath, -1));
        Assert.False(draws.Hit(1, 1, DemoPopulationAction.NaturalDeath, double.NaN));
        Assert.True(draws.Hit(1, 1, DemoPopulationAction.NaturalDeath, 1));
        var raw = draws.Raw(1, 1, DemoPopulationAction.NaturalDeath) >> 11;
        Assert.Equal(raw < (ulong)Math.Floor(0.5 * 9007199254740992d), draws.Hit(1, 1, DemoPopulationAction.NaturalDeath, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => draws.Raw(0, 1, DemoPopulationAction.Conception));
        // 大量抽樣的命中率接近機率（只是健全性檢查）。
        var hits = Enumerable.Range(1, 20000).Count(id => draws.Hit(id, 3, DemoPopulationAction.NewbornSex, 0.5));
        Assert.InRange(hits, 9500, 10500);
    }

    private static DemoObserverWorld Living(ulong worldSeed, ulong historySeed, int days)
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(worldSeed, historySeed), DemoWeeklyTempConfig.LivingForagingSample);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    private static string LifeEvents(DemoObserverWorld world)
        => string.Join("|", world.WeekReports().SelectMany(w => w.Events)
            .Where(e => e.Kind is DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath)
            .Select(e => $"{e.Week}:{e.Kind}:{e.Cause}"));

    [Fact]
    public void SameWorldSeed_DifferentHistorySeed_SameMapAndOpening_DifferentLives()
    {
        var differences = 0;
        foreach (var (w, a, b) in new (ulong, ulong, ulong)[] { (42, 1, 2), (7, 3, 4), (11, 5, 6), (3, 7, 8), (99, 9, 10) })
        {
            var openA = Living(w, a, 0);
            var openB = Living(w, b, 0);
            Assert.Equal(openA.Map.Cells, openB.Map.Cells);
            Assert.Equal(openA.Sites.Settlements, openB.Sites.Settlements);
            Assert.Equal(openA.HumanPopulationSnapshot!.People, openB.HumanPopulationSnapshot!.People);

            var tenA = Living(w, a, 10 * 365);
            var tenB = Living(w, b, 10 * 365);
            if (LifeEvents(tenA) != LifeEvents(tenB))
                differences++;
        }
        Assert.True(differences >= 1, "五組不同歷史種子的十年生死事件全部相同。");
    }

    [Fact]
    public void SameRecipe_GivesTheSameWeeklyEventsAndFingerprints()
    {
        var a = new DemoObserverWorld(WorldRecipe.ForSeeds(42, 8), DemoWeeklyTempConfig.LivingForagingSample);
        var b = new DemoObserverWorld(WorldRecipe.ForSeeds(42, 8), DemoWeeklyTempConfig.LivingForagingSample);
        for (var i = 0; i < 3 * 365; i++)
        {
            a.AdvanceOneDay();
            b.AdvanceOneDay();
            if (i % 7 == 6)
                Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        }
        Assert.Equal(LifeEvents(a), LifeEvents(b));
    }
}
