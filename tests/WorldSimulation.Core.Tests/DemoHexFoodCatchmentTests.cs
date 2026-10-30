using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #112 contract tests for only one shared food budget per land source.
/// This is not connected to the currently playable weekly world (#113 is required).
/// </summary>
public sealed class DemoHexFoodCatchmentTests
{
    private static readonly DemoHexWorld Map = DemoHexWorld.Generate(42);
    private static DemoHexCell Forest()
        => Map.Cells.First(c => c.Terrain == DemoTerrain.Forest &&
            Map.Cells.Count(t => t.Terrain != DemoTerrain.Water && DemoHexFoodCatchment.IsInReach(c, t, false)) >= 2);
    private static DemoHexCell River()
        => Map.Cells.First(c => c.Terrain == DemoTerrain.Water &&
            Map.Cells.Any(t => t.Terrain != DemoTerrain.Water && DemoHexFoodCatchment.IsInReach(c, t, false)));
    private static DemoHexCell[] Neighbours(DemoHexCell c)
        => Map.Cells.Where(t => DemoHexFoodCatchment.IsInReach(c, t, false)).ToArray();

    [Fact]
    public void AxialCoordinatesHaveAtMostSixNeighboursAndNeverReachTheSecondRing()
    {
        foreach (var source in Map.Cells)
        {
            Assert.InRange(Neighbours(source).Length, 2, 6);
            Assert.False(DemoHexFoodCatchment.IsInReach(source, source, false));
            Assert.True(DemoHexFoodCatchment.IsInReach(source, source, true));
            foreach (var remote in Map.Cells.Except(Neighbours(source)).Where(c => c != source))
                Assert.False(DemoHexFoodCatchment.IsInReach(source, remote, true));
        }
    }

    [Fact]
    public void ForestCanFeedItsOwnCellAndOnlyAdjacentLand()
    {
        var forest = Forest();
        var source = new DemoCatchmentFoodSource(11, forest.Column, forest.Row,
            DemoCatchmentSourceKind.Forest, FoodAvailable: 12);
        var eligible = DemoHexFoodCatchment.EligibleCells(Map, source);
        Assert.Contains(eligible, c => c.Column == forest.Column && c.Row == forest.Row);
        Assert.Equal(Neighbours(forest).Count(t => t.Terrain != DemoTerrain.Water) + 1, eligible.Count);
        Assert.All(eligible, c => Assert.True(DemoHexFoodCatchment.IsInReach(forest, c, true)));
        var claim = new DemoCatchmentFoodClaim(1, forest.Column, forest.Row, 5, 5);
        var r = DemoHexFoodCatchment.Allocate(Map, new[] { source }, new[] { claim });
        Assert.Equal(5, r.TotalHarvested);
        Assert.Equal(0, r.UnmetDemand[1]);
        Assert.Equal(7, r.UnharvestedSourceFood[11]);
    }

    [Fact]
    public void TwoSettlementsShareOneForestSourceNotTwoCopiesOfFood()
    {
        var forest = Forest();
        var neighbours = Neighbours(forest).Where(c => c.Terrain != DemoTerrain.Water).Take(2).ToArray();
        var source = new DemoCatchmentFoodSource(2, forest.Column, forest.Row,
            DemoCatchmentSourceKind.Forest, 9);
        var claims = new[] {
            new DemoCatchmentFoodClaim(10, neighbours[0].Column, neighbours[0].Row, 8, 8),
            new DemoCatchmentFoodClaim(20, neighbours[1].Column, neighbours[1].Row, 8, 8)
        };
        var r = DemoHexFoodCatchment.Allocate(Map, new[] { source }, claims);
        Assert.Equal(9, r.TotalHarvested);
        // 第 06 章 c 節：同勞力依比例平分（4／4），取整餘數 1 依聚落 ID 遞增給 #10。
        Assert.Equal(5, r.Harvests.Single(h => h.SettlementId == 10).FoodHarvested);
        Assert.Equal(4, r.Harvests.Single(h => h.SettlementId == 20).FoodHarvested);
        Assert.Equal(4, r.UnmetDemand[20]);
        var greedy = DemoHexFoodCatchment.Allocate(Map, new[] { source }, claims, DemoCatchmentAllocation.Greedy);
        Assert.Equal(8, greedy.Harvests.Single(h => h.SettlementId == 10).FoodHarvested);
        Assert.Equal(1, greedy.Harvests.Single(h => h.SettlementId == 20).FoodHarvested);
        Assert.Equal(0, r.UnharvestedSourceFood[2]);
        var repeated = DemoHexFoodCatchment.Allocate(Map, new[] { source }, claims.Reverse());
        Assert.Equal(r.Harvests, repeated.Harvests);
    }

    [Fact]
    public void RiverIsExplicitAndCannotFeedItselfOrOtherWaterTiles()
    {
        var river = River();
        var source = new DemoCatchmentFoodSource(7, river.Column, river.Row,
            DemoCatchmentSourceKind.MainRiver, 3);
        var cells = DemoHexFoodCatchment.EligibleCells(Map, source);
        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.True(c.Terrain != DemoTerrain.Water
            && DemoHexFoodCatchment.IsInReach(river, c, false)));
        Assert.DoesNotContain(cells, c => c == river);
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map,
            new[] { source }, new[] {
                new DemoCatchmentFoodClaim(1, river.Column, river.Row, 3, 3)
            }));
        var land = cells[0];
        var r = DemoHexFoodCatchment.Allocate(Map, new[] { source }, new[] {
            new DemoCatchmentFoodClaim(1, land.Column, land.Row, 6, 6)
        });
        Assert.Equal(3, r.TotalHarvested);
        Assert.Equal(3, r.UnmetDemand[1]);
    }

    [Fact]
    public void GrasslandDoesNotSilentlyBecomeADevelopedFarm()
    {
        var grass = Map.Cells.First(c => c.Terrain == DemoTerrain.Grassland);
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map,
            new[] { new DemoCatchmentFoodSource(1, grass.Column, grass.Row, DemoCatchmentSourceKind.Forest, 30) },
            Array.Empty<DemoCatchmentFoodClaim>()));
        var noFarm = DemoHexFoodCatchment.Allocate(Map, Array.Empty<DemoCatchmentFoodSource>(),
            new[] { new DemoCatchmentFoodClaim(1, grass.Column, grass.Row, 20, 20) });
        Assert.Empty(noFarm.Harvests);
        Assert.Equal(20, noFarm.UnmetDemand[1]);
    }

    [Fact]
    public void MultipleSourcesCannotReuseTheSameWorkforceOrFeedMoreThanDemand()
    {
        var land = Map.Cells.First(c => c.Terrain == DemoTerrain.Grassland
            && Map.Cells.Count(t => t.Terrain == DemoTerrain.Forest
                && DemoHexFoodCatchment.IsInReach(c, t, true)) >= 2);
        var woods = Map.Cells.Where(t => t.Terrain == DemoTerrain.Forest
            && DemoHexFoodCatchment.IsInReach(land, t, true)).Take(2).ToArray();
        var r = DemoHexFoodCatchment.Allocate(Map, new[] {
            new DemoCatchmentFoodSource(2, woods[1].Column, woods[1].Row, DemoCatchmentSourceKind.Forest, 15),
            new DemoCatchmentFoodSource(1, woods[0].Column, woods[0].Row, DemoCatchmentSourceKind.Forest, 15)
        }, new[] { new DemoCatchmentFoodClaim(1, land.Column, land.Row, 20, 6) });
        Assert.Equal(6, r.TotalHarvested);
        Assert.Equal(14, r.UnmetDemand[1]);
        Assert.Equal(24, r.UnharvestedSourceFood.Values.Sum());
    }

    [Fact]
    public void SourceAndIdentityDuplicationAndNegativeBudgetsAreRejected()
    {
        var forest = Forest();
        var f = new DemoCatchmentFoodSource(1, forest.Column, forest.Row,
            DemoCatchmentSourceKind.Forest, 6);
        var claim = new DemoCatchmentFoodClaim(1, forest.Column, forest.Row, 8, 6);
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map, new[] { f, f }, new[] { claim }));
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map, new[] { f }, new[] { claim, claim }));
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map, new[] { f with { FoodAvailable = -1 } }, new[] { claim }));
        Assert.Throws<InvalidOperationException>(() => DemoHexFoodCatchment.Allocate(Map, new[] { f }, new[] { claim with { HarvestCapacity = -1 } }));
    }

    [Fact]
    public void ValidationDoesNotCreateFoodOrMutateAnyWorldTerrain()
    {
        var forest = Forest();
        var source = new DemoCatchmentFoodSource(5, forest.Column, forest.Row,
            DemoCatchmentSourceKind.Forest, 14);
        var before = Map.Cells.ToArray();
        var a = DemoHexFoodCatchment.Allocate(Map, new[] { source },
            new[] { new DemoCatchmentFoodClaim(1, forest.Column, forest.Row, 14, 14) });
        Assert.Equal(14, a.TotalHarvested);
        Assert.Equal(before, Map.Cells);
        Assert.Equal(14, source.FoodAvailable);
        Assert.Equal(0, a.UnharvestedSourceFood[5]);
    }

    [Fact]
    public void ApprovedTempForest20AndRiver5AreReadFromMachineReadableDemoSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "terrain-food-temp.json");
        var settings = DemoCatchmentTempSettings.FromJson(File.ReadAllText(path));
        Assert.Equal("RACE_HUMAN", settings.ApplicableRace);
        Assert.Equal(20, settings.ForestWeeklySupply);
        Assert.Equal(5, settings.MainRiverWeeklySupply);
        Assert.Equal(0, settings.UncultivatedFarmlandWeeklySupply);

        var river = River();
        var sources = settings.BuildPotentialSources(Map,
            new[] { new DemoCellRef(river.Column, river.Row) });
        Assert.Equal(Map.Cells.Count, sources.Count);
        Assert.All(sources.Where(s => s.Kind == DemoCatchmentSourceKind.Forest),
            s => Assert.Equal(20, s.FoodAvailable));
        Assert.Single(sources.Where(s => s.Kind == DemoCatchmentSourceKind.MainRiver
            && s.FoodAvailable == 5));
        Assert.All(sources.Where(s => s.Kind == DemoCatchmentSourceKind.GrasslandForage), // 第 06-3 章：丘陵為草原一半
            s => Assert.Equal(Map.GetCell(s.Column, s.Row).Terrain == DemoTerrain.Hills ? 4 : 8, s.FoodAvailable));
        Assert.All(sources.Where(s => s.Kind == DemoCatchmentSourceKind.OpenWater),
            s => Assert.Equal(3, s.FoodAvailable));
        var otherWaterTiles = Map.Cells.Where(c => c.Terrain == DemoTerrain.Water
            && !(c.Column == river.Column && c.Row == river.Row));
        Assert.All(otherWaterTiles, c => Assert.Contains(sources,
            s => s.Column == c.Column && s.Row == c.Row && s.Kind == DemoCatchmentSourceKind.OpenWater));
    }

    [Fact]
    public void OccupiedNaturalHexHasNoProductionButCanReceiveNeighbouringFood()
    {
        var settings = DemoCatchmentTempSettings.FromJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "terrain-food-temp.json")));
        var forest = Forest();
        var occupied = new DemoCellRef(forest.Column, forest.Row);
        var food = settings.BuildPotentialSources(Map, Array.Empty<DemoCellRef>(), new[] { occupied });
        Assert.DoesNotContain(food, s => s.Column == forest.Column && s.Row == forest.Row);
        Assert.Equal(Map.Cells.Count - 1, food.Count);
        var neighbouring = food.Where(s => DemoHexFoodCatchment.IsInReach(
            Map.GetCell(s.Column, s.Row), forest, s.Kind is DemoCatchmentSourceKind.Forest
            or DemoCatchmentSourceKind.GrasslandForage)).ToArray();
        var result = DemoHexFoodCatchment.Allocate(Map, neighbouring,
            new[] { new DemoCatchmentFoodClaim(1, forest.Column, forest.Row, 100, 100) });
        Assert.True(result.TotalHarvested > 0); // A village can forage next door, never on occupied land.
        Assert.Equal(result.Harvests.Sum(h => h.FoodHarvested), result.TotalHarvested);
    }

    [Fact]
    public void DifferentNaturalTerrainsUseDifferentBudgetsAndNoNaturalTileIsMissed()
    {
        var settings = DemoCatchmentTempSettings.FromJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "terrain-food-temp.json")));
        var natural = settings.BuildPotentialSources(Map, Array.Empty<DemoCellRef>());
        Assert.Equal(Map.Cells.Count, natural.Count);
        Assert.All(natural, source =>
        {
            var terrain = Map.GetCell(source.Column, source.Row).Terrain;
            var expected = terrain switch
            {
                DemoTerrain.Forest => 20,
                DemoTerrain.Grassland => 8,
                DemoTerrain.Hills => 4, // 第 06-3 章
                _ => 3
            };
            Assert.Equal(expected, source.FoodAvailable);
            Assert.True(source.FoodAvailable > 0);
        });
    }

    [Fact]
    public void DemoSettingsRejectsNonHumanAndFreeFarmingAndNonRiverSource()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "terrain-food-temp.json");
        var valid = DemoCatchmentTempSettings.FromJson(File.ReadAllText(path));
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { ApplicableRace = "RACE_ELF" }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { UncultivatedFarmlandWeeklySupply = 1 }).Validate());
        var forest = Forest();
        Assert.Throws<InvalidOperationException>(() =>
            valid.BuildPotentialSources(Map, new[] { new DemoCellRef(forest.Column, forest.Row) }));
        var river = River();
        Assert.Throws<InvalidOperationException>(() =>
            valid.BuildPotentialSources(Map, new[] {
                new DemoCellRef(river.Column, river.Row),
                new DemoCellRef(river.Column, river.Row)
            }));
    }
}
