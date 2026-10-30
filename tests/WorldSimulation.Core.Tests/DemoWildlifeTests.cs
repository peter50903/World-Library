using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-1 章 C1：野生動物的承載、年結算、狩獵加成與存檔。</summary>
public sealed class DemoWildlifeTests
{
    [Fact]
    public void WildlifeStaysWithinCapacityAndSettlementsEmptyTheirCells()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = world.Wildlife;
        Assert.Equal(world.Map.Cells.Count, opening.Prey.Length);
        for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var w = world.Wildlife;
        Assert.True(w.LastYearWeek >= 52 * 29);
        foreach (var (cell, i) in world.Map.Cells.Select((c, i) => (c, i)))
        {
            var cap = DemoWildlife.Capacity(cell);
            Assert.InRange(w.Prey[i], 0, cap.Prey);
            Assert.InRange(w.Wolves[i], 0, cap.Wolves);
        }
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
            Assert.Equal(0, w.Prey[s.Row * world.Map.Columns + s.Column]);
        Assert.Contains(world.Sites.Settlements.Where(s => !s.IsRuin),
            s => DemoPlayerDetail.Of(world, s.Column, s.Row).Tabs[0].Lines.Any(l => l.StartsWith("附近獵物：", StringComparison.Ordinal)));
        // 讀檔依狩獵名單重算來源帳；不一致時讀檔失敗。
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(w.Fingerprint(), reloaded.Wildlife.Fingerprint());
    }

    [Fact]
    public void HuntingBonusAppliesFromTheWeekAfterTheYearlySettlement()
    {
        var state = new DemoWildlifeState(System.Collections.Immutable.ImmutableArray<int>.Empty, System.Collections.Immutable.ImmutableArray<int>.Empty, 0)
        {
            Hunting = System.Collections.Immutable.ImmutableList.Create(new DemoHuntingYear(52, System.Collections.Immutable.ImmutableList.Create(3)))
        };
        Assert.False(state.HuntingAt(3, 52));
        Assert.True(state.HuntingAt(3, 53));
        Assert.False(state.HuntingAt(4, 53));
    }
}
