using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 06 章 a 節：主水道與路線網（跨格可通行性的單一權威）。</summary>
public sealed class DemoRouteNetworkTests
{
    private static IEnumerable<ulong> Seeds => Enumerable.Range(1, 60).Select(i => (ulong)i).Append(42UL);

    private static DemoHexCell? Neighbour(DemoHexWorld map, DemoHexCell cell, int dq, int dr)
        => map.Cells.FirstOrDefault(c => c.AxialQ == cell.AxialQ + dq && c.AxialR == cell.AxialR + dr);

    private static DemoCellRef Ref(DemoHexCell c) => new(c.Column, c.Row);

    [Fact]
    public void MainWaterway_IsAContinuousWaterSequence_FromTheNorthEdgeToTheSouthEdge()
    {
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            var waterway = map.MainWaterway;
            Assert.Equal(DemoHexWorld.Rows, waterway.Count);
            Assert.Equal(Enumerable.Range(0, DemoHexWorld.Rows), waterway.Select(c => c.Row));
            Assert.All(waterway, c => Assert.Equal(DemoTerrain.Water, map.GetCell(c.Column, c.Row).Terrain));
            Assert.All(waterway.Zip(waterway.Skip(1)), p => Assert.True(DemoMigrationRoutes.AreAdjacent(p.First, p.Second)));
            // 相鄰水道格之間是河流段（水上路線），不供陸路。
            Assert.All(waterway.Zip(waterway.Skip(1)), p =>
            {
                var segment = map.Routes.Between(p.First, p.Second);
                Assert.NotNull(segment);
                Assert.Equal(DemoRouteKind.River, segment!.Kind);
                Assert.False(map.Routes.IsLandPassable(p.First, p.Second));
            });
        }
    }

    [Fact]
    public void SameSeed_SameMapAndRoutes_CellByCell()
    {
        var a = DemoHexWorld.Generate(17);
        var b = DemoHexWorld.Generate(17);
        Assert.Equal(a.Cells.ToArray(), b.Cells.ToArray());
        Assert.Equal(a.Routes.Segments.ToArray(), b.Routes.Segments.ToArray());
        Assert.Equal(a.Routes.Ford, b.Routes.Ford);
    }

    [Fact]
    public void AdjacentCellsWithoutASegment_AreNotPassable_AndExitsComeOnlyFromSegments()
    {
        var checkedPairs = 0;
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            foreach (var cell in map.Cells)
            {
                var exits = map.Routes.Exits(map, Ref(cell));
                Assert.Equal(map.Routes.Segments.Count(s => s.A == Ref(cell) || s.B == Ref(cell)), exits.Count);
                foreach (var (dq, dr, _) in DemoRouteNetwork.Directions)
                {
                    if (Neighbour(map, cell, dq, dr) is not { } next)
                        continue;
                    var segment = map.Routes.Between(Ref(cell), Ref(next));
                    if (segment is null)
                    {
                        Assert.False(map.Routes.IsLandPassable(Ref(cell), Ref(next)));
                        checkedPairs++;
                    }
                    // 陸地與一般水域（非渡口）之間永遠沒有陸路。
                    if (cell.Terrain != DemoTerrain.Water && next.Terrain == DemoTerrain.Water && map.Routes.Ford != Ref(next))
                        Assert.False(map.Routes.IsLandPassable(Ref(cell), Ref(next)));
                    if (cell.Terrain != DemoTerrain.Water && next.Terrain != DemoTerrain.Water)
                        Assert.Equal(DemoRouteKind.Wild, segment!.Kind);
                }
            }
        }
        Assert.True(checkedPairs > 0);
    }

    [Fact]
    public void CrossingTheMainWaterway_ArrivesOnlyThroughTheFord()
    {
        var crossings = 0;
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            // 不經渡口時的陸地連通區塊（只走野地段）。
            var component = new Dictionary<DemoCellRef, int>();
            var label = 0;
            foreach (var cell in map.Cells.Where(c => c.Terrain != DemoTerrain.Water))
            {
                if (component.ContainsKey(Ref(cell))) continue;
                var queue = new Queue<DemoCellRef>(new[] { Ref(cell) });
                component[Ref(cell)] = ++label;
                while (queue.Count > 0)
                {
                    var here = queue.Dequeue();
                    foreach (var s in map.Routes.Segments.Where(s => s.Kind == DemoRouteKind.Wild && (s.A == here || s.B == here)))
                    {
                        var other = s.Other(here);
                        if (component.TryAdd(other, label))
                            queue.Enqueue(other);
                    }
                }
            }

            var land = map.Cells.Where(c => c.Terrain != DemoTerrain.Water).Select(Ref).ToArray();
            for (var i = 0; i < land.Length; i += 7)
            for (var j = land.Length - 1; j > i; j -= 11)
            {
                var path = DemoMigrationRoutes.FindRoute(map, land[i], land[j]);
                if (path.IsEmpty) continue;
                Assert.All(path.Zip(path.Skip(1)), p => Assert.True(map.Routes.IsLandPassable(p.First, p.Second)));
                if (component[land[i]] != component[land[j]])
                {
                    Assert.NotNull(map.Routes.Ford);
                    Assert.Contains(map.Routes.Ford!.Value, path);
                    crossings++;
                }
            }
        }
        Assert.True(crossings > 0, "沒有任何一條經渡口跨越水道的路線可檢查。");
    }

    [Fact]
    public void NoSettlementEverSitsOnTheMainWaterway()
    {
        foreach (var seed in Seeds.Take(30))
        foreach (var config in new[] { DemoWeeklyTempConfig.Sample, DemoWeeklyTempConfig.ForagingSample, DemoWeeklyTempConfig.FrontierSample })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
            for (var day = 0; day < 140; day++)
                world.AdvanceOneDay();
            Assert.All(world.Sites.Settlements, s => Assert.False(world.Map.GetCell(s.Column, s.Row).MainWaterway));
            Assert.All(world.Sites.Settlements, s => Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(s.Column, s.Row).Terrain));
        }
    }

    [Fact]
    public void SegmentCosts_ComeFromTheTempRouteSettings()
    {
        var settings = DemoRouteTempSettings.Current;
        Assert.Equal("TEMP", settings.Status);
        Assert.True(settings.WildCost > settings.RoadCost);
        var map = DemoHexWorld.Generate(42);
        Assert.All(map.Routes.Segments, s => Assert.Equal(settings.CostOf(s.Kind), s.Cost));
    }

    [Fact]
    public void CellQuery_ListsTheRoutesOfTheCell()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        var settlement = world.Sites.Settlements[0];
        var view = world.QueryCell(settlement.Column, settlement.Row);
        Assert.Contains(view.Lines, l => l.StartsWith("路線：", StringComparison.Ordinal));
        var waterway = world.Map.MainWaterway[0];
        Assert.Contains(world.QueryCell(waterway.Column, waterway.Row).Lines, l => l.Contains("主水道，禁止定居", StringComparison.Ordinal));
    }
}
