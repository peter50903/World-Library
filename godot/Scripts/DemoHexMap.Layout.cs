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
    public readonly record struct Lot(int X, int Y, int W, int H, Building Kind, bool Construction = false, int Facing = 0, int Variant = 1);


    /// <summary>一個聚落的版面。<see cref="Wall"/>＝城牆或木柵圍住的方格範圍（沒有＝null）。</summary>
    public sealed record SettlementPlan(int SettlementId, Rect2I Plaza, IReadOnlyList<Vector2I> Roads, IReadOnlyList<Lot> Lots, Rect2I? Wall, bool StoneWall,
        IReadOnlyList<Vector2I> Gates, Rect2I? InnerWall = null, IReadOnlyList<Vector2I>? InnerGates = null, int WallKind = -1);

    /// <summary>【TEMP】人口超過下一棟房子所需的這個比例時，多一塊建地。</summary>
    private const float TempConstructionAt = 0.5f;

    /// <summary>第 08-18b 章：住屋依等級升級（帳篷→木屋→房子→石屋）；同一聚落裡靠中心的舊屋先升一級。</summary>
    private static Building HouseFor(DemoSettlementTier tier, int index, int count) => tier switch
    {
        DemoSettlementTier.Camp => Building.Tent,
        DemoSettlementTier.Village => index < Math.Max(1, count / 5) ? Building.House : Building.Hut,
        DemoSettlementTier.Town => index < count * 3 / 10 ? Building.StoneHouse : index >= count * 8 / 10 ? Building.Hut : Building.House,
        // 第 09-1c 章：城市以上內圈是連棟街屋，往外石屋、房子。
        DemoSettlementTier.City => index < count * 4 / 10 ? Building.RowHouse : index >= count * 8 / 10 ? Building.House : Building.StoneHouse,
        _ => index < count * 5 / 10 ? Building.RowHouse : index < count * 8 / 10 ? Building.BrickHouse : Building.StoneHouse // 第 08-20e 章：首都以磚屋為主
    };

    private static bool IsResidence(Building kind) => kind is Building.Tent or Building.Hut or Building.House or Building.StoneHouse or Building.BrickHouse or Building.Farmhouse or Building.RowHouse;

    /// <summary>住屋數與是否有建地（人口快到下一棟時）。</summary>
    private static (int Houses, bool Building) HouseCount(long people, DemoSettlementTier tier)
    {
        if (tier == DemoSettlementTier.Camp)
            return ((int)Math.Clamp(people / TempPeoplePerTent, 1, 4), false);
        var per = PeoplePerHouse(tier);
        var max = MaxHouses(tier);
        var houses = (int)Math.Clamp(people / per, 1, max);
        var growing = houses < max && people % per >= per * TempConstructionAt;
        return (houses, growing);
    }

    /// <summary>【TEMP】第 08-18k 章：每棟住屋住幾人（等級越高越密）、住屋上限。</summary>
    private static int PeoplePerHouse(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => TempPeoplePerHouse,
        DemoSettlementTier.Town => 10,
        DemoSettlementTier.City => 8,
        _ => 7
    };

    private const int TempMaxHouses = 220;

    /// <summary>
    /// 【TEMP】第 09-1c 章（參考 CK3：據點不照比例，依等級換大小）：畫面上的住屋數上限——村莊 14、城鎮 28、城市 44、首都 60。
    /// 人口再多也只畫這麼多（象徵性），聚落不會攤成好幾格寬。
    /// </summary>
    private static int MaxHouses(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => 14,
        DemoSettlementTier.Town => 28,
        DemoSettlementTier.City => 44,
        _ => Math.Min(60, TempMaxHouses)
    };

    /// <summary>
    /// 第 08-18k 章（試玩回饋 2026-11-11 之 2：農田一直閃爍）：版面用的人口有黏性——人口變動不到一成（至少兩棟房子的人）就沿用上次的版面，
    /// 不會因為每週幾個人的出生死亡就重排道路、房子與屋地。
    /// </summary>
    private long LayoutPeople(int id, long people, DemoSettlementTier tier)
    {
        if (_layoutPeople.TryGetValue(id, out var last) && last.Tier == tier
            && Math.Abs(people - last.People) < Math.Max(PeoplePerHouse(tier) * 2L, last.People / 10))
            return last.People;
        _layoutPeople[id] = (people, tier);
        return people;
    }

    private readonly Dictionary<int, (long People, DemoSettlementTier Tier)> _layoutPeople = new();

    /// <summary>
    /// 第 08-20e 章：建築佔的方格＝第九批素材的寬高÷16（整張就是它的範圍；門在底部正中）。
    /// <paramref name="variant"/>＝款式（石屋第 2 款三層、磚屋第 2 款連棟比較大）。
    /// </summary>
    private static (int W, int H) LotSize(Building kind, int variant = 1) => kind switch
    {
        Building.Tent or Building.Well => (2, 2),
        Building.Hut or Building.Kiln or Building.Granary or Building.HuntingLodge or Building.FishingHut or Building.Dock => (3, 3),
        Building.House => (3, 4),
        Building.StoneHouse => variant == 2 ? (3, 5) : (3, 4),
        Building.BrickHouse => variant == 2 ? (4, 4) : (3, 4),
        Building.HallVillage or Building.School or Building.Scriptorium => (4, 4),
        Building.HallTown => (5, 5),
        Building.Market => (5, 3),
        Building.Temple => (5, 6),
        Building.Smithy or Building.Workshop => (4, 3),
        Building.Shrine => (2, 3),
        Building.Keep => (6, 7),
        Building.Watchtower => (2, 4),
        Building.Windmill => (3, 5),
        Building.Watermill => (5, 4),
        // 第 09-1b 章（第十一批素材寬高÷16）
        Building.Farmhouse => (4, 4),
        Building.Inn => (5, 5),
        Building.Warehouse => (5, 4),
        Building.Stable => (5, 3),
        Building.BellTower => (2, 6),
        Building.Barracks => (7, 5),
        Building.Archery => (6, 4),
        Building.Statue => (2, 3),
        Building.RowHouse => (3, 5),
        _ => (3, 3)
    };

    private readonly Dictionary<int, SettlementPlan> _layouts = new();
    private readonly Dictionary<int, long> _layoutSignatures = new();
    private Dictionary<int, byte> _ground = new();
    private (object?, object?, object?, object?, object?, object?, object?, object?, int) _layoutKey;

    /// <summary>
    /// 全部聚落的版面；同時產生地表要畫的聚落地面。每個聚落只在自己的版面條件（等級、屋數、佔格、佔格上的田、信仰、井）改變時重算，
    /// 快轉時不必每次都重排全部聚落。
    /// </summary>
    private void EnsureLayouts()
    {
        if (World is null || _surface is null)
            return;
        // 第 08-20g 章：每週推進工地（CurrentWeek 在鍵裡）。
        var key = ((object?)World, (object?)Sites, (object?)FadedRuins, (object?)SettlementScale, (object?)Lineage, (object?)Land, (object?)FaithOf, (object?)Hunters, CurrentWeek);
        if (key.Equals(_layoutKey))
            return;
        if (!ReferenceEquals(key.Item1, _layoutKey.Item1))
        {
            _layouts.Clear();
            _layoutSignatures.Clear();
            _layoutPeople.Clear();
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
        // 編號小（早建立）的聚落先排；後面的不壓到前面已經用掉的方格。
        var occupied = new HashSet<Vector2I>();
        long prefix = 17;
        foreach (var site in alive)
        {
            var layout = ComputeLayout(site, occupied, prefix);
            unchecked
            {
                prefix = prefix * 31 + Signature(layout); // 第 09-3 章：後面聚落的快取鍵含前面聚落的用地
            }
            foreach (var lot in layout.Lots)
                for (var j = 0; j < lot.H; j++)
                    for (var i = 0; i < lot.W; i++)
                        occupied.Add(new Vector2I(lot.X + i, lot.Y + j));
            var signature = Signature(layout);
            unchecked
            {
                // 第 08-6b 章：相鄰格的田與開墾進度（田地環的塊數與作物）。
                foreach (var n in World.Neighbors(World.GetCell(site.Column, site.Row)))
                    if (Land.ParcelAt(new DemoCellRef(n.Column, n.Row)) is { } parcel)
                        signature = signature * 37 + (int)parcel.State * 7 + parcel.DevelopmentPercent / 30;
                signature = signature * 5 + (int)(SettlementScale.TryGetValue(site.Id, out var sc) ? sc.Tier : DemoSettlementTier.Camp);
            }
            if (_layoutSignatures.TryGetValue(site.Id, out var old) && old == signature)
                continue;
            _layoutSignatures[site.Id] = signature;
            _layouts[site.Id] = layout;
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
                // 第 08-20f 章：聚落裡的路、廣場、整地蓋過田（田在牆外）；水上不鋪。
                if (x >= 0 && y >= 0 && x < tx && _surface.TileOwner(x, y) is var o and >= 0 && IsLand(o))
                    ground[y * tx + x] = kind;
            }
            // 第 08-20f 章（使用者 2026-11-15：牆內不應該還有原始地塊）：牆內的陸地全部整成聚落地面（城市以上鋪石子地、其餘夯土）。
            if (layout.Wall is { } inner)
                for (var y = inner.Position.Y; y < inner.End.Y; y++)
                    for (var x = inner.Position.X; x < inner.End.X; x++)
                        if (tier >= DemoSettlementTier.Town || !layout.Plaza.HasPoint(new Vector2I(x, y))) // 第 08-20g 章：村莊的廣場留成綠地
                            Mark(x, y, tier >= DemoSettlementTier.City ? TerrainSurface.GroundCobble : TerrainSurface.GroundPlazaDirt);
            // 第 08-20e 章：牆內道路與廣場——營地、村莊泥土路，城鎮石頭路，城市以上磚地（第九批路面）。
            var street = tier switch
            {
                DemoSettlementTier.Camp or DemoSettlementTier.Village => TerrainSurface.GroundRoad,
                DemoSettlementTier.Town => TerrainSurface.GroundStreet,
                _ => TerrainSurface.GroundBrick
            };
            // 第 08-20g 章：住屋的後院菜園。
            foreach (var lot in layout.Lots)
                foreach (var (tile, kind) in Garden(lot))
                    if (!layout.Plaza.HasPoint(tile)) // 廣場南邊那排房子的後院不伸進廣場
                        Mark(tile.X, tile.Y, kind);
            foreach (var road in layout.Roads)
                Mark(road.X, road.Y, street);
            // 村莊的廣場是綠地（夯土上留草：不鋪），城鎮以上鋪成廣場。
            if (tier >= DemoSettlementTier.Town)
                for (var y = layout.Plaza.Position.Y; y < layout.Plaza.End.Y; y++)
                    for (var x = layout.Plaza.Position.X; x < layout.Plaza.End.X; x++)
                        Mark(x, y, tier >= DemoSettlementTier.City ? TerrainSurface.GroundPlazaStone : street);
            // 建築的範圍鋪院子地；碼頭、水磨坊在水上的部分不鋪。
            foreach (var lot in layout.Lots)
                for (var y = lot.Y; y < lot.Y + lot.H; y++)
                    for (var x = lot.X; x < lot.X + lot.W; x++)
                        Mark(x, y, TerrainSurface.GroundYard);
        }
        // 第 08-6b 章：田畫在城牆外一圈（建築、道路優先，所以最後才鋪、不蓋過別的聚落）。
        foreach (var layout in _layouts.Values.OrderBy(l => l.SettlementId))
            AddFieldRing(ground, layout, tx);
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

    /// <summary>版面的指紋（建築、工地階段、街、牆都一樣就不重鋪地面）。</summary>
    private static long Signature(SettlementPlan layout)
    {
        unchecked
        {
            long h = layout.WallKind * 7 + 1;
            foreach (var lot in layout.Lots)
                h = h * 31 + lot.X * 65_537 + lot.Y * 257 + (int)lot.Kind * 13 + lot.Variant * 3 + (lot.Construction ? 1 : 0);
            foreach (var r in layout.Roads.OrderBy(r => r.Y).ThenBy(r => r.X))
                h = h * 17 + r.X * 4099 + r.Y;
            if (layout.Wall is { } w)
                h = h * 13 + w.Position.X * 7 + w.Position.Y * 11 + w.Size.X * 3 + w.Size.Y;
            return h;
        }
    }

    /// <summary>【TEMP】第 08-6b 章：城牆外田地環的寬度（方格）。</summary>
    public const int TempFieldRingWidth = 3;

    /// <summary>【TEMP】第 08-6b 章：田地環畫滿時的田數（＝田數上限的最大值，見 <c>DemoLandClearing.FieldCap</c>）。</summary>
    public const int TempFieldRingFull = 6;

    /// <summary>
    /// 第 08-6b 章（使用者 2026-11-15：一個聚集地只有城牆外圍一圈可以建造農田）：
    /// 聚落格相鄰 6 格裡的農田、開墾地，畫成城牆外寬 <see cref="TempFieldRingWidth"/> 格的一圈田；
    /// 從南面正中開始、繞著牆左右兩邊往北長，一塊田佔一圈的 1/<see cref="TempFieldRingFull"/>。
    /// 成熟的田依雜湊是麥、菜、亞麻、青麥；開墾中的田依進度是翻土、播種、綠苗。水上與別的聚落地面不鋪。
    /// </summary>
    private void AddFieldRing(Dictionary<int, byte> ground, SettlementPlan layout, int tx)
    {
        if (layout.Wall is not { } wall || Sites?.Settlements.FirstOrDefault(s => s.Id == layout.SettlementId) is not { } site)
            return;
        var fields = World!.Neighbors(World.GetCell(site.Column, site.Row))
            .Select(c => new DemoCellRef(c.Column, c.Row))
            .Select(c => (Cell: c, State: Land.StateOf(c)))
            .Where(c => c.State is DemoLandDevelopment.Farmland or DemoLandDevelopment.Clearing)
            .OrderByDescending(c => c.State == DemoLandDevelopment.Farmland).ThenBy(c => c.Cell.Row).ThenBy(c => c.Cell.Column)
            .ToList();
        if (fields.Count == 0)
            return;
        var outer = wall.Grow(TempFieldRingWidth);
        var center = new Vector2(wall.Position.X + wall.Size.X / 2f, wall.Position.Y + wall.Size.Y / 2f);
        var ring = new List<(int X, int Y, float Angle)>();
        for (var y = outer.Position.Y; y < outer.End.Y; y++)
            for (var x = outer.Position.X; x < outer.End.X; x++)
            {
                if (wall.HasPoint(new Vector2I(x, y)) || x < 0 || y < 0 || x >= tx || y >= _surface!.TilesY)
                    continue;
                // 從正南（角度 0）往兩側長：左右對稱，角度取絕對值。
                var angle = MathF.Abs(MathF.Atan2(x + 0.5f - center.X, y + 0.5f - center.Y));
                ring.Add((x, y, angle));
            }
        ring.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        var per = Math.Max(1, ring.Count / TempFieldRingFull);
        for (var i = 0; i < ring.Count && i / per < fields.Count; i++)
        {
            var (x, y, _) = ring[i];
            if (_surface!.TileOwner(x, y) is not (var o and >= 0) || !IsLand(o))
                continue;
            var (cell, state) = fields[i / per];
            var h = LayoutHash(site.Id, cell.Row * 977 + cell.Column, 211);
            var progress = Land.ParcelAt(cell)?.DevelopmentPercent ?? 0;
            var crop = state == DemoLandDevelopment.Clearing
                ? progress < 50 ? TerrainSurface.GroundFieldPlowed : progress < 80 ? TerrainSurface.GroundFieldSown : TerrainSurface.GroundFieldSprout
                : h < 0.55f ? TerrainSurface.GroundFieldWheat : h < 0.75f ? TerrainSurface.GroundFieldVegetable : h < 0.88f ? TerrainSurface.GroundFieldFlax : TerrainSurface.GroundFieldGreen;
            ground.TryAdd(y * tx + x, crop);
        }
    }

    private bool IsField(int cell)
        => Land.StateOf(new DemoCellRef(cell % World!.Columns, cell / World.Columns)) is DemoLandDevelopment.Farmland or DemoLandDevelopment.Clearing;

    private bool IsLand(int cell) => World!.Cells[cell].Terrain != DemoTerrain.Water;

    /// <summary>【TEMP】第 08-20e 章：圍牆依等級（0＝圍籬、1＝木柵、2＝石牆、3＝磚牆）。</summary>
    private static int WallKindFor(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 0,
        DemoSettlementTier.Village => 1,
        DemoSettlementTier.Town => 2,
        _ => 3
    };

    private static readonly string[] WallNames = { "fence", "palisade", "stone", "brick" };

    /// <summary>下牆的正面與城門往上長幾列（佔掉牆內最南幾列）：圍籬 0、木柵 1、石牆與磚牆 2（城門三格高）。</summary>
    private static int WallBottomPad(int wallKind) => wallKind switch { 0 => 0, 1 => 1, _ => 2 };

    /// <summary>城門寬（方格）：圍籬與木柵 2、石牆與磚牆 3。</summary>
    private static int GateWidth(int wallKind) => wallKind >= 2 ? 3 : 2;

    /// <summary>第 08-20e 章：住屋的款式（第九批：帳篷 2、木屋 2、城鎮住屋 3、石屋 3、磚屋 2 款）。</summary>
    private static int Variant(Building kind, int siteId, int index)
    {
        var count = kind switch
        {
            Building.Tent or Building.BrickHouse or Building.Farmhouse or Building.RowHouse => 2,
            Building.Hut or Building.House or Building.StoneHouse => 3, // 第 09-1a 章：第十一批木屋 3 款
            _ => 1
        };
        return Math.Clamp((int)(LayoutHash(siteId, index, (int)kind + 140) * count), 0, count - 1) + 1;
    }

    /// <summary>附近有田（水磨坊只蓋在有田的臨河聚落）。</summary>
    private bool FieldsNear(DemoSettlement site)
    {
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
                if (World!.Contains(site.Column + dc, site.Row + dr) && Land.StateOf(new DemoCellRef(site.Column + dc, site.Row + dr)) == DemoLandDevelopment.Farmland)
                    return true;
        return false;
    }

    /// <summary>
    /// 碼頭（3×3，最上一列在岸上、下兩列在水裡）、水磨坊（5×4，左三欄在岸上、右兩欄在水上）：
    /// 在聚落格與相鄰格裡找最近、沒被用掉的位置；找不到就不放。
    /// </summary>
    private void PlaceWaterfront(DemoSettlement site, Vector2I center, List<Lot> lots, HashSet<int> used, int tx, Building kind)
    {
        var surface = _surface!;
        var (w, h) = SpriteSize(new Lot(0, 0, 0, 0, kind)) ?? LotSize(kind, 1);
        bool OnLand(int x, int y) => x >= 0 && y >= 0 && x < tx && y < surface.TilesY && surface.TileOwner(x, y) is var o and >= 0 && IsLand(o) && !IsField(o);
        bool OnWater(int x, int y) => x >= 0 && y >= 0 && x < tx && y < surface.TilesY && surface.TileOwner(x, y) is var o and >= 0 && !IsLand(o);
        bool Near(int x, int y) => surface.TileOwner(x, y) is var o and >= 0
            && Math.Max(Math.Abs(o % World!.Columns - site.Column), Math.Abs(o / World.Columns - site.Row)) <= 1;
        (int X, int Y)? best = null;
        var bestScore = float.MaxValue;
        var reach = TerrainSurface.TilesPerCell * 2;
        for (var y = center.Y - reach; y <= center.Y + reach; y++)
            for (var x = center.X - reach; x <= center.X + reach; x++)
            {
                var ok = Near(x, y);
                for (var j = 0; j < h && ok; j++)
                    for (var i = 0; i < w && ok; i++)
                    {
                        var wantLand = kind == Building.Dock ? j == 0 : i < 3;
                        ok = !used.Contains((y + j) * tx + x + i) && (wantLand ? OnLand(x + i, y + j) : OnWater(x + i, y + j));
                    }
                if (!ok)
                    continue;
                var score = Math.Abs(x - center.X) + Math.Abs(y - center.Y) + LayoutHash(site.Id, x, y + (int)kind);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                }
            }
        if (best is not { } spot)
            return;
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
                used.Add((spot.Y + j) * tx + spot.X + i);
        lots.Add(new Lot(spot.X, spot.Y, w, h, kind));
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
            if (!OnScreen(start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius), radius * 2f * (TempPlanRadius / (float)TerrainSurface.TilesPerCell + 1f))) // 第 08-20g 章：規劃半徑
                continue;
            if (layout.Wall is { } wall)
                AddFrontWall(items, layout, wall, Screen, radius); // 第 08-20e 章：第九批圍牆
            var roof = CultureOf.TryGetValue(site.Id, out var culture) ? CultureColor(culture) : new Color(0.62f, 0.25f, 0.18f);
            // 第 09-1b 章：街過水的橋、住屋院子的小物件與農舍果樹。
            var (_, lotTier) = SettlementScale.TryGetValue(site.Id, out var lotScale) ? lotScale : (0L, DemoSettlementTier.Camp);
            AddBridges(items, layout, Screen, radius);
            AddYardProps(items, layout, lotTier, Screen, radius);
            AddRowRuns(items, layout, Screen, radius, roof); // 第 09-1c 章：連棟街屋接成整排
            foreach (var lot in layout.Lots)
            {
                if (lot.Kind == Building.RowHouse && !lot.Construction)
                    continue;
                var bottom = Screen(lot.X + lot.W / 2f, lot.Y + lot.H);
                var placed = lot;
                // 第 08-20e 章：第九批正面建築（第 09-1a 章：舊的第四～八批與程式畫的建築已移除）。
                items.Add((bottom.Y, () => DrawFrontLot(placed, bottom, radius, roof)));
            }
            // 第 08-18b 章：中心是篝火加囤糧圍欄（營地起就有）。
            // 第 08-18d 章：廣場三方格以上時篝火往上移、素材的囤糧圍欄（三方格寬）放在廣場下緣；小廣場照舊畫小圍欄。
            var plaza = layout.Plaza;
            var big = plaza.Size.X >= 3;
            var (_, siteTier) = SettlementScale.TryGetValue(site.Id, out var siteScale) ? siteScale : (0L, DemoSettlementTier.Camp);
            // 第 08-20e 章：篝火只在營地與村莊的廣場；囤糧圍欄只在營地（城鎮以上的廣場是空的石頭地）。
            if (siteTier <= DemoSettlementTier.Village || layout.WallKind < 0)
            {
                var fire = Screen(plaza.Position.X + plaza.Size.X / 2f, plaza.Position.Y + (big ? 0.9f : plaza.Size.Y / 2f));
                items.Add((fire.Y, () => DrawCampfire(fire, radius)));
            }
            if (siteTier == DemoSettlementTier.Camp || layout.WallKind < 0)
            {
                var store = big ? Screen(plaza.Position.X + plaza.Size.X / 2f, plaza.End.Y) : Screen(plaza.Position.X + 0.5f, plaza.Position.Y + 1f);
                items.Add((store.Y, () => DrawStockpile(store, radius, big)));
            }
            if (ShowDisasterMarks && RecentDisasters.Contains(site.Id)) // 第 12-1 章 C3：一年內遭天災
            {
                var mark = Screen(layout.Plaza.End.X + 0.5f, layout.Plaza.Position.Y - 0.5f);
                items.Add((float.MaxValue, () =>
                {
                    if (!DrawSymbol("b11_sym_disaster", mark - new Vector2(0f, radius * 0.25f), Math.Max(radius * 0.5f, 18f), Colors.White) // 第 09-1b 章
                        && !DrawPixelPart("px_icon_disaster", mark, radius))
                        DrawMarker(ArtAssets.IconDisaster, mark, radius * 0.7f);
                }));
            }
        }
        foreach (var (_, draw) in items.OrderBy(i => i.Y))
            draw();
        DrawCreatures(start, radius); // 地下城入口
        DrawNameplates(start, radius, realmsOnly: true); // 第 08-18k 章（試玩回饋 2026-11-11 之 3）：勢力合併後只標首都
    }

    private void DrawCampfire(Vector2 at, float radius)
    {
        MarkAnimated(at, radius); // 第 09-1e 章：篝火在畫面內時才持續重畫
        if (PixelParts.Texture("bld_bonfire") is { } bonfire)
        {
            // 第 08-18d 章：篝火素材（4 格動畫），以中心為準往下半張。
            var size = PixelParts.ScreenSize(bonfire, radius, 4);
            DrawArt(bonfire, at + new Vector2(0f, size.Y * 0.5f), radius, 4, (int)(Time.GetTicksMsec() / 150) % 4);
            return;
        }
        var p = radius / TerrainSurface.TempArtTexelsPerRadius;
        DrawCircle(at, 4f * p, new Color(0.30f, 0.26f, 0.22f));
        var flicker = 0.8f + 0.2f * Mathf.Sin(Time.GetTicksMsec() / 120f);
        DrawCircle(at - new Vector2(0, 1.5f * p), 2.6f * p * flicker, new Color(0.98f, 0.55f, 0.15f));
        DrawCircle(at - new Vector2(0, 2.2f * p), 1.3f * p * flicker, new Color(1f, 0.85f, 0.35f));
    }
}
