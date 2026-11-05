using System.Text.Json;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 06 章 a 節（44D RouteSegment）：路線段類型。「野地」是尚無道路時的跨格移動。</summary>
public enum DemoRouteKind
{
    Wild = 1,
    Road = 2,
    River = 3,
    Waterway = 4,
    Bridge = 5,
    Ford = 6
}

/// <summary>
/// 第 06 章 a 節：一條路線段——兩格（依列、欄排序，A 在前）、類型、通行成本。
/// 跨格可通行性以路線段為單一權威；畫面相鄰或線條相交不代表連通。
/// </summary>
public sealed record DemoRouteSegment(DemoCellRef A, DemoCellRef B, DemoRouteKind Kind, int Cost)
{
    /// <summary>陸路可通行：野地、道路、橋、渡口。河流與水路是水上路線，不供陸路行走。</summary>
    public bool IsLand => Kind is DemoRouteKind.Wild or DemoRouteKind.Road or DemoRouteKind.Bridge or DemoRouteKind.Ford;

    public DemoCellRef Other(DemoCellRef cell)
        => cell == A ? B : cell == B ? A : throw new ArgumentException("這一格不在路線段上。", nameof(cell));
}

/// <summary>第 06 章 a 節：TEMP 路線成本與單格尺度（config/demo/map-routes-temp.json）。</summary>
public sealed record DemoRouteTempSettings(
    int SchemaVersion,
    string Status,
    int KmPerCell,
    int WildCost,
    int RoadCost,
    int BridgeCost,
    int FordCost,
    int RiverCost,
    int WaterwayCost)
{
    public static DemoRouteTempSettings Current { get; } = Load();

    private static DemoRouteTempSettings Load()
    {
        var settings = JsonSerializer.Deserialize<DemoRouteTempSettings>(Config.OfficialConfig.ReadDemoText("map-routes-temp.json"))
            ?? throw new InvalidOperationException("config/demo/map-routes-temp.json 讀不到。");
        if (settings.SchemaVersion != 1 || settings.Status != "TEMP" || settings.KmPerCell <= 0
            || new[] { settings.WildCost, settings.RoadCost, settings.BridgeCost, settings.FordCost, settings.RiverCost, settings.WaterwayCost }.Any(c => c <= 0))
            throw new InvalidOperationException("config/demo/map-routes-temp.json 內容不合法。");
        return settings;
    }

    public int CostOf(DemoRouteKind kind) => kind switch
    {
        DemoRouteKind.Wild => WildCost,
        DemoRouteKind.Road => RoadCost,
        DemoRouteKind.Bridge => BridgeCost,
        DemoRouteKind.Ford => FordCost,
        DemoRouteKind.River => RiverCost,
        DemoRouteKind.Waterway => WaterwayCost,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>
/// 第 06 章 a 節：地圖的路線網（不可變，隨地圖一起由世界亂數產生、同種子可重現）。
/// 規則（第 08-18a 章方格）：八鄰的兩格陸地之間一條野地段；斜角段要兩側的兩個直鄰格都是陸地才有（不能從兩塊水之間的縫鑽過去），
/// 成本是直走的 1.5 倍（無條件進位）【TEMP】。主水道上下左右相鄰的格之間一條河流段（水上，不供陸路）；
/// 渡口連接該水道格與上下左右的陸地（過河的唯一陸路）；一般水域沒有陸路。出入口只由路線段衍生。
/// </summary>
public sealed class DemoRouteNetwork
{
    /// <summary>第 08-18a 章：八個方向（欄差、列差；列往下為南），順序：東、東南、南、西南、西、西北、北、東北。</summary>
    public static readonly IReadOnlyList<(int Dq, int Dr, string Name)> Directions = new[]
    {
        (1, 0, "東"), (1, 1, "東南"), (0, 1, "南"), (-1, 1, "西南"), (-1, 0, "西"), (-1, -1, "西北"), (0, -1, "北"), (1, -1, "東北")
    };

    /// <summary>第 08-18a 章：上下左右四個方向（河流、渡口只走這四個）。</summary>
    public static readonly IReadOnlyList<(int Dq, int Dr, string Name)> Orthogonal = Directions.Where(d => d.Dq == 0 || d.Dr == 0).ToArray();

    /// <summary>【TEMP】第 08-18a 章：斜角段的成本倍率（百分比，無條件進位）。</summary>
    public const int TempDiagonalCostPercent = 150;

    /// <summary>斜角段的成本：直走成本×1.5，無條件進位到整數。</summary>
    public static int DiagonalCost(int cost) => (cost * TempDiagonalCostPercent + 99) / 100;

    private readonly Dictionary<(DemoCellRef, DemoCellRef), DemoRouteSegment> _byPair = new();
    private readonly Dictionary<DemoCellRef, List<DemoRouteSegment>> _byCell = new();

    private DemoRouteNetwork(IReadOnlyList<DemoRouteSegment> segments, IReadOnlyList<DemoCellRef> fords)
    {
        Segments = segments;
        Fords = fords;
        foreach (var s in segments)
        {
            _byPair[(s.A, s.B)] = s;
            _byPair[(s.B, s.A)] = s;
            foreach (var cell in new[] { s.A, s.B })
            {
                if (!_byCell.TryGetValue(cell, out var list))
                    _byCell[cell] = list = new List<DemoRouteSegment>();
                list.Add(s);
            }
        }
    }

    /// <summary>全部路線段（依 A 的列、欄，再依 B 排序）。</summary>
    public IReadOnlyList<DemoRouteSegment> Segments { get; }

    /// <summary>主水道上的第一個渡口格（依列）；沒有可設渡口的水道格時為 null。</summary>
    public DemoCellRef? Ford => Fords.Count == 0 ? null : Fords[0];

    /// <summary>第 07-1 章 c 節：全部渡口格（依列、欄）。</summary>
    public IReadOnlyList<DemoCellRef> Fords { get; }

    public DemoRouteSegment? Between(DemoCellRef a, DemoCellRef b) => _byPair.GetValueOrDefault((a, b));

    /// <summary>兩格之間有陸路路線段才可步行通行（相鄰但沒有路線段＝不可通行）。</summary>
    public bool IsLandPassable(DemoCellRef a, DemoCellRef b) => Between(a, b) is { IsLand: true };

    /// <summary>這一格的出入口（由路線段衍生，依方向順序）。</summary>
    public IReadOnlyList<(string Direction, DemoRouteSegment Segment)> Exits(DemoHexWorld map, DemoCellRef cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        var here = map.GetCell(cell.Column, cell.Row);
        var result = new List<(string, DemoRouteSegment)>();
        foreach (var (dq, dr, name) in Directions)
        {
            var next = map.AtAxial(here.AxialQ + dq, here.AxialR + dr);
            if (next is not null && Between(cell, new DemoCellRef(next.Column, next.Row)) is { } segment)
                result.Add((name, segment));
        }
        return result;
    }

    public static string KindName(DemoRouteKind kind) => kind switch
    {
        DemoRouteKind.Wild => "野地",
        DemoRouteKind.Road => "道路",
        DemoRouteKind.River => "河流",
        DemoRouteKind.Waterway => "水路",
        DemoRouteKind.Bridge => "橋",
        DemoRouteKind.Ford => "淺灘渡口",
        _ => kind.ToString()
    };

    /// <summary>由地圖與渡口位置建立路線網（純函式）。</summary>
    internal static DemoRouteNetwork Build(IReadOnlyList<DemoHexCell> cells, DemoRouteTempSettings settings)
    {
        var fords = cells.Where(c => c.Shallow).OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();
        var byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        var segments = new List<DemoRouteSegment>();
        static DemoCellRef Ref(DemoHexCell c) => new(c.Column, c.Row);
        static int Order(DemoHexCell c) => c.Row * 100000 + c.Column;
        foreach (var cell in cells)
        {
            foreach (var (dq, dr, _) in Directions)
            {
                if (!byAxial.TryGetValue((cell.AxialQ + dq, cell.AxialR + dr), out var next) || Order(next) <= Order(cell))
                    continue; // 每對只看一次
                DemoRouteKind? kind = null;
                var diagonal = dq != 0 && dr != 0;
                bool Land(int q, int r) => byAxial.TryGetValue((q, r), out var c) && c.Terrain != DemoTerrain.Water;
                if (diagonal)
                {
                    // 斜角只有野地段，而且兩側的直鄰格都要是陸地。
                    if (cell.Terrain != DemoTerrain.Water && next.Terrain != DemoTerrain.Water
                        && Land(cell.AxialQ + dq, cell.AxialR) && Land(cell.AxialQ, cell.AxialR + dr))
                        segments.Add(new DemoRouteSegment(Ref(cell), Ref(next), DemoRouteKind.Wild, DiagonalCost(settings.CostOf(DemoRouteKind.Wild))));
                    continue;
                }
                if (cell.Terrain != DemoTerrain.Water && next.Terrain != DemoTerrain.Water)
                    kind = DemoRouteKind.Wild;
                else if (cell.MainWaterway && next.MainWaterway)
                    kind = DemoRouteKind.River;
                else if ((cell.Shallow && next.Terrain != DemoTerrain.Water)
                             || (next.Shallow && cell.Terrain != DemoTerrain.Water))
                    kind = DemoRouteKind.Ford;
                if (kind is { } k)
                    segments.Add(new DemoRouteSegment(Ref(cell), Ref(next), k, settings.CostOf(k)));
            }
        }
        return new DemoRouteNetwork(segments.OrderBy(s => s.A.Row).ThenBy(s => s.A.Column)
            .ThenBy(s => s.B.Row).ThenBy(s => s.B.Column).ToArray(), fords);
    }
}
