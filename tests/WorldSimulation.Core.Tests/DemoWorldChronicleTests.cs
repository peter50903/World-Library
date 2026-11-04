using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10b 章：世界編年史（依年分組、重大／一般、出生老死每年合計、讀檔前後相同）。</summary>
public sealed class DemoWorldChronicleTests
{
    private static readonly Lazy<DemoObserverWorld> Shared = new(() => Living(3, 45));

    private static DemoObserverWorld Living(ulong seed, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < years * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void ChronicleListsEveryYearAndTheMajorEventsOfTheWorld()
    {
        var world = Shared.Value;
        var years = DemoWorldChronicle.Build(world);

        Assert.Equal(Enumerable.Range(1, 45), years.Select(y => y.Year));
        var all = years.SelectMany(y => y.Entries).ToList();
        Assert.Equal(world.Sites.Settlements.Count(s => s.IsFounded), all.Count(e => e.Category == DemoChronicleCategory.Founding));
        Assert.Equal(world.Sites.Settlements.Count(s => s.IsRuin), all.Count(e => e.Category == DemoChronicleCategory.Ruin));
        Assert.Equal(
            world.Sites.Settlements.Sum(s => world.ChronicleOf(s.Id).Milestones.Count(DemoSettlementChronicle.IsTierChange)),
            all.Count(e => e.Category == DemoChronicleCategory.Tier));
        Assert.Equal(
            world.Knowledge.Records.Where(r => r.Learned).Select(r => r.Technology).Distinct().Count(),
            all.Count(e => e.Category == DemoChronicleCategory.Knowledge && e.Level == DemoChronicleLevel.Major));

        var events = world.SettlementEvents();
        Assert.Equal(events.Where(e => e.Kind == DemoSettlementEventKind.Birth).Sum(e => e.Count), years.Sum(y => y.Births));
        Assert.Equal(events.Where(e => e.Kind == DemoSettlementEventKind.NaturalDeath).Sum(e => e.Count), years.Sum(y => y.NaturalDeaths));
        Assert.All(years, y => Assert.Equal(y.Births, y.BySettlement.Values.Sum(v => v.Births)));

        Assert.Equal(world.Era, years[^1].Era);
        Assert.All(years.Where(y => y.EraStarted), y => Assert.Contains(y.Entries, e => e.Category == DemoChronicleCategory.Era));
        Assert.All(all, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Text));
            Assert.True(e.SettlementId > 0);
            Assert.Contains(world.Sites.Settlements, s => s.Id == e.SettlementId);
        });
        // 同一年內起因依週次排列，後續（第 08-16 章）緊接在起因下面、不早於起因；出生老死不出現在條目裡（統計級每年一行）。
        Assert.All(years, y =>
        {
            var ids = y.Entries.Where(e => e.EventId > 0).Select(e => e.EventId).ToHashSet();
            var roots = y.Entries.Where(e => !(e.IsFollowUp && ids.Contains(e.ParentEventId))).Select(e => e.Week).ToList();
            Assert.Equal(roots.OrderBy(w => w), roots);
            for (var i = 0; i < y.Entries.Count; i++)
                if (y.Entries[i].IsFollowUp && ids.Contains(y.Entries[i].ParentEventId))
                {
                    var parent = y.Entries.ToList().FindIndex(e => e.EventId == y.Entries[i].ParentEventId);
                    Assert.InRange(parent, 0, i - 1);
                    Assert.True(y.Entries[parent].Week <= y.Entries[i].Week);
                }
        });
        Assert.DoesNotContain(all, e => e.Text.Contains("出生", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildingTheChronicleDoesNotChangeTheWorldAndSurvivesSaveAndLoad()
    {
        var world = Shared.Value;
        var before = world.ExportTempSave();
        var first = Texts(DemoWorldChronicle.Build(world));
        Assert.Equal(before, world.ExportTempSave());

        var loaded = DemoObserverWorld.ImportTempSave(before);
        Assert.Equal(first, Texts(DemoWorldChronicle.Build(loaded)));
    }

    [Fact]
    public void OldYearsFoldIntoDecadeSummaries()
    {
        Assert.Empty(DemoWorldChronicle.FoldedDecades(19));
        Assert.Equal(new[] { (1, 10) }, DemoWorldChronicle.FoldedDecades(25));
        Assert.Equal(new[] { (1, 10), (11, 20) }, DemoWorldChronicle.FoldedDecades(35));

        var empty = new DemoChronicleYear(1, DemoEra.Hope, false, Array.Empty<DemoChronicleEntry>(), 3, 1,
            new Dictionary<int, (long Births, long NaturalDeaths)>());
        Assert.Equal("第 1～1 年：沒有重大事件｜出生 3、老死 1", DemoWorldChronicle.DecadeSummary(new[] { empty }));

        var years = DemoWorldChronicle.Build(Shared.Value);
        var summary = DemoWorldChronicle.DecadeSummary(years.Take(10).ToList());
        Assert.StartsWith("第 1～10 年：", summary, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Texts(IReadOnlyList<DemoChronicleYear> years)
        => years.SelectMany(y => new[] { $"{y.Year}|{y.Era}|{y.EraStarted}|{y.StatisticLine}" }
            .Concat(y.Entries.Select(e => $"{e.Week}|{e.Level}|{e.Category}|{e.Text}|{e.SettlementId}"))).ToList();
}
