using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-44 章：航海殖民——陸路找不到空地時搭船到海外的空地；航路避開海上怪物；路線每一步都走得通。</summary>
public sealed class DemoSeaColonyTests
{
    private static bool IsSea(DemoHexWorld map, DemoCellRef c)
        => map.Contains(c.Column, c.Row) && map.GetCell(c.Column, c.Row) is { Terrain: DemoTerrain.Water, Sea: true };

    [Fact]
    public void VoyagesLandOnOverseasGroundAndAvoidMonsterSeas()
    {
        var map = new DemoObserverWorld(WorldRecipe.ForSeeds(42UL, null, DemoMapSize.Large), DemoWeeklyTempConfig.ForagingSample).Map;
        var empty = new HashSet<DemoCellRef>();
        var coast = map.Cells.Where(c => c.Terrain != DemoTerrain.Water)
            .Select(c => new DemoCellRef(c.Column, c.Row))
            .Where(c => Enumerable.Range(-1, 3).Any(dr => Enumerable.Range(-1, 3).Any(dc => IsSea(map, new DemoCellRef(c.Column + dc, c.Row + dr)))))
            .OrderBy(c => c.Row).ThenBy(c => c.Column);
        DemoCellRef home = default;
        System.Collections.Immutable.ImmutableArray<DemoCellRef> path = default;
        foreach (var c in coast)
        {
            path = DemoSeaLanes.Voyage(map, c, DemoSeaLanes.TempPortVoyageReach, empty, _ => true);
            if (path.Length >= 2)
            {
                home = c;
                break;
            }
        }
        Assert.True(path.Length >= 3, "大地圖上找不到能渡海的海岸");
        Assert.Equal(home, path[0]);
        var landing = path[^1];
        Assert.NotEqual(DemoTerrain.Water, map.GetCell(landing.Column, landing.Row).Terrain);
        Assert.True(DemoMigrationRoutes.FindRoute(map, home, landing).IsEmpty, "上岸的地方應該是陸路走不到的海外");
        Assert.All(path.Skip(1).Take(path.Length - 2), c => Assert.True(IsSea(map, c)));
        Assert.All(path.Zip(path.Skip(1)), p => Assert.True(DemoSeaLanes.Passable(map, p.First, p.Second), $"{p.First}→{p.Second} 走不通"));
        // 同樣的輸入同樣的結果；家門口的海全被怪物佔了就出不了海。
        Assert.Equal(path, DemoSeaLanes.Voyage(map, home, DemoSeaLanes.TempPortVoyageReach, empty, _ => true));
        var blocked = Enumerable.Range(-1, 3).SelectMany(dr => Enumerable.Range(-1, 3).Select(dc => new DemoCellRef(home.Column + dc, home.Row + dr))).ToHashSet();
        Assert.Empty(DemoSeaLanes.Voyage(map, home, DemoSeaLanes.TempPortVoyageReach, blocked, _ => true));
        // 沒有允許的空地、或出不了海＝空。
        Assert.Empty(DemoSeaLanes.Voyage(map, home, DemoSeaLanes.TempPortVoyageReach, empty, _ => false));
        Assert.Empty(DemoSeaLanes.Voyage(map, home, 0, empty, _ => true));
        // 海上一步最多 3 格；兩格陸地沒有路線段不能直接走。
        var sea = map.Cells.First(c => DemoDungeons.OpenSea(map, c));
        var a = new DemoCellRef(sea.Column, sea.Row);
        var b = map.Cells.Select(c => new DemoCellRef(c.Column, c.Row)).First(c => IsSea(map, c) && DemoHexWorld.Distance(a, c) == 3);
        Assert.True(DemoSeaLanes.Passable(map, a, b));
        Assert.False(DemoSeaLanes.Passable(map, a, a));
    }
}
