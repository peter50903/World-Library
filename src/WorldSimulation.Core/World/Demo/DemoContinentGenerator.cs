using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 06-4 章【TEMP 規則】中、大、巨大地圖的高度圖大陸（參考 WorldBox、Fantasy Map Simulator：先有高度，地形之間才有因果）。
/// ① 三層平滑雜訊產生高度，四邊往下壓，邊緣多半是海；② 最高的 <see cref="TempLandPercent"/>% 是陸地，其餘是海（含內陸湖）；
/// ③ 陸地中最高的 <see cref="TempHillPercent"/>% 是丘陵（沿高處成山脈），丘陵 1/3 有礦脈；
/// ④ 河從丘陵附近出發，每步往最低的相鄰陸地走，到海或匯入別的河為止；每 <see cref="TempFordSpacing"/> 格找一處淺灘；
/// ⑤ 森林與草原沿用第 06-2 章的擲硬幣＋多數決平滑（第 06-4 章第 3 步再改成依濕度）。
/// 只用四種既有地形，不加新規則。全部用地理流的實體局部亂數（不推進計數器），同種子同地圖。高度只在生成時用，不存。
/// </summary>
public static class DemoContinentGenerator
{
    /// <summary>【TEMP】陸地佔全圖的比例（%）。</summary>
    public const int TempLandPercent = 55;

    /// <summary>【TEMP】陸地中最高的多少 % 是丘陵。</summary>
    public const int TempHillPercent = 15;

    /// <summary>【TEMP】河道上每隔幾格找一處淺灘。</summary>
    public const int TempFordSpacing = 8;

    /// <summary>【TEMP】兩條河的源頭至少相隔幾格。</summary>
    public const int TempRiverSourceSpacing = 6;

    /// <summary>【TEMP】長度不到幾格的河不留（卡在低窪處的短溝）。</summary>
    public const int TempMinRiverLength = 3;

    /// <summary>【TEMP】離中心多遠（d²，四邊中點＝1000000）開始往下壓。</summary>
    public const long TempFalloffStart = 300_000;

    private const string Namespace = "TEMP-CONTINENT-v1";
    private const long Unit = 1000; // 一格寬＝1000（定點數，避免浮點）

    public static DemoHexCell[] Generate(RngRegistry rng, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var random = EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Geography, Namespace);
        ulong Draw(ulong index) => random.GetUInt64(index);
        var count = columns * rows;

        // ① 高度（0～1000，邊緣往下壓）。
        var height = new long[count];
        var spacing = Math.Max(4, columns / 5) * Unit;
        var cx = (columns - 1) * Unit / 2;
        var cy = (rows - 1) * 866L / 2;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var x = col * Unit + (row & 1) * Unit / 2;
                var y = row * 866L; // 尖頂六角：列距＝√3/2 格寬
                var h = (4 * Noise(Draw, 0, x, y, spacing) + 2 * Noise(Draw, 1, x, y, Math.Max(2 * Unit, spacing / 2))
                    + Noise(Draw, 2, x, y, Math.Max(2 * Unit, spacing / 4))) / 7;
                // 橢圓下壓：離中心越遠越低（d² 以 1000² 為單位，四邊中點＝1000000），海岸自然成圓弧而不是方框。
                var dx = (x - cx) * 1000 / cx;
                var dy = (y - cy) * 1000 / cy;
                var d2 = dx * dx + dy * dy;
                if (d2 > TempFalloffStart)
                    h -= (d2 - TempFalloffStart) * 800 / (1_000_000 - TempFalloffStart);
                height[row * columns + col] = h;
            }
        }

        // ⑤ 森林與草原（先擲再平滑，海之後蓋上）。
        var raw = Enumerable.Range(0, count).Select(i => (DemoTerrain)(Draw(2_000_000UL + (ulong)i) % 2UL)).ToArray();
        var land = DemoHexWorld.SmoothLand(raw, columns, rows);

        // ② 海平面：最高的 TempLandPercent% 是陸地（同高時依格編號，結果固定）。
        var order = Enumerable.Range(0, count).OrderByDescending(i => height[i]).ThenBy(i => i).ToArray();
        var landCount = Math.Max(1, count * TempLandPercent / 100);
        var isLand = new bool[count];
        foreach (var i in order.Take(landCount))
            isLand[i] = true;

        // ③ 丘陵：陸地中最高的一段。
        var hillCount = landCount * TempHillPercent / 100;
        var terrain = new DemoTerrain[count];
        for (var i = 0; i < count; i++)
            terrain[i] = isLand[i] ? land[i] : DemoTerrain.Water;
        foreach (var i in order.Take(hillCount))
            terrain[i] = DemoTerrain.Hills;

        var cells = new DemoHexCell[count];
        for (var row = 0; row < rows; row++)
            for (var col = 0; col < columns; col++)
            {
                var i = row * columns + col;
                cells[i] = new DemoHexCell(col, row, col - (row - (row & 1)) / 2, row, terrain[i], Sea: !isLand[i]);
            }

        // ④ 河流與淺灘。
        var byAxial = cells.Select((c, i) => (c, i)).ToDictionary(x => (x.c.AxialQ, x.c.AxialR), x => x.i);
        int[] Neighbours(int i) => DemoRouteNetwork.Directions
            .Select(d => byAxial.TryGetValue((cells[i].AxialQ + d.Dq, cells[i].AxialR + d.Dr), out var n) ? n : -1).ToArray();
        var hills = order.Take(hillCount).ToArray();
        var sources = new List<int>();
        var rivers = DemoHexWorld.RiversFor(columns);
        for (var k = 0; k < rivers && hills.Length > 0; k++)
        {
            // 每條河最多試 16 個源頭：要和別的源頭夠遠，而且走得出夠長的河（卡在低窪處的短溝不算）。
            for (ulong attempt = 0; attempt < 16; attempt++)
            {
                var candidate = hills[(int)(Draw(4_000_000UL + (ulong)k * 16 + attempt) % (ulong)hills.Length)];
                if (!sources.All(s => DemoWorldSites.HexDistance(cells[s], cells[candidate]) >= TempRiverSourceSpacing))
                    continue;
                var path = Walk(candidate);
                if (path.Count < TempMinRiverLength)
                    continue;
                sources.Add(candidate);
                foreach (var i in path)
                    cells[i] = cells[i] with { Terrain = DemoTerrain.Water, MainWaterway = true };
                for (var p = TempFordSpacing / 2; p < path.Count; p += TempFordSpacing)
                {
                    var ford = path.Skip(p).Take(TempFordSpacing).FirstOrDefault(i => IsCrossing(i), -1);
                    if (ford >= 0)
                        cells[ford] = cells[ford] with { Shallow = true };
                }
                break;
            }
        }

        // 河道通過的丘陵已變成水；礦脈只放在剩下的丘陵（1/3，與第 06-3 章相同）。
        for (var i = 0; i < count; i++)
            if (cells[i].Terrain == DemoTerrain.Hills && Draw(3_000_000UL + (ulong)i) % (ulong)DemoHexWorld.TempOreOneIn == 0)
                cells[i] = cells[i] with { Ore = true };
        return cells;

        // 從源頭每步往最低的相鄰陸地走，到海（河口）或匯入別的河為止；源頭本身留著是丘陵。
        List<int> Walk(int source)
        {
            var path = new List<int>();
            var visited = new HashSet<int> { source };
            var at = source;
            for (var step = 0; step < count; step++)
            {
                var around = Neighbours(at);
                if (path.Count > 0 && around.Any(n => n >= 0 && (cells[n].Sea || (cells[n].MainWaterway && !path.Contains(n)))))
                    break;
                var next = around.Where(n => n >= 0 && !visited.Contains(n) && !cells[n].Sea && !cells[n].MainWaterway)
                    .OrderBy(n => height[n]).ThenBy(n => n).DefaultIfEmpty(-1).First();
                if (next < 0)
                    break;
                visited.Add(next);
                path.Add(next);
                at = next;
            }
            return path;
        }

        // 渡口：不臨海，兩岸（不相鄰的兩個方向）都有陸地——涉水過河才有意義。
        bool IsCrossing(int i)
        {
            var around = Neighbours(i);
            if (around.Any(n => n >= 0 && cells[n].Sea))
                return false;
            var landDirs = Enumerable.Range(0, 6).Where(d => around[d] >= 0 && cells[around[d]].Terrain != DemoTerrain.Water).ToList();
            return landDirs.Any(a => landDirs.Any(b => Math.Abs(a - b) is >= 2 and <= 4));
        }
    }

    /// <summary>一層平滑雜訊（格點值 0～1000，雙線性＋平滑插值；定點數）。</summary>
    private static long Noise(Func<ulong, ulong> draw, int octave, long x, long y, long spacing)
    {
        long Lattice(long gx, long gy) => (long)(draw(10_000_000UL * (ulong)(octave + 1) + (ulong)gy * 4096UL + (ulong)gx) % 1001UL);
        static long Fade(long t) => t * t * (3000 - 2 * t) / 1_000_000;
        static long Lerp(long a, long b, long t) => a + (b - a) * t / 1000;
        var gx = x / spacing;
        var gy = y / spacing;
        var fx = Fade(x % spacing * 1000 / spacing);
        var fy = Fade(y % spacing * 1000 / spacing);
        return Lerp(Lerp(Lattice(gx, gy), Lattice(gx + 1, gy), fx), Lerp(Lattice(gx, gy + 1), Lattice(gx + 1, gy + 1), fx), fy);
    }
}
