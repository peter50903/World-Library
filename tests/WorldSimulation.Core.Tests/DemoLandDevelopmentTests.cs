using System.Text;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08 章 a 節：舊「草原自動農田」退場；土地開發狀態與地形分離，本章不產糧。</summary>
public sealed class DemoLandDevelopmentTests
{
    public static TheoryData<string> Scenarios() => new()
    {
        "Sample", "Foraging", "Living", "Crowded", "Famine", "LocalFamine", "Frontier"
    };

    private static DemoWeeklyTempConfig Config(string name) => name switch
    {
        "Sample" => DemoWeeklyTempConfig.Sample,
        "Foraging" => DemoWeeklyTempConfig.ForagingSample,
        "Living" => DemoWeeklyTempConfig.LivingForagingSample,
        "Crowded" => DemoWeeklyTempConfig.CrowdedForagingSample,
        "Famine" => DemoWeeklyTempConfig.FamineSample,
        "LocalFamine" => DemoWeeklyTempConfig.LocalFamineSample,
        _ => DemoWeeklyTempConfig.FrontierSample
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void NoScenarioHasFreeFarmFoodOrAutomaticFarmland(string scenario)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), Config(scenario));
        Assert.DoesNotContain(world.Sites.ResourcePoints, p => world.Map.GetCell(p.Column, p.Row).Terrain == DemoTerrain.Grassland
            && p.Kind != DemoResourceKind.Uncultivated);
        Assert.All(world.Sites.ResourcePoints.Where(p => p.Kind == DemoResourceKind.Uncultivated),
            p => Assert.Equal(0, world.ResourceReserve(p.Id)));
        for (var day = 0; day < 2 * 365; day++)
            world.AdvanceOneDay();
        Assert.Empty(world.Land.Parcels); // 時間流逝或人口增加不會自動變農田
        Assert.All(world.WeekReports().SelectMany(w => w.Settlements), l => Assert.Equal(0, l.FoodFromFarming));
    }

    [Fact]
    public void OnlyUnsettledGrasslandCanBeDevelopedAndStatesAreValidated()
    {
        // 第 08-1 章起自然採集世界的開局聚落都靠水，這裡用開局有草原聚落的一般示範地圖驗證土地模型。
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        var grass = world.Map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && world.Sites.SettlementAt(c.Column, c.Row) is null);
        var forest = world.Map.Cells.First(c => c.Terrain == DemoTerrain.Forest);
        var land = DemoLandDevelopmentMap.Empty;

        var clearing = land.With(world.Map, world.Sites, new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Clearing, 40));
        Assert.Equal(DemoLandDevelopment.Clearing, clearing.StateOf(new DemoCellRef(grass.Column, grass.Row)));
        var farm = clearing.With(world.Map, world.Sites, new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Farmland, 100));
        Assert.Single(farm.FarmlandCells());
        Assert.Empty(farm.With(world.Map, world.Sites, new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Undeveloped, 0)).Parcels);
        Assert.Empty(land.Parcels); // 不可變

        Assert.Throws<InvalidOperationException>(() => land.With(world.Map, world.Sites,
            new DemoLandParcel(forest.Column, forest.Row, DemoLandDevelopment.Farmland, 50)));
        var settled = world.Sites.Settlements.First(s => world.Map.GetCell(s.Column, s.Row).Terrain == DemoTerrain.Grassland);
        Assert.Throws<InvalidOperationException>(() => land.With(world.Map, world.Sites,
            new DemoLandParcel(settled.Column, settled.Row, DemoLandDevelopment.Clearing, 10)));
        Assert.Throws<InvalidOperationException>(() => land.With(world.Map, world.Sites,
            new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Clearing, 100)));
        Assert.Throws<InvalidOperationException>(() => land.With(world.Map, world.Sites,
            new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Farmland, 0)));
    }

    [Fact]
    public void FarmlandNoLongerProvidesWildForage()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        var grass = world.Map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && world.Sites.SettlementAt(c.Column, c.Row) is null);
        bool HasSource(DemoLandDevelopmentMap land) => DemoForagingWorld.Sources(world.Map, world.Sites, land)
            .Any(s => s.Column == grass.Column && s.Row == grass.Row);
        Assert.True(HasSource(DemoLandDevelopmentMap.Empty));
        var clearing = DemoLandDevelopmentMap.Empty.With(world.Map, world.Sites,
            new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Clearing, 50));
        Assert.True(HasSource(clearing)); // 開墾中仍可野採【TEMP】
        var farm = clearing.With(world.Map, world.Sites, new DemoLandParcel(grass.Column, grass.Row, DemoLandDevelopment.Farmland, 100));
        Assert.False(HasSource(farm));
    }

    [Fact]
    public void LoadingRejectsLandDevelopmentWithoutALegalSource()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        for (var day = 0; day < 21; day++) world.AdvanceOneDay();
        var grass = world.Map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && world.Sites.SettlementAt(c.Column, c.Row) is null);
        var doc = JsonNode.Parse(world.ExportTempSave())!;
        Assert.Empty(doc["State"]!["Land"]!.AsArray());
        doc["State"]!["Land"] = new JsonArray(new JsonObject
        {
            ["Column"] = grass.Column, ["Row"] = grass.Row, ["State"] = (int)DemoLandDevelopment.Farmland, ["DevelopmentPercent"] = 100
        });
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }
}
