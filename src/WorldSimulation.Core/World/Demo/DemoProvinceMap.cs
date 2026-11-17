namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-21 章（使用者 2026-11-17：往十字軍之王 CK3 的地圖世界開發，不再走 WorldBox 路線）：政治地圖的「省份」。
/// <list type="number">
/// <item>每一格陸地歸給一個聚落（省份＝聚落編號）：先照勢力範圍（<see cref="DemoTerritoryMap"/>，聚落實際用到的地），
///   再從聚落與它用到的地往外長，最遠到依層級的距離【TEMP】<see cref="TempReach"/>；長不到的陸地是荒野（<see cref="Wild"/>）。</item>
/// <item>往外長的代價：直走 <see cref="TempStraight"/>、斜走 <see cref="TempDiagonal"/>，進丘陵、森林、河流另加；同樣代價先給編號小的聚落。
///   河流在政治地圖上是畫在陸地上的細線（不是一格寬的水），可以跨過，但代價高，所以省界常沿著河。</item>
/// <item>海、湖不屬於任何省份；怪物地盤（<see cref="DemoMonsterNest.Cells"/>）也不屬於任何省份，另記是哪個地下城的。</item>
/// </list>
/// 純函式：同樣的地圖、聚落、勢力範圍與怪物，結果永遠一樣；不抽亂數、不存檔、不影響模擬，只供畫面與點選。
/// </summary>
public sealed class DemoProvinceMap
{
    /// <summary>荒野（沒有省份的陸地）。</summary>
    public const int Wild = 0;

    /// <summary>【TEMP】往外長一步的代價：直走、斜走（約 1:1.4，省份才是圓的，不是方的）。</summary>
    public const int TempStraight = 10, TempDiagonal = 14;

    /// <summary>【TEMP】進入丘陵、森林、河流格另加的代價。</summary>
    public const int TempHillCost = 6, TempForestCost = 3, TempRiverCost = 12;

    /// <summary>【TEMP】省份從聚落往外最遠長多遠（代價；直走一格 10）：營地 2.5 格、村莊 3.5、城鎮 4.5、城市 5.5、首都 6.5。</summary>
    public static int TempReach(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 25,
        DemoSettlementTier.Village => 35,
        DemoSettlementTier.Town => 45,
        DemoSettlementTier.City => 55,
        _ => 65
    };

    private readonly int[] _province;
    private readonly int[] _nest;

    private DemoProvinceMap(int columns, int rows, int[] province, int[] nest, IReadOnlyList<DemoMonsterNest> nests)
    {
        Columns = columns;
        Rows = rows;
        _province = province;
        _nest = nest;
        Nests = nests;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>有怪物佔據的地下城（<see cref="NestAt"/> 的編號從 1 起對應這裡的第 0 個）。</summary>
    public IReadOnlyList<DemoMonsterNest> Nests { get; }

    /// <summary>這一格的省份（聚落編號）；荒野、水、地圖外回傳 <see cref="Wild"/>。</summary>
    public int ProvinceAt(int column, int row)
        => column < 0 || row < 0 || column >= Columns || row >= Rows ? Wild : _province[row * Columns + column];

    /// <summary>這一格是哪個怪物地盤（<see cref="Nests"/> 的位置＋1；不是＝0）。</summary>
    public int NestAt(int column, int row)
        => column < 0 || row < 0 || column >= Columns || row >= Rows ? 0 : _nest[row * Columns + column];

    /// <summary>政治地圖上算陸地的格：草原、森林、丘陵與河流（含淺灘）；海與湖不算。</summary>
    public static bool IsLand(DemoHexCell cell) => cell.Terrain != DemoTerrain.Water || (cell.MainWaterway && !cell.Sea);

    /// <summary>河流格（政治地圖畫成陸地上的細線）。</summary>
    public static bool IsRiver(DemoHexCell cell) => cell.Terrain == DemoTerrain.Water && cell.MainWaterway && !cell.Sea;

    /// <summary>
    /// 算出全圖的省份。<paramref name="settlements"/>＝畫面上看得到的聚落（遺跡自動略過）；
    /// <paramref name="tierOf"/>＝聚落等級（決定最遠長多遠）。
    /// </summary>
    public static DemoProvinceMap Compute(
        DemoHexWorld map,
        IEnumerable<DemoSettlement> settlements,
        DemoTerritoryMap territory,
        Func<int, DemoSettlementTier> tierOf,
        IEnumerable<DemoMonsterNest>? nests = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(territory);
        ArgumentNullException.ThrowIfNull(tierOf);
        var columns = map.Columns;
        var rows = map.Rows;
        var count = columns * rows;
        var province = new int[count];
        var nest = new int[count];

        // ① 怪物地盤（依地下城事件編號排序，編號固定）。
        var nestList = (nests ?? Array.Empty<DemoMonsterNest>())
            .Where(n => n.Monsters > 0 && n.Cells.Count > 0)
            .OrderBy(n => n.Dungeon.EventId)
            .ToList();
        for (var i = 0; i < nestList.Count; i++)
            foreach (var c in nestList[i].Cells)
                if (map.Contains(c.Column, c.Row) && nest[c.Row * columns + c.Column] == 0)
                    nest[c.Row * columns + c.Column] = i + 1;

        bool Land(int index) => IsLand(map.GetCell(index % columns, index / columns));

        // ② 起點：聚落格與它實際用到的地（勢力範圍）。用到的地固定屬於它，別的聚落長不進去。
        var alive = settlements.Where(s => !s.IsRuin && map.Contains(s.Column, s.Row)).OrderBy(s => s.Id).ToList();
        var reach = new Dictionary<int, int>();
        var home = new Dictionary<int, DemoCellRef>();
        foreach (var s in alive)
        {
            reach[s.Id] = TempReach(tierOf(s.Id));
            home[s.Id] = new DemoCellRef(s.Column, s.Row);
        }
        var fixedOwner = new int[count];
        var best = new int[count];
        Array.Fill(best, int.MaxValue);
        var queue = new PriorityQueue<int, (int Cost, int Owner, int Cell)>();
        void Seed(int index, int owner, int cost)
        {
            if (cost > best[index])
                return;
            best[index] = cost;
            queue.Enqueue(index, (cost, owner, index));
        }
        foreach (var (cell, owner) in territory.Cells.OrderBy(c => c.Key.Row).ThenBy(c => c.Key.Column))
        {
            if (!reach.ContainsKey(owner.SettlementId) || !map.Contains(cell.Column, cell.Row))
                continue;
            var index = cell.Row * columns + cell.Column;
            if (!Land(index) || nest[index] != 0)
                continue;
            fixedOwner[index] = owner.SettlementId;
            Seed(index, owner.SettlementId, DemoHexWorld.Distance(cell, home[owner.SettlementId]) * TempStraight);
        }
        foreach (var s in alive)
        {
            var index = s.Row * columns + s.Column;
            fixedOwner[index] = s.Id; // 聚落格一定是自己的（聚落格不會在別人用到的地上）
            Seed(index, s.Id, 0);
        }

        // ③ 往外長（多起點最短路；代價、編號、格依序比，結果固定）。
        var settled = new bool[count];
        while (queue.TryDequeue(out var index, out var key))
        {
            // 先出佇列的一定是這一格最好的（代價、編號、格依序比）；之後同一格的都略過。
            if (settled[index] || fixedOwner[index] != 0 && fixedOwner[index] != key.Owner)
                continue;
            settled[index] = true;
            province[index] = key.Owner;
            var column = index % columns;
            var row = index / columns;
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0 || !map.Contains(column + dc, row + dr))
                        continue;
                    var next = (row + dr) * columns + column + dc;
                    if (settled[next] || !Land(next) || nest[next] != 0 || fixedOwner[next] != 0 && fixedOwner[next] != key.Owner)
                        continue;
                    var cell = map.GetCell(column + dc, row + dr);
                    var step = (dr != 0 && dc != 0 ? TempDiagonal : TempStraight) + cell.Terrain switch
                    {
                        DemoTerrain.Hills => TempHillCost,
                        DemoTerrain.Forest => TempForestCost,
                        DemoTerrain.Water => TempRiverCost,
                        _ => 0
                    };
                    var cost = key.Cost + step;
                    if (cost > reach[key.Owner] && fixedOwner[next] != key.Owner)
                        continue;
                    Seed(next, key.Owner, cost);
                }
        }
        return new DemoProvinceMap(columns, rows, province, nest, nestList);
    }
}
