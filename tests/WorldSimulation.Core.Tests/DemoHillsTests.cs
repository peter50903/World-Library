using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 06-3 章：丘陵帶與礦脈。</summary>
public sealed class DemoHillsTests
{
    [Theory]
    [InlineData(1UL, DemoMapSize.Small)]
    [InlineData(7UL, DemoMapSize.Medium)]
    public void HillRangesAreLandWithSomeOreAndAreDeterministic(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        var hills = map.Cells.Where(c => c.Terrain == DemoTerrain.Hills).ToList();
        Assert.NotEmpty(hills);
        Assert.All(hills, c => Assert.False(c.MainWaterway || c.Sea));
        Assert.All(map.Cells.Where(c => c.Ore), c => Assert.Equal(DemoTerrain.Hills, c.Terrain));
        Assert.Equal(map.Cells, DemoHexWorld.Generate(seed, size).Cells);
    }

    [Fact]
    public void HillsForageAtHalfTheGrasslandRateAndCannotBeFarmed()
    {
        var map = Enumerable.Range(1, 20).Select(s => DemoHexWorld.Generate((ulong)s)).First(m => m.Cells.Any(c => c.Terrain == DemoTerrain.Hills));
        var hill = map.Cells.First(c => c.Terrain == DemoTerrain.Hills);
        var source = DemoForagingWorld.Sources(map, Array.Empty<DemoCellRef>()).Single(s => s.Column == hill.Column && s.Row == hill.Row);
        Assert.Equal(DemoCatchmentSourceKind.GrasslandForage, source.Kind);
        Assert.Equal(DemoForagingWorld.Settings.WeeklySupply(DemoCatchmentSourceKind.GrasslandForage) / 2, source.FoodAvailable);
        Assert.StartsWith("丘陵", DemoCellQuery.ShortName(map, null, hill));
    }
}
