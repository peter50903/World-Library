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
        // 陸地約佔四成多（海平面依比例切，河道另佔一些）。
        var land = cells.Count(c => c.Terrain != DemoTerrain.Water);
        Assert.InRange(land * 100 / cells.Count, DemoContinentGenerator.TempLandPercent - 10, DemoContinentGenerator.TempLandPercent);
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

    /// <summary>第 06-5 章：大、巨大地圖有多塊大陸（被海隔開、每塊至少 12 格）。</summary>
    [Theory]
    [InlineData(1UL, DemoMapSize.Large)]
    [InlineData(2UL, DemoMapSize.Large)]
    [InlineData(3UL, DemoMapSize.Huge)]
    [InlineData(4UL, DemoMapSize.Huge)]
    public void BigWorldsHaveSeveralContinents(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        var seen = new HashSet<(int, int)>();
        var masses = new List<int>();
        foreach (var start in map.Cells.Where(c => !c.Sea))
        {
            if (!seen.Add((start.Column, start.Row)))
                continue;
            var queue = new Queue<DemoHexCell>();
            queue.Enqueue(start);
            var n = 0;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                n++;
                foreach (var d in DemoRouteNetwork.Directions)
                    if (map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is { Sea: false } next && seen.Add((next.Column, next.Row)))
                        queue.Enqueue(next);
            }
            masses.Add(n);
        }
        Assert.True(masses.Count(m => m >= 12) >= 2, $"大陸塊（格數）：{string.Join("、", masses.OrderByDescending(m => m))}");
    }

    [Fact]
    public void RiversFlowDownFromHillsToTheSeaOrAnotherRiver()
    {
        var map = DemoHexWorld.Generate(5, DemoMapSize.Large);
        DemoHexCell? At(DemoHexCell c, (int Dq, int Dr, string Name) d) => map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr);
        var river = map.Cells.Where(c => c.MainWaterway).ToList();
        // 每個河流格都和另一個河流格或丘陵（源頭）上下左右相連（第 08-18a 章：河道只走四方向）；至少一個河口臨海。
        Assert.All(river, c => Assert.Contains(DemoRouteNetwork.Orthogonal, d => At(c, d) is { MainWaterway: true } or { Terrain: DemoTerrain.Hills }));
        Assert.Contains(river, c => DemoRouteNetwork.Orthogonal.Any(d => At(c, d) is { Sea: true }));
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

/// <summary>第 06-4 章第 3 步：森林依濕度；第 06-6 章改成風帶與雨影——森林成片（迎風面多、背風與內陸草原），不再沿海岸一圈。</summary>
public sealed class DemoForestMoistureTests
{
    [Theory]
    [InlineData(1UL, DemoMapSize.Large)]
    [InlineData(2UL, DemoMapSize.Huge)]
    [InlineData(6UL, DemoMapSize.Medium)]
    public void ForestsFormLargePatches(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        var plains = map.Cells.Where(c => c.Terrain is DemoTerrain.Forest or DemoTerrain.Grassland).ToList();
        Assert.InRange(plains.Average(c => c.Terrain == DemoTerrain.Forest ? 1.0 : 0.0), 0.40, 0.50);
        // 成片：相鄰平地同為森林或草原的比例。
        double same = 0, pairs = 0;
        foreach (var c in plains)
            foreach (var d in DemoRouteNetwork.Directions)
                if (map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is { Terrain: DemoTerrain.Forest or DemoTerrain.Grassland } n)
                {
                    pairs++;
                    if (n.Terrain == c.Terrain) same++;
                }
        Assert.True(same / pairs >= 0.7, $"同地形相鄰比例 {same / pairs:F2}");
    }
}

/// <summary>第 06-6 章：自然的世界——大陸不是橢圓（有半島與海灣）、丘陵沿山脈成串。</summary>
public sealed class DemoNaturalWorldTests
{
    /// <summary>大陸的實心度（格數÷凸包面積）：橢圓約 0.92，有半島、海灣、旁支的大陸低很多。</summary>
    [Theory]
    [InlineData(DemoMapSize.Large, new ulong[] { 1, 2, 3, 4 })]
    [InlineData(DemoMapSize.Huge, new ulong[] { 1, 2 })]
    public void ContinentsHavePeninsulasAndBays(DemoMapSize size, ulong[] seeds)
    {
        var solidity = seeds.Select(seed =>
        {
            var map = DemoHexWorld.Generate(seed, size);
            return Components(map, c => !c.Sea).Where(m => m.Count >= 50).Average(m => m.Count / HullArea(m));
        }).ToList();
        Assert.True(solidity.Average() <= 0.86, $"大陸實心度 {string.Join("、", solidity.Select(v => v.ToString("F2")))}（橢圓約 0.92）");
    }

    /// <summary>丘陵沿山脈成串：最長的一串丘陵（≥20 格）細長度（最遠兩格距離÷√格數）至少 2（一團圓的約 1.1～1.3）。</summary>
    [Theory]
    [InlineData(1UL, DemoMapSize.Large)]
    [InlineData(2UL, DemoMapSize.Large)]
    [InlineData(3UL, DemoMapSize.Large)]
    [InlineData(3UL, DemoMapSize.Huge)]
    public void HillsRunInMountainRanges(ulong seed, DemoMapSize size)
    {
        var map = DemoHexWorld.Generate(seed, size);
        var ranges = Components(map, c => c.Terrain == DemoTerrain.Hills).Where(m => m.Count >= 20).ToList();
        Assert.NotEmpty(ranges);
        double Elongation(List<DemoHexCell> m) => m.Max(a => m.Max(b => Math.Sqrt(Math.Pow(a.Column - b.Column, 2) + Math.Pow(a.Row - b.Row, 2)))) / Math.Sqrt(m.Count);
        var longest = ranges.Max(Elongation);
        Assert.True(longest >= 2.0, $"最細長的山脈 {longest:F2}");
    }

    private static List<List<DemoHexCell>> Components(DemoHexWorld map, Func<DemoHexCell, bool> wanted)
    {
        var seen = new HashSet<(int, int)>();
        var all = new List<List<DemoHexCell>>();
        foreach (var start in map.Cells.Where(wanted))
        {
            if (!seen.Add((start.Column, start.Row)))
                continue;
            var part = new List<DemoHexCell>();
            var queue = new Queue<DemoHexCell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                part.Add(c);
                foreach (var d in DemoRouteNetwork.Directions)
                    if (map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is { } next && wanted(next) && seen.Add((next.Column, next.Row)))
                        queue.Enqueue(next);
            }
            all.Add(part);
        }
        return all;
    }

    /// <summary>格子四角的凸包面積（單調鏈）。</summary>
    private static double HullArea(List<DemoHexCell> cells)
    {
        var points = cells.SelectMany(c => new[] { (X: c.Column, Y: c.Row), (X: c.Column + 1, Y: c.Row), (X: c.Column, Y: c.Row + 1), (X: c.Column + 1, Y: c.Row + 1) })
            .Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        static long Cross((int X, int Y) o, (int X, int Y) a, (int X, int Y) b) => (long)(a.X - o.X) * (b.Y - o.Y) - (long)(a.Y - o.Y) * (b.X - o.X);
        var hull = new List<(int X, int Y)>();
        foreach (var pass in new[] { points, Enumerable.Reverse(points).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        double area = 0;
        for (var i = 0; i < hull.Count; i++)
        {
            var (a, b) = (hull[i], hull[(i + 1) % hull.Count]);
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return Math.Abs(area) / 2;
    }
}
