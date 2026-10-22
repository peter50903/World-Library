using System.Text;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 04-1 章：分層存檔——只存目前狀態＋近期週報，較早的存年帳、事件與聚落小史。</summary>
public sealed class DemoLayeredSaveTests
{
    private static DemoObserverWorld Run(ulong seed, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < years * 365; day++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void ThreeYearSave_KeepsOnlyRecentWeeks_AndLoadsWithCompleteYearsEventsAndChronicles()
    {
        var world = Run(1, 3);
        var json = world.ExportTempSave();
        var state = JsonNode.Parse(json)!["State"]!;
        var weekBase = state["WeekBase"]!.GetValue<int>();
        Assert.True(weekBase > 0, "三年的世界應只保留近期週報");
        Assert.Equal(world.CommittedWeeks - weekBase, state["WeekReports"]!.AsArray().Count);

        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(weekBase + 1, loaded.FirstRetainedWeek);
        Assert.Equal(world.CommittedWeeks, loaded.CommittedWeeks);
        Assert.Equal(world.YearSummaries(), loaded.YearSummaries());
        Assert.Equal(world.SettlementEvents(), loaded.SettlementEvents());
        Assert.Empty(loaded.BuildCausalIndex().Problems);
        foreach (var s in world.Sites.Settlements)
            Assert.Equal(DemoSettlementHistory.Lines(world, s.Id), DemoSettlementHistory.Lines(loaded, s.Id));
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());

        for (var day = 0; day < 365; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
        Assert.Equal(world.YearSummaries(), loaded.YearSummaries());
    }

    [Fact]
    public void SaveSize_StopsGrowingWithTheWeeklyLedger()
    {
        var world = Run(2, 10);
        var tenYears = Encoding.UTF8.GetByteCount(world.ExportTempSave());
        for (var day = 0; day < 20 * 365; day++)
            world.AdvanceOneDay();
        var thirtyYears = Encoding.UTF8.GetByteCount(world.ExportTempSave());
        // 舊格式三倍年數約三倍大；分層後只多出年帳、事件與小史。
        Assert.True(thirtyYears < tenYears * 2, $"十年 {tenYears / 1024} KB、三十年 {thirtyYears / 1024} KB");
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
    }

    [Fact]
    public void ChroniclesMatchTheOldScanOfAllWeeklyReports()
    {
        // 聚落小史增量更新＝第 08-3 章逐週掃描全部週報的結果（新世界保留全部週報，可直接比對）。
        var world = Run(3, 5);
        foreach (var s in world.Sites.Settlements)
        {
            var c = world.ChronicleOf(s.Id);
            var pops = world.WeekReports().SelectMany(w => w.Settlements.Where(l => l.SettlementId == s.Id)
                .Select(l => (w.Week, l.PopulationClosing))).ToList();
            var peak = pops.Count == 0 ? (Week: 0, PopulationClosing: 0L) : pops.Aggregate((a, b) => b.PopulationClosing > a.PopulationClosing ? b : a);
            Assert.Equal(peak.PopulationClosing, c.Peak);
            if (c.Peak > 0) Assert.Equal(peak.Week, c.PeakWeek);
            var appointed = world.WeekReports().SelectMany(w => w.Appointments).Count(r => r.SettlementId == s.Id);
            Assert.Equal(appointed, c.Representatives.Count(l => l.Text.Contains("成為", StringComparison.Ordinal)));
        }
    }
}
