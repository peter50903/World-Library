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
    public void MainWaterway_IsAContinuousWaterSequence_FromTheNorthEdgeToTheSea()
    {
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            var waterway = map.MainWaterway;
            // 第 06-1 章：從北緣連續往南（每一列都有河，第 08-18a 章斜走時同一列有兩格），河口接海。
            var lastRow = waterway[^1].Row;
            Assert.Equal(Enumerable.Range(0, lastRow + 1), waterway.Select(c => c.Row).Distinct());
            Assert.Contains(waterway.Where(c => c.Row == lastRow), c => DemoRouteNetwork.Orthogonal.Any(d => Neighbour(map, map.GetCell(c.Column, c.Row), d.Dq, d.Dr) is { Sea: true }));
            Assert.All(waterway, c => Assert.False(map.GetCell(c.Column, c.Row).Sea));
            Assert.All(waterway, c => Assert.Equal(DemoTerrain.Water, map.GetCell(c.Column, c.Row).Terrain));
            // 第 08-18a 章：河道只走上下左右——每格都有上下左右相鄰的河道格。
            if (waterway.Count > 1)
                Assert.All(waterway, c => Assert.Contains(DemoRouteNetwork.Orthogonal, d => Neighbour(map, map.GetCell(c.Column, c.Row), d.Dq, d.Dr) is { MainWaterway: true }));
            // 上下左右相鄰的水道格之間是河流段（水上路線），不供陸路；斜角相鄰的水道格之間沒有路線段。
            foreach (var a in waterway)
            foreach (var b in waterway.Where(b => DemoMigrationRoutes.AreAdjacent(a, b)))
            {
                var segment = map.Routes.Between(a, b);
                if (a.Column != b.Column && a.Row != b.Row)
                {
                    Assert.Null(segment);
                    continue;
                }
                Assert.NotNull(segment);
                Assert.Equal(DemoRouteKind.River, segment!.Kind);
                Assert.False(map.Routes.IsLandPassable(a, b));
            }
        }
    }

    [Fact]
    public void SeaIsOneSouthernCoast_AndEveryWaterCellIsSeaOrRiver()
    {
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            foreach (var col in Enumerable.Range(0, DemoHexWorld.SmallColumns))
            {
                // 每欄最南一列是海，海往北只連續 1～2 列（TEMP），不會散落在陸地中間。
                var depth = Enumerable.Range(0, DemoHexWorld.SmallRows).Count(r => map.GetCell(col, r).Sea);
                Assert.InRange(depth, DemoHexWorld.TempSeaMinDepth, DemoHexWorld.TempSeaMaxDepth);
                Assert.All(Enumerable.Range(DemoHexWorld.SmallRows - depth, depth), r => Assert.True(map.GetCell(col, r).Sea));
            }
            Assert.All(map.Cells.Where(c => c.Terrain == DemoTerrain.Water), c => Assert.True(c.Sea ^ c.MainWaterway));
            Assert.All(map.Cells.Where(c => c.Sea), c => Assert.False(c.Shallow)); // 海只有深水
        }
    }

    [Fact]
    public void TheFordIsAShallowRiverCell_WithLandOnBothBanks_AwayFromTheSea()
    {
        var fords = 0;
        foreach (var seed in Seeds)
        {
            var map = DemoHexWorld.Generate(seed);
            Assert.True(map.Cells.Count(c => c.Shallow) <= 1);
            if (map.Routes.Ford is not { } ford)
                continue;
            fords++;
            var cell = map.GetCell(ford.Column, ford.Row);
            Assert.True(cell.Shallow && cell.MainWaterway);
            var neighbours = DemoRouteNetwork.Directions.Select(d => Neighbour(map, cell, d.Dq, d.Dr)).Where(n => n is not null).Select(n => n!).ToList();
            Assert.DoesNotContain(neighbours, n => n.Sea);
            Assert.Contains(neighbours, n => n.Terrain != DemoTerrain.Water && n.ScreenX < cell.ScreenX);
            Assert.Contains(neighbours, n => n.Terrain != DemoTerrain.Water && n.ScreenX > cell.ScreenX);
        }
        Assert.True(fords >= 50, $"61 個種子只有 {fords} 個有淺灘");
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
                    // 第 08-18a 章：直走的兩格陸地一定有野地段；斜走要兩側的直鄰格也都是陸地。
                    var diagonal = dq != 0 && dr != 0;
                    var open = !diagonal || (Neighbour(map, cell, dq, 0) is { Terrain: not DemoTerrain.Water }
                        && Neighbour(map, cell, 0, dr) is { Terrain: not DemoTerrain.Water });
                    if (cell.Terrain != DemoTerrain.Water && next.Terrain != DemoTerrain.Water && open)
                        Assert.Equal(DemoRouteKind.Wild, segment!.Kind);
                    else if (diagonal)
                        Assert.Null(segment);
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
        // 第 08-18a 章：斜角段是直走的 1.5 倍（無條件進位）。
        Assert.All(map.Routes.Segments, s => Assert.Equal(s.A.Column != s.B.Column && s.A.Row != s.B.Row
            ? DemoRouteNetwork.DiagonalCost(settings.CostOf(s.Kind)) : settings.CostOf(s.Kind), s.Cost));
        Assert.Contains(map.Routes.Segments, s => s.A.Column != s.B.Column && s.A.Row != s.B.Row);
        Assert.Equal(3, DemoRouteNetwork.DiagonalCost(2));
        Assert.Equal(2, DemoRouteNetwork.DiagonalCost(1));
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
