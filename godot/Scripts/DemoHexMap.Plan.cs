using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20g 章（使用者 2026-11-15：聚集地的建築、道路規劃要先研究怎樣看起來像城市、村子；都市不應該瞬間建好）：
/// 每個聚落一份固定的「總體規劃」（建立後不再變），依人口與等級一棟一棟動工、蓋好。
/// <list type="bullet">
/// <item><b>街與街區</b>：東西向的街每隔 <see cref="TempStreetSpacing"/> 列一條；每個街區由南往北是「房子（門朝南、開在街上）＋後院菜園」，
/// 菜園的北緣就是上一條街——中世紀城鎮「臨街窄面、後院深長」的地塊（burgage plot）在正面視角的畫法。</item>
/// <item><b>中心廣場</b>：主街上的一塊空地（村莊是綠地與水井、城鎮以上是市集）；北面是會所，兩旁是信仰建築與學校。</item>
/// <item><b>南北巷</b>：廣場西側是主巷（南北城門），往外每隔十多格一條巷子，把街連起來。</item>
/// <item><b>先後</b>：主街先蓋滿（村莊像沿路長的街村），再往南北兩條街擴張；越外圍的房子間距越不規則。</item>
/// <item><b>公共建築</b>：放在規劃裡固定的位置（第幾塊臨街地）；還沒到該等級時那塊地先蓋住屋，升級後拆掉改建。</item>
/// </list>
/// 只改畫面；同一世界每次相同。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>
    /// 【TEMP】第 08-20g 章：街距、房子深度、規劃半徑、廣場寬與深、別的聚落中心保留半徑（方格）。
    /// 第 09-1c 章（使用者 2026-11-16：城市規劃太鬆散、佔太多空間）：街距 13→7（街 1＋房子 5＋後院 1）、房深 6→5、規劃半徑 44→26、保留半徑 12→10。
    /// </summary>
    public const int TempStreetSpacing = 7, TempHouseDepth = 5, TempPlanRadius = 26, TempSquareWidth = 14, TempSquareDepth = 5, TempCenterClear = 10;

    /// <summary>【TEMP】第 08-20g 章：圍牆往外多留幾格（牆不必每蓋一棟就往外移）。</summary>
    public const int TempWallSlack = 2; // 第 09-1c 章：4→2

    /// <summary>規劃裡一塊地的用途。</summary>
    private enum SlotRole
    {
        House, Hall, Faith, School, Market, Market2, Well, Granary, Granary2, Smithy, Workshop, Kiln, Windmill, Windmill2,
        Scriptorium, Keep, HuntingLodge, FishingHut, Dock, Watermill,
        // 第 09-1b 章
        Inn, Warehouse, Stable, BellTower, Barracks, Archery, Fountain
    }

    /// <summary>一塊地（方格；左上角與寬高，門在下緣正中、門下一格是街）。<see cref="Street"/>＝臨哪一條街（廣場、水邊為 int.MinValue）。</summary>
    private sealed record PlanSlot(int X, int Y, int W, int H, SlotRole Role, int Street);

    /// <summary>一個聚落的總體規劃。</summary>
    private sealed record MasterPlan(int SiteId, Vector2I Center, Rect2I Square, int MainLane, IReadOnlyList<int> Lanes, IReadOnlyList<PlanSlot> Slots);

    /// <summary>蓋到哪裡：蓋好的（地塊 → 建築、款式）、施工中的（→ 建築、款式、開工週）、圍牆範圍。</summary>
    private sealed class BuildState
    {
        public readonly Dictionary<int, (Building Kind, int Variant)> Built = new();
        public readonly Dictionary<int, (Building Kind, int Variant, int Start)> Work = new();
        public Rect2I? Wall;
        public int Week;
        public bool Settled; // 第 09-3 章：該蓋的都蓋好了（沒有工地、沒有缺的）
    }

    private readonly Dictionary<int, MasterPlan> _plans = new();
    private readonly Dictionary<int, BuildState> _builds = new();
    private object? _plansWorld;

    /// <summary>第 08-20g 章：公共建築在臨街地的第幾塊（依由近到遠的順序；其餘是住屋）。</summary>
    private static readonly Dictionary<int, SlotRole> RoleAt = new()
    {
        [2] = SlotRole.Granary, [4] = SlotRole.HuntingLodge, [5] = SlotRole.FishingHut, [7] = SlotRole.Smithy, [9] = SlotRole.Workshop,
        [16] = SlotRole.Windmill, [20] = SlotRole.Kiln, [24] = SlotRole.Granary2, [28] = SlotRole.Scriptorium, [34] = SlotRole.Keep,
        [40] = SlotRole.Market2, [44] = SlotRole.Windmill2,
        // 第 09-1b 章：旅店靠近廣場；鐘樓、倉庫、馬廄在中圈；兵營、靶場在外圈（靠城門）。
        [3] = SlotRole.Inn, [10] = SlotRole.BellTower, [12] = SlotRole.Warehouse, [14] = SlotRole.Stable, [18] = SlotRole.Barracks, [22] = SlotRole.Archery
    };

    /// <summary>【TEMP】每種用途預留的地（寬、深）：取可能蓋在上面的建築裡最大的。</summary>
    private static (int W, int H) SlotSize(SlotRole role) => role switch
    {
        // 第 09-1a 章：改成第十一批的大小（街距 13＝街 1＋地 12，最深的主堡 12 格放得下）。
        SlotRole.Hall => (7, 7),
        SlotRole.Faith => (8, 10),
        SlotRole.School => (6, 6),
        SlotRole.Scriptorium => (6, 5),
        SlotRole.Smithy or SlotRole.Workshop => (5, 4),
        SlotRole.Keep => (12, 12),
        SlotRole.Windmill or SlotRole.Windmill2 => (4, 7),
        SlotRole.Market or SlotRole.Market2 => (6, 3),
        SlotRole.Well => (2, 2),
        SlotRole.HuntingLodge => (4, 3),
        // 第 09-1b 章（素材寬高÷16）
        SlotRole.Inn => (5, 5),
        SlotRole.Warehouse => (5, 4),
        SlotRole.Stable => (5, 3),
        SlotRole.BellTower => (2, 6),
        SlotRole.Barracks => (7, 5),
        SlotRole.Archery => (6, 4),
        SlotRole.Fountain => (3, 3),
        _ => (4, TempHouseDepth)
    };

    private const int TempPlanItems = 140;

    /// <summary>【TEMP】臨街地往外找位置時最多跳過幾格（巷子、水、別人的地）。</summary>
    private const int TempMaxSkip = 8;

    /// <summary>
    /// 第 08-20g 章：算總體規劃。只用規劃半徑內的陸地，避開編號比較小（比較早建立）的聚落中心；
    /// 臨街地依「離中心多遠」輪流從各條街的東西兩端往外排。
    /// </summary>
    private MasterPlan BuildMasterPlan(DemoSettlement site)
    {
        var surface = _surface!;
        var tx = surface.TilesX;
        var ty = surface.TilesY;
        var c = TerrainSurface.TileOfCell(site.Column, site.Row);
        var older = Sites!.Settlements.Where(o => o.Id < site.Id).Select(o => TerrainSurface.TileOfCell(o.Column, o.Row))
            .Where(o => Math.Max(Math.Abs(o.X - c.X), Math.Abs(o.Y - c.Y)) <= TempPlanRadius + TempCenterClear).ToList();
        var square = new Rect2I(c.X - TempSquareWidth / 2, c.Y, TempSquareWidth, TempSquareDepth);
        var mainLane = square.Position.X - 1;
        var lanes = new List<int> { mainLane, square.End.X };
        for (int x = mainLane, k = 0; x > c.X - TempPlanRadius; k++)
            lanes.Add(x -= 13 + (int)(LayoutHash(site.Id, k, 301) * 6));
        for (int x = square.End.X, k = 0; x < c.X + TempPlanRadius; k++)
            lanes.Add(x += 13 + (int)(LayoutHash(site.Id, k, 302) * 6));
        var laneSet = lanes.ToHashSet();
        var taken = new HashSet<Vector2I>();

        bool Ok(int x, int y)
        {
            if (x < 0 || y < 0 || x >= tx || y >= ty || Math.Max(Math.Abs(x - c.X), Math.Abs(y - c.Y)) > TempPlanRadius)
                return false;
            if (surface.TileOwner(x, y) is not (var o and >= 0) || !IsLand(o))
                return false;
            foreach (var other in older)
                if (Math.Max(Math.Abs(x - other.X), Math.Abs(y - other.Y)) <= TempCenterClear)
                    return false;
            return true;
        }
        bool Fits(int x, int y, int w, int h, int street)
        {
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                {
                    var t = new Vector2I(x + i, y + j);
                    if (!Ok(t.X, t.Y) || laneSet.Contains(t.X) || square.HasPoint(t) || taken.Contains(t) || t.Y == c.Y)
                        return false; // 第 09-1c 章：比街距高的建築可以跨過後面的街，但不能壓到主街
                }
            return Ok(x + w / 2, street); // 門下一格（街）也要是陸地
        }
        void Take(PlanSlot slot)
        {
            for (var j = 0; j < slot.H; j++)
                for (var i = 0; i < slot.W; i++)
                    taken.Add(new Vector2I(slot.X + i, slot.Y + j));
        }

        var slots = new List<PlanSlot>();
        void Add(PlanSlot slot)
        {
            Take(slot);
            slots.Add(slot);
        }
        // 廣場上的市集與水井（不臨街）。
        var (mw, mh) = SlotSize(SlotRole.Market);
        Add(new PlanSlot(c.X - mw / 2, square.End.Y - 1 - mh, mw, mh, SlotRole.Market, int.MinValue));
        Add(new PlanSlot(square.Position.X + 1, square.End.Y - 3, 2, 2, SlotRole.Well, int.MinValue));
        // 第 09-1b 章：廣場右邊的噴泉（城市以上）；首都的井位改立雕像（見 Desired）。
        var (fw, fh) = SlotSize(SlotRole.Fountain);
        if (square.End.X - 1 - fw >= c.X + mw - mw / 2)
            Add(new PlanSlot(square.End.X - 1 - fw, square.End.Y - 1 - fh, fw, fh, SlotRole.Fountain, int.MinValue));
        // 會所在廣場北面正中（門開在主街＝廣場第一列）。
        var (hw, hh) = SlotSize(SlotRole.Hall);
        if (Fits(c.X - hw / 2, c.Y - hh, hw, hh, c.Y))
            Add(new PlanSlot(c.X - hw / 2, c.Y - hh, hw, hh, SlotRole.Hall, c.Y));

        // 各條街東西兩端的游標，依「離中心多遠」輪流往外放地。
        var streets = TempPlanRadius / TempStreetSpacing + 1;
        var cursors = new List<(int Street, int Dir, int X, bool First)>();
        for (var k = -streets; k <= streets; k++)
        {
            var y = c.Y + k * TempStreetSpacing;
            if (k == 0)
            {
                cursors.Add((y, -1, c.X - hw / 2 - 2, true));
                cursors.Add((y, 1, c.X - hw / 2 + hw + 1, true));
            }
            else
            {
                cursors.Add((y, -1, c.X - 1, false));
                cursors.Add((y, 1, c.X, false));
            }
        }
        var item = 0;
        while (item < TempPlanItems && cursors.Count > 0)
        {
            var best = 0;
            var bestScore = float.MaxValue;
            for (var i = 0; i < cursors.Count; i++)
            {
                var cur = cursors[i];
                var score = Math.Abs(cur.X - c.X) + Math.Abs(cur.Street - c.Y) * 1.1f + LayoutHash(site.Id, cur.Street, cur.Dir) * 2f; // 第 09-1c 章：1.5→1.1（街距變小，往南北長得一樣快，聚落接近方形）
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            var (street, dir, x0, first) = cursors[best];
            var role = first ? (dir < 0 ? SlotRole.Faith : SlotRole.School) : RoleAt.GetValueOrDefault(item, SlotRole.House);
            var (w, h) = SlotSize(role);
            var placed = false;
            // 往外找放得下的位置；隔著水或別人的地超過 TempMaxSkip 格就不再往外（不會冒出一棟孤零零的房子）。
            for (var x = x0; Math.Abs(x - c.X) <= TempPlanRadius && Math.Abs(x - x0) <= TempMaxSkip; x += dir)
            {
                var left = dir < 0 ? x - w + 1 : x;
                if (!Fits(left, street - h, w, h, street))
                    continue;
                Add(new PlanSlot(left, street - h, w, h, role, street));
                // 第 09-1c 章：臨街連排不留縫（中世紀城鎮的街屋）；外圍偶爾留一格小巷。
                var gap = Math.Abs(x - c.X) > 8 && LayoutHash(site.Id, left, street) < 0.2f ? 1 : 0;
                cursors[best] = (street, dir, dir < 0 ? left - 1 - gap : left + w + gap, false);
                placed = true;
                break;
            }
            if (!placed)
            {
                cursors.RemoveAt(best);
                continue;
            }
            if (!first)
                item++;
        }

        // 水邊：碼頭、水磨坊（規劃時就找好位置）。
        var used = taken.Select(t => t.Y * tx + t.X).ToHashSet();
        foreach (var (kind, role) in new[] { (Building.Dock, SlotRole.Dock), (Building.Watermill, SlotRole.Watermill) })
        {
            var found = new List<Lot>();
            PlaceWaterfront(site, c, found, used, tx, kind);
            if (found.Count > 0)
                slots.Add(new PlanSlot(found[0].X, found[0].Y, found[0].W, found[0].H, role, int.MinValue));
        }
        return new MasterPlan(site.Id, c, square, mainLane, lanes, slots);
    }

    /// <summary>【TEMP】第 08-20g 章：各種建築要蓋幾週。</summary>
    private static int BuildWeeks(Building kind) => kind switch
    {
        Building.Tent or Building.Well => 1,
        Building.Hut or Building.HuntingLodge or Building.FishingHut => 3,
        Building.House => 6,
        Building.StoneHouse or Building.Granary or Building.Kiln or Building.Dock => 10,
        Building.BrickHouse or Building.Smithy or Building.Workshop or Building.Windmill or Building.Watermill or Building.Market => 12,
        Building.HallVillage or Building.Shrine => 12,
        Building.HallTown or Building.School or Building.Scriptorium => 20,
        Building.Temple => 30,
        Building.Keep => 52,
        // 第 09-1b 章
        Building.Farmhouse => 6,
        Building.RowHouse => 10,
        Building.Fountain => 8,
        Building.Stable or Building.Statue => 10,
        Building.Warehouse or Building.Archery => 12,
        Building.Inn => 16,
        Building.BellTower => 20,
        Building.Barracks => 24,
        _ => 8
    };

    /// <summary>【TEMP】第 08-20g 章：同時有幾處工地（營地 2、村莊 2、城鎮 3、城市 4、首都 6）。</summary>
    private static int Crews(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Town => 3,
        DemoSettlementTier.City => 4,
        DemoSettlementTier.Capital => 6,
        _ => 2
    };

    /// <summary>【TEMP】週數一次跳超過這麼多（讀檔、換世界）就直接蓋好，不逐週模擬。</summary>
    private const int TempInstantJumpWeeks = 520;

    /// <summary>第 08-20g 章：依等級、人口，每塊地現在「應該」是什麼建築（null＝空地）。</summary>
    private Dictionary<int, (Building Kind, int Variant)> Desired(MasterPlan plan, DemoSettlement site, DemoSettlementTier tier, long people, Func<PlanSlot, bool> blocked)
    {
        var faith = FaithOf.ContainsKey(site.Id);
        var nearWater = World!.IsNearWater(new DemoCellRef(site.Column, site.Row));
        var village = tier >= DemoSettlementTier.Village;
        var town = tier >= DemoSettlementTier.Town;
        var city = tier >= DemoSettlementTier.City;
        var capital = tier == DemoSettlementTier.Capital;
        Building? Civic(SlotRole role) => role switch
        {
            SlotRole.Hall => !village ? null : town ? Building.HallTown : Building.HallVillage,
            SlotRole.Faith => city ? (faith ? Building.Temple : Building.Shrine) : village && faith ? Building.Shrine : null,
            SlotRole.School => city ? Building.School : null,
            SlotRole.Market => town ? Building.Market : null,
            SlotRole.Market2 => capital ? Building.Market : null,
            SlotRole.Well => capital ? Building.Statue : village && !nearWater ? Building.Well : null, // 第 09-1b 章：首都的井位立雕像
            SlotRole.Granary => town || (village && people >= 40) ? Building.Granary : null,
            SlotRole.Granary2 => city ? Building.Granary : null,
            SlotRole.Smithy => town ? Building.Smithy : null,
            SlotRole.Workshop => town ? Building.Workshop : null,
            SlotRole.Windmill => town ? Building.Windmill : null,
            SlotRole.Windmill2 => capital ? Building.Windmill : null,
            SlotRole.Kiln => city || (town && people >= 200) ? Building.Kiln : null,
            SlotRole.Scriptorium => capital ? Building.Scriptorium : null,
            SlotRole.Keep => capital ? Building.Keep : null,
            SlotRole.HuntingLodge => !town && village && Hunters.Contains(site.Id) ? Building.HuntingLodge : null,
            SlotRole.FishingHut => village && nearWater ? Building.FishingHut : null,
            SlotRole.Dock => village && nearWater ? Building.Dock : null,
            SlotRole.Watermill => village && nearWater && FieldsNear(site) ? Building.Watermill : null,
            // 第 09-1b 章
            SlotRole.Inn => town ? Building.Inn : null,
            SlotRole.Stable => town ? Building.Stable : null,
            SlotRole.Warehouse => city || (town && people >= 150) ? Building.Warehouse : null,
            SlotRole.BellTower => city ? Building.BellTower : null,
            SlotRole.Barracks => city ? Building.Barracks : null,
            SlotRole.Archery => capital ? Building.Archery : null,
            SlotRole.Fountain => city ? Building.Fountain : null,
            _ => null
        };
        var desired = new Dictionary<int, (Building, int)>();
        var (houses, _) = HouseCount(people, tier);
        var farms = tier == DemoSettlementTier.Village && FieldsNear(site);
        var assigned = 0;
        for (var i = 0; i < plan.Slots.Count; i++)
        {
            var slot = plan.Slots[i];
            if (blocked(slot))
                continue;
            if (Civic(slot.Role) is { } civic)
            {
                desired[i] = (civic, Variant(civic, site.Id, i));
                continue;
            }
            // 沒用到的公共建築地先蓋住屋（廣場上、水邊的地除外）。
            if (slot.Street == int.MinValue || assigned >= houses || slot.W < 2)
                continue;
            // 第 09-1c 章：村莊隔一塊蓋一棟（留菜園、空地，看得出是村子）；升成城鎮後空地補滿成連排街屋。
            if (tier <= DemoSettlementTier.Village && (slot.X / 4 + slot.Street) % 2 != 0)
                continue;
            var kind = HouseFor(tier, assigned, houses);
            // 第 09-1b 章：附近有田的村莊，外圍四成的木屋是農舍（後院種果樹，見 DemoHexMap.Extras）。
            if (kind == Building.Hut && tier == DemoSettlementTier.Village && assigned >= houses * 6 / 10 && farms)
                kind = Building.Farmhouse;
            var variant = Variant(kind, site.Id, i);
            if (kind == Building.BrickHouse && slot.W < 4)
                variant = 1; // 連棟的第 2 款要 4 格寬
            desired[i] = (kind, variant);
            assigned++;
        }
        return desired;
    }

    /// <summary>第 08-20g 章：往前推一週——工期到了的完工；有空的工班依規劃順序開工（新建、升級改建）。</summary>
    private static void Step(BuildState state, Dictionary<int, (Building Kind, int Variant)> desired, int crews)
    {
        state.Week++;
        foreach (var (slot, current) in state.Work.ToList())
        {
            var work = current;
            if (desired.TryGetValue(slot, out var want) && (want.Kind != work.Kind || want.Variant != work.Variant))
                state.Work[slot] = work = (want.Kind, want.Variant, work.Start); // 還在蓋時目標變了：照新的蓋
            if (state.Week - work.Start >= BuildWeeks(work.Kind))
            {
                state.Built[slot] = (work.Kind, work.Variant);
                state.Work.Remove(slot);
            }
        }
        foreach (var (slot, want) in desired.OrderBy(d => d.Key))
        {
            if (state.Work.Count >= crews)
                break;
            if (state.Work.ContainsKey(slot) || (state.Built.TryGetValue(slot, out var has) && has == want))
                continue;
            state.Work[slot] = (want.Kind, want.Variant, state.Week);
        }
    }

    /// <summary>
    /// 第 08-20g 章：一個聚落現在的樣子——蓋好的建築、工地、看得到的街巷、圍牆與城門。
    /// <paramref name="occupied"/>＝編號比較小的聚落已經用掉的方格（不重疊）。
    /// </summary>
    /// <summary>
    /// 第 09-3 章：每週版面的快取。輸入（前面聚落的用地、等級、人口、信仰、狩獵、別的聚落數）沒變、而且沒有工地時，
    /// 這一週什麼都不會動，直接沿用上次的版面（量測：每週版面原本約 33 毫秒）。
    /// </summary>
    private readonly Dictionary<int, (long Key, SettlementPlan Layout)> _settled = new();

    private SettlementPlan ComputeLayout(DemoSettlement site, HashSet<Vector2I> occupied, long prefix = 0)
    {
        var (_, tierNow) = SettlementScale.TryGetValue(site.Id, out var scaleNow) ? scaleNow : (0L, DemoSettlementTier.Camp);
        long key;
        unchecked
        {
            key = prefix * 1_000_003 + (int)tierNow * 7919 + LayoutPeople(site.Id, scaleNow.Item1, tierNow) * 31
                + (FaithOf.ContainsKey(site.Id) ? 1 : 0) + (Hunters.Contains(site.Id) ? 2 : 0) + Sites!.Settlements.Count * 131
                + (FieldsNear(site) ? 4 : 0);
        }
        if (ReferenceEquals(_plansWorld, World) && _settled.TryGetValue(site.Id, out var cached) && cached.Key == key
            && _builds.TryGetValue(site.Id, out var quiet) && quiet.Work.Count == 0 && CurrentWeek >= quiet.Week && CurrentWeek - quiet.Week <= TempInstantJumpWeeks)
        {
            quiet.Week = CurrentWeek;
            return cached.Layout;
        }
        var computed = ComputeLayoutFresh(site, occupied);
        // 只有蓋完（沒有工地、也沒有該蓋沒蓋的）才記下來沿用。
        if (_builds.TryGetValue(site.Id, out var after) && after.Work.Count == 0 && after.Settled)
            _settled[site.Id] = (key, computed);
        else
            _settled.Remove(site.Id);
        return computed;
    }

    private SettlementPlan ComputeLayoutFresh(DemoSettlement site, HashSet<Vector2I> occupied)
    {
        if (!ReferenceEquals(_plansWorld, World))
        {
            _plans.Clear();
            _builds.Clear();
            _settled.Clear();
            _plansWorld = World;
        }
        if (!_plans.TryGetValue(site.Id, out var plan))
            _plans[site.Id] = plan = BuildMasterPlan(site);
        var (rawPeople, tier) = SettlementScale.TryGetValue(site.Id, out var scale) ? scale : (0L, DemoSettlementTier.Camp);
        var people = LayoutPeople(site.Id, rawPeople, tier);
        var others = Sites!.Settlements.Where(o => o.Id != site.Id && !o.IsRuin).Select(o => TerrainSurface.TileOfCell(o.Column, o.Row)).ToList();
        bool Blocked(PlanSlot slot)
        {
            for (var j = 0; j < slot.H; j++)
                for (var i = 0; i < slot.W; i++)
                    if (occupied.Contains(new Vector2I(slot.X + i, slot.Y + j)))
                        return true;
            foreach (var o in others)
                if (Math.Abs(slot.X + slot.W / 2 - o.X) <= TempCenterClear && Math.Abs(slot.Y + slot.H / 2 - o.Y) <= TempCenterClear)
                    return true;
            return false;
        }
        var desired = Desired(plan, site, tier, people, Blocked);
        var crews = Crews(tier);
        if (!_builds.TryGetValue(site.Id, out var state) || CurrentWeek < state.Week || CurrentWeek - state.Week > TempInstantJumpWeeks)
        {
            // 第一次看到（讀檔、換世界、新聚落）：照現況直接蓋好。
            state = new BuildState { Week = CurrentWeek };
            foreach (var (slot, want) in desired)
                state.Built[slot] = want;
            _builds[site.Id] = state;
        }
        while (state.Week < CurrentWeek)
            Step(state, desired, crews);
        state.Settled = state.Work.Count == 0 && desired.All(d => state.Built.TryGetValue(d.Key, out var has) && has == d.Value);

        // ── 看得到的建築：蓋好的＋工地（改建中的舊屋已拆） ──
        var lots = new List<Lot>();
        foreach (var (index, built) in state.Built.OrderBy(b => b.Key))
            if (!state.Work.ContainsKey(index))
            {
                var slot = plan.Slots[index];
                lots.Add(LotIn(slot, built.Kind, false, built.Variant));
            }
        foreach (var (index, work) in state.Work.OrderBy(w => w.Key))
        {
            var slot = plan.Slots[index];
            var progress = (state.Week - work.Start) / (float)Math.Max(1, BuildWeeks(work.Kind));
            lots.Add(LotIn(slot, work.Kind, true, progress < 0.34f ? 1 : progress < 0.67f ? 2 : 3));
        }

        // ── 街：有房子臨街的那一段（左右多一格）；主街一定經過廣場 ──
        var roads = new HashSet<Vector2I>();
        var spans = new SortedDictionary<int, (int Min, int Max)>();
        void Span(int street, int x)
            => spans[street] = spans.TryGetValue(street, out var s) ? (Math.Min(s.Min, x), Math.Max(s.Max, x)) : (x, x);
        var slotOf = state.Built.Keys.Where(k => !state.Work.ContainsKey(k)).Concat(state.Work.Keys).Select(k => plan.Slots[k]);
        foreach (var slot in slotOf)
            if (slot is { Street: not int.MinValue })
            {
                Span(slot.Street, slot.X - 1);
                Span(slot.Street, slot.X + slot.W);
            }
        Span(plan.Center.Y, Heart(plan, tier).Position.X - 1);
        Span(plan.Center.Y, Heart(plan, tier).End.X);
        foreach (var (street, (min, max)) in spans)
            for (var x = min; x <= max; x++)
                roads.Add(new Vector2I(x, street));
        // 主巷：從最北到最南有房子的街；其他巷子：兩條相鄰的街都延伸到那裡才開。
        var rows = spans.Keys.ToList();
        for (var y = rows[0]; y <= rows[^1]; y++)
            roads.Add(new Vector2I(plan.MainLane, y));
        for (var i = 1; i < rows.Count; i++)
            foreach (var lane in plan.Lanes)
                if (lane != plan.MainLane && spans[rows[i - 1]].Min <= lane && lane <= spans[rows[i - 1]].Max && spans[rows[i]].Min <= lane && lane <= spans[rows[i]].Max)
                    for (var y = rows[i - 1]; y <= rows[i]; y++)
                        roads.Add(new Vector2I(lane, y));

        // 第 09-1c 章：比街距高的建築（神殿、主堡…）跨過後面的街，那一段街不鋪。
        foreach (var lot in lots)
            roads.RemoveWhere(r => r.X >= lot.X && r.X < lot.X + lot.W && r.Y >= lot.Y && r.Y < lot.Y + lot.H);

        // ── 圍牆：圍住廣場、建築與街；放不下時一次往外多擴 TempWallSlack 格 ──
        var wallKind = WallKindFor(tier);
        var landLots = lots.Where(l => l.Kind is not (Building.Dock or Building.Watermill)).ToList();
        var heart = Heart(plan, tier);
        var box = heart;
        foreach (var lot in landLots)
            box = box.Merge(new Rect2I(lot.X, lot.Y, lot.W, lot.H + 1));
        foreach (var r in roads)
            box = box.Merge(new Rect2I(r, Vector2I.One));
        var need = box.Grow(1);
        need = new Rect2I(need.Position, need.Size + new Vector2I(0, WallBottomPad(wallKind)));
        if (state.Wall is not { } wall || !wall.Encloses(need))
            state.Wall = wall = state.Wall is { } old ? need.Grow(TempWallSlack).Merge(old) : need.Grow(TempWallSlack);

        // 城門：主街穿過左右牆、主巷穿過上下牆；門與門外一格要是陸地。街與巷一路鋪到門口。
        var gates = new List<Vector2I>();
        bool Interior(Vector2I v) => v.X > wall.Position.X && v.X < wall.End.X - 1 && v.Y > wall.Position.Y && v.Y < wall.End.Y - 1;
        void Gate(Vector2I from, Vector2I step)
        {
            var path = new List<Vector2I>();
            var at = from;
            while (Interior(at))
            {
                path.Add(at);
                at += step;
            }
            var outside = at + step;
            if (!TileLand(at.X, at.Y) || !TileLand(outside.X, outside.Y) || path.Any(t => !TileLand(t.X, t.Y)))
                return;
            foreach (var t in path)
                roads.Add(t);
            roads.Add(at);
            roads.Add(outside);
            gates.Add(at);
        }
        Gate(new Vector2I(spans[plan.Center.Y].Min, plan.Center.Y), new Vector2I(-1, 0));
        Gate(new Vector2I(spans[plan.Center.Y].Max, plan.Center.Y), new Vector2I(1, 0));
        Gate(new Vector2I(plan.MainLane, rows[0]), new Vector2I(0, -1));
        Gate(new Vector2I(plan.MainLane, rows[^1]), new Vector2I(0, 1));
        roads.RemoveWhere(r => !wall.HasPoint(r) && !gates.Any(g => (g - r).Length() <= 1.01f));
        return new SettlementPlan(site.Id, heart, roads.ToList(), lots, wall, wallKind >= 2, gates, WallKind: wallKind);
    }

    /// <summary>聚落中心的空地：營地只是篝火旁一小塊，村莊以上是整個廣場。</summary>
    private static Rect2I Heart(MasterPlan plan, DemoSettlementTier tier)
        => tier == DemoSettlementTier.Camp ? new Rect2I(plan.Center.X - 2, plan.Center.Y, 4, 3) : plan.Square;

    /// <summary>地塊上的建築（範圍＝整塊地，素材底部置中在地的下緣）。</summary>
    private static Lot LotIn(PlanSlot slot, Building kind, bool construction, int variant)
        => new(slot.X, slot.Y, slot.W, slot.H, kind, construction, 0, variant);

    /// <summary>
    /// 第 08-20g 章：住屋的後院（房子正北、一樣寬）。第 09-1c 章：街距縮成 7 以後只剩一列——
    /// 木屋、農舍是菜畦（村子），其他住屋是院子（城鎮）。
    /// </summary>
    private static IEnumerable<(Vector2I Tile, byte Ground)> Garden(Lot lot)
    {
        if (!IsResidence(lot.Kind) || lot.Construction || lot.Kind == Building.Tent)
            yield break;
        var kind = lot.Kind is Building.Hut or Building.Farmhouse ? TerrainSurface.GroundGarden : TerrainSurface.GroundYard;
        for (var i = 0; i < lot.W; i++)
            yield return (new Vector2I(lot.X + i, lot.Y - 1), kind);
    }
}
