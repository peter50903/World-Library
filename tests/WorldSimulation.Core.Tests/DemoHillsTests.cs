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

/// <summary>第 06-3 章／08-9 D2：礦脈讓採礦冶金、珠寶精工有從業者；丘陵讓石造有從業者。</summary>
public sealed class DemoMiningResearchTests
{
    private static DemoKnowledge.Context Ctx(bool ore, bool hills)
        => new(DemoSettlementTier.Village, 10, 2, true, 0, false, true, true, false, false, false, 0, ore, hills);

    [Fact]
    public void OreWithinReachUnlocksMiningAndJewelleryPractitioners()
    {
        var prospecting = DemoTechCatalog.Get("TECH_PROSPECTING");
        Assert.Equal(0, DemoKnowledge.Practitioners(prospecting, Ctx(false, false), _ => false));
        Assert.Equal(10, DemoKnowledge.Practitioners(prospecting, Ctx(true, false), _ => false));
        var jewel = DemoTechCatalog.All.First(n => n.Domain == "10M");
        Assert.Equal(10, DemoKnowledge.Practitioners(jewel, Ctx(true, false), _ => true));
        var stone = DemoTechCatalog.All.FirstOrDefault(n => n.Domain == "10D" && n.Branch == "石造");
        if (stone is not null)
        {
            Assert.Equal(0, DemoKnowledge.Practitioners(stone, Ctx(false, false), _ => true));
            Assert.Equal(10, DemoKnowledge.Practitioners(stone, Ctx(false, true), _ => true));
        }
    }

    [Fact]
    public void NearOreMeansAnOreVeinWithinTwoCells()
    {
        var map = Enumerable.Range(1, 30).Select(s => DemoHexWorld.Generate((ulong)s)).First(m => m.Cells.Any(c => c.Ore));
        var ore = map.Cells.First(c => c.Ore);
        var onIt = new DemoSettlement(1, "甲", ore.Column, ore.Row);
        Assert.True(DemoKnowledge.NearOre(map, onIt));
        var far = map.Cells.FirstOrDefault(c => map.Cells.Where(o => o.Ore).All(o => DemoWorldSites.HexDistance(o, c) > DemoKnowledge.TempOreRange));
        if (far is not null)
            Assert.False(DemoKnowledge.NearOre(map, new DemoSettlement(2, "乙", far.Column, far.Row)));
    }
}
