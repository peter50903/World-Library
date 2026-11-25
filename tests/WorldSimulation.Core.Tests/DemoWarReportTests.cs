using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-47 章：從事件重建一場戰爭的經過（會戰、攻陷、結局），史書寫成完整的一段。</summary>
public sealed class DemoWarReportTests
{
    [Fact]
    public void AWarIsRebuiltFromItsEventsAndToldAsOneParagraph()
    {
        var sites = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample).Sites;
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (a, c, d) = (alive[0], alive[1], alive[2]);
        var x = new DemoSettlement(99_999, "某處", 0, 0);
        DemoSettlementEvent Ev(int week, DemoSettlement s, DemoSettlementEventKind kind, long count, string cause, long id)
            => new(week, TimeSpan.FromDays(week * 7), s.Id, s.Name, kind, count, cause + "【TEMP】", EventId: id);
        var events = new[]
        {
            Ev(5, a, DemoSettlementEventKind.WarDeclared, c.Id, $"{a.Name}向{c.Name}宣戰，為了擴張領土", 1),
            Ev(7, a, DemoSettlementEventKind.ConquestBattle, 10, $"野戰：征討{c.Name}的大軍行至 ({c.Column},{c.Row})，遭守軍迎擊", 2),
            Ev(7, c, DemoSettlementEventKind.ConquestBattle, 20, $"野戰：{c.Name}的守軍出城，在 ({c.Column},{c.Row}) 迎擊{a.Name}的大軍", 3),
            Ev(9, a, DemoSettlementEventKind.ConquestBattle, 3, $"出兵征討{c.Name}", 4),
            Ev(9, c, DemoSettlementEventKind.ConquestBattle, 5, $"征服戰爭：抵抗{a.Name}的大軍", 5),
            Ev(9, x, DemoSettlementEventKind.ConquestBattle, 99, "征服戰爭：抵抗某國的大軍", 6), // 別場戰爭的不算
            Ev(12, c, DemoSettlementEventKind.Occupied, a.Id, $"{a.Name}攻陷了{c.Name}", 7),
            Ev(15, d, DemoSettlementEventKind.Occupied, a.Id, $"{a.Name}的軍隊佔領了{d.Name}", 8),
            Ev(20, c, DemoSettlementEventKind.Conquered, a.Id, $"{a.Name}征服了{c.Name}", 9),
            Ev(30, a, DemoSettlementEventKind.WarDeclared, d.Id, $"{a.Name}向{d.Name}宣戰", 10),
            Ev(32, a, DemoSettlementEventKind.ConquestBattle, 4, $"出兵征討{d.Name}", 11)
        };
        var reports = DemoWarReports.All(events, sites);
        Assert.Equal(2, reports.Count);
        var won = reports[0];
        Assert.Equal((a.Id, c.Id, 5, 20, 9L, true), (won.AttackerId, won.TargetId, won.StartWeek, won.EndWeek, won.EndEventId, won.Won));
        Assert.Equal(2, won.Battles.Count);
        Assert.True(won.Battles[0].Field);
        Assert.True(won.Battles[0].AttackerWon);
        Assert.Equal((10L, 20L), (won.Battles[0].AttackerDeaths, won.Battles[0].DefenderDeaths));
        Assert.Equal((false, c.Id, 3L, 5L), (won.Battles[1].Field, won.Battles[1].Besieged, won.Battles[1].AttackerDeaths, won.Battles[1].DefenderDeaths));
        Assert.Equal(new[] { (12, c.Id), (15, d.Id) }, won.Occupations);
        var story = DemoWarReports.Story(won, id => sites.Settlements.First(s => s.Id == id).Name);
        Assert.StartsWith("這場戰爭前後打了 15 週：", story, StringComparison.Ordinal);
        Assert.Contains("野戰得勝（攻方死 10、守方死 20 人）", story, StringComparison.Ordinal);
        Assert.Contains($"圍城後攻陷{c.Name}", story, StringComparison.Ordinal);
        Assert.Contains($"又佔領{d.Name}", story, StringComparison.Ordinal);
        Assert.EndsWith("雙方共死 38 人（攻方 13、守方 25）。", story, StringComparison.Ordinal);
        // 還在打的排在最後、沒有結束週。
        var ongoing = reports[1];
        Assert.Equal((d.Id, 0, 4L), (ongoing.TargetId, ongoing.EndWeek, ongoing.AttackerDeaths));
    }
}
