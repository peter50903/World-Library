using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 07 章 c 節：新聚落佔格、選址需可及自然來源、人口歸零滿 52 週（TEMP）成為遺跡並恢復自然來源。</summary>
public sealed class DemoRuinTests
{
    private static readonly int RuinWeeks = DemoSettlementTempSettings.Current.RuinAfterEmptyWeeks;

    [Fact]
    public void RuinRuleIsTempFiftyTwoWeeks()
        => Assert.Equal(52, RuinWeeks);

    [Fact]
    public void OccupiedCellsFollowFoundingAndRuinWeeks()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        var sites = world.Sites;
        var first = sites.Settlements[0];
        var cell = new DemoCellRef(first.Column, first.Row);
        Assert.Contains(cell, sites.OccupiedCells());
        Assert.DoesNotContain(DemoForagingWorld.Sources(world.Map, sites), s => s.Column == cell.Column && s.Row == cell.Row);

        var ruined = sites.WithRuin(first.Id, 10);
        Assert.True(ruined.Settlements[0].IsRuin);
        Assert.DoesNotContain(cell, ruined.OccupiedCells());
        Assert.Contains(cell, ruined.OccupiedCellsAtWeek(10));      // 第 10 週末才成遺跡
        Assert.DoesNotContain(cell, ruined.OccupiedCellsAtWeek(11)); // 第 11 週起恢復
        Assert.Contains(DemoForagingWorld.Sources(world.Map, ruined), s => s.Column == cell.Column && s.Row == cell.Row);
        Assert.Throws<InvalidOperationException>(() => ruined.WithRuin(first.Id, 20));
    }

    [Fact]
    public void FoundingSiteMustReachAFreeNaturalSource()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        var land = world.Map.Cells.First(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null);
        var cell = new DemoCellRef(land.Column, land.Row);
        Assert.True(DemoForagingWorld.ReachesFreeSource(world.Map, Array.Empty<DemoCellRef>(), cell));
        var everythingElse = world.Map.Cells.Select(c => new DemoCellRef(c.Column, c.Row)).Where(c => c != cell).ToArray();
        Assert.False(DemoForagingWorld.ReachesFreeSource(world.Map, everythingElse, cell));
    }

    [Fact]
    public void EmptySettlementBecomesARuinAfterFiftyTwoWeeksAndSurvivesSaveLoad()
    {
        foreach (var seed in new ulong[] { 42, 7, 3, 11, 99, 20261021 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            for (var day = 1; day <= 6 * TestCalendar.DaysPerYear; day++)
            {
                world.AdvanceOneDay();
                var ruinEvent = world.SettlementEvents().FirstOrDefault(e => e.Kind == DemoSettlementEventKind.SettlementRuined);
                if (ruinEvent is null) continue;

                var ruin = world.Sites.Settlements.Single(s => s.Id == ruinEvent.SettlementId);
                Assert.True(ruin.IsRuin);
                Assert.Equal(ruinEvent.Week, ruin.RuinedWeek);
                Assert.Equal(RuinWeeks, ruinEvent.Count);
                Assert.Equal(0, ruinEvent.CauseEventId); // 根因
                // 成為遺跡前連續 52 週期末人口都是 0。
                var reports = world.WeekReports();
                for (var w = ruin.RuinedWeek - RuinWeeks + 1; w <= ruin.RuinedWeek; w++)
                    Assert.Equal(0, reports[w - 1].Settlements.Single(l => l.SettlementId == ruin.Id).PopulationClosing);
                // 前一週還不夠，不會提早成為遺跡。
                Assert.DoesNotContain(reports.Take(ruin.RuinedWeek - 1).SelectMany(r => r.Events),
                    e => e.Kind == DemoSettlementEventKind.SettlementRuined && e.SettlementId == ruin.Id);
                Assert.Equal("TEMP_EV_SETTLEMENT_RUINED", world.EventHistory.Find(ruinEvent.EventId)!.EventTypeId);

                // 遺跡不是遷徙目的地：之後沒有任何隊伍前往它。
                for (var more = 0; more < 26 * 7; more++)
                    world.AdvanceOneDay();
                Assert.DoesNotContain(world.WeekReports().Skip(ruin.RuinedWeek).SelectMany(r => r.Migrations),
                    m => m.Departed && m.ToSettlementId == ruin.Id);
                Assert.Single(world.SettlementEvents(), e => e.Kind == DemoSettlementEventKind.SettlementRuined && e.SettlementId == ruin.Id);

                var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
                Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
                Assert.True(loaded.Sites.Settlements.Single(s => s.Id == ruin.Id).IsRuin);

                // 竄改：拿掉遺跡週次 → 拒讀。
                var doc = System.Text.Json.Nodes.JsonNode.Parse(world.ExportTempSave())!;
                foreach (var s in doc["State"]!["Settlements"]!.AsArray())
                    if (s!["Id"]!.GetValue<int>() == ruin.Id) s["RuinedWeek"] = 0;
                doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(System.Text.Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
                Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
                return;
            }
        }
        Assert.Fail("開拓情境多個種子 6 年內都沒有聚落成為遺跡。");
    }
}
