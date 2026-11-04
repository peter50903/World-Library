using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 06-4 章：中地圖以上的高度圖大陸。</summary>
public sealed class DemoContinentTests
{
    [Theory]
    [InlineData(1UL, DemoMapSize.Medium)]
    [InlineData(2UL, DemoMapSize.Large)]
    [InlineData(3UL, DemoMapSize.Huge)]
    public void ContinentHasCoastsOnEverySideHillsRiversAndIsDeterministic(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        var cells = map.Cells;
        // 陸地約佔一半（海平面依比例切，河道另佔一些）。
        var land = cells.Count(c => c.Terrain != DemoTerrain.Water);
        Assert.InRange(land * 100 / cells.Count, 40, DemoContinentGenerator.TempLandPercent);
        // 四邊都以海為主（不再只有南緣）。
        double SeaShare(IEnumerable<DemoHexCell> edge) => edge.Average(c => c.Sea ? 1.0 : 0.0);
        Assert.True(SeaShare(cells.Where(c => c.Row == 0)) >= 0.7, "北緣不是海");
        Assert.True(SeaShare(cells.Where(c => c.Row == map.Rows - 1)) >= 0.7, "南緣不是海");
        Assert.True(SeaShare(cells.Where(c => c.Column == 0)) >= 0.7, "西緣不是海");
        Assert.True(SeaShare(cells.Where(c => c.Column == map.Columns - 1)) >= 0.7, "東緣不是海");
        // 丘陵是陸地、礦脈只在丘陵；河與海分開。
        Assert.Contains(cells, c => c.Terrain == DemoTerrain.Hills);
        Assert.All(cells.Where(c => c.Ore), c => Assert.Equal(DemoTerrain.Hills, c.Terrain));
        Assert.All(cells.Where(c => c.MainWaterway), c => Assert.False(c.Sea));
        Assert.All(cells.Where(c => c.Sea), c => Assert.Equal(DemoTerrain.Water, c.Terrain));
        Assert.NotEmpty(map.MainWaterway);
        Assert.Contains(cells, c => c.Terrain == DemoTerrain.Forest);
        Assert.Contains(cells, c => c.Terrain == DemoTerrain.Grassland);
        Assert.Equal(cells, DemoHexWorld.Generate(seed, size).Cells);
        Assert.NotEqual(cells, DemoHexWorld.Generate(seed + 100, size).Cells);
    }

    [Fact]
    public void RiversFlowDownFromHillsToTheSeaOrAnotherRiver()
    {
        var map = DemoHexWorld.Generate(5, DemoMapSize.Large);
        DemoHexCell? At(DemoHexCell c, (int Dq, int Dr, string Name) d) => map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr);
        var river = map.Cells.Where(c => c.MainWaterway).ToList();
        // 每個河流格都和另一個河流格或丘陵（源頭）相連；至少一個河口臨海。
        Assert.All(river, c => Assert.Contains(DemoRouteNetwork.Directions, d => At(c, d) is { MainWaterway: true } or { Terrain: DemoTerrain.Hills }));
        Assert.Contains(river, c => DemoRouteNetwork.Directions.Any(d => At(c, d) is { Sea: true }));
    }

    [Fact]
    public void LargeWorldPlacesSettlementsOnLandAndSurvivesSaveLoad()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(4, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        Assert.NotEmpty(world.Sites.Settlements);
        Assert.All(world.Sites.Settlements, s => Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(s.Column, s.Row).Terrain));
        for (var d = 0; d < 2 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        Assert.True(world.WorldPopulation > 0);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Map.Cells, loaded.Map.Cells);
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
    }
}

/// <summary>第 06-4 章第 3 步：森林依濕度（沿海沿河多森林、內陸多草原）。</summary>
public sealed class DemoForestMoistureTests
{
    [Theory]
    [InlineData(1UL, DemoMapSize.Large)]
    [InlineData(6UL, DemoMapSize.Medium)]
    public void ForestsHugWaterAndGrasslandsFillTheInterior(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        bool NearWater(DemoHexCell c) => DemoRouteNetwork.Directions.Any(d => map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is { Terrain: DemoTerrain.Water });
        var plains = map.Cells.Where(c => c.Terrain is DemoTerrain.Forest or DemoTerrain.Grassland).ToList();
        double ForestShare(IEnumerable<DemoHexCell> cells) => cells.DefaultIfEmpty().Average(c => c?.Terrain == DemoTerrain.Forest ? 1.0 : 0.0);
        var shore = ForestShare(plains.Where(NearWater));
        var inland = ForestShare(plains.Where(c => !NearWater(c)));
        Assert.True(shore > inland + 0.2, $"岸邊森林比例 {shore:F2} 沒有明顯高於內陸 {inland:F2}");
        Assert.InRange(ForestShare(plains), 0.40, 0.50);
        // 成片：相鄰平地同為森林或草原的比例。
        double same = 0, pairs = 0;
        foreach (var c in plains)
            foreach (var d in DemoRouteNetwork.Directions)
                if (map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is { Terrain: DemoTerrain.Forest or DemoTerrain.Grassland } n)
                {
                    pairs++;
                    if (n.Terrain == c.Terrain) same++;
                }
        Assert.True(same / pairs >= 0.65, $"同地形相鄰比例 {same / pairs:F2}");
    }
}
