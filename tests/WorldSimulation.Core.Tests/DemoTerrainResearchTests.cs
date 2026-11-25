using WorldSimulation.Core.World.Demo;
using Xunit;
using static WorldSimulation.Core.World.Demo.DemoKnowledge;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-43 章：地形帶動相應技術的研究（靠水捕魚造船、靠山冶鐵打獵……）。</summary>
public sealed class DemoTerrainResearchTests
{
    private static DemoTechNode Node(string id) => DemoTechCatalog.All.Single(n => n.Id == id);

    [Fact]
    public void WaterHillsForestAndGrasslandEachSpeedUpTheirOwnTechs()
    {
        var p = (long)TempTerrainResearchPercent;
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_NET_FISHING"), TerrainSide.Water));
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_BASIC_BOATBUILDING"), TerrainSide.Water));
        Assert.Equal(p + TempSeaResearchPercent, TerrainResearchPercent(Node("TECH_OCEAN_SHIPBUILDING"), TerrainSide.Water | TerrainSide.Sea));
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_OCEAN_SHIPBUILDING"), TerrainSide.Water)); // 只靠河：遠洋不另加
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_HUNTING"), TerrainSide.Hills));
        Assert.All(DemoTechCatalog.All.Where(n => n.Domain == "10C"), n => Assert.Equal(p, TerrainResearchPercent(n, TerrainSide.Hills)));
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_POTTERY_FIRING"), TerrainSide.Water));
        // 地形不對沒有加成；同一項技術符合多種地形只算一次。
        Assert.Equal(0, TerrainResearchPercent(Node("TECH_NET_FISHING"), TerrainSide.Hills | TerrainSide.Forest));
        Assert.Equal(0, TerrainResearchPercent(Node("TECH_HUNTING"), TerrainSide.Water));
        Assert.Equal(p, TerrainResearchPercent(Node("TECH_BASIC_BOATBUILDING"), TerrainSide.Water | TerrainSide.Forest | TerrainSide.Hills));
        Assert.Equal(0, TerrainResearchPercent(Node("TECH_BARTER"), TerrainSide.Water | TerrainSide.Sea | TerrainSide.Hills | TerrainSide.Forest | TerrainSide.Grassland));
        Assert.Null(TerrainLine(TerrainSide.None));
        Assert.Contains("靠海", TerrainLine(TerrainSide.Water | TerrainSide.Sea), StringComparison.Ordinal);
        Assert.Contains("狩獵", TerrainLine(TerrainSide.Hills), StringComparison.Ordinal);
    }
}
