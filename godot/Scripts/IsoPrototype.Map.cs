using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>第 08-20 章：3D 原型小地圖的地形（只用於原型，與世界模擬無關）。</summary>
public enum IsoTerrain { Sea, River, Sand, Grass, Forest, Hill }

/// <summary>第 08-20 章：原型裡的聚落（編號、名字、類型、勢力、中心格、佔哪些格）。</summary>
public sealed record IsoSettlement(int Id, string Name, string Kind, int Realm, Vector2I Center, IReadOnlyList<Vector2I> Cells);

/// <summary>
/// 第 08-20 章【TEMP】3D 等角原型的手擺小地圖（16×12 正方形格）：西側海岸、斜穿的河流、東北丘陵、兩片森林；
/// 河西是金色勢力的木柵城鎮（金柵鎮），河東是藍色勢力的河邊農村（河灣村，水車與 3×3 方田）。
/// 世界座標：X＝欄、Z＝列、一格＝1×1，Y 向上。純資料，不建任何節點（冒煙測試直接驗證）。
/// </summary>
public sealed class IsoProtoMap
{
    public const int Columns = 16, Rows = 12;

    /// <summary>【TEMP】地塊頂面高度、水面高度、地板底部（看得出厚度的土層）。</summary>
    public const float SeaBed = -0.12f, RiverBed = 0.02f, SandTop = 0.24f, LandTop = 0.36f, HillTop = 0.72f, WaterLevel = 0.2f, Bottom = -0.75f;

    public const int Gold = 1, Blue = 2;

    private readonly IsoTerrain[,] _terrain = new IsoTerrain[Columns, Rows];
    private readonly int[,] _realm = new int[Columns, Rows];

    /// <summary>河道（由北往南、上下左右相連）。</summary>
    public static readonly Vector2I[] RiverPath =
    {
        new(9, 0), new(9, 1), new(9, 2), new(8, 2), new(8, 3), new(8, 4), new(8, 5), new(7, 5), new(7, 6), new(7, 7),
        new(7, 8), new(6, 8), new(6, 9), new(6, 10), new(6, 11)
    };

    public IReadOnlyList<IsoSettlement> Settlements { get; }

    /// <summary>工作地圖示（格、字）：農田、伐木。</summary>
    public IReadOnlyList<(Vector2 At, string Glyph, string Tip)> Icons { get; }

    public IsoTerrain TerrainAt(int c, int r) => _terrain[c, r];

    public int RealmAt(int c, int r) => Contains(c, r) ? _realm[c, r] : 0;

    public static bool Contains(int c, int r) => c >= 0 && r >= 0 && c < Columns && r < Rows;

    public bool IsWater(int c, int r) => _terrain[c, r] is IsoTerrain.Sea or IsoTerrain.River;

    private IsoProtoMap()
    {
        for (var c = 0; c < Columns; c++)
            for (var r = 0; r < Rows; r++)
                _terrain[c, r] = IsoTerrain.Grass;
        for (var r = 0; r < Rows; r++)
        {
            _terrain[0, r] = IsoTerrain.Sea;
            _terrain[1, r] = r is <= 2 or >= 9 ? IsoTerrain.Sea : IsoTerrain.Sand;
            if (r is <= 2 or >= 9)
                _terrain[2, r] = IsoTerrain.Sand;
        }
        for (var c = 11; c < Columns; c++)
            for (var r = 0; r <= 2; r++)
                if (c >= 12 || r == 0)
                    _terrain[c, r] = IsoTerrain.Hill;
        _terrain[14, 3] = _terrain[15, 3] = IsoTerrain.Hill;
        foreach (var (c0, c1, r0, r1) in new[] { (3, 6, 1, 3), (12, 15, 9, 11), (3, 4, 9, 10) })
            for (var c = c0; c <= c1; c++)
                for (var r = r0; r <= r1; r++)
                    _terrain[c, r] = IsoTerrain.Forest;
        foreach (var p in RiverPath)
            _terrain[p.X, p.Y] = IsoTerrain.River;

        // 河西（第 3 列以南）是金色勢力，河東（到第 13 欄、不含丘陵）是藍色勢力；河本身不屬於任何勢力（兩岸各一條邊界）。
        for (var r = 0; r < Rows; r++)
        {
            var west = RiverPath.Where(p => p.Y == r).Min(p => p.X);
            var east = RiverPath.Where(p => p.Y == r).Max(p => p.X);
            for (var c = 0; c < Columns; c++)
            {
                if (IsWater(c, r) || r < 3 || _terrain[c, r] == IsoTerrain.Hill)
                    continue;
                if (c < west && c >= 1)
                    _realm[c, r] = Gold;
                else if (c > east && c <= 13)
                    _realm[c, r] = Blue;
            }
        }

        static List<Vector2I> Block(int c0, int c1, int r0, int r1)
            => Enumerable.Range(c0, c1 - c0 + 1).SelectMany(c => Enumerable.Range(r0, r1 - r0 + 1).Select(r => new Vector2I(c, r))).ToList();
        var town = Block(2, 4, 5, 7);
        var village = Block(8, 10, 6, 8).Concat(Block(11, 13, 5, 7)).ToList();
        foreach (var cell in town.Concat(village))
            _terrain[cell.X, cell.Y] = IsoTerrain.Grass;
        Settlements = new[]
        {
            new IsoSettlement(1, "金柵鎮", "城鎮", Gold, new Vector2I(3, 6), town),
            new IsoSettlement(2, "河灣村", "農村", Blue, new Vector2I(9, 7), village)
        };
        Icons = new[]
        {
            (new Vector2(12.5f, 6.5f), "農", "河灣村的方田（農田）"),
            (new Vector2(5.5f, 2.5f), "木", "金柵鎮的伐木場")
        };
    }

    public static IsoProtoMap Create() => new();

    /// <summary>格的頂面高度（丘陵另有中央隆起，見 <see cref="PeakOf"/>）。</summary>
    public float TopOf(int c, int r)
    {
        if (!Contains(c, r))
            return Bottom;
        return _terrain[c, r] switch
        {
            IsoTerrain.Sea => SeaBed,
            IsoTerrain.River => RiverBed,
            IsoTerrain.Sand => SandTop,
            IsoTerrain.Hill => HillTop,
            _ => LandTop
        };
    }

    /// <summary>頂面中心點往上（低多邊形的起伏）：丘陵 0.15～0.5、其他 ±0.03。</summary>
    public float PeakOf(int c, int r)
        => _terrain[c, r] == IsoTerrain.Hill ? 0.15f + 0.35f * Hash01(c, r, 3) : (Hash01(c, r, 7) - 0.5f) * 0.06f;

    /// <summary>位置雜湊（0～1，只用於外形）。</summary>
    public static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    public IsoSettlement? SettlementAt(int c, int r) => Settlements.FirstOrDefault(s => s.Cells.Contains(new Vector2I(c, r)));

    /// <summary>
    /// 射線打到哪一格（沿射線走，第一個低於該格頂面的點；打不到回傳 null）。正交鏡頭的射線彼此平行，從高處往下走即可。
    /// </summary>
    public Vector2I? CellUnderRay(Vector3 origin, Vector3 direction)
    {
        if (direction.Y >= -1e-4f)
            return null;
        // 從 y＝1.5 的平面開始走（高於所有地塊）。
        var t = (1.5f - origin.Y) / direction.Y;
        var at = origin + direction * Math.Max(0f, t);
        var step = direction.Normalized() * 0.03f;
        for (var i = 0; i < 400; i++)
        {
            var c = (int)Mathf.Floor(at.X);
            var r = (int)Mathf.Floor(at.Z);
            if (Contains(c, r))
            {
                var top = Math.Max(TopOf(c, r), IsWater(c, r) ? WaterLevel : TopOf(c, r));
                if (at.Y <= top)
                    return new Vector2I(c, r);
            }
            else if (at.Y < Bottom)
                return null;
            at += step;
        }
        return null;
    }

    public static string TerrainName(IsoTerrain t) => t switch
    {
        IsoTerrain.Sea => "海",
        IsoTerrain.River => "河流",
        IsoTerrain.Sand => "沙灘",
        IsoTerrain.Forest => "森林",
        IsoTerrain.Hill => "丘陵",
        _ => "草原"
    };
}
