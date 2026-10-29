using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-8 章 B1：開局文化、特性池、文化比例隨遷徙流動、代表人物的文化。</summary>
public sealed class DemoCultureTests
{
    private static readonly Lazy<DemoObserverWorld> Lived = new(() =>
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 40 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    });

    [Fact]
    public void OpeningSettlementsNearEachOtherShareOneCultureWithDrawnTraits()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var culture = world.Culture;
        Assert.NotEmpty(culture.Cultures);
        Assert.Equal(Enumerable.Range(1, culture.Cultures.Count), culture.Cultures.Select(c => c.Id));
        foreach (var s in world.Sites.Settlements)
            Assert.Equal(1000, Assert.Single(culture.SharesOf(s.Id)).Permille);
        // 相距 ≤ 6 格的開局聚落同文化。
        foreach (var a in world.Sites.Settlements)
            foreach (var b in world.Sites.Settlements.Where(b => b.Id > a.Id && DemoKnowledge.Distance(world.Map, a, b) <= DemoCultures.TempClusterRange))
                Assert.Equal(culture.MainCultureOf(a.Id), culture.MainCultureOf(b.Id));
        // 特性：每池在上下限內、同互斥組最多一個、家庭結構恰一個、生計一到兩個。
        var pool = DemoCultures.TraitPool;
        foreach (var c in culture.Cultures)
        {
            Assert.EndsWith("人", c.Name, StringComparison.Ordinal);
            foreach (var g in pool.GroupBy(e => e.PoolId))
            {
                var n = c.Traits.Count(t => g.Any(e => e.TraitId == t));
                Assert.InRange(n, g.First().DrawMin, g.First().DrawMax);
            }
            Assert.All(pool.Where(e => e.ExclusiveGroup.Length > 0).GroupBy(e => e.ExclusiveGroup),
                g => Assert.True(c.Traits.Count(t => g.Any(e => e.TraitId == t)) <= 1));
            Assert.DoesNotContain("CT_WANDERER", c.Traits); // 開局文化不是開拓建立的
        }
        // 同一個種子重建，文化相同（實體局部亂數，可重現）。
        Assert.Equal(culture, new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample).Culture);
    }

    [Fact]
    public void MixWeightsByPeopleAndKeepsAThousand()
    {
        var a = ImmutableList.Create(new DemoCultureShare(1, 1000));
        var b = ImmutableList.Create(new DemoCultureShare(2, 1000));
        var mixed = DemoCultures.Mix(a, 3, b, 1);
        Assert.Equal(new[] { new DemoCultureShare(1, 750), new DemoCultureShare(2, 250) }, mixed);
        var thirds = DemoCultures.Mix(DemoCultures.Mix(a, 1, b, 1), 2, ImmutableList.Create(new DemoCultureShare(3, 1000)), 1);
        Assert.Equal(1000, thirds.Sum(x => x.Permille));
        Assert.Equal(a, DemoCultures.Mix(a, 5, b, 0));
        Assert.Equal(2, DemoCultures.Pick(mixed, 900));
        Assert.Equal(1, DemoCultures.Pick(mixed, 10));
    }

    [Fact]
    public void SharesFlowWithMigrationAndRepresentativesCarryACulture()
    {
        var world = Lived.Value;
        var culture = world.Culture;
        Assert.All(culture.Settlements, s => Assert.Equal(1000, s.Shares.Sum(x => x.Permille)));
        // 開拓建立的聚落都有文化比例（沿用開拓隊帶的比例）。
        Assert.All(world.Sites.Settlements.Where(s => s.IsFounded), s => Assert.NotEmpty(culture.SharesOf(s.Id)));
        // 每位在任的代表人物都有文化，且是所屬聚落比例裡的文化（或就任時的比例；比例之後可能變化，至少文化存在）。
        foreach (var s in world.Sites.Settlements)
            foreach (var r in world.RepresentativesOf(s.Id))
                Assert.NotNull(culture.Culture(culture.CultureOfRepresentative(r.Id) ?? 0));
        // 詳情與總覽顯示文化。
        var living = world.Sites.Settlements.First(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0);
        Assert.Contains(world.PlayerDetail(living.Column, living.Row).Tabs[0].Lines, l => l.StartsWith("文化：", StringComparison.Ordinal));
        Assert.Contains(DemoWorldOverview.Of(world, TestCalendar.DaysPerYear).Lines(), l => l.StartsWith("文化 ", StringComparison.Ordinal));
        // 存讀檔後文化相同、續跑一致。
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(culture, loaded.Culture);
        Assert.Empty(new DemoObserverWorld(DemoScenario.Sample(3)).Culture.Cultures); // 非人類繁衍世界沒有文化
    }
}
