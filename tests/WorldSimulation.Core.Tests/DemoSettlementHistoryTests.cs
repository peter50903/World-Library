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
        // 人口曾達 10 人的聚落記下里程碑；第 08-6 章 d 節起村莊另需木匠（見 DemoKnowledgeTests）。
        var grown = world.Sites.Settlements.First(s => world.WeekReports()
            .Any(w => w.Settlements.Any(l => l.SettlementId == s.Id && l.PopulationClosing >= DemoSettlementHistory.TempUpPopulation(DemoSettlementTier.Village))));
        Assert.Contains(DemoSettlementHistory.Lines(world, grown.Id), l => l.Contains("人口首次達到 10 人"));
        // 第 08-11 章：沒有任何營地級設施的專家（木匠、農師、獵人、巫祝）就還是營地。
        var campRoles = DemoSettlementHistory.FacilitiesOf(DemoSettlementTier.Camp).Select(f => f.Role).ToHashSet();
        Assert.All(world.Sites.Settlements.Where(s => !world.RepresentativesOf(s.Id).Any(r => campRoles.Contains(r.Role))),
            s => Assert.Equal(DemoSettlementTier.Camp, world.ChronicleOf(s.Id).Tier));
    }

    [Fact]
    public void ConceptionNeedsRoomForTheFutureAdult()
    {
        var config = DemoWeeklyTempConfig.LivingForagingSample;
        var ledger = new DemoSettlementWeekLedger(1, 1, "測試", 0, 0, 0, 0, 0, 0, 20, 0, 0, 5, 0, 0, 5,
            AdultWorkersOpening: 3, WaterCarriers: 1, SourceSlack: 8);
        // 5 人中 3 名成人、2 名孩子：孩子長大後多吃 2×(4－2)＝4；這一胎 4 → 預留 8。
        // 勞力 (3－1)×16＝32 ≥ 需求 20＋8；餘量 8 ≥ 8。
        Assert.True(DemoCrowding.RoomForChild(ledger, pregnant: 0));
        // 已有一胎：預留 12 超過餘量。
        Assert.False(DemoCrowding.RoomForChild(ledger, pregnant: 1));
        Assert.True(DemoCrowding.RoomForChild(ledger with { SourceSlack = 12 }, pregnant: 1));
        // 只剩一名採集者：16 ＜ 20＋8。
        Assert.False(DemoCrowding.RoomForChild(ledger with { AdultWorkersOpening = 2 }, pregnant: 0));
        Assert.Equal(4, config.FoodPerPersonPerWeek);
    }

    [Fact]
    public void StoryGroupsTheSameRecordsIntoPhasesWithASummaryAndRenown()
    {
        // 第 08-10d 章 C：分期的小史與原本的小史是同一批紀錄；時期依時間相接；讀檔前後相同。
        var world = Living(3, 30);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        foreach (var s in world.Sites.Settlements)
        {
            var story = DemoSettlementStory.Build(world, s.Id);
            Assert.False(string.IsNullOrWhiteSpace(story.Renown));
            Assert.NotEmpty(story.Phases);
            Assert.StartsWith("草創", story.Phases[0].Title, StringComparison.Ordinal);
            Assert.All(story.Phases, p =>
            {
                Assert.True(p.FromYear <= p.ToYear);
                Assert.NotEmpty(p.Lines);
            });
            for (var i = 1; i < story.Phases.Count; i++)
                Assert.True(story.Phases[i - 1].ToYear <= story.Phases[i].FromYear);
            if (s.IsRuin)
            {
                Assert.Equal("廢棄", story.Phases[^1].Title);
                Assert.StartsWith("已成遺跡", story.Renown, StringComparison.Ordinal);
            }

            // 每一筆原本的小史都在某個時期裡（文字相同），沒有多、沒有少。
            var original = DemoSettlementHistory.Lines(world, s.Id).Skip(2).Select(l => l.Trim()).OrderBy(l => l, StringComparer.Ordinal).ToList();
            var phased = story.Phases.SelectMany(p => p.Lines).OrderBy(l => l, StringComparer.Ordinal).ToList();
            Assert.Equal(original, phased);

            var lines = DemoSettlementStory.Lines(world, s.Id);
            Assert.StartsWith("聞名：", lines[0], StringComparison.Ordinal);
            Assert.Equal(story.Phases.Count, lines.Count(l => l.StartsWith("【", StringComparison.Ordinal)));
            Assert.Equal(lines, DemoSettlementStory.Lines(loaded, s.Id));
        }
    }
}
