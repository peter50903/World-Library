using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08-3 章：聚落小史（建立年數、開局／開拓建立、人口里程碑、分出開拓隊、高峰、遺跡）出現在點格詳情；
/// 受孕預留將來口糧（來源餘量與勞力）。
/// </summary>
public sealed class DemoSettlementHistoryTests
{
    private static DemoObserverWorld Living(ulong seed, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < years * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void HistoryListsFoundingMilestonesAndAgeInTheCellQuery()
    {
        var world = Living(3, 30);
        foreach (var s in world.Sites.Settlements)
        {
            var lines = DemoSettlementHistory.Lines(world, s.Id);
            Assert.StartsWith("規模：", lines[0], StringComparison.Ordinal);
            Assert.Contains("建立至今", lines[0]);
            Assert.Equal("聚落小史：", lines[1]);
            Assert.Contains(lines, l => s.IsFounded ? l.Contains("的開拓隊移居此地") : l.Contains("開局時已有人定居"));
            // 年份不倒退。
            var years = lines.Skip(2).Select(l => int.Parse(l.Trim().Split(' ')[1])).ToList();
            Assert.Equal(years.OrderBy(y => y), years);
            // 點格詳情含同一份小史。
            var view = world.QueryCell(s.Column, s.Row);
            Assert.All(lines, l => Assert.Contains(l, view.Lines));
        }

        // 開拓出去的聚落在母聚落小史裡有一筆。
        var child = world.Sites.Settlements.FirstOrDefault(s => s.IsFounded);
        Assert.NotNull(child);
        Assert.Contains(DemoSettlementHistory.Lines(world, child!.FoundedFromSettlementId), l => l.Contains($"建立{child.Name}"));
        // 人口曾達 10 人的聚落記下「發展為村莊」。
        var grown = world.Sites.Settlements.First(s => world.WeekReports()
            .Any(w => w.Settlements.Any(l => l.SettlementId == s.Id && l.PopulationClosing >= DemoSettlementHistory.TempVillagePopulation)));
        Assert.Contains(DemoSettlementHistory.Lines(world, grown.Id), l => l.Contains("由聚落發展為村莊"));
    }

    [Fact]
    public void ConceptionNeedsRoomForTheFutureAdult()
    {
        var config = DemoWeeklyTempConfig.LivingForagingSample;
        var ledger = new DemoSettlementWeekLedger(1, 1, "測試", 0, 0, 0, 0, 0, 0, 20, 0, 0, 5, 0, 0, 5,
            AdultWorkersOpening: 3, WaterCarriers: 1, SourceSlack: 8);
        // 勞力 2×16＝32 ≥ 需求 20＋預留 4；餘量 8 ≥ 4。
        Assert.True(DemoCrowding.RoomForChild(ledger, pregnant: 0));
        // 已有一胎：預留 8，餘量 8 剛好；兩胎：預留 12 超過餘量。
        Assert.True(DemoCrowding.RoomForChild(ledger, pregnant: 1));
        Assert.False(DemoCrowding.RoomForChild(ledger, pregnant: 2));
        // 只剩一名採集者：16 ＜ 20＋4。
        Assert.False(DemoCrowding.RoomForChild(ledger with { AdultWorkersOpening = 2 }, pregnant: 0));
        Assert.Equal(4, config.FoodPerPersonPerWeek);
    }
}
