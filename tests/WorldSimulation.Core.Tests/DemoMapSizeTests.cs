using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 07-1 章 c 節：建立世界時選地圖大小（小 12×8 預設不變、中 24×16、大 48×32）。</summary>
public sealed class DemoMapSizeTests
{
    [Theory]
    [InlineData(DemoMapSize.Small, 12, 8)]
    [InlineData(DemoMapSize.Medium, 24, 16)]
    [InlineData(DemoMapSize.Large, 48, 32)]
    public void EverySize_HasItsGrid_DrawCount_SouthSea_RiverAndFords(DemoMapSize size, int columns, int rows)
    {
        var rng = RngRegistry.Create(42);
        var map = DemoHexWorld.Generate(rng, size);

        Assert.Equal((columns, rows), (map.Columns, map.Rows));
        Assert.Equal(columns * rows, map.Cells.Count);
        Assert.Equal((ulong)DemoHexWorld.GeographyDrawsFor(size), rng.GetDrawCounter(RngStreams.Geography));
        // 海在南緣、每欄都從最南一列連續往北；北緣沒有海。
        foreach (var col in Enumerable.Range(0, columns))
        {
            var depth = Enumerable.Range(0, rows).Count(r => map.GetCell(col, r).Sea);
            Assert.InRange(depth, 1, rows / 2);
            Assert.All(Enumerable.Range(rows - depth, depth), r => Assert.True(map.GetCell(col, r).Sea));
        }
        Assert.NotEmpty(map.MainWaterway);
        Assert.Equal(0, map.MainWaterway[0].Row);
        // 第 06-2 章：每 12 欄一條河（小 1、中 2、大 4），每條每 8 列一個渡口。
        Assert.InRange(map.Routes.Fords.Count, 1, DemoHexWorld.RiversFor(columns) * Math.Max(1, rows / DemoHexWorld.TempRowsPerFord));
        Assert.All(map.Routes.Fords, f => Assert.True(map.GetCell(f.Column, f.Row) is { Shallow: true, MainWaterway: true }));
        Assert.Equal(map.Routes.Fords[0], map.Routes.Ford);
    }

    [Fact]
    public void SmallIsTheDefault_AndUnchanged()
    {
        Assert.Equal(DemoHexWorld.GeographyDraws, DemoHexWorld.GeographyDrawsFor(DemoMapSize.Small));
        Assert.Equal(DemoHexWorld.Generate(7).Cells, DemoHexWorld.Generate(7, DemoMapSize.Small).Cells);
        Assert.Equal(DemoMapSize.Small, WorldRecipe.ForSeeds(7).MapSize);
        Assert.DoesNotContain("地圖", WorldRecipe.ForSeeds(7).Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void MediumWorld_ScalesOpeningSettlements_AndSurvivesSaveLoad()
    {
        var recipe = WorldRecipe.ForSeeds(3, null, DemoMapSize.Medium);
        var world = new DemoObserverWorld(recipe, DemoWeeklyTempConfig.LivingForagingSample);
        Assert.Equal(DemoMapSize.Medium, world.Map.Size);
        Assert.InRange(world.Sites.Settlements.Count, 1, 4 * 4);
        Assert.Contains("地圖 中（24×16）", recipe.Describe(), StringComparison.Ordinal);
        Assert.All(world.Sites.Settlements, s => Assert.InRange(DemoNames.Length(s.Name), 1, 6));
        Assert.Equal(world.Sites.Settlements.Count, world.Sites.Settlements.Select(s => s.Name).Distinct().Count());

        for (var d = 0; d < 365; d++)
            world.AdvanceOneDay();
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(DemoMapSize.Medium, loaded.Recipe.MapSize);
        Assert.Equal(world.Map.Cells, loaded.Map.Cells);
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
    }
}
