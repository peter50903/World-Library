using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 06-4 章【TEMP 規則】中、大、巨大地圖的高度圖大陸（參考 WorldBox、Fantasy Map Simulator：先有高度，地形之間才有因果）。
/// ① 三層平滑雜訊產生高度，四邊往下壓，邊緣多半是海；② 最高的 <see cref="TempLandPercent"/>% 是陸地，其餘是海（含內陸湖）；
/// ③ 陸地中最高的 <see cref="TempHillPercent"/>% 是丘陵（沿高處成山脈），丘陵 1/3 有礦脈；
/// ④ 河從丘陵附近出發，每步往最低的相鄰陸地走，到海或匯入別的河為止；每 <see cref="TempFordSpacing"/> 格找一處淺灘；
/// ⑤ 森林依濕度（離海與河越近越濕，加平滑雜訊；第 06-4 章第 3 步）。
/// 只用四種既有地形，不加新規則。全部用地理流的實體局部亂數（不推進計數器），同種子同地圖。高度只在生成時用，不存。
/// </summary>
public static class DemoContinentGenerator
{
    /// <summary>【TEMP】陸地佔全圖的比例（%）；第 06-5 章多大陸後降低，留出海峽與大洋。</summary>
    public const int TempLandPercent = 44;

    /// <summary>【TEMP】第 06-5 章：高度裡起伏雜訊佔幾 %（其餘是大陸棚）；海岸扭曲幾格。</summary>
    public const int TempReliefPercent = 40;
    public const long TempWarpCells = 2;

    /// <summary>【TEMP】第 06-5 章：大陸數——中地圖 2 塊、大地圖 3～4 塊、巨大地圖 4～6 塊。</summary>
    public static int ContinentCount(int columns, Func<ulong, ulong> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        return columns <= 24 ? 2 : columns <= 48 ? 3 + (int)(draw(6_000_000UL) % 2UL) : 4 + (int)(draw(6_000_000UL) % 3UL);
    }

    /// <summary>
    /// 第 06-5 章：大陸中心與半徑（定點數）。中心落在四邊各 18% 內側，彼此至少相隔「平均每塊大陸的寬度」的 85%；
    /// 半徑依每塊大陸的平均面積 ×0.7～1.0，長寬比 0.7～1.4。
    /// </summary>
    private static List<(long X, long Y, long Rx, long Ry)> PlaceContinents(int columns, int rows, int count, Func<ulong, ulong> draw)
    {
        var centers = new List<(long X, long Y, long Rx, long Ry)>();
        var area = (long)columns * rows * Unit * Unit;
        var each = (long)Math.Sqrt(area / count); // 每塊大陸平均佔的邊長
        var minGap = each * 85 / 100;
        long Pick(ulong index, long size) => size * 18 / 100 + (long)(draw(index) % (ulong)Math.Max(1, size * 64 / 100));
        for (var k = 0; k < count; k++)
        {
            (long X, long Y) best = (Pick(6_100_000UL + (ulong)k * 128, columns * Unit), Pick(6_100_001UL + (ulong)k * 128, rows * Unit));
            for (ulong attempt = 0; attempt < 48; attempt++)
            {
                var at = (Pick(6_100_000UL + (ulong)k * 128 + attempt * 2, columns * Unit), Pick(6_100_001UL + (ulong)k * 128 + attempt * 2, rows * Unit));
                best = at;
                if (centers.All(c => Distance(c.X, c.Y, at.Item1, at.Item2) >= minGap))
                    break;
            }
            var size = each * (70 + (long)(draw(6_200_000UL + (ulong)k) % 31UL)) / 100 / 2;
            var aspect = 70 + (long)(draw(6_300_000UL + (ulong)k) % 71UL); // 0.70～1.40
            centers.Add((best.X, best.Y, Math.Max(2 * Unit, size * aspect / 100), Math.Max(2 * Unit, size * 100 / aspect)));
        }
        return centers;
    }

    private static long Distance(long ax, long ay, long bx, long by) => (long)Math.Sqrt((double)((ax - bx) * (ax - bx) + (ay - by) * (ay - by)));

    /// <summary>【TEMP】陸地中最高的多少 % 是丘陵。</summary>
    public const int TempHillPercent = 15;

    /// <summary>【TEMP】河道上每隔幾格找一處淺灘。</summary>
    public const int TempFordSpacing = 8;

    /// <summary>【TEMP】兩條河的源頭至少相隔幾格。</summary>
    public const int TempRiverSourceSpacing = 6;

    /// <summary>【TEMP】長度不到幾格的河不留（卡在低窪處的短溝）。</summary>
    public const int TempMinRiverLength = 3;

    /// <summary>【TEMP】第 3 步森林：平地中最濕的多少 % 是森林；離水每格乾多少、最多算幾格；雜訊權重（%）。</summary>
    public const int TempForestPercent = 45;
    public const int TempMoisturePerStep = 120;
    public const int TempMoistureReach = 6;
    public const int TempMoistureNoisePercent = 120;


    private const string Namespace = "TEMP-CONTINENT-v1"; // 第 06-5 章沿用同一命名空間（新的抽樣索引不與舊的重疊）
    private const long Unit = 1000; // 一格寬＝1000（定點數，避免浮點）

    public static DemoHexCell[] Generate(RngRegistry rng, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var random = EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Geography, Namespace);
        ulong Draw(ulong index) => random.GetUInt64(index);
        var count = columns * rows;

        // ① 高度。第 06-5 章（試玩回饋 2026-11-11 之 6、7：世界不像大陸、要像現實一樣有多個大陸）：
        // 先放幾塊大陸的中心（彼此夠遠），每塊大陸是被雜訊扭曲過的橢圓「大陸棚」，再疊上三層平滑雜訊的起伏；
        // 大陸之間自然是海峽與大洋，海岸線因扭曲而有半島與海灣。地圖四邊兩格內往下壓，外圍一定是海。
        var height = new long[count];
        var spacing = Math.Max(4, columns / 5) * Unit;
        var continents = ContinentCount(columns, Draw);
        var centers = PlaceContinents(columns, rows, continents, Draw);
        var warp = Math.Max(2 * Unit, spacing / 2);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var x = col * Unit;
                var y = row * Unit; // 第 08-18a 章：方格，列距＝欄距
                var relief = (4 * Noise(Draw, 0, x, y, spacing) + 2 * Noise(Draw, 1, x, y, Math.Max(2 * Unit, spacing / 2))
                    + Noise(Draw, 2, x, y, Math.Max(2 * Unit, spacing / 4))) / 7;
                // 扭曲座標（約 ±TempWarpCells 格）再量到每塊大陸中心的橢圓距離，取最近的一塊。
                var wx = x + (Noise(Draw, 6, x, y, warp) - 500) * TempWarpCells * Unit / 500;
                var wy = y + (Noise(Draw, 7, x, y, warp) - 500) * TempWarpCells * Unit / 500;
                long shelf = 0;
                foreach (var (ccx, ccy, rx, ry) in centers)
                {
                    var dx = (wx - ccx) * 1000 / rx;
                    var dy = (wy - ccy) * 1000 / ry;
                    shelf = Math.Max(shelf, 1000 - Math.Min(1000, (dx * dx + dy * dy) / 1000));
                }
                var h = relief * TempReliefPercent / 100 + shelf * (100 - TempReliefPercent) / 100;
                var edge = Math.Min(Math.Min(col, columns - 1 - col), Math.Min(row, rows - 1 - row));
                if (edge < 2)
                    h -= (2 - edge) * 300;
                height[row * columns + col] = h;
            }
        }

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
            terrain[i] = isLand[i] ? DemoTerrain.Grassland : DemoTerrain.Water; // 森林在河流定案後依濕度決定（⑤）
        foreach (var i in order.Take(hillCount))
            terrain[i] = DemoTerrain.Hills;

        var cells = new DemoHexCell[count];
        for (var row = 0; row < rows; row++)
            for (var col = 0; col < columns; col++)
            {
                var i = row * columns + col;
                cells[i] = new DemoHexCell(col, row, col, row, terrain[i], Sea: !isLand[i]);
            }

        // ④ 河流與淺灘。
        var byAxial = cells.Select((c, i) => (c, i)).ToDictionary(x => (x.c.AxialQ, x.c.AxialR), x => x.i);
        int[] Neighbours(int i) => DemoRouteNetwork.Directions
            .Select(d => byAxial.TryGetValue((cells[i].AxialQ + d.Dq, cells[i].AxialR + d.Dr), out var n) ? n : -1).ToArray();
        // 第 08-18a 章：河道只走上下左右（順序：東、南、西、北）。
        int[] Orthogonal(int i) => DemoRouteNetwork.Orthogonal
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

        // ⑤ 第 06-4 章第 3 步：森林依濕度——離水（海、河）越近越濕，加上平滑雜訊；
        // 草原與森林中最濕的 TempForestPercent% 是森林。結果是沿海、沿河的林帶與內陸的大草原，而不是零碎斑點。
        var wet = new int[count];
        Array.Fill(wet, int.MaxValue);
        var queue = new Queue<int>();
        for (var i = 0; i < count; i++)
            if (cells[i].Terrain == DemoTerrain.Water)
            {
                wet[i] = 0;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            foreach (var n in Neighbours(i))
                if (n >= 0 && wet[n] > wet[i] + 1)
                {
                    wet[n] = wet[i] + 1;
                    queue.Enqueue(n);
                }
        }
        var rawNoise = Enumerable.Range(0, count).Select(i => (long)(Draw(5_000_000UL + (ulong)i) % 1000UL)).ToArray();
        long Moisture(int i)
        {
            var around = Neighbours(i).Where(n => n >= 0).ToArray();
            var noise = (rawNoise[i] + around.Sum(n => rawNoise[n])) / (1 + around.Length);
            return 1000 - Math.Min(wet[i], TempMoistureReach) * TempMoisturePerStep + noise * TempMoistureNoisePercent / 100;
        }
        var plains = Enumerable.Range(0, count).Where(i => cells[i].Terrain == DemoTerrain.Grassland).ToArray();
        foreach (var i in plains.OrderByDescending(Moisture).ThenBy(i => i).Take(plains.Length * TempForestPercent / 100))
            cells[i] = cells[i] with { Terrain = DemoTerrain.Forest };

        // 河道通過的丘陵已變成水；礦脈只放在剩下的丘陵（1/3，與第 06-3 章相同）。
        for (var i = 0; i < count; i++)
            if (cells[i].Terrain == DemoTerrain.Hills && Draw(3_000_000UL + (ulong)i) % (ulong)DemoHexWorld.TempOreOneIn == 0)
                cells[i] = cells[i] with { Ore = true };
        return cells;

        // 從源頭每步往最低的上下左右陸地走，到海（河口）或匯入別的河為止；源頭本身留著是丘陵。
        List<int> Walk(int source)
        {
            var path = new List<int>();
            var visited = new HashSet<int> { source };
            var at = source;
            for (var step = 0; step < count; step++)
            {
                var around = Orthogonal(at);
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

        // 渡口：不臨海，兩岸（東西兩側或南北兩側）都有陸地——涉水過河才有意義。
        bool IsCrossing(int i)
        {
            if (Neighbours(i).Any(n => n >= 0 && cells[n].Sea))
                return false;
            var around = Orthogonal(i); // 東、南、西、北
            bool Land(int d) => around[d] >= 0 && cells[around[d]].Terrain != DemoTerrain.Water;
            return (Land(0) && Land(2)) || (Land(1) && Land(3));
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
