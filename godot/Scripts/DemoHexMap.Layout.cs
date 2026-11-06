using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 3 節：WorldBox 式聚落版面——對齊方格的廣場（篝火＋囤糧圍欄）、直角長出的路網、散在路邊的房子、
/// 建地與城牆。只改畫面。營地：篝火旁的帳篷；村莊：土路、木屋；城鎮：石子路、房子、市集；城市：石牆圍住、石屋、神殿；
/// 首都再加主樓。房子不排成棋盤、彼此留一方格；屋頂是主要文化的代表色。人口快到下一棟房子時，多一塊木樁圍起的建地。
/// 不臨水的聚落在廣場邊有一口井。
/// 版面依聚落編號與方格固定決定（同一世界每次相同），只用聚落佔格（<see cref="FootprintCells"/>）裡的陸地，不壓田地、不跨到別的聚落。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>一塊建築地塊（方格座標；左上角與寬高）。</summary>
    public readonly record struct Lot(int X, int Y, int W, int H, Building Kind, bool Construction = false);

    /// <summary>一個聚落的版面。<see cref="Wall"/>＝城牆或木柵圍住的方格範圍（沒有＝null）。</summary>
    public sealed record SettlementPlan(int SettlementId, Rect2I Plaza, IReadOnlyList<Vector2I> Roads, IReadOnlyList<Lot> Lots, Rect2I? Wall, bool StoneWall,
        IReadOnlyList<Vector2I> Gates);

    /// <summary>【TEMP】廣場邊長、道路長度（方格）。</summary>
    private static int PlazaSize(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp or DemoSettlementTier.Village => 2,
        DemoSettlementTier.Town => 3,
        _ => 4
    };

    private static int RoadLength(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 0,
        DemoSettlementTier.Village => 5,
        DemoSettlementTier.Town => 8,
        DemoSettlementTier.City => 10,
        _ => 12
    };

    /// <summary>【TEMP】第 08-18b 章：支路——主路每隔 3 方格有 60% 長出一條垂直的支路，長度是主路剩下長度的一半（至少 2）。</summary>
    private const int TempBranchEvery = 3;
    private const float TempBranchChance = 0.6f;

    /// <summary>【TEMP】人口超過下一棟房子所需的這個比例時，多一塊建地。</summary>
    private const float TempConstructionAt = 0.5f;

    /// <summary>第 08-18b 章：住屋依等級升級（帳篷→木屋→房子→石屋）；同一聚落裡靠中心的舊屋先升一級。</summary>
    private static Building HouseFor(DemoSettlementTier tier, int index, int count) => tier switch
    {
        DemoSettlementTier.Camp => Building.Tent,
        DemoSettlementTier.Village => index < Math.Max(1, count / 5) ? Building.House : Building.Hut,
        DemoSettlementTier.Town => index < count * 3 / 10 ? Building.StoneHouse : index >= count * 8 / 10 ? Building.Hut : Building.House,
        _ => index >= count * 7 / 10 ? Building.House : Building.StoneHouse
    };

    private static bool IsResidence(Building kind) => kind is Building.Tent or Building.Hut or Building.House or Building.StoneHouse;

    /// <summary>住屋數與是否有建地（人口快到下一棟時）。</summary>
    private static (int Houses, bool Building) HouseCount(long people, DemoSettlementTier tier)
    {
        if (tier == DemoSettlementTier.Camp)
            return ((int)Math.Clamp(people / TempPeoplePerTent, 1, 4), false);
        var houses = (int)Math.Clamp(people / TempPeoplePerHouse, 1, 60);
        var growing = houses < 60 && people % TempPeoplePerHouse >= TempPeoplePerHouse * TempConstructionAt;
        return (houses, growing);
    }

    /// <summary>建築地塊的大小（方格）：寬依素材寬度、深 2～4 格；素材往上長出地塊（俯視略帶正面）。</summary>
    private static (int W, int H) LotSize(Building kind) => kind switch
    {
        Building.Tent => (2, 2),
        Building.Market or Building.Temple => (5, 3),
        Building.Keep => (5, 4),
        Building.Well => (2, 2),
        _ => (3, 2)
    };

    private readonly Dictionary<int, SettlementPlan> _layouts = new();
    private readonly Dictionary<int, long> _layoutSignatures = new();
    private Dictionary<int, byte> _ground = new();
    private (object?, object?, object?, object?, object?, object?, object?) _layoutKey;

    /// <summary>
    /// 全部聚落的版面；同時產生地表要畫的聚落地面。每個聚落只在自己的版面條件（等級、屋數、佔格、佔格上的田、信仰、井）改變時重算，
    /// 快轉時不必每次都重排全部聚落。
    /// </summary>
    private void EnsureLayouts()
    {
        if (World is null || _surface is null)
            return;
        var key = ((object?)World, (object?)Sites, (object?)FadedRuins, (object?)SettlementScale, (object?)Lineage, (object?)Land, (object?)FaithOf);
        if (key.Equals(_layoutKey))
            return;
        if (!ReferenceEquals(key.Item1, _layoutKey.Item1))
        {
            _layouts.Clear();
            _layoutSignatures.Clear();
        }
        _layoutKey = key;
        _surface.EnsureGrid(World.Columns, World.Rows);
        var alive = Sites?.Settlements.Where(x => !x.IsRuin && !FadedRuins.Contains(x.Id)).OrderBy(x => x.Id).ToList() ?? new List<DemoSettlement>();
        var changed = false;
        foreach (var gone in _layouts.Keys.Where(id => alive.All(s => s.Id != id)).ToList())
        {
            _layouts.Remove(gone);
            _layoutSignatures.Remove(gone);
            changed = true;
        }
        foreach (var site in alive)
        {
            var signature = LayoutSignature(site);
            if (_layoutSignatures.TryGetValue(site.Id, out var old) && old == signature)
                continue;
            _layoutSignatures[site.Id] = signature;
            _layouts[site.Id] = ComputeLayout(site);
            changed = true;
        }
        if (!changed && _ground.Count > 0)
            return;
        var ground = new Dictionary<int, byte>();
        var tx = _surface.TilesX;
        foreach (var layout in _layouts.Values.OrderBy(l => l.SettlementId))
        {
            var (_, tier) = SettlementScale.TryGetValue(layout.SettlementId, out var scale) ? scale : (0L, DemoSettlementTier.Camp);
            void Mark(int x, int y, byte kind)
            {
                if (x >= 0 && y >= 0 && x < tx)
                    ground[y * tx + x] = kind;
            }
            if (layout.Wall is { } wall && tier >= DemoSettlementTier.City)
                for (var y = wall.Position.Y + 1; y < wall.End.Y - 1; y++)
                    for (var x = wall.Position.X + 1; x < wall.End.X - 1; x++)
                    {
                        var inside = _surface.TileOwner(x, y);
                        if (inside >= 0 && IsLand(inside) && !IsField(inside))
                            ground.TryAdd(y * tx + x, TerrainSurface.GroundCobble); // 城內：石子地
                    }
            foreach (var road in layout.Roads)
                Mark(road.X, road.Y, tier >= DemoSettlementTier.Town ? TerrainSurface.GroundStreet : TerrainSurface.GroundRoad); // 城鎮以上石子路
            for (var y = layout.Plaza.Position.Y; y < layout.Plaza.End.Y; y++)
                for (var x = layout.Plaza.Position.X; x < layout.Plaza.End.X; x++)
                    Mark(x, y, tier >= DemoSettlementTier.Town ? TerrainSurface.GroundPlazaStone : TerrainSurface.GroundPlazaDirt);
            foreach (var lot in layout.Lots)
                for (var y = lot.Y; y < lot.Y + lot.H; y++)
                    for (var x = lot.X; x < lot.X + lot.W; x++)
                        Mark(x, y, TerrainSurface.GroundYard);
        }
        _ground = ground;
        // 有聚落地面的格（給地表代碼：聚落格不長樹）。
        _builtCells.Clear();
        foreach (var tile in ground.Keys)
        {
            var cell = _surface.TileOwner(tile % tx, tile / tx);
            if (cell >= 0)
                _builtCells.Add(new DemoCellRef(cell % World.Columns, cell / World.Columns));
        }
        foreach (var site in alive)
            _builtCells.Add(new DemoCellRef(site.Column, site.Row));
    }

    /// <summary>版面條件的指紋（相同就沿用上次的版面）。</summary>
    private long LayoutSignature(DemoSettlement site)
    {
        var (people, tier) = SettlementScale.TryGetValue(site.Id, out var scale) ? scale : (0L, DemoSettlementTier.Camp);
        var (houses, building) = HouseCount(people, tier);
        unchecked
        {
            var h = (long)tier * 1_000_003 + (houses * 2 + (building ? 1 : 0)) * 7919 + (FaithOf.ContainsKey(site.Id) ? 1 : 0);
            foreach (var c in FootprintCells(site, tier))
                h = h * 31 + c.Row * 65_537 + c.Column * 3 + (int)Land.StateOf(c);
            return h;
        }
    }

    private bool IsField(int cell)
        => Land.StateOf(new DemoCellRef(cell % World!.Columns, cell / World.Columns)) is DemoLandDevelopment.Farmland or DemoLandDevelopment.Clearing;

    private bool IsLand(int cell) => World!.Cells[cell].Terrain != DemoTerrain.Water;

    /// <summary>可以蓋東西的方格：屬於這個聚落的佔格、是陸地、不是田、還沒被別的聚落用掉。</summary>
    private SettlementPlan ComputeLayout(DemoSettlement site)
    {
        var surface = _surface!;
        var (people, tier) = SettlementScale.TryGetValue(site.Id, out var scale) ? scale : (0L, DemoSettlementTier.Camp);
        var cells = FootprintCells(site, tier).Select(c => c.Row * World!.Columns + c.Column).ToHashSet();
        var tx = surface.TilesX;
        var used = new HashSet<int>();
        bool Free(int x, int y)
        {
            if (x < 0 || y < 0 || x >= tx || y >= surface.TilesY)
                return false;
            var o = surface.TileOwner(x, y);
            return o >= 0 && cells.Contains(o) && IsLand(o) && !IsField(o) && !used.Contains(y * tx + x);
        }
        void Use(int x, int y) => used.Add(y * tx + x);

        var center = TerrainSurface.TileOfCell(site.Column, site.Row);
        // 廣場：以格中心為中心（陸地不夠時照樣畫在能畫的方格上）。
        var size = PlazaSize(tier);
        var plaza = new Rect2I(center.X - size / 2, center.Y - size / 2, size, size);
        for (var y = plaza.Position.Y; y < plaza.End.Y; y++)
            for (var x = plaza.Position.X; x < plaza.End.X; x++)
                if (Free(x, y))
                    Use(x, y);

        // 道路：從廣場四邊中點往外直走，遇到不能蓋的方格就停。
        var roads = new List<Vector2I>();
        var length = RoadLength(tier);
        var tips = new List<(Vector2I Next, Vector2I Step)>(); // 每條路的下一格與方向（圍牆之後再延伸到城門）
        if (length > 0)
        {
            var mid = plaza.Position + plaza.Size / 2;
            foreach (var (dx, dy, sx, sy) in new[] { (1, 0, plaza.End.X, mid.Y), (-1, 0, plaza.Position.X - 1, mid.Y), (0, 1, mid.X, plaza.End.Y), (0, -1, mid.X, plaza.Position.Y - 1) })
            {
                var k = 0;
                for (; k < length; k++)
                {
                    var x = sx + dx * k;
                    var y = sy + dy * k;
                    if (!Free(x, y))
                        break;
                    Use(x, y);
                    roads.Add(new Vector2I(x, y));
                }
                if (k == length)
                    tips.Add((new Vector2I(sx + dx * k, sy + dy * k), new Vector2I(dx, dy)));
                // 第 08-18b 章：支路——沿主路每隔幾方格往一側長出垂直的短路（直角路網，像 WorldBox 一樣慢慢長出去）。
                for (var b = TempBranchEvery - 1; b < k - 1; b += TempBranchEvery)
                {
                    if (LayoutHash(site.Id, b, dx * 3 + dy) > TempBranchChance)
                        continue;
                    var side = LayoutHash(site.Id, b, dx * 5 + dy * 7 + 11) < 0.5f ? 1 : -1;
                    var (bx, by) = (dy * side, dx * side); // 垂直方向
                    var branch = Math.Max(2, (length - b) / 2);
                    for (var j = 1; j <= branch; j++)
                    {
                        var x = sx + dx * b + bx * j;
                        var y = sy + dy * b + by * j;
                        if (!Free(x, y))
                            break;
                        Use(x, y);
                        roads.Add(new Vector2I(x, y));
                    }
                }
            }
        }
        var paved = roads.ToHashSet();
        for (var y = plaza.Position.Y; y < plaza.End.Y; y++)
            for (var x = plaza.Position.X; x < plaza.End.X; x++)
                paved.Add(new Vector2I(x, y));

        // 建築：先放地標（主樓、市集、神殿、井），再放住屋；都要貼著廣場或道路（營地貼著火堆）。
        var kinds = new List<Building>();
        if (tier == DemoSettlementTier.Capital)
            kinds.Add(Building.Keep);
        if (tier >= DemoSettlementTier.Town)
            kinds.Add(Building.Market);
        if (tier >= DemoSettlementTier.City && FaithOf.ContainsKey(site.Id))
            kinds.Add(Building.Temple);
        if (!World!.IsNearWater(new DemoCellRef(site.Column, site.Row)))
            kinds.Add(Building.Well);
        var (houses, construction) = HouseCount(people, tier);
        var landmarks = kinds.Count;
        for (var i = 0; i < houses; i++)
            kinds.Add(HouseFor(tier, i, houses));
        if (construction)
            kinds.Add(HouseFor(tier, houses, houses + 1));
        var house = HouseFor(tier, 0, 1);

        var lots = new List<Lot>();
        var built = new HashSet<Vector2I>();
        var reach = 4 + length + size;
        var candidates = new List<(int X, int Y)>();
        for (var y = center.Y - reach; y <= center.Y + reach; y++)
            for (var x = center.X - reach; x <= center.X + reach; x++)
                candidates.Add((x, y));
        paved.UnionWith(roads);
        for (var index = 0; index < kinds.Count; index++)
        {
            var kind = kinds[index];
            var site2 = index == kinds.Count - 1 && construction;
            var (w, h) = LotSize(kind);
            bool Fits(int x, int y)
            {
                for (var j = 0; j < h; j++)
                    for (var i = 0; i < w; i++)
                        if (!Free(x + i, y + j))
                            return false;
                // 第 08-18b 章：房子之間留一方格（散在路邊，不排成街區）。
                for (var j = -1; j <= h; j++)
                    for (var i = -1; i <= w; i++)
                        if (built.Contains(new Vector2I(x + i, y + j)))
                            return false;
                return true;
            }
            bool Fronts(int x, int y)
            {
                if (tier == DemoSettlementTier.Camp)
                    return Math.Max(Math.Abs(x + w / 2 - center.X), Math.Abs(y + h / 2 - center.Y)) <= 4;
                // 正面（下緣）或側面貼著道路、廣場。
                bool Near(int ax, int ay) => paved.Contains(new Vector2I(ax, ay));
                for (var i = -1; i <= w; i++)
                    if (Near(x + i, y + h) || Near(x + i, y - 1))
                        return true;
                for (var j = 0; j < h; j++)
                    if (Near(x - 1, y + j) || Near(x + w, y + j))
                        return true;
                return false;
            }
            var best = candidates
                .Where(c => Fits(c.X, c.Y) && Fronts(c.X, c.Y))
                .Select(c => (c.X, c.Y, Score: Math.Abs(c.X + w / 2f - center.X) + Math.Abs(c.Y + h / 2f - center.Y) * 1.2f
                    + LayoutHash(site.Id, c.X, c.Y) * (index < landmarks ? 0.8f : 3f)))
                .OrderBy(c => c.Score).ThenBy(c => c.Y).ThenBy(c => c.X)
                .Select(c => ((int, int)?)(c.X, c.Y)).FirstOrDefault();
            if (best is not { } spot)
                continue;
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                {
                    Use(spot.Item1 + i, spot.Item2 + j);
                    built.Add(new Vector2I(spot.Item1 + i, spot.Item2 + j));
                }
            lots.Add(new Lot(spot.Item1, spot.Item2, w, h, kind, site2));
        }
        // 至少一棟（陸地太少時直接放在廣場上）。
        if (lots.Count == 0)
            lots.Add(new Lot(plaza.Position.X, plaza.Position.Y, 2, 2, house));

        // 第 08-18b 章：城牆只有城市以上才有，圍住廣場與所有建築（外留一格）；道路穿過的地方是城門。
        Rect2I? wall = null;
        var gates = new List<Vector2I>();
        if (tier >= DemoSettlementTier.City)
        {
            var box = plaza;
            foreach (var lot in lots)
                box = box.Merge(new Rect2I(lot.X, lot.Y, lot.W, lot.H));
            box = box.Grow(1);
            wall = box;
            // 道路一路通到城牆（城門）再往外一格。
            foreach (var (next, step) in tips)
            {
                var at = next;
                while (box.HasPoint(at) && Free(at.X, at.Y))
                {
                    Use(at.X, at.Y);
                    roads.Add(at);
                    at += step;
                }
                if (!box.HasPoint(at) && Free(at.X, at.Y))
                    roads.Add(at);
            }
            foreach (var road in roads)
                if (road.X == box.Position.X || road.X == box.End.X - 1 || road.Y == box.Position.Y || road.Y == box.End.Y - 1)
                    gates.Add(road);
        }
        return new SettlementPlan(site.Id, plaza, roads, lots, wall, tier >= DemoSettlementTier.City, gates);
    }

    private static float LayoutHash(int a, int b, int c)
    {
        unchecked
        {
            var h = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>版面裡的建築、牆畫在地表之上（由上而下排序，前面的屋子蓋住後面的）。</summary>
    private void DrawLayouts(Vector2 start, float radius)
    {
        if (World is null || Sites is null || _surface is null)
            return;
        EnsureLayouts();
        var t = TerrainSurface.TempTile;
        Vector2 Screen(float tx, float ty) => TerrainSurface.TexelToScreen(new Vector2(tx * t, ty * t), start, radius);
        var items = new List<(float Y, Action Draw)>();
        foreach (var site in Sites.Settlements)
        {
            if (site.IsRuin || FadedRuins.Contains(site.Id) || !_layouts.TryGetValue(site.Id, out var layout))
                continue;
            var cell = World.GetCell(site.Column, site.Row);
            if (!OnScreen(start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius), radius * 4f))
                continue;
            if (layout.Wall is { } wall)
                AddWall(items, layout, wall, Screen, radius);
            var roof = CultureOf.TryGetValue(site.Id, out var culture) ? CultureColor(culture) : new Color(0.62f, 0.25f, 0.18f);
            foreach (var lot in layout.Lots)
            {
                var bottom = Screen(lot.X + lot.W / 2f, lot.Y + lot.H);
                var kind = lot.Kind;
                var width = lot.W * t * radius / TerrainSurface.TempTexelsPerRadius;
                if (lot.Construction)
                    items.Add((bottom.Y, () => DrawConstruction(bottom, width, lot.H * t * radius / TerrainSurface.TempTexelsPerRadius, radius)));
                else if (IsResidence(kind))
                    items.Add((bottom.Y, () => DrawHouse(kind, bottom, width, roof, radius)));
                else
                    items.Add((bottom.Y, () => DrawLotBuilding(kind, bottom, width, radius)));
            }
            // 第 08-18b 章：中心是篝火加囤糧圍欄（營地起就有）。
            var fire = Screen(layout.Plaza.Position.X + layout.Plaza.Size.X / 2f, layout.Plaza.Position.Y + layout.Plaza.Size.Y / 2f);
            items.Add((fire.Y, () => DrawCampfire(fire, radius)));
            var store = Screen(layout.Plaza.Position.X + 0.5f, layout.Plaza.Position.Y + 1f);
            items.Add((store.Y, () => DrawStockpile(store, radius)));
            if (RecentDisasters.Contains(site.Id)) // 第 12-1 章 C3：一年內遭天災
            {
                var mark = Screen(layout.Plaza.End.X + 0.5f, layout.Plaza.Position.Y - 0.5f);
                items.Add((float.MaxValue, () =>
                {
                    if (!DrawPixelPart("px_icon_disaster", mark, radius))
                        DrawMarker(ArtAssets.IconDisaster, mark, radius * 0.7f);
                }));
            }
        }
        foreach (var (_, draw) in items.OrderBy(i => i.Y))
            draw();
        DrawCreatures(start, radius); // 第 08-18b 章：野生動物與地下城怪物
        DrawWalkers(start, radius); // 第 08-18b 章第 4 節：近景小人
        DrawNameplates(start, radius);
    }

    private void DrawLotBuilding(Building kind, Vector2 bottom, float lotWidth, float radius)
    {
        // 落地陰影（光源左上：陰影往右下）。
        DrawSetTransform(bottom + new Vector2(lotWidth * 0.06f, -lotWidth * 0.03f), 0f, new Vector2(1f, 0.3f));
        DrawCircle(Vector2.Zero, lotWidth * 0.55f, new Color(0.08f, 0.06f, 0.04f, 0.38f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (kind == Building.Well)
        {
            DrawWell(bottom, radius);
            return;
        }
        var px = kind switch
        {
            Building.Tent => "bld_tent", Building.Hut => "bld_hut", Building.House => "bld_house", Building.StoneHouse => "bld_stone_house",
            Building.Market => "bld_market", Building.Temple => "bld_temple", _ => "bld_keep"
        };
        if (!DrawPixelPart(px, bottom, radius))
            DrawBuilding(kind, bottom - new Vector2(0f, radius * 0.3f), radius);
    }

    private void DrawCampfire(Vector2 at, float radius)
    {
        var p = radius / TerrainSurface.TempArtTexelsPerRadius;
        DrawCircle(at, 4f * p, new Color(0.30f, 0.26f, 0.22f));
        var flicker = 0.8f + 0.2f * Mathf.Sin(Time.GetTicksMsec() / 120f);
        DrawCircle(at - new Vector2(0, 1.5f * p), 2.6f * p * flicker, new Color(0.98f, 0.55f, 0.15f));
        DrawCircle(at - new Vector2(0, 2.2f * p), 1.3f * p * flicker, new Color(1f, 0.85f, 0.35f));
    }

    /// <summary>
    /// 第 08-17 章第 3 節：城牆——上下兩側畫正面（素材橫向重複或程式畫的牆面與垛口），左右兩側只畫牆頂（一條窄帶），
    /// 四角與城門畫塔或門柱。上側與左右在建築之前畫（在後面），下側在建築之後畫（在前面）。
    /// </summary>
    private void AddWall(List<(float Y, Action Draw)> items, SettlementPlan layout, Rect2I wall, Func<float, float, Vector2> screen, float radius)
    {
        var p = radius / TerrainSurface.TempArtTexelsPerRadius; // 一個（第四批零件）像素的畫面大小
        var stone = layout.StoneWall;
        var face = stone ? new Color(0.62f, 0.60f, 0.56f) : new Color(0.50f, 0.33f, 0.18f);
        var dark = face.Darkened(0.4f);
        var top = face.Lightened(0.15f);
        var faceHeight = (stone ? 12f : 9f) * p;
        var gates = layout.Gates.ToHashSet();
        var sprite = PixelParts.Texture(stone ? "bld_stone_wall" : "bld_palisade", repeat: true);

        void Front(float y, float x0, float x1)
        {
            // 一段正面牆：從 x0 到 x1（方格），底邊在方格 y 的下緣。
            var a = screen(x0, y + 1);
            var b = screen(x1, y + 1);
            if (b.X - a.X <= 0.5f)
                return;
            if (sprite is not null)
            {
                var h = sprite.GetHeight() * p;
                DrawTextureRectRegion(sprite, new Rect2(a.X, a.Y - h, b.X - a.X, h), new Rect2(0f, 0f, (b.X - a.X) / p, sprite.GetHeight()));
            }
            else
            {
                DrawRect(new Rect2(a.X, a.Y - faceHeight, b.X - a.X, faceHeight), face);
                DrawRect(new Rect2(a.X, a.Y - faceHeight, b.X - a.X, 2f * p), top);
                if (stone)
                    for (var x = a.X; x < b.X - 2f * p; x += 4f * p)
                        DrawRect(new Rect2(x, a.Y - faceHeight - 2f * p, 2f * p, 2f * p), face); // 垛口
                else
                    for (var x = a.X; x < b.X; x += 3f * p)
                        DrawRect(new Rect2(x, a.Y - faceHeight - 1.5f * p, 2f * p, 1.5f * p), face.Lightened(0.1f)); // 尖木樁
                DrawRect(new Rect2(a.X, a.Y - 1f * p, b.X - a.X, 1f * p), dark);
            }
        }
        void Side(float x)
        {
            // 左右兩側：只看得到牆頂（寬 3 個圖像素的一條），城門處斷開。
            for (var y = wall.Position.Y + 1; y < wall.End.Y - 1; y++)
            {
                if (gates.Contains(new Vector2I((int)x, y)))
                    continue;
                var a = screen(x + 0.5f, y);
                var w = 3f * p;
                DrawRect(new Rect2(a.X - w / 2f, a.Y, w, TerrainSurface.TempTile * radius / TerrainSurface.TempTexelsPerRadius), dark);
                DrawRect(new Rect2(a.X - w / 2f + p * 0.5f, a.Y, w - p, TerrainSurface.TempTile * radius / TerrainSurface.TempTexelsPerRadius), top);
            }
        }
        void Segments(int y)
        {
            // 一整排正面牆，城門處斷開、門兩旁加門柱。
            var x0 = wall.Position.X;
            for (var x = wall.Position.X; x <= wall.End.X; x++)
            {
                if (x < wall.End.X && !gates.Contains(new Vector2I(x, y)))
                    continue;
                Front(y, x0, x);
                if (x < wall.End.X)
                {
                    Tower(screen(x, y + 1), 3f);
                    Tower(screen(x + 1, y + 1), 3f);
                }
                x0 = x + 1;
            }
        }
        void Tower(Vector2 bottom, float half)
        {
            var w = half * 2f * p;
            var h = faceHeight + 4f * p;
            DrawRect(new Rect2(bottom.X - w / 2f, bottom.Y - h, w, h), face.Darkened(0.08f));
            DrawRect(new Rect2(bottom.X - w / 2f, bottom.Y - h, w, 2f * p), top);
            DrawRect(new Rect2(bottom.X - w / 2f, bottom.Y - h, w, h), dark, false, Math.Max(1f, p * 0.6f));
        }
        var topRow = wall.Position.Y;
        var bottomRow = wall.End.Y - 1;
        items.Add((screen(0, topRow + 1).Y - 0.01f, () =>
        {
            Side(wall.Position.X);
            Side(wall.End.X - 1);
            Segments(topRow);
            if (stone)
            {
                Tower(screen(wall.Position.X + 0.5f, topRow + 1), 4f);
                Tower(screen(wall.End.X - 0.5f, topRow + 1), 4f);
            }
        }));
        items.Add((screen(0, bottomRow + 1).Y + 0.01f, () =>
        {
            Segments(bottomRow);
            if (stone)
            {
                Tower(screen(wall.Position.X + 0.5f, bottomRow + 1), 4f);
                Tower(screen(wall.End.X - 0.5f, bottomRow + 1), 4f);
            }
        }));
    }
}
