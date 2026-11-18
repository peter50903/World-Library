using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 06-4 章【TEMP 規則】中、大、巨大地圖的高度圖大陸（參考 WorldBox、Fantasy Map Simulator：先有高度，地形之間才有因果）。
/// ① 高度＝大陸棚＋起伏雜訊，四邊往下壓，邊緣多半是海；② 最高的 <see cref="TempLandPercent"/>% 是陸地，其餘是海（含內陸湖）；
/// ③ 陸地中最崎嶇的 <see cref="TempHillPercent"/>% 是丘陵，丘陵 1/3 有礦脈；
/// ④ 河從丘陵附近出發，每步往最低的相鄰陸地走，到海或匯入別的河為止；每 <see cref="TempFordSpacing"/> 格找一處淺灘；
/// ⑤ 森林依濕度。
/// 第 06-6 章（試玩回饋 2026-11-18 之 6：世界地圖太假）：每塊大陸由 2～3 個轉了方向的橢圓組成、海岸兩層扭曲與五層雜訊（半島、海灣）、
/// 沿折線隆起的山脈（丘陵成山脈，不再是大陸中央一團）、大洋裡的群島、依風向與雨影的濕度（森林成片，不再沿海岸一圈）。
/// 只用四種既有地形，不加新規則。全部用地理流的實體局部亂數（不推進計數器），同種子同地圖。高度只在生成時用，不存。
/// </summary>
public static class DemoContinentGenerator
{
    /// <summary>【TEMP】陸地佔全圖的比例（%）；第 06-5 章多大陸後降低，留出海峽與大洋。</summary>
    public const int TempLandPercent = 44;

    /// <summary>【TEMP】第 06-5 章：高度裡起伏雜訊佔幾 %（其餘是大陸棚）；第 06-6 章 40→45、雜訊五層（權重 8:6:5:4:2，中細層多一點，海岸多半島與海灣）。</summary>
    public const int TempReliefPercent = 45;
    private static readonly int[] ReliefWeights = { 8, 6, 5, 4, 2 };

    /// <summary>【TEMP】第 06-6 章：海岸扭曲——大尺度＝欄數÷12 格（2～8 格）、細尺度＝欄數÷30 格（1～3 格，每 4 格起伏一次）。</summary>
    public static long CoastWarp(int columns) => Math.Clamp(columns * Unit / 12, 2 * Unit, 8 * Unit);
    public static long FineCoastWarp(int columns) => Math.Clamp(columns * Unit / 30, Unit, 3 * Unit);

    /// <summary>【TEMP】第 06-5 章：兩塊大陸的大陸棚值相差不到這個數的地方壓低成海峽（約兩格寬）；第 06-6 章再加 0～160（雜訊），海峽寬窄不一。</summary>
    public const long TempStraitWidth = 260;
    public const long TempStraitJitter = 160;

    /// <summary>【TEMP】第 06-6 章：每塊大陸除了主體（原橢圓的 85%），再長 1～2 個旁支：離中心 30～50% 半徑、大小 40～55%，各自轉一個方向。</summary>
    public const int TempMaxExtraLobes = 2;

    /// <summary>【TEMP】第 06-6 章：山脈——每塊大陸 1 條（大地圖以上 1～2 條），4 段折線（每段半徑的 30～45%、每段轉向 ±17°），半寬約 2 格（±30% 雜訊），最高加 450。</summary>
    public const int TempRidgeSegments = 4;
    public const long TempRidgeHalfWidth = 2 * Unit;
    public const long TempRidgeHeight = 450;

    /// <summary>【TEMP】第 06-6 章：群島——離大陸遠（大陸棚 &lt;250）、大尺度雜訊 &gt;780 的海域，細雜訊 &gt;760 的峰頂最多抬高 600；單格的小島不留。</summary>
    public const long TempArchipelagoMask = 780;
    public const long TempIslandPeak = 760;
    public const long TempIslandLift = 600;

    /// <summary>【TEMP】第 06-5 章：大陸數——中地圖 2 塊、大地圖 3～4 塊、巨大地圖 4～6 塊。</summary>
    public static int ContinentCount(int columns, Func<ulong, ulong> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        return columns <= 24 ? 2 : columns <= 72 ? 3 + (int)(draw(6_000_000UL) % 2UL) : 4 + (int)(draw(6_000_000UL) % 3UL);
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
            // 第 08-20h 章：大、巨大地圖多找幾次（48 次都太近時大陸會連成一塊）；前 48 次的抽樣不變，中地圖結果不變。
            var attempts = columns > 48 ? 400UL : 48UL;
            for (ulong attempt = 0; attempt < attempts; attempt++)
            {
                var at = attempt < 48
                    ? (Pick(6_100_000UL + (ulong)k * 128 + attempt * 2, columns * Unit), Pick(6_100_001UL + (ulong)k * 128 + attempt * 2, rows * Unit))
                    : (Pick(6_400_000UL + (ulong)k * 1024 + attempt * 2, columns * Unit), Pick(6_400_001UL + (ulong)k * 1024 + attempt * 2, rows * Unit));
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

    /// <summary>【TEMP】第 08-20h 章：大陸連成一塊時最多重來幾次。</summary>
    public const int TempContinentRetries = 8;

    /// <summary>至少 <paramref name="minimum"/> 格的陸塊有幾塊（八鄰相連；與測試的算法相同）。</summary>
    private static int SeparateMasses(bool[] isLand, int columns, int rows, int minimum)
    {
        var seen = new bool[isLand.Length];
        var big = 0;
        var stack = new Stack<int>();
        for (var start = 0; start < isLand.Length; start++)
        {
            if (!isLand[start] || seen[start])
                continue;
            seen[start] = true;
            stack.Push(start);
            var size = 0;
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                size++;
                int r = i / columns, c = i % columns;
                for (var dr = -1; dr <= 1; dr++)
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= columns)
                            continue;
                        var j = rr * columns + cc;
                        if (isLand[j] && !seen[j])
                        {
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
            }
            if (size >= minimum)
                big++;
        }
        return big;
    }

    private static long Distance(long ax, long ay, long bx, long by) => (long)Math.Sqrt((double)((ax - bx) * (ax - bx) + (ay - by) * (ay - by)));

    /// <summary>【TEMP】陸地中最高的多少 % 是丘陵（第 06-6 章：改看崎嶇度——山脈＋起伏）。</summary>
    public const int TempHillPercent = 15;

    /// <summary>【TEMP】河道上每隔幾格找一處淺灘。</summary>
    public const int TempFordSpacing = 8;

    /// <summary>【TEMP】兩條河的源頭至少相隔幾格。</summary>
    public const int TempRiverSourceSpacing = 6;

    /// <summary>【TEMP】長度不到幾格的河不留（卡在低窪處的短溝）。</summary>
    public const int TempMinRiverLength = 3;

    /// <summary>【TEMP】第 3 步森林：平地中最濕的多少 % 是森林。</summary>
    public const int TempForestPercent = 45;

    /// <summary>
    /// 【TEMP】第 06-6 章：濕度（取代第 06-4 章「離水越近越濕」的一圈林帶）——風從海上帶來濕氣（北、南三分之一吹西風，中間三分之一吹東風），
    /// 每過一格陸地乾 15、翻過丘陵乾 200（雨影）、經過海面回 250；陸地上 5×5 平滑兩次（海上的濕氣不往四周海岸滲）；赤道帶濕、副熱帶乾（±150）；
    /// 臨水 1、2 格加 40、20；兩層平滑雜訊（6 格、2.5 格）×110% 讓森林成片、有林間空地。
    /// </summary>
    public const int TempDryPerCell = 15;
    public const int TempRainShadow = 200;
    public const int TempSeaRecharge = 250;
    public const int TempShoreBonus = 40;
    public const int TempLatitudeSwing = 150;
    public const int TempPatchNoisePercent = 110;


    private const string Namespace = "TEMP-CONTINENT-v1"; // 第 06-5 章沿用同一命名空間（新的抽樣索引不與舊的重疊）
    private const long Unit = 1000; // 一格寬＝1000（定點數，避免浮點）

    public static DemoHexCell[] Generate(RngRegistry rng, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var random = EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Geography, Namespace);
        // 第 08-20h 章：大、巨大地圖的大陸連成一塊時換一組抽樣重來（salt 往後移；最多 TempContinentRetries 次）。中地圖以下不重來。
        var salt = 0UL;
        ulong Draw(ulong index) => random.GetUInt64(unchecked(index + salt));
        var count = columns * rows;

        // ① 高度。第 06-5 章（試玩回饋 2026-11-11 之 6、7：世界不像大陸、要像現實一樣有多個大陸）：
        // 先放幾塊大陸的中心（彼此夠遠），每塊大陸是被雜訊扭曲過的「大陸棚」，再疊上平滑雜訊的起伏；
        // 大陸之間自然是海峽與大洋。地圖四邊往下壓，外圍一定是海。
        // 第 06-6 章（試玩回饋 2026-11-18 之 6：世界地圖太假）：大陸由主體＋旁支的轉向橢圓組成、海岸兩層扭曲、
        // 五層雜訊（中細層較重）、沿折線隆起的山脈、遠洋群島；崎嶇度（山脈＋起伏，不含大陸棚）決定哪裡是丘陵。
        long[] height;
        long[] rugged;
        int[] order;
        int landCount;
        bool[] isLand;
        for (var retry = 0; ; retry++)
        {
            height = new long[count];
            rugged = new long[count];
            var spacing = Math.Max(4, columns / 5) * Unit;
            var continents = ContinentCount(columns, Draw);
            var centers = PlaceContinents(columns, rows, continents, Draw);
            var lobes = LobesFor(centers, columns, rows, Draw);
            var ridges = RidgesFor(centers, lobes, columns, Draw);
            var warp = Math.Max(2 * Unit, spacing / 2);
            var coastWarp = CoastWarp(columns);
            var fineWarp = FineCoastWarp(columns);
            var archipelago = Math.Max(6, columns / 4) * Unit;
            var reliefTotal = ReliefWeights.Sum();
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < columns; col++)
                {
                    var x = col * Unit;
                    var y = row * Unit; // 第 08-18a 章：方格，列距＝欄距
                    long relief = 0;
                    for (var o = 0; o < ReliefWeights.Length; o++)
                        relief += ReliefWeights[o] * Noise(Draw, o, x, y, Math.Max(3 * Unit / 2, spacing >> o));
                    relief /= reliefTotal;
                    // 扭曲座標（大尺度＋細尺度）再量到每塊大陸（主體與旁支取最大）的距離，取最近的兩塊。
                    var wx = x + (Noise(Draw, 6, x, y, warp) - 500) * coastWarp / 500 + (Noise(Draw, 8, x, y, 4 * Unit) - 500) * fineWarp / 500;
                    var wy = y + (Noise(Draw, 7, x, y, warp) - 500) * coastWarp / 500 + (Noise(Draw, 9, x, y, 4 * Unit) - 500) * fineWarp / 500;
                    long shelf = 0, second = 0;
                    foreach (var continent in lobes)
                    {
                        long here = 0;
                        foreach (var lobe in continent)
                            here = Math.Max(here, lobe.Shelf(wx, wy));
                        if (here > shelf)
                            (shelf, second) = (here, shelf);
                        else if (here > second)
                            second = here;
                    }
                    // 四邊 4 格內大陸棚漸弱（外圍一定是海，陸地不會被地圖邊切成直線）。
                    var edge = Math.Min(Math.Min(col, columns - 1 - col), Math.Min(row, rows - 1 - row));
                    var fade = Math.Min(1000L, edge * 1000L / 4);
                    shelf = shelf * fade / 1000;
                    second = second * fade / 1000;
                    // 起伏：靠近大陸（大陸棚 ≥150）全額，大洋裡只剩三成（遠洋不會冒出零碎的小島）。
                    var h = relief * TempReliefPercent / 100 * (300 + Math.Min(shelf, 150) * 700 / 150) / 1000
                        + shelf * (100 - TempReliefPercent) / 100;
                    // 兩塊大陸之間（離兩邊差不多近）壓低成海峽，大陸不會連成一片；寬窄依雜訊。
                    var strait = TempStraitWidth + Noise(Draw, 16, x, y, 3 * Unit) * TempStraitJitter / 1000;
                    if (shelf - second < strait)
                        h -= (strait - (shelf - second)) * 2;
                    // 山脈：離折線越近越高（半寬依雜訊），只在大陸上（大陸棚 200→600 漸強）。
                    var distance = long.MaxValue;
                    foreach (var ridge in ridges)
                        for (var k = 0; k + 1 < ridge.Length; k++)
                            distance = Math.Min(distance, SegmentDistance(wx, wy, ridge[k], ridge[k + 1]));
                    var width = TempRidgeHalfWidth * (700 + Noise(Draw, 13, x, y, 4 * Unit) * 600 / 1000) / 1000;
                    var ridgeness = distance == long.MaxValue ? 0 : Math.Max(0, 1000 - distance * 1000 / Math.Max(1, width));
                    var landish = Math.Clamp((shelf - 200) * 1000 / 400, 0, 1000);
                    var lift = ridgeness * landish / 1000 * TempRidgeHeight / 1000;
                    h += lift;
                    rugged[row * columns + col] = lift + relief * TempReliefPercent / 100;
                    // 群島：遠洋裡某些海域（大尺度雜訊高）的細雜訊峰頂冒出小島。
                    var zone = Noise(Draw, 10, x, y, archipelago);
                    var peak = Noise(Draw, 11, x, y, 5 * Unit / 2);
                    if (shelf < 250 && edge >= 4 && zone > TempArchipelagoMask && peak > TempIslandPeak)
                        h += (peak - TempIslandPeak) * (zone - TempArchipelagoMask) / (1000 - TempArchipelagoMask) * TempIslandLift / (1000 - TempIslandPeak);
                    if (edge < 3)
                        h -= (3 - edge) * 250;
                    height[row * columns + col] = h;
                }
            }

            // ② 海平面：最高的 TempLandPercent% 是陸地（同高時依格編號，結果固定）；第 06-6 章：四周八格都是海的單格小島不留。
            order = Enumerable.Range(0, count).OrderByDescending(i => height[i]).ThenBy(i => i).ToArray();
            landCount = Math.Max(1, count * TempLandPercent / 100);
            isLand = new bool[count];
            foreach (var i in order.Take(landCount))
                isLand[i] = true;
            for (var i = 0; i < count; i++)
                if (isLand[i] && !AnyLandAround(isLand, i, columns, rows))
                    isLand[i] = false;
            landCount = isLand.Count(l => l);
            // 第 08-20h 章：大、巨大地圖的大陸連成一塊時換一組抽樣重來；第 06-6 章：小島不算，要有「大陸數 −2」塊（至少 2 塊）夠大的陸地。
            if (columns <= 48 || retry >= TempContinentRetries
                || SeparateMasses(isLand, columns, rows, Math.Max(12, landCount / (3 * continents))) >= Math.Max(2, continents - 2))
                break;
            salt = unchecked(salt + 0x1_0000_0000UL);
        }

        // ③ 丘陵：陸地中最崎嶇的一段（第 06-6 章：看山脈與起伏，不看大陸棚，所以丘陵沿山脈成串）。
        var hillCount = landCount * TempHillPercent / 100;
        var terrain = new DemoTerrain[count];
        for (var i = 0; i < count; i++)
            terrain[i] = isLand[i] ? DemoTerrain.Grassland : DemoTerrain.Water; // 森林在河流定案後依濕度決定（⑤）
        var hills = Enumerable.Range(0, count).Where(i => isLand[i]).OrderByDescending(i => rugged[i]).ThenBy(i => i).Take(hillCount).ToArray();
        foreach (var i in hills)
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

        // ⑤ 第 06-4 章第 3 步：森林依濕度；第 06-6 章改成風帶與雨影（森林成片，不再沿海岸一圈）。
        // 風從海上帶來濕氣：北、南三分之一吹西風，中間三分之一吹東風；每過一格陸地乾一點，翻過丘陵乾很多（雨影），經過海面回來。
        var humid = new long[count];
        for (var row = 0; row < rows; row++)
        {
            var easterly = row * 3 / rows == 1;
            long carry = 1000;
            for (var step = 0; step < columns; step++)
            {
                var i = row * columns + (easterly ? columns - 1 - step : step);
                if (cells[i].Sea)
                    carry = Math.Min(1000, carry + TempSeaRecharge);
                else
                    carry = Math.Max(0, carry - (cells[i].Terrain == DemoTerrain.Hills ? TempRainShadow : TempDryPerCell));
                humid[i] = carry;
            }
        }
        for (var pass = 0; pass < 2; pass++)
            humid = BoxBlur(humid, columns, rows, 2, i => !cells[i].Sea); // 風帶交界不留直線；只平均陸地，海上的濕氣不會讓每條海岸都變濕
        // 臨水（海、河、湖）1、2 格小加成。
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
        long Moisture(int i)
        {
            var (row, col) = (i / columns, i % columns);
            var shore = wet[i] == 1 ? TempShoreBonus : wet[i] == 2 ? TempShoreBonus / 2 : 0;
            var patch = (2 * Noise(Draw, 12, col * Unit, row * Unit, 6 * Unit) + Noise(Draw, 15, col * Unit, row * Unit, 5 * Unit / 2)) / 3;
            return humid[i] + shore + LatitudeMoisture(row, rows) + (patch - 500) * TempPatchNoisePercent / 100;
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

    /// <summary>第 06-6 章：大陸的一塊橢圓（中心、兩軸半徑、主軸方向〔單位向量×1000〕）。</summary>
    private readonly record struct Lobe(long X, long Y, long A, long B, long Ux, long Uy)
    {
        /// <summary>大陸棚值：中心 1000、橢圓邊上 0（1－r²）。</summary>
        public long Shelf(long wx, long wy)
        {
            long dx = wx - X, dy = wy - Y;
            var along = (dx * Ux + dy * Uy) / 1000;
            var across = (-dx * Uy + dy * Ux) / 1000;
            var ex = along * 1000 / A;
            var ey = across * 1000 / B;
            return 1000 - Math.Min(1000, (ex * ex + ey * ey) / 1000);
        }
    }

    /// <summary>亂數方向（單位向量×1000）；太短時取正東。</summary>
    private static (long X, long Y) Direction(Func<ulong, ulong> draw, ulong index)
    {
        var dx = (long)(draw(index) % 2001UL) - 1000;
        var dy = (long)(draw(index + 1) % 2001UL) - 1000;
        var length = (long)Math.Sqrt((double)(dx * dx + dy * dy));
        return length < 100 ? (1000, 0) : (dx * 1000 / length, dy * 1000 / length);
    }

    /// <summary>【TEMP】第 06-6 章：每塊大陸＝主體（原橢圓的 85%、轉一個方向）＋1～2 個旁支（中心在地圖 12%～88% 內）。</summary>
    private static List<Lobe[]> LobesFor(List<(long X, long Y, long Rx, long Ry)> centers, int columns, int rows, Func<ulong, ulong> draw)
    {
        var all = new List<Lobe[]>();
        for (var k = 0; k < centers.Count; k++)
        {
            var (cx, cy, rx, ry) = centers[k];
            var key = 7_000_000UL + (ulong)k * 64;
            var count = 2 + (int)(draw(key) % (ulong)TempMaxExtraLobes);
            var (ux, uy) = Direction(draw, key + 10);
            var lobes = new List<Lobe> { new(cx, cy, rx * 85 / 100, ry * 85 / 100, ux, uy) };
            var r = (rx + ry) / 2;
            for (var j = 1; j < count; j++)
            {
                var at = key + 20 + (ulong)j * 4;
                var (vx, vy) = Direction(draw, at);
                var offset = r * (30 + (long)(draw(at + 2) % 21UL)) / 100;
                var lx = Math.Clamp(cx + vx * offset / 1000, columns * Unit * 12 / 100, columns * Unit * 88 / 100);
                var ly = Math.Clamp(cy + vy * offset / 1000, rows * Unit * 12 / 100, rows * Unit * 88 / 100);
                var a = rx * (40 + (long)(draw(key + 30 + (ulong)j * 4) % 16UL)) / 100;
                var b = ry * (40 + (long)(draw(key + 31 + (ulong)j * 4) % 16UL)) / 100;
                var (tx, ty) = Direction(draw, key + 40 + (ulong)j * 4);
                lobes.Add(new Lobe(lx, ly, Math.Max(2 * Unit, a), Math.Max(2 * Unit, b), tx, ty));
            }
            all.Add(lobes.ToArray());
        }
        return all;
    }

    /// <summary>【TEMP】第 06-6 章：山脈折線——沿主體的長軸穿過大陸（偏離中心 ±40% 半徑），4 段、每段轉 ±17°。</summary>
    private static List<(long X, long Y)[]> RidgesFor(List<(long X, long Y, long Rx, long Ry)> centers, List<Lobe[]> lobes, int columns, Func<ulong, ulong> draw)
    {
        var ridges = new List<(long X, long Y)[]>();
        for (var k = 0; k < centers.Count; k++)
        {
            var key = 7_500_000UL + (ulong)k * 64;
            var count = 1 + (columns > 48 ? (int)(draw(key) % 2UL) : 0);
            var main = lobes[k][0];
            var r = (centers[k].Rx + centers[k].Ry) / 2;
            for (var j = 0; j < count; j++)
            {
                var at = key + 1 + (ulong)j * 8;
                var (ux, uy) = main.B > main.A ? (-main.Uy, main.Ux) : (main.Ux, main.Uy);
                if (draw(at) % 2UL == 1)
                    (ux, uy) = (-ux, -uy);
                var lengths = Enumerable.Range(0, TempRidgeSegments).Select(s => r * (30 + (long)(draw(at + 1 + (ulong)s) % 16UL)) / 100).ToArray();
                var total = lengths.Sum();
                var shift = r * ((long)(draw(at + 6) % 81UL) - 40) / 100;
                var points = new (long X, long Y)[TempRidgeSegments + 1];
                points[0] = (main.X - ux * total / 2000 - uy * shift / 1000, main.Y - uy * total / 2000 + ux * shift / 1000);
                for (var s = 0; s < TempRidgeSegments; s++)
                {
                    var bend = (long)(draw(key + 40 + (ulong)j * 8 + (ulong)s) % 601UL) - 300;
                    var nx = ux - uy * bend / 1000;
                    var ny = uy + ux * bend / 1000;
                    var length = Math.Max(1, (long)Math.Sqrt((double)(nx * nx + ny * ny)));
                    (ux, uy) = (nx * 1000 / length, ny * 1000 / length);
                    points[s + 1] = (points[s].X + ux * lengths[s] / 1000, points[s].Y + uy * lengths[s] / 1000);
                }
                ridges.Add(points);
            }
        }
        return ridges;
    }

    /// <summary>點到線段的距離（定點數）。</summary>
    private static long SegmentDistance(long px, long py, (long X, long Y) a, (long X, long Y) b)
    {
        long vx = b.X - a.X, vy = b.Y - a.Y, wx = px - a.X, wy = py - a.Y;
        var vv = vx * vx + vy * vy;
        var t = vv == 0 ? 0 : Math.Clamp((wx * vx + wy * vy) * 1000 / vv, 0, 1000);
        long cx = a.X + vx * t / 1000, cy = a.Y + vy * t / 1000;
        return (long)Math.Sqrt((double)((px - cx) * (px - cx) + (py - cy) * (py - cy)));
    }

    /// <summary>八鄰有沒有陸地。</summary>
    private static bool AnyLandAround(bool[] isLand, int i, int columns, int rows)
    {
        int r = i / columns, c = i % columns;
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
                if ((dr != 0 || dc != 0) && r + dr >= 0 && r + dr < rows && c + dc >= 0 && c + dc < columns && isLand[(r + dr) * columns + c + dc])
                    return true;
        return false;
    }

    /// <summary>方框平均（半徑 <paramref name="radius"/> 格；只平均 <paramref name="counts"/> 的格，地圖外不算；周圍一格都不算時保留原值）。</summary>
    private static long[] BoxBlur(long[] values, int columns, int rows, int radius, Func<int, bool> counts)
    {
        var result = new long[values.Length];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                long sum = 0, n = 0;
                for (var dr = -radius; dr <= radius; dr++)
                    for (var dc = -radius; dc <= radius; dc++)
                        if (r + dr >= 0 && r + dr < rows && c + dc >= 0 && c + dc < columns && counts((r + dr) * columns + c + dc))
                        {
                            sum += values[(r + dr) * columns + c + dc];
                            n++;
                        }
                result[r * columns + c] = n == 0 ? values[r * columns + c] : sum / n;
            }
        return result;
    }

    /// <summary>【TEMP】第 06-6 章：緯度的濕度——赤道帶（中間 30%）最多 +150，副熱帶（離赤道 40%～80%）最多 −150。</summary>
    private static long LatitudeMoisture(int row, int rows)
    {
        var lat = Math.Abs(row * 1000L / Math.Max(1, rows - 1) - 500) * 2; // 0＝赤道、1000＝兩極
        if (lat < 300)
            return TempLatitudeSwing * (300 - lat) / 300;
        if (lat > 400 && lat < 800)
            return -TempLatitudeSwing * (1000 - Math.Abs(lat - 600) * 1000 / 200) / 1000;
        return 0;
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
