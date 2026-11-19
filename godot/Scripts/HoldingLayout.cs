using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-33 章：領地圖的排版（只算位置，不畫；由 <see cref="HoldingView"/> 畫）。使用者 2026-11-18：聚落點開要像 CK3 的領地，
/// 只畫有蓋、正在蓋、被破壞耐久度歸零的建築；城牆、農田、城內建築、兵營、城堡都要有；要視覺化，不要九宮格塞滿插圖；插槽越多＝建築越多＝城越大。
/// <list type="bullet">
/// <item>城區以廣場為中心（營地是營火、村鎮是井、城都是噴泉）：最大的地標（城堡＞神殿＞市政廳…）站在廣場正北，其餘地標排在兩側；
///   大道從廣場往南通到城門，鎮以上有橫街。</item>
/// <item>工坊、商店、倉庫沿大道靠近廣場；住宅由近而遠找空地擺（越多建築城區越大）；兵營、馬廄等排在城區最南邊、城門兩側。</item>
/// <item>城牆照城區大小圍一圈（施工中的一段段是鷹架、受損的有缺口、毀損的全是斷牆），城門在大道南端；塔樓站在牆角外側。</item>
/// <item>牆外：大道兩側是拼布般的農田（附近已開墾的農田越多越大片，外圍一圈籬笆），田邊是農舍、牲口與磨坊；
///   西邊木材場（附近有森林時成林），東邊採石場與礦場（丘陵地面），臨海時最下面是沙灘、海與港口。</item>
/// <item>建築腳下與門前是踩實的地面（村、鎮泥土，城、都石板），邊緣用第十一批的過渡圖塊（四角 Wang 組）。</item>
/// </list>
/// 素材都是第十一、十二批正面像素圖（一格 16 像素），排版只用素材的大小；同一個聚落、同一組建築得到同一張圖。
/// </summary>
public static class HoldingLayout
{
    public const int T = 16;
    private const int Cell = 4; // 佔位格（像素）

    public enum Kind
    {
        Building,
        House,
        Ruin,
        Wall,
        Prop,
        Tree,
        Fence
    }

    /// <summary>圖上的一件東西。座標是畫布像素（左上角）；<see cref="BuildingId"/>＝對應的建築（-1＝不是建築）。</summary>
    public sealed class Piece
    {
        public string Sprite = "";
        public float X, Y, W, H;
        public int Frames = 1;
        public Kind Kind;
        public int BuildingId = -1;
        public string Tip = "";

        /// <summary>施工進度 0～1（-1＝不是工地）。</summary>
        public float Progress = -1f;

        /// <summary>耐久度（千分比）。</summary>
        public int Durability = 1000;

        /// <summary>由幾張素材橫排組成（市場的攤位、都城的連排屋）；null＝單張。</summary>
        public (string Sprite, float Width)[]? Group;

        /// <summary>城牆的第幾段（-1＝不是城牆段）、共幾段與哪一種段（h、v、corner_nw…）。</summary>
        public int Segment = -1, Segments;

        public string Part = "";

        public float Foot => Y + H;
        public Rect2 Rect => new(X, Y, W, H);
    }

    /// <summary>一張領地圖：東西、地面（道路、廣場、田、沙灘、海、丘陵、菜園）、踩實地面的角點與畫布大小。</summary>
    public sealed class Picture
    {
        public readonly List<Piece> Pieces = new();
        public readonly Dictionary<Vector2I, string> Ground = new();
        public readonly HashSet<Vector2I> Yard = new();
        public string? Wang;
        public Vector2I Size;
        public uint Seed;

        /// <summary>草地一格（每格依位置挑四種之一）。</summary>
        public string GrassAt(int tx, int ty) => "b11_g_grass_temperate_" + (Pick(Seed, tx * 13 + ty * 7, 4) + 1);
    }

    /// <summary>不會改變排版的狀態（進度、耐久度）以外的建築內容；一樣就沿用上次的排版。</summary>
    public static string Signature(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var h = scene.Holding;
        var sb = new System.Text.StringBuilder();
        sb.Append(scene.Site.Id).Append('|').Append((int)scene.Tier).Append('|').Append(scene.Fields).Append('|').Append(scene.Nearby).Append('|')
            .Append(h.Dwellings).Append('/').Append(h.LargeDwellings).Append('/').Append(h.RuinedDwellings).Append('/').Append(h.DwellingProgress > 0);
        foreach (var b in h.Buildings)
            sb.Append(';').Append(b.Id).Append(b.DefId).Append((int)b.Status);
        return sb.ToString();
    }

    private static int Pick(uint seed, int k, int count)
    {
        unchecked
        {
            var x = seed ^ (uint)(k * 40503);
            x *= 2246822519u;
            x ^= x >> 13;
            return (int)((x >> 7) % (uint)Math.Max(1, count));
        }
    }

    /// <summary>素材的大小（多格動畫取一格）；缺圖時 3×3 格。</summary>
    public static Vector2 SizeOf(string sprite)
    {
        if (PixelParts.Texture(sprite) is not { } texture)
            return new Vector2(3 * T, 3 * T);
        var (width, height, _) = PixelParts.Metrics(texture);
        return new Vector2(width / FramesOf(sprite), height);
    }

    public static int FramesOf(string sprite) => sprite is "b11_windmill" or "b11_watermill" or "b11_bonfire" or "fx_fire" or "fx_fire_big" or "fx_smoke" ? 4 : 1;

    /// <summary>第 08-33 章：建築表 ID → 正面像素素材（營、村的神殿是祭壇、廚房是風車磨坊、鞣革場是獵屋；名稱見 <see cref="DemoHoldingReport.Name"/>）。</summary>
    public static string BuildingSprite(string defId, int id, DemoSettlementTier tier)
    {
        var small = tier <= DemoSettlementTier.Village;
        return defId switch
        {
            "BUILD_WORKSHOP" => "b11_workshop",
            "BUILD_KITCHEN" => small ? "b11_windmill" : "b12_kitchen",
            "BUILD_TANNERY" => small ? "b11_hunting_lodge" : "b12_tannery",
            "BUILD_CHURCH" => small ? "b11_shrine" : "b11_temple",
            "BUILD_SMITHY" => "b11_smithy",
            "BUILD_SCRIPTORIUM" => "b11_scriptorium",
            "BUILD_KILN" => "b11_kiln",
            "BUILD_SCHOOL" => "b11_school",
            "BUILD_TOWN_HALL" => small ? "b11_hall_village" : "b11_hall_town",
            "BUILD_CASTLE" => "b11_keep",
            "BUILD_GRANARY" => "b11_granary",
            "BUILD_FARM" => "b11_farmhouse_" + (id % 2 + 1),
            "BUILD_BARRACK" => "b11_barracks",
            "BUILD_OUTPOST" => "b11_watchtower",
            "BUILD_INN" => "b11_inn",
            "BUILD_WAREHOUSE" => "b11_warehouse",
            "BUILD_STABLE" => "b11_stable",
            "BUILD_ARCHERY_RANGE" => "b11_archery",
            "BUILD_SHELTER" => "b11_tent_" + (id % 2 + 1),
            "BUILD_HOUSE_BASIC" => "b11_house_" + (id % 3 + 1),
            "BUILD_HOUSE_LARGE" => "b11_stone_house_" + (id % 3 + 1),
            _ => "b12_" + defId["BUILD_".Length..].ToLowerInvariant()
        };
    }

    /// <summary>石造（廢墟用石造殘骸）：建築表的石料多於木料。</summary>
    private static bool Stony(string defId) => DemoBuildingCatalog.Contains(defId) && DemoBuildingCatalog.Get(defId).Stone > DemoBuildingCatalog.Get(defId).Wood;

    /// <summary>工地：依原建築寬度挑小、中、大，依進度挑三個階段。</summary>
    public static string SiteSprite(float width, float progress)
    {
        var scale = width <= 56 ? "s" : width <= 88 ? "m" : "l";
        var stage = progress < 0.34f ? 1 : progress < 0.67f ? 2 : 3;
        return $"b11_site_{scale}_{stage}";
    }

    private static string RuinSprite(string defId, float width)
    {
        if (defId is DemoBuildings.Outpost or DemoBuildings.StoneTower)
            return "b12_ruin_tower";
        if (Stony(defId))
            return width >= 150 ? "b12_ruin_stone_l" : width >= 80 ? "b12_ruin_stone_m" : "b11_ruin_small";
        return width >= 80 ? "b12_ruin_wood_m" : "b12_ruin_wood_s";
    }

    private static string HouseSprite(DemoSettlementTier tier, bool large, int v) => (tier, large) switch
    {
        (DemoSettlementTier.Camp, _) => "b11_tent_" + (v % 2 + 1),
        (DemoSettlementTier.Village, _) => v % 3 == 0 ? "b11_hut_" + (v % 3 + 1) : "b11_house_" + (v % 3 + 1),
        (DemoSettlementTier.Town, true) => "b11_stone_house_" + (v % 3 + 1),
        (DemoSettlementTier.Town, false) => "b11_house_" + (v % 3 + 1),
        (DemoSettlementTier.City, true) => "b11_brick_house_" + (v % 2 + 1),
        (DemoSettlementTier.City, false) => "b11_stone_house_" + (v % 3 + 1),
        (_, true) => "ROW",
        _ => "b11_brick_house_" + (v % 2 + 1)
    };

    /// <summary>【TEMP】排版參數：建築之間的空隙（像素）、住宅位置的隨機程度（格）、最多畫幾棟住宅與幾塊田。</summary>
    private static int TempGap(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp or DemoSettlementTier.Village => 8,
        DemoSettlementTier.Town => 4,
        DemoSettlementTier.City => 3,
        _ => 2
    };

    private static float TempHouseSpread(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 1.0f,
        DemoSettlementTier.Village => 0.9f,
        DemoSettlementTier.Town => 0.45f,
        DemoSettlementTier.City => 0.3f,
        _ => 0.25f
    };

    public const int TempMaxHouses = 64, TempMaxRuinedHouses = 12, TempMaxPlots = 32;

    /// <summary>排一張領地圖。</summary>
    public static Picture Compose(DemoHoldingScene scene)
    {
        var picture = new Builder(scene).Build();
        Refresh(picture, scene);
        return picture;
    }

    /// <summary>正在蓋的住宅（<see cref="Piece.BuildingId"/> 用這個值）。</summary>
    public const int HouseSiteId = -2;

    /// <summary>
    /// 不改排版、只更新會每週變的部分：施工進度（工地的階段）、耐久度、城牆每一段（施工中前段是牆後段是鷹架、受損的有缺口）與提示文字。
    /// </summary>
    public static void Refresh(Picture picture, DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(scene);
        var byId = scene.Holding.Buildings.ToDictionary(b => b.Id);
        foreach (var p in picture.Pieces)
        {
            if (p.BuildingId == HouseSiteId)
            {
                p.Progress = scene.HousePercent / 100f;
                p.Sprite = Stage(p.Sprite, p.Progress);
                p.Tip = $"住宅 施工中 {scene.HousePercent}%";
                continue;
            }
            if (p.BuildingId < 0 || !byId.TryGetValue(p.BuildingId, out var b))
                continue;
            var percent = PercentOf(b);
            p.Tip = DemoHoldingReport.Name(b.DefId, scene.Tier) + " " + DemoHoldingReport.StateText(b.Status, percent);
            p.Durability = b.Status == DemoBuildingStatus.Ruined ? 0 : b.Durability;
            if (p.Segment >= 0)
            {
                p.Sprite = WallSprite(p, b, scene.Tier, picture.Seed);
                continue;
            }
            p.Progress = b.Status == DemoBuildingStatus.Building ? Math.Clamp(b.Progress / (float)Math.Max(1, b.Def.Hours), 0f, 1f) : -1f;
            if (p.Progress >= 0f)
                p.Sprite = Stage(p.Sprite, p.Progress);
        }
    }

    private static int PercentOf(DemoBuilding b) => b.Status switch
    {
        DemoBuildingStatus.Building => Math.Clamp(b.Progress * 100 / Math.Max(1, b.Def.Hours), 0, 99),
        DemoBuildingStatus.Ruined => 0,
        _ => Math.Clamp(b.Durability / 10, 1, 100)
    };

    /// <summary>工地圖換成進度對應的階段（b11_site_m_1 → _2 → _3）。</summary>
    private static string Stage(string sprite, float progress)
        => sprite.StartsWith("b11_site_", StringComparison.Ordinal) && sprite.Length > 2
            ? sprite[..^1] + (progress < 0.34f ? 1 : progress < 0.67f ? 2 : 3)
            : sprite;

    /// <summary>城牆一段的素材：施工中依進度前段是牆、後段是鷹架；受損時依耐久度換一部分成斷牆；毀損全是斷牆。</summary>
    private static string WallSprite(Piece p, DemoBuilding wall, DemoSettlementTier tier, uint seed)
    {
        var stone = wall.DefId == DemoBuildings.WallStone;
        var kind = stone ? (tier >= DemoSettlementTier.Capital ? "brick" : "stone") : "palisade";
        var material = stone ? "stone" : "palisade";
        var shortPart = p.Part == "v" ? "v" : "h";
        var builtUpTo = wall.Status == DemoBuildingStatus.Building
            ? (int)(p.Segments * Math.Clamp(wall.Progress / (float)Math.Max(1, wall.Def.Hours), 0f, 1f))
            : p.Segments;
        if (wall.Status == DemoBuildingStatus.Building && p.Segment >= builtUpTo)
            return $"b12_site_{material}_{shortPart}";
        if (wall.Status == DemoBuildingStatus.Ruined || Pick(seed, 1000 + p.Segment, 1000) < (1000 - wall.Durability) * 6 / 10)
            return $"b12_ruin_{material}_{shortPart}";
        return $"b11_{kind}_{p.Part}";
    }

    private sealed class Builder
    {
        private readonly DemoHoldingScene _scene;
        private readonly DemoSettlementTier _tier;
        private readonly uint _seed;
        private readonly Picture _picture;
        private readonly int _gap;
        private readonly float _avenue;
        private readonly string _road;

        // 佔位圖（4 像素一格，原點在廣場左上角附近）。
        private const int Span = 1200; // 往各方向多少像素
        private readonly bool[,] _occupied = new bool[2 * Span / Cell, 2 * Span / Cell];
        private readonly Dictionary<(int, int, int, int, int), List<(float D, int X, int Y)>> _candidates = new();

        public Builder(DemoHoldingScene scene)
        {
            _scene = scene;
            _tier = scene.Tier;
            _seed = unchecked((uint)scene.Site.Id * 2654435761u);
            _picture = new Picture { Seed = _seed };
            _gap = TempGap(_tier);
            _avenue = _tier >= DemoSettlementTier.Village ? 2 * T : 0;
            _road = _tier switch
            {
                DemoSettlementTier.Camp or DemoSettlementTier.Village => "b11_g_road_dirt_",
                DemoSettlementTier.Town or DemoSettlementTier.City => "b11_g_road_stone_",
                _ => "b11_g_road_brick_"
            };
        }

        private int Pick(int k, int count) => HoldingLayout.Pick(_seed, k, count);

        private string Var(string prefix, int k, int n = 2) => prefix.EndsWith('_') ? prefix + (Pick(k, n) + 1) : prefix;

        private readonly Dictionary<Vector2I, string> _ground = new();

        private void Tile(float x, float y, string sprite) => _ground[new Vector2I((int)x, (int)y)] = sprite;

        // ── 佔位 ──

        private bool Free(float x, float y, float w, float h)
        {
            int x0 = (int)MathF.Floor(x / Cell) + Span / Cell, x1 = (int)MathF.Ceiling((x + w) / Cell) + Span / Cell;
            int y0 = (int)MathF.Floor(y / Cell) + Span / Cell, y1 = (int)MathF.Ceiling((y + h) / Cell) + Span / Cell;
            if (x0 < 0 || y0 < 0 || x1 > _occupied.GetLength(0) || y1 > _occupied.GetLength(1))
                return false;
            for (var cy = y0; cy < y1; cy++)
                for (var cx = x0; cx < x1; cx++)
                    if (_occupied[cx, cy])
                        return false;
            return true;
        }

        private void Take(float x, float y, float w, float h)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(x / Cell) + Span / Cell), x1 = Math.Min(_occupied.GetLength(0), (int)MathF.Ceiling((x + w) / Cell) + Span / Cell);
            int y0 = Math.Max(0, (int)MathF.Floor(y / Cell) + Span / Cell), y1 = Math.Min(_occupied.GetLength(1), (int)MathF.Ceiling((y + h) / Cell) + Span / Cell);
            for (var cy = y0; cy < y1; cy++)
                for (var cx = x0; cx < x1; cx++)
                    _occupied[cx, cy] = true;
        }

        /// <summary>離錨點（腳的中心）由近而遠的候選位置（8 像素一步；北邊界以北加重距離；加一點依位置的隨機，排起來不死板）。</summary>
        private List<(float D, int X, int Y)> Candidates(float ax, float ay, float ratio, float north, float spread)
        {
            var key = ((int)ax, (int)ay, (int)(ratio * 100), (int)north, (int)(spread * 100));
            if (_candidates.TryGetValue(key, out var known))
                return known;
            const int R = 70;
            var list = new List<(float, int, int)>((2 * R + 1) * (2 * R + 1));
            for (var dy = -R; dy <= R; dy++)
                for (var dx = -R; dx <= R; dx++)
                {
                    var fx = (int)ax + dx * 8;
                    var fy = (int)ay + dy * 8;
                    float ux = MathF.Abs(dx * 8f / T) / ratio, uy = MathF.Abs(dy * 8f / T);
                    var d = MathF.Max(ux, uy) + 0.1f * MathF.Min(ux, uy);
                    if (fy < north)
                        d += (north - fy) / T * 3f;
                    d += spread * (Pick((fx * 7 + fy * 13) & 0xffff, 1000) / 1000f);
                    list.Add((d, fx, fy));
                }
            list.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item3 != b.Item3 ? a.Item3.CompareTo(b.Item3) : a.Item2.CompareTo(b.Item2));
            _candidates[key] = list;
            return list;
        }

        private bool Place(Piece p, float ax, float ay, float ratio, float north, float spread)
        {
            foreach (var (_, fx, fy) in Candidates(ax, ay, ratio, north, spread))
            {
                var x = fx - p.W / 2f;
                var y = fy - p.H;
                if (!Free(x - _gap, y - _gap / 2f, p.W + 2 * _gap, p.H + _gap))
                    continue;
                p.X = x;
                p.Y = y;
                Take(x - _gap / 2f, y, p.W + _gap, p.H + _gap / 2f);
                _picture.Pieces.Add(p);
                return true;
            }
            return false; // 放不下（極端大的城）：不畫，列表照樣列
        }

        // ── 建築 ──

        private Piece PieceFor(DemoBuilding b)
        {
            var name = DemoHoldingReport.Name(b.DefId, _tier);
            var built = BuildingSprite(b.DefId, b.Id, _tier);
            (string, float)[]? group = null;
            Vector2 size;
            if (b.DefId == "BUILD_MARKET")
            {
                group = Enumerable.Range(0, 3).Select(k => ($"b11_stall_{(b.Id + k) % 3 + 1}", 36f)).ToArray();
                size = new Vector2(3 * 36 - 4, 32);
            }
            else
                size = SizeOf(built);
            var piece = new Piece { BuildingId = b.Id, Durability = b.Durability, Kind = Kind.Building };
            switch (b.Status)
            {
                case DemoBuildingStatus.Building:
                    piece.Progress = Math.Clamp(b.Progress / (float)Math.Max(1, b.Def.Hours), 0f, 1f);
                    piece.Sprite = SiteSprite(size.X, piece.Progress);
                    break;
                case DemoBuildingStatus.Ruined:
                    piece.Sprite = RuinSprite(b.DefId, size.X);
                    piece.Kind = Kind.Ruin;
                    piece.Durability = 0;
                    break;
                default:
                    piece.Sprite = group is null ? built : "GROUP";
                    piece.Group = group;
                    break;
            }
            if (piece.Group is null)
                size = SizeOf(piece.Sprite);
            piece.W = size.X;
            piece.H = size.Y;
            piece.Frames = piece.Group is null ? FramesOf(piece.Sprite) : 1;
            piece.Tip = name + " " + DemoHoldingReport.StateText(b.Status, b.Status switch
            {
                DemoBuildingStatus.Building => (int)(piece.Progress * 100),
                DemoBuildingStatus.Ruined => 0,
                _ => Math.Clamp(b.Durability / 10, 1, 100)
            });
            return piece;
        }

        private Piece Simple(string sprite, Kind kind, string tip = "")
        {
            var size = SizeOf(sprite);
            return new Piece { Sprite = sprite, W = size.X, H = size.Y, Kind = kind, Tip = tip, Frames = FramesOf(sprite) };
        }

        public Picture Build()
        {
            var h = _scene.Holding;
            var zones = h.Buildings.OrderBy(b => b.Id).GroupBy(b => DemoHoldingReport.ZoneOf(b.DefId, _tier)).ToDictionary(g => g.Key, g => g.ToList());
            List<DemoBuilding> Zone(DemoHoldingZone z) => zones.GetValueOrDefault(z) ?? new List<DemoBuilding>();
            var towers = Zone(DemoHoldingZone.Walls).Where(b => b.DefId is DemoBuildings.Outpost or DemoBuildings.StoneTower).ToList();
            var wall = Zone(DemoHoldingZone.Walls).FirstOrDefault(b => b.DefId is DemoBuildings.WallWood or DemoBuildings.WallStone);
            var gate = Zone(DemoHoldingZone.Walls).FirstOrDefault(b => b.DefId is DemoBuildings.GateWood or DemoBuildings.GateStone);

            // ── 住宅（大型在前；都城的大型住宅三、四棟連成一排）──
            var houses = new List<Piece>();
            var total = Math.Min(TempMaxHouses, h.Dwellings + h.LargeDwellings);
            var rowParts = 0;
            for (var i = 0; i < total; i++)
            {
                var large = i < h.LargeDwellings;
                var sprite = HouseSprite(_tier, large, Pick(100 + i, 6));
                if (sprite == "ROW")
                {
                    rowParts++;
                    continue;
                }
                houses.Add(Simple(sprite, Kind.House, large ? "大型住宅" : _tier == DemoSettlementTier.Camp ? "棚屋" : "住宅"));
            }
            var terraces = new List<Piece>();
            for (var k = 0; k < rowParts;)
            {
                var n = Math.Min(4, rowParts - k);
                var parts = n == 1
                    ? new[] { "b11_row_mid_1" }
                    : new[] { "b11_row_left_" + (Pick(300 + k, 2) + 1) }
                        .Concat(Enumerable.Range(0, n - 2).Select(j => "b11_row_mid_" + (Pick(310 + k + j, 2) + 1)))
                        .Append("b11_row_right_" + (Pick(320 + k, 2) + 1)).ToArray();
                var group = parts.Select(p => (p, SizeOf(p).X)).ToArray();
                terraces.Add(new Piece
                {
                    Sprite = "GROUP", Group = group, W = group.Sum(g => g.Item2), H = parts.Max(p => SizeOf(p).Y), Kind = Kind.House,
                    Tip = $"大型住宅（連排 {n} 棟）"
                });
                k += n;
            }
            houses.InsertRange(0, terraces);
            for (var i = 0; i < Math.Min(TempMaxRuinedHouses, h.RuinedDwellings); i++)
            {
                var ruin = Simple(_tier >= DemoSettlementTier.City ? "b11_ruin_small" : "b12_ruin_wood_s", Kind.Ruin, "毀損的住宅");
                ruin.Durability = 0;
                houses.Insert(Math.Min(houses.Count, 2 + i * 4), ruin);
            }
            if (_scene.HousePercent > 0)
            {
                var progress = _scene.HousePercent / 100f;
                var site = Simple(SiteSprite(_tier <= DemoSettlementTier.Village ? 40 : 64, progress), Kind.House, $"住宅 施工中 {_scene.HousePercent}%");
                site.Progress = progress;
                site.BuildingId = HouseSiteId;
                houses.Add(site);
            }

            var landmarks = Zone(DemoHoldingZone.Keep).Concat(Zone(DemoHoldingZone.Sacred)).Select(PieceFor)
                .OrderByDescending(p => p.W * p.H).ToList();
            var town = Zone(DemoHoldingZone.Town).Select(PieceFor).ToList();
            var military = Zone(DemoHoldingZone.Military).Select(PieceFor).ToList();

            // ── 中心：廣場（營地只有營火）──
            var plazaW = MathF.Ceiling(((landmarks.Count > 0 ? MathF.Max(landmarks[0].W, 4 * T) : 4 * T) + 2 * T) / (2 * T)) * 2 * T;
            var plazaH = _tier >= DemoSettlementTier.Town ? 3 * T : 2 * T;
            float px0 = -plazaW / 2f, py0 = 0f;
            if (_tier >= DemoSettlementTier.Village)
                for (var ty = py0; ty < py0 + plazaH; ty += T)
                    for (var tx = px0; tx < px0 + plazaW; tx += T)
                        Tile(tx, ty, _tier >= DemoSettlementTier.City ? Var("b11_g_plaza_stone_", (int)(tx * 3 + ty)) : "b11_g_plaza_dirt_1");
            Take(px0, py0, plazaW, plazaH);
            var deco = Simple(_tier switch
            {
                DemoSettlementTier.Camp => "b11_bonfire",
                DemoSettlementTier.Village or DemoSettlementTier.Town => "b11_well",
                _ => "b11_fountain"
            }, Kind.Prop);
            deco.X = -deco.W / 2f;
            deco.Y = py0 + plazaH / 2f - deco.H / 2f;
            _picture.Pieces.Add(deco);
            // 大道：廣場往南；鎮以上有橫街。
            const float reach = 60 * T;
            Take(-_avenue / 2f, py0 + plazaH, _avenue, reach);
            var streets = new List<float>();
            if (_tier >= DemoSettlementTier.Town)
                streets.Add(py0 + plazaH + 6 * T);
            if (_tier >= DemoSettlementTier.City)
                streets.Add(py0 + plazaH + 13 * T);
            foreach (var sy in streets)
                Take(-reach, sy, 2 * reach, T);

            // 地標：最大的在廣場正北，其餘排在廣場兩側。
            if (landmarks.Count > 0)
            {
                var c = landmarks[0];
                c.X = -c.W / 2f;
                c.Y = py0 - c.H;
                Take(c.X - _gap, c.Y, c.W + 2 * _gap, c.H);
                _picture.Pieces.Add(c);
                float lx = c.X - _gap, rx = c.X + c.W + _gap;
                for (var j = 1; j < landmarks.Count; j++)
                {
                    var p = landmarks[j];
                    var ax = j % 2 == 1 ? lx - p.W / 2f - T : rx + p.W / 2f + T;
                    Place(p, ax, py0 + plazaH * 0.5f, 2.5f, -9999f, 0f);
                }
            }
            var northLine = py0 - (landmarks.Count > 0 ? landmarks[0].H * 0.6f : 2 * T);
            // 工坊、商店、倉庫：大道兩側靠近廣場。
            for (var j = 0; j < town.Count; j++)
            {
                var side = j % 2 == 0 ? -1 : 1;
                Place(town[j], side * (_avenue / 2f + town[j].W / 2f + T), py0 + plazaH + 3 * T, 1f, northLine, 0.6f);
            }
            // 住宅：以廣場南邊為中心往外長。
            foreach (var p in houses)
                Place(p, 0, py0 + plazaH + 4 * T, 1.35f, northLine, TempHouseSpread(_tier));
            // 兵營、馬廄、靶場：城區最南邊、城門兩側。
            var south = _picture.Pieces.Where(q => q.Kind != Kind.Prop).Select(q => q.Foot).DefaultIfEmpty(0).Max();
            south = MathF.Max(south, py0 + plazaH + 2 * T);
            for (var j = 0; j < military.Count; j++)
            {
                var side = j % 2 == 0 ? -1 : 1;
                Place(military[j], side * (_avenue / 2f + military[j].W / 2f + T), south + military[j].H, 2.5f, northLine, 0f);
            }

            // ── 城區範圍（左右對稱，大道在正中；北牆可以藏在大建築的屋頂後面）──
            var inner = _picture.Pieces.ToList();
            var x0 = MathF.Floor((inner.Select(q => q.X).Append(px0).Min() - T) / T) * T;
            var x1 = MathF.Ceiling((inner.Select(q => q.X + q.W).Append(px0 + plazaW).Max() + T) / T) * T;
            var y0 = MathF.Floor((inner.Select(q => q.Y + q.H * 0.55f).Append(py0).Min() - T) / T) * T;
            var y1 = MathF.Ceiling((inner.Select(q => q.Foot).Append(py0 + plazaH).Max() + T) / T) * T;
            var half = MathF.Max(-x0, x1);
            x0 = -half;
            x1 = half;

            // 地面：大道、橫街。
            for (var ty = py0 + plazaH; ty < y1; ty += T)
                for (var tx = -_avenue / 2f; tx < _avenue / 2f; tx += T)
                    Tile(tx, ty, Var(_road, (int)(tx + ty * 5)));
            foreach (var sy in streets.Where(street => street < y1 - T))
                for (var tx = x0; tx < x1; tx += T)
                    Tile(tx, sy, Var(_road, (int)(tx * 7 + sy)));
            if (_tier >= DemoSettlementTier.Village)
                Yards(x0, x1);

            // ── 城牆 ──
            float ringX0 = x0, ringY0 = y0, ringX1 = x1, ringY1 = y1;
            if (wall is not null)
            {
                Walls(wall, gate, x0, x1, y0, y1);
                (ringX0, ringY0, ringX1, ringY1) = (x0 - T, y0 - 2 * T, x1 + T, y1 + 2 * T);
            }
            // 塔樓：牆角外側（沒有城牆時在城區四角）。
            var spots = new[] { (ringX0 - T, ringY0 + 4 * T), (ringX1 + T, ringY0 + 4 * T), (ringX0 - T, ringY1), (ringX1 + T, ringY1) };
            for (var j = 0; j < towers.Count; j++)
            {
                var p = PieceFor(towers[j]);
                var (sx, sf) = spots[j % 4];
                p.X = sx < 0 ? sx - p.W : sx;
                p.Y = sf - p.H + j / 4 * 3 * T;
                _picture.Pieces.Add(p);
            }

            // ── 牆外：農田、農舍、牲口、磨坊 ──
            var fy = ringY1 + 2 * T;
            var (fieldLeft, fieldRight, fieldBottom) = Fields(fy);
            var outsideBottom = fieldBottom;
            float fl = fieldLeft - T, fr = fieldRight + T;
            var farms = Zone(DemoHoldingZone.Fields);
            for (var j = 0; j < farms.Count; j++)
            {
                var p = PieceFor(farms[j]);
                if (j % 2 == 0)
                {
                    p.X = fl - p.W;
                    fl -= p.W + T;
                }
                else
                {
                    p.X = fr;
                    fr += p.W + T;
                }
                p.Y = fy + 3 * T - p.H;
                _picture.Pieces.Add(p);
                if (farms[j].DefId == "BUILD_FARM" && farms[j].Status == DemoBuildingStatus.Standing)
                {
                    for (var a = 0; a < 3; a++)
                    {
                        var animal = Simple(Pick(700 + j * 5 + a, 3) != 0 ? "b12_p_cow" : "b12_p_sheep", Kind.Prop);
                        animal.X = p.X + 4 + a * 18 - (j % 2 == 0 ? 0 : 8);
                        animal.Y = p.Foot + 6 + a % 2 * 8;
                        _picture.Pieces.Add(animal);
                    }
                    var hay = Simple("b11_p_haystack", Kind.Prop);
                    hay.X = j % 2 == 0 ? p.X - 18 : p.X + p.W + 2;
                    hay.Y = p.Foot - hay.H;
                    _picture.Pieces.Add(hay);
                }
            }
            if (farms.Count > 0)
                outsideBottom = MathF.Max(outsideBottom, fy + 6 * T);

            // 西：木材場（木柴堆）；東：採石場、礦場（丘陵地面）。
            var outside = Zone(DemoHoldingZone.Outside);
            var wx = MathF.Min(ringX0, fl) - 3 * T;
            var wy = ringY0 + 4 * T;
            foreach (var b in outside.Where(o => o.DefId == "BUILD_LUMBERYARD"))
            {
                var p = PieceFor(b);
                p.X = wx - p.W;
                p.Y = wy + 2 * T;
                _picture.Pieces.Add(p);
                for (var a = 0; a < 3; a++)
                {
                    var pile = Simple("b11_p_woodpile", Kind.Prop);
                    pile.X = p.X - 12 - a * 12;
                    pile.Y = p.Foot - pile.H - a % 2 * 6;
                    _picture.Pieces.Add(pile);
                }
                wy += p.H + 3 * T;
            }
            var ex = MathF.Max(ringX1, fr) + 3 * T;
            var ey = ringY0 + 4 * T;
            foreach (var b in outside.Where(o => o.DefId is "BUILD_QUARRY" or "BUILD_MINE"))
            {
                var p = PieceFor(b);
                p.X = ex;
                p.Y = ey;
                for (var ty = MathF.Floor((ey + p.H * 0.3f) / T) * T; ty < ey + p.H + T; ty += T)
                    for (var tx = MathF.Floor((ex - T) / T) * T; tx < ex + p.W + T; tx += T)
                        Tile(tx, ty, Var("b11_g_hill_", (int)(tx * 3 + ty * 11), 3));
                _picture.Pieces.Add(p);
                ey += p.H + 2 * T;
            }

            // ── 畫布範圍；臨海時最下面是沙灘、海與港口 ──
            var all = _picture.Pieces;
            var cx0 = MathF.Min(all.Select(q => q.X).DefaultIfEmpty(0).Min(), fieldLeft) - 3 * T;
            var cx1 = MathF.Max(all.Select(q => q.X + q.W).DefaultIfEmpty(0).Max(), fieldRight) + 3 * T;
            var cy0 = all.Select(q => q.Y).DefaultIfEmpty(0).Min() - 2 * T;
            var cy1 = MathF.Max(all.Select(q => q.Foot).DefaultIfEmpty(0).Max(), outsideBottom) + 2 * T;
            var port = outside.FirstOrDefault(b => b.DefId == "BUILD_PORT");
            float roadEnd;
            if (_scene.Nearby.Coast || port is not null)
            {
                var sy = MathF.Ceiling(cy1 / T) * T;
                for (var tx = MathF.Floor(cx0 / T) * T; tx < cx1 + T; tx += T)
                {
                    Tile(tx, sy, Var("b11_g_sand_", (int)tx));
                    Tile(tx, sy + T, "b11_g_sand_wet_1");
                    Tile(tx, sy + 2 * T, Var("b11_g_water_shallow_", (int)(tx * 5)));
                    Tile(tx, sy + 3 * T, Var("b11_g_water_shallow_", (int)(tx * 7 + 1)));
                    Tile(tx, sy + 4 * T, Var("b11_g_water_sea_", (int)(tx * 3)));
                    Tile(tx, sy + 5 * T, Var("b11_g_water_deep_", (int)(tx * 3)));
                }
                roadEnd = sy;
                if (port is not null)
                {
                    var p = PieceFor(port);
                    p.X = _avenue / 2f + 2 * T;
                    p.Y = sy + 3 * T - p.H;
                    _picture.Pieces.Add(p);
                    for (var a = 0; a < 2; a++)
                    {
                        var boat = Simple(a == 0 ? "b11_boat_1" : "b11_boat_2", Kind.Prop);
                        boat.X = p.X + p.W + 12 + a * 44;
                        boat.Y = sy + 3 * T + 6 + a * 10;
                        _picture.Pieces.Add(boat);
                    }
                }
                cy1 = sy + 6 * T;
            }
            else
                roadEnd = cy1;
            for (var ty = ringY1; ty < roadEnd; ty += T)
                for (var tx = -_avenue / 2f; tx < _avenue / 2f; tx += T)
                    Tile(tx, ty, Var(_road, (int)(tx + ty)));

            Trees(wall is not null, (ringX0, ringY0, ringX1, ringY1), (cx0, cy0, cx1, cy1));

            // 位移到正座標（以格對齊）。
            var ox = -(int)MathF.Floor(cx0 / T) * T;
            var oy = -(int)MathF.Floor(cy0 / T) * T;
            foreach (var q in _picture.Pieces)
            {
                q.X += ox;
                q.Y += oy;
            }
            foreach (var (at, sprite) in _ground)
                _picture.Ground[new Vector2I(at.X + ox, at.Y + oy)] = sprite;
            foreach (var v in _yard)
                _picture.Yard.Add(new Vector2I(v.X + ox / T, v.Y + oy / T));
            _picture.Size = new Vector2I((int)MathF.Ceiling((cx1 + ox) / T) * T, (int)MathF.Ceiling((cy1 + oy) / T) * T);
            return _picture;
        }

        private readonly HashSet<Vector2I> _yard = new();

        /// <summary>建築腳下與門前是踩實的地面（角點；邊緣交給過渡圖塊）；村、鎮的住宅旁偶爾一小塊菜園；屋旁的小東西。</summary>
        private void Yards(float x0, float x1)
        {
            _picture.Wang = _tier <= DemoSettlementTier.Town ? "b11_w_grass_dirt" : "b11_w_grass_stone";
            foreach (var q in _picture.Pieces.ToList())
            {
                if (q.Kind is not (Kind.House or Kind.Building or Kind.Ruin))
                    continue;
                var vx0 = (int)MathF.Floor((q.X + 6) / T);
                var vx1 = (int)MathF.Ceiling((q.X + q.W - 6) / T);
                var vy0 = (int)MathF.Floor((q.Foot - MathF.Min(q.H * 0.4f, 2 * T)) / T);
                var vy1 = (int)MathF.Ceiling(q.Foot / T) + 1;
                for (var vy = vy0; vy <= vy1; vy++)
                    for (var vx = vx0; vx <= vx1; vx++)
                        _yard.Add(new Vector2I(vx, vy));
                if (q.Kind == Kind.House && _tier <= DemoSettlementTier.Town && Pick((int)(q.X * 3 + q.Y) & 0xffff, 3) == 0)
                {
                    var gx = vx1 * T;
                    for (var gy = (vy1 - 2) * T; gy < vy1 * T; gy += T)
                        if (!_ground.ContainsKey(new Vector2I(gx, gy)) && Free(gx, gy, T, T))
                            Tile(gx, gy, Var("b11_g_garden_", gx + gy));
                }
            }
            var props = _tier switch
            {
                DemoSettlementTier.Village => new[] { "b11_p_woodpile", "b11_p_barrel", "b11_p_coop", "b11_p_clothesline", "b11_p_doghouse", "b11_p_haystack" },
                DemoSettlementTier.Town => new[] { "b11_p_barrel", "b11_p_crate", "b11_p_woodpile", "b11_p_clothesline", "b11_p_flowerbed", "b11_p_cart" },
                DemoSettlementTier.City => new[] { "b11_p_barrel", "b11_p_crate", "b11_p_flowerbed", "b11_p_bench", "b11_p_cart", "b11_p_torch" },
                _ => new[] { "b11_p_barrel", "b11_p_crate", "b11_p_flowerbed", "b11_p_bench", "b11_p_torch", "b11_p_signboard" }
            };
            var owners = _picture.Pieces.Where(q => q.Kind is Kind.House or Kind.Building).ToList();
            for (var j = 0; j < owners.Count; j++)
            {
                if (Pick(800 + j, 3) != 0)
                    continue;
                var q = owners[j];
                var prop = Simple(props[Pick(820 + j, props.Length)], Kind.Prop);
                foreach (var px in new[] { q.X + q.W + 2, q.X - prop.W - 2 })
                {
                    if (px < x0 + 2 || px + prop.W > x1 - 2 || !Free(px, q.Foot - prop.H, prop.W, prop.H))
                        continue;
                    prop.X = px;
                    prop.Y = q.Foot - prop.H;
                    Take(px, prop.Y, prop.W, prop.H);
                    _picture.Pieces.Add(prop);
                    break;
                }
            }
        }

        /// <summary>城牆一圈：施工中依進度前段是牆、後段是鷹架；受損時一部分換成斷牆；毀損全是斷牆。城門在大道南端。</summary>
        private void Walls(DemoBuilding wall, DemoBuilding? gate, float x0, float x1, float y0, float y1)
        {
            var stone = wall.DefId == DemoBuildings.WallStone;
            var kind = stone ? (_tier >= DemoSettlementTier.Capital ? "brick" : "stone") : "palisade";
            var material = stone ? "stone" : "palisade";
            var gateWidth = gate is null ? 0f : SizeOf($"b11_{kind}_gate_h").X;
            var segments = new List<(string Part, float X, float Foot)>();
            float xa = x0 - T, xb = x1;
            segments.Add(("corner_nw", xa, y0));
            for (var x = x0; x < x1; x += T)
                segments.Add(("h", x, y0));
            segments.Add(("corner_ne", xb, y0));
            for (var y = y0 + T; y < y1 + T; y += T)
            {
                segments.Add(("v", xa, y));
                segments.Add(("v", xb, y));
            }
            segments.Add(("corner_sw", xa, y1 + T));
            for (var x = x0; x < x1; x += T)
            {
                if (gate is not null ? x > -gateWidth / 2f - T && x < gateWidth / 2f : x > -_avenue / 2f - T && x < _avenue / 2f)
                    continue; // 城門（沒有城門時留個缺口）
                segments.Add(("h", x, y1 + T));
            }
            segments.Add(("corner_se", xb, y1 + T));
            for (var k = 0; k < segments.Count; k++)
            {
                var (part, x, foot) = segments[k];
                var piece = new Piece { Kind = Kind.Wall, BuildingId = wall.Id, Segment = k, Segments = segments.Count, Part = part };
                piece.Sprite = WallSprite(piece, wall, _tier, _seed);
                // 牆、鷹架、斷牆同一種段的高度一樣（素材依此繪製），排版用完好的牆量。
                var size = SizeOf($"b11_{kind}_{part}");
                piece.W = size.X;
                piece.H = size.Y;
                piece.X = x;
                piece.Y = foot + T - piece.H;
                _picture.Pieces.Add(piece);
            }
            if (gate is not null)
            {
                var progress = Math.Clamp(gate.Progress / (float)Math.Max(1, gate.Def.Hours), 0f, 1f);
                var sprite = gate.Status switch
                {
                    DemoBuildingStatus.Ruined => $"b12_ruin_{material}_gate_h",
                    DemoBuildingStatus.Building => SiteSprite(gateWidth, progress),
                    _ => $"b11_{kind}_gate_h"
                };
                var piece = Simple(sprite, Kind.Wall, DemoHoldingReport.Name(gate.DefId, _tier) + " " + DemoHoldingReport.StateText(gate.Status,
                    gate.Status == DemoBuildingStatus.Building ? (int)(progress * 100) : gate.Status == DemoBuildingStatus.Ruined ? 0 : Math.Clamp(gate.Durability / 10, 1, 100)));
                piece.BuildingId = gate.Id;
                piece.Durability = gate.Durability;
                piece.Progress = gate.Status == DemoBuildingStatus.Building ? progress : -1f;
                piece.X = -piece.W / 2f;
                piece.Y = y1 + 2 * T - piece.H;
                _picture.Pieces.Add(piece);
            }
        }

        /// <summary>農田：大道兩側各一塊拼布（附近已開墾的農田越多越大片，最多 <see cref="TempMaxPlots"/> 塊，每塊 3×2 格），外圍一圈籬笆。回傳左右邊界與底。</summary>
        private (float Left, float Right, float Bottom) Fields(float fy)
        {
            var plots = Math.Min(TempMaxPlots, _scene.Fields);
            const float pw = 3 * T, ph = 2 * T;
            var perRow = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(MathF.Max(1f, plots / 2f) * 2f)), 2, 6);
            var crops = new[]
            {
                "b11_g_field_wheat", "b11_g_field_green", "b11_g_field_vegetable", "b11_g_field_flax", "b11_g_field_sprout",
                "b11_g_field_plowed", "b11_g_field_sown", "b11_g_field_wheat", "b11_g_field_fallow"
            };
            float left = 0, right = 0;
            var rows = 0;
            foreach (var side in new[] { -1, 1 })
            {
                var mine = Enumerable.Range(0, plots).Where(f => (f % 2 == 0 ? -1 : 1) == side).ToList();
                if (mine.Count == 0)
                    continue;
                var cols = Math.Min(perRow, mine.Count);
                var sideRows = (mine.Count + cols - 1) / cols;
                rows = Math.Max(rows, sideRows);
                var bx0 = side > 0 ? _avenue / 2f + 2 * T : -(_avenue / 2f + 2 * T) - cols * pw;
                for (var k = 0; k < mine.Count; k++)
                {
                    var px = bx0 + k % cols * pw;
                    var py = fy + T + k / cols * ph;
                    var crop = crops[Pick(500 + mine[k], crops.Length)];
                    for (var dy = 0f; dy < ph; dy += T)
                        for (var dx = 0f; dx < pw; dx += T)
                            Tile(px + dx, py + dy, crop);
                }
                float fx0 = bx0 - T, fx1 = bx0 + cols * pw, fy0 = fy, fy1 = fy + T + sideRows * ph;
                var fence = new List<(string Part, float X, float Y)> { ("corner_nw", fx0, fy0), ("corner_ne", fx1, fy0), ("corner_sw", fx0, fy1), ("corner_se", fx1, fy1) };
                for (var x = fx0 + T; x < fx1; x += T)
                {
                    fence.Add(("h", x, fy0));
                    fence.Add(("h", x, fy1));
                }
                for (var y = fy0 + T; y < fy1; y += T)
                {
                    fence.Add(("v", fx0, y));
                    fence.Add(("v", fx1, y));
                }
                foreach (var (part, x, y) in fence)
                {
                    var piece = Simple($"b11_fence_{part}", Kind.Fence);
                    piece.X = x;
                    piece.Y = y + T - piece.H;
                    _picture.Pieces.Add(piece);
                }
                left = MathF.Min(left, fx0);
                right = MathF.Max(right, fx1 + T);
            }
            return (left, right, fy + MathF.Max(rows * ph + 3 * T, 4 * T));
        }

        /// <summary>樹：附近有森林時西側成林，其餘零星；有城牆時牆內不種。</summary>
        private void Trees(bool walled, (float X0, float Y0, float X1, float Y1) ring, (float X0, float Y0, float X1, float Y1) canvas)
        {
            var blocked = new HashSet<(int, int)>();
            void Block(float x, float y, float w, float hgt)
            {
                for (var cy = (int)MathF.Floor(y / 8); cy < (int)MathF.Ceiling((y + hgt) / 8); cy++)
                    for (var cx = (int)MathF.Floor(x / 8); cx < (int)MathF.Ceiling((x + w) / 8); cx++)
                        blocked.Add((cx, cy));
            }
            bool Open(float x, float y, float w, float hgt)
            {
                for (var cy = (int)MathF.Floor(y / 8); cy < (int)MathF.Ceiling((y + hgt) / 8); cy++)
                    for (var cx = (int)MathF.Floor(x / 8); cx < (int)MathF.Ceiling((x + w) / 8); cx++)
                        if (blocked.Contains((cx, cy)))
                            return false;
                return true;
            }
            foreach (var q in _picture.Pieces)
                Block(q.X - 2, q.Y + q.H * 0.4f, q.W + 4, q.H * 0.6f + 6);
            foreach (var (at, sprite) in _ground)
                if (!sprite.StartsWith("b11_g_hill_", StringComparison.Ordinal))
                    Block(at.X - 4, at.Y - 4, T + 8, T + 8);
            foreach (var v in _yard)
                Block(v.X * T - 8, v.Y * T - 8, T, T);
            if (walled)
                Block(ring.X0 - T, ring.Y0, ring.X1 - ring.X0 + 2 * T, ring.Y1 - ring.Y0 + T);
            var wanted = (_scene.Nearby.Forest ? 46 : 14) + (!walled && _tier <= DemoSettlementTier.Town ? 8 : 0);
            var names = new[] { "b12_n_tree_oak_1", "b12_n_tree_oak_2", "b12_n_tree_pine_1", "b12_n_tree_pine_2" };
            float width = canvas.X1 - canvas.X0, height = canvas.Y1 - canvas.Y0;
            for (int attempt = 0, placed = 0; placed < wanted && attempt < wanted * 20; attempt++)
            {
                var tree = Simple(names[Pick(900 + attempt, 4)], Kind.Tree);
                var tx = _scene.Nearby.Forest && attempt % 4 != 0
                    ? canvas.X0 + Pick(910 + attempt, (int)MathF.Max(1, ring.X0 - canvas.X0 - tree.W))
                    : canvas.X0 + Pick(920 + attempt, (int)MathF.Max(1, width - tree.W));
                var ty = canvas.Y0 + Pick(930 + attempt, (int)MathF.Max(1, height - tree.H - 6 * T));
                if (!Open(tx + 4, ty + tree.H - 12, tree.W - 8, 12))
                    continue;
                tree.X = tx;
                tree.Y = ty;
                _picture.Pieces.Add(tree);
                Block(tx + 2, ty + tree.H - 16, tree.W - 4, 16);
                placed++;
            }
        }
    }
}
