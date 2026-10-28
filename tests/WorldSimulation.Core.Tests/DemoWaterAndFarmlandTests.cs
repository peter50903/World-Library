using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-11 章：內陸挑水與掘井、農田荒廢（試玩回饋 1、3）。</summary>
public sealed class DemoWaterAndFarmlandTests
{
    [Fact]
    public void CarryingWaterDoublesCarriersAndWellsAllowAnyLand()
    {
        var map = DemoHexWorld.Generate(3);
        var normal = DemoSurvivalTempSettings.Current.PeoplePerWaterCarrier;
        var beside = map.Cells.First(c => c.Terrain != DemoTerrain.Water && map.HasWaterAccess(new DemoCellRef(c.Column, c.Row)));
        var carry = map.Cells.FirstOrDefault(c => c.Terrain != DemoTerrain.Water && !map.HasWaterAccess(new DemoCellRef(c.Column, c.Row))
            && map.IsNearWater(new DemoCellRef(c.Column, c.Row)));
        var far = map.Cells.FirstOrDefault(c => c.Terrain != DemoTerrain.Water && !map.IsNearWater(new DemoCellRef(c.Column, c.Row)));
        DemoCellRef Ref(DemoHexCell c) => new(c.Column, c.Row);

        Assert.Equal(normal, DemoMigrationRoutes.PeoplePerCarrier(map, Ref(beside), false, normal));
        Assert.True(DemoMigrationRoutes.Habitable(map, Ref(beside)));
        Assert.Equal((2L, 20L), DemoMigrationRoutes.WaterPlan(map, Ref(beside), false, 20, 15));
        // 不滿 10 人不需要專職取水者（大家順手取水）；只剩 1 人也能採集。
        Assert.Equal((0L, 2L), DemoMigrationRoutes.WaterPlan(map, Ref(beside), false, 2, 2));
        Assert.Equal((0L, 1L), DemoMigrationRoutes.WaterPlan(map, Ref(beside), false, 1, 1));
        Assert.Equal((1L, 19L), DemoMigrationRoutes.WaterPlan(map, Ref(beside), false, 19, 10));
        // 人手不夠：20 人只有 1 名成年人 → 只取到 10 人份。
        Assert.Equal((1L, 10L), DemoMigrationRoutes.WaterPlan(map, Ref(beside), false, 20, 1));
        if (carry is not null)
        {
            Assert.True(DemoMigrationRoutes.Habitable(map, Ref(carry)));
            Assert.Equal(normal / 2, DemoMigrationRoutes.PeoplePerCarrier(map, Ref(carry), false, normal));
            Assert.Equal((4L, 20L), DemoMigrationRoutes.WaterPlan(map, Ref(carry), false, 20, 15));
            Assert.Equal(normal, DemoMigrationRoutes.PeoplePerCarrier(map, Ref(carry), true, normal));
        }
        if (far is not null)
        {
            Assert.False(DemoMigrationRoutes.Habitable(map, Ref(far)));
            Assert.True(DemoMigrationRoutes.Habitable(map, Ref(far), wells: true));
            Assert.Equal((0L, 0L), DemoMigrationRoutes.WaterPlan(map, Ref(far), false, 20, 15));
        }
        Assert.Equal("掘井", DemoTechCatalog.Get(DemoKnowledge.WellDiggingId).Name);
        Assert.Equal(new[] { "TECH_BASIC_CONSTRUCTION" }, DemoTechCatalog.Get(DemoKnowledge.WellDiggingId).Parents);
    }

    [Fact]
    public void WellsComeFromOwnKnowledgeOrTheFoundingParent()
    {
        var well = DemoTechnology.Of(DemoKnowledge.WellDiggingId);
        var state = DemoKnowledgeState.Initial with
        {
            Records = ImmutableList.Create(new DemoKnowledgeRecord(1, well, DemoKnowledge.ThresholdOf(well), 10)),
            Era = DemoEra.Pioneer, EraWeek = 5
        };
        var sites = new[]
        {
            new DemoSettlement(1, "甲", 2, 2),
            new DemoSettlement(2, "乙", 5, 2, FoundedWeek: 20, FoundedFromSettlementId: 1, FoundedByBatchId: 1),
            new DemoSettlement(3, "丙", 7, 2, FoundedWeek: 8, FoundedFromSettlementId: 1, FoundedByBatchId: 2)
        };
        Assert.False(DemoKnowledge.Wells(state, sites, 1, 10));
        Assert.True(DemoKnowledge.Wells(state, sites, 1, 11));
        Assert.True(DemoKnowledge.Wells(state, sites, 2, 21));  // 建立前來源已會
        Assert.False(DemoKnowledge.Wells(state, sites, 3, 30)); // 建立時來源還不會
    }

    [Fact]
    public void FarmlandWithNoNeighbourDecaysBackToGrassland()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 60 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var events = world.SettlementEvents();
        // 每塊農田只會在附近無人時荒廢；荒廢後的格不再是農田，除非之後又開墾完成。
        foreach (var g in events.Where(e => e.Kind is DemoSettlementEventKind.FarmlandCleared or DemoSettlementEventKind.FarmlandAbandoned)
                     .GroupBy(e => e.Count))
        {
            var kinds = g.OrderBy(e => e.EventId).Select(e => e.Kind).ToList();
            Assert.All(kinds.Select((k, i) => (k, i)), x => Assert.Equal(x.i % 2 == 0, x.k == DemoSettlementEventKind.FarmlandCleared));
            var cell = DemoLandClearing.CellOf(world.Map, g.Key);
            Assert.Equal(kinds[^1] == DemoSettlementEventKind.FarmlandCleared, world.Land.StateOf(cell) == DemoLandDevelopment.Farmland);
        }
        // 目前的農田都有相鄰的有人聚落，或剛開始荒廢（開發度已低於 100 的不再是農田）。
        foreach (var parcel in world.Land.Parcels.Where(p => p.State == DemoLandDevelopment.Farmland))
        {
            var origin = world.Map.GetCell(parcel.Column, parcel.Row);
            Assert.Contains(DemoRouteNetwork.Directions.Select(d => world.Map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr))
                .Where(c => c is not null).Select(c => world.Sites.SettlementAt(c!.Column, c.Row)),
                s => s is { IsRuin: false } && world.SettlementPopulation(s.Id) > 0);
        }
        Assert.Empty(world.BuildCausalIndex().Problems);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Land.Parcels, loaded.Land.Parcels);
    }
}
