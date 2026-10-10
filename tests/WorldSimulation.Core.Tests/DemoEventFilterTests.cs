using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>#96：歷史事件搜尋與篩選只讀、決定性，結果都是 Core 的真實事件。</summary>
public sealed class DemoEventFilterTests
{
    /// <summary>開拓情境：與因果測試同樣的種子搜尋（42，再 1～30），取第一個兩年內建立新聚落的世界。</summary>
    private static readonly Lazy<DemoObserverWorld> Frontier = new(() =>
    {
        foreach (var seed in new ulong[] { 42 }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i)))
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            for (var day = 0; day < 2 * TestCalendar.DaysPerYear; day++)
                world.AdvanceOneDay();
            if (world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.SettlementFounded))
                return world;
        }

        throw new InvalidOperationException("找不到兩年內建立新聚落的開拓種子。");
    });

    [Fact]
    public void NoFilter_ReturnsEveryEventNewestFirst()
    {
        var events = Frontier.Value.SettlementEvents();
        Assert.True(DemoEventFilter.All.IsAll);
        var result = DemoEventFilter.All.Apply(events);
        Assert.Equal(events.Reverse(), result);
        Assert.True(result.Zip(result.Skip(1)).All(p => p.First.EventId > p.Second.EventId));
    }

    [Fact]
    public void EachCriterionKeepsOnlyMatchingRealEvents()
    {
        var world = Frontier.Value;
        var events = world.SettlementEvents();
        var sample = events[events.Count / 2];

        var byKind = new DemoEventFilter(Kind: DemoSettlementEventKind.Emigration).Apply(events);
        Assert.NotEmpty(byKind);
        Assert.All(byKind, e => Assert.Equal(DemoSettlementEventKind.Emigration, e.Kind));
        Assert.Equal(events.Count(e => e.Kind == DemoSettlementEventKind.Emigration), byKind.Count);

        var bySettlement = new DemoEventFilter(SettlementId: sample.SettlementId).Apply(events);
        Assert.All(bySettlement, e => Assert.Equal(sample.SettlementId, e.SettlementId));
        Assert.Equal(events.Count(e => e.SettlementId == sample.SettlementId), bySettlement.Count);

        var day = DemoEventFilter.DayOf(sample);
        var byDay = new DemoEventFilter(FromDay: day, ToDay: day).Apply(events);
        Assert.Contains(sample, byDay);
        Assert.All(byDay, e => Assert.Equal(day, DemoEventFilter.DayOf(e)));

        var byId = new DemoEventFilter(Text: $" #{sample.EventId} ").Apply(events);
        Assert.Equal(new[] { sample }, byId);

        var byName = new DemoEventFilter(Text: sample.SettlementName).Apply(events);
        Assert.Contains(sample, byName);

        var byKindText = new DemoEventFilter(Text: "建立新聚落").Apply(events);
        Assert.NotEmpty(byKindText);
        Assert.Contains(byKindText, e => e.Kind == DemoSettlementEventKind.SettlementFounded);

        var combined = new DemoEventFilter(day, day, sample.SettlementId, sample.Kind, $"#{sample.EventId}").Apply(events);
        Assert.Equal(new[] { sample }, combined);
    }

    [Fact]
    public void EmptyResults_AreEmptyNotErrors()
    {
        var events = Frontier.Value.SettlementEvents();
        Assert.Empty(new DemoEventFilter(Text: "不存在的關鍵字zzz").Apply(events));
        Assert.Empty(new DemoEventFilter(FromDay: 100, ToDay: 99).Apply(events)); // 起日大於迄日：空，不對調
        Assert.Empty(new DemoEventFilter(SettlementId: 99999).Apply(events));
        Assert.Empty(new DemoEventFilter(Text: "#0").Apply(events));
        Assert.Empty(DemoEventFilter.All.Apply(Array.Empty<DemoSettlementEvent>()));
    }

    [Fact]
    public void FilteringNeverChangesTheWorld_AndIsDeterministic()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(7), DemoWeeklyTempConfig.LocalFamineSample);
        for (var day = 0; day < 400; day++)
            world.AdvanceOneDay();
        var before = world.ExportTempSave();
        var filter = new DemoEventFilter(FromDay: 7, ToDay: 380, Text: "饑荒");

        var first = filter.Apply(world.SettlementEvents());
        var second = filter.Apply(world.SettlementEvents());
        Assert.Equal(first, second);
        Assert.Equal(before, world.ExportTempSave());

        // 存讀檔後同一篩選得到同一結果。
        var loaded = DemoObserverWorld.ImportTempSave(before);
        Assert.Equal(first, filter.Apply(loaded.SettlementEvents()));
    }

    [Fact]
    public void WhitespaceOnlyTextIsNoFilter()
    {
        Assert.True(new DemoEventFilter(Text: "   ").IsAll);
        Assert.False(new DemoEventFilter(Kind: DemoSettlementEventKind.Hunger).IsAll);
        Assert.False(new DemoEventFilter(FromDay: 1).IsAll);
    }
}
