using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-32 章：建築的狀態——施工中、完成（有耐久度）、毀損（耐久度歸零，留下廢墟）。</summary>
public enum DemoBuildingStatus
{
    Building = 0,
    Standing = 1,
    Ruined = 2
}

/// <summary>
/// 第 08-32 章：一棟建築（城牆是一整圈、城門一座）。<see cref="Progress"/>＝已投入的工時；<see cref="Durability"/>＝耐久度（千分比，0＝毀損）；
/// <see cref="SinceWeek"/>＝開工、完工或毀損的那一週。升級、重建保留同一個編號（18C PreserveEntityID）。
/// </summary>
public sealed record DemoBuilding(int Id, string DefId, DemoBuildingStatus Status, int Progress, int Durability, int SinceWeek)
{
    /// <summary>這棟建築在建築表的那一列（不存檔）。</summary>
    [JsonIgnore]
    public DemoBuildingDef Def => DemoBuildingCatalog.Get(DefId);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"b{Id}:{DefId}/{(int)Status}/{Progress}/{Durability}/{SinceWeek}");
}

/// <summary>
/// 第 08-32 章：一個聚落的建築——個別建築、住宅（以棟數計：普通、大型、毀損；正在蓋的那一棟的工時）與建材庫存（木、石、鐵）。
/// </summary>
public sealed record DemoHolding(
    int SettlementId,
    IReadOnlyList<DemoBuilding> Buildings,
    int Dwellings,
    int LargeDwellings,
    int RuinedDwellings,
    int DwellingProgress,
    long Wood,
    long Stone,
    long Iron)
{
    public static DemoHolding EmptyFor(int settlementId) => new(settlementId, Array.Empty<DemoBuilding>(), 0, 0, 0, 0, 0, 0, 0);

    /// <summary>住得下幾人（普通住宅 5 人、大型 12 人，照 18_建築 的容量；不存檔）。</summary>
    [JsonIgnore]
    public long Housing => (long)Dwellings * DemoBuildings.HouseCapacity + (long)LargeDwellings * DemoBuildings.LargeHouseCapacity;

    public int Count(string defId, DemoBuildingStatus status) => Buildings.Count(b => b.DefId == defId && b.Status == status);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"h{SettlementId}:{Dwellings}/{LargeDwellings}/{RuinedDwellings}/{DwellingProgress}/{Wood}/{Stone}/{Iron}[{string.Join(",", Buildings)}]");
}

/// <summary>第 08-32 章：全世界的建築（依聚落編號排序）與下一個建築編號。</summary>
public sealed record DemoBuildingsState(IReadOnlyList<DemoHolding> Holdings, int NextBuildingId)
{
    public static DemoBuildingsState Empty { get; } = new(Array.Empty<DemoHolding>(), 1);

    public DemoHolding? Of(int settlementId)
    {
        var lo = 0;
        var hi = Holdings.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var id = Holdings[mid].SettlementId;
            if (id == settlementId)
                return Holdings[mid];
            if (id < settlementId)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return null;
    }

    public string Fingerprint()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"buildings#{NextBuildingId};");
        foreach (var h in Holdings)
            sb.Append(h).Append(';');
        return sb.ToString();
    }
}

/// <summary>
/// 第 08-32 章【TEMP 規則】正式建築系統第一版（使用者 2026-11-18：直接做正式建築系統）：建築由建材＋工時＋時間建成、有耐久度、
/// 會被怪物、戰爭、天災破壞，耐久度歸零留下廢墟、之後重建。
/// <list type="bullet">
/// <item>人手：成年人的 <see cref="TempBuilderPercent"/>%（至少 1 人）每週各做 <see cref="TempHoursPerBuilder"/> 小時；先修理，再接著蓋，最後開新工程。</item>
/// <item>建材：工程缺料時工人改去採集——附近 2 格有森林、丘陵、礦脈（且會探礦）採得快；附近沒有礦脈時不要求鐵。</item>
/// <item>蓋什麼：住宅跟著人口；等級設施（08-11、08-24）對應的建築；城牆與城門、塔樓；糧倉、農舍、兵營、市場、資源建築等。
///   住宅、城牆、城門以外的建築不超過等級的<b>建築格</b>（<see cref="Slots"/>）——格越多，城越大。</item>
/// </list>
/// 第一版只改建築狀態，不回頭影響人口、糧食、等級與戰鬥（等級照舊看設施，之後的章節再改看真的建築）。
/// 第 08-34 章：城牆、城門、塔樓開始有作用。第 08-36 章：等級改看真的建築（<see cref="FacilityBuilt(DemoHolding, DemoTierFacility, bool)"/>），
/// 升下一級要的本級設施建築先保留建築格（<see cref="Reserve"/>）；廢墟原地重建不必另有空格。
/// </summary>
public static class DemoBuildings
{
    public const string HouseBasic = "BUILD_HOUSE_BASIC", HouseLarge = "BUILD_HOUSE_LARGE", Shelter = "BUILD_SHELTER";
    public const string WallWood = "BUILD_WALL_WOOD", WallStone = "BUILD_WALL_STONE", GateWood = "BUILD_GATE_WOOD", GateStone = "BUILD_GATE_STONE";
    public const string Outpost = "BUILD_OUTPOST", StoneTower = "BUILD_STONE_TOWER";

    /// <summary>【TEMP】人手：成年人的幾 %（至少 1 人）、每人每週幾小時（其餘時間照舊耕作採集）。</summary>
    public const int TempBuilderPercent = 6;
    public const int TempHoursPerBuilder = 20;

    /// <summary>【TEMP】住宅容量（照 18_建築：普通 5 人、大型 12 人）。</summary>
    public const int HouseCapacity = 5, LargeHouseCapacity = 12;

    /// <summary>【TEMP】採集：每採到 1 單位要幾小時（附近 2 格有森林／丘陵／礦脈時、沒有時）。附近沒有礦脈時不要求鐵。</summary>
    public const int TempWoodHours = 2, TempWoodHoursFar = 5, TempStoneHours = 3, TempStoneHoursFar = 10, TempIronHours = 7;

    /// <summary>【TEMP】閒時備料：沒有要蓋的時候，木、石各補到「等級＋1」×這個數（營 40、村 80、鎮 120…）。</summary>
    public const int TempStockPerTier = 40;

    /// <summary>【TEMP】修理：補回全部耐久度要原工時的一半，不用建材。</summary>
    public const int TempRepairPercent = 50;

    /// <summary>【TEMP】同時開工的工程數（營、村 1，鎮 2，城、都 3）與建築格（住宅、城牆、城門不佔格）。</summary>
    public static int Projects(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp or DemoSettlementTier.Village => 1,
        DemoSettlementTier.Town => 2,
        _ => 3
    };

    public static int Slots(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 3,
        DemoSettlementTier.Village => 8,
        DemoSettlementTier.Town => 16,
        DemoSettlementTier.City => 26,
        _ => 36
    };

    /// <summary>等級設施（08-11、08-24）對應的正式建築；同一種建築被幾個設施需要時取最多的數量（祭壇、神殿＝1 座神殿，大神殿＝第 2 座）。</summary>
    public static readonly IReadOnlyDictionary<string, (string DefId, int Count)> FacilityBuildings = new Dictionary<string, (string, int)>
    {
        ["木工坊"] = ("BUILD_WORKSHOP", 1),
        ["磨坊"] = ("BUILD_KITCHEN", 1),
        ["獵屋"] = ("BUILD_TANNERY", 1),
        ["祭壇"] = ("BUILD_CHURCH", 1),
        ["市集"] = ("BUILD_MARKET", 1),
        ["鐵匠鋪"] = ("BUILD_SMITHY", 1),
        ["抄寫所"] = ("BUILD_SCRIPTORIUM", 1),
        ["陶窯"] = ("BUILD_KILN", 1),
        ["城牆"] = (WallWood, 1),
        ["神殿"] = ("BUILD_CHURCH", 1),
        ["學校"] = ("BUILD_SCHOOL", 1),
        ["市政廳"] = ("BUILD_TOWN_HALL", 1),
        ["城堡"] = ("BUILD_CASTLE", 1),
        ["大神殿"] = ("BUILD_CHURCH", 2),
        ["圖書館"] = ("BUILD_ACADEMY", 1),
        ["醫院"] = ("BUILD_HOSPITAL", 1)
    };

    /// <summary>不佔建築格的（住宅以棟數另計）。</summary>
    public static bool IsWallwork(string defId) => defId is WallWood or WallStone or GateWood or GateStone;

    /// <summary>
    /// 第 08-36 章：某項等級設施的建築有沒有（數量照 <see cref="FacilityBuildings"/>：大神殿＝第 2 座神殿；城牆木造、石造都算）。
    /// <paramref name="standingOnly"/>＝只算完好可用的（升級用）；否則施工中、毀損待重建的也算（建築還在，保留等級與建築格用）。
    /// </summary>
    public static bool FacilityBuilt(DemoHolding? holding, DemoTierFacility facility, bool standingOnly)
        => holding is not null && FacilityBuilt(holding.Buildings, facility, standingOnly);

    public static bool FacilityBuilt(IReadOnlyList<DemoBuilding> buildings, DemoTierFacility facility, bool standingOnly)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(facility);
        if (!FacilityBuildings.TryGetValue(facility.Name, out var map))
            return false;
        var family = Family(map.DefId);
        return buildings.Count(b => Family(b.DefId) == family && (!standingOnly || b.Status == DemoBuildingStatus.Standing)) >= map.Count;
    }

    /// <summary>
    /// 第 08-36 章【TEMP 規則】升下一級還差幾種本級設施的建築（每種要一格）：已解鎖且建築已在（完好、施工中、毀損待重建）的算有。
    /// 這些格先保留給本級設施，其他建築不能佔（首都沒有下一級＝0）。
    /// </summary>
    public static int Reserve(IReadOnlyList<DemoBuilding> buildings, DemoSettlementTier tier, Func<DemoTierFacility, bool> unlocked)
    {
        ArgumentNullException.ThrowIfNull(unlocked);
        if (tier >= DemoSettlementTier.Capital)
            return 0;
        var ready = DemoSettlementHistory.FacilitiesOf(tier).Count(f => unlocked(f) && FacilityBuilt(buildings, f, standingOnly: false));
        return Math.Max(0, DemoSettlementHistory.TempFacilitiesNeeded - ready);
    }

    private const string Namespace = "TEMP-BUILDINGS-v1";

    /// <summary>一週的建築：先算本週的破壞（怪物、戰爭、天災、成為遺跡），再讓每個有人的聚落施工。</summary>
    public static DemoBuildingsState Advance(DemoBuildingsState state, DemoBuildingWeek week)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(week);
        var holdings = state.Holdings.ToDictionary(h => h.SettlementId);
        var nextId = state.NextBuildingId;
        // ① 破壞
        foreach (var e in week.Events)
            if (holdings.TryGetValue(e.SettlementId, out var hit) && Damage(e, hit, week) is { } damaged)
                holdings[e.SettlementId] = damaged;
        // ② 施工
        foreach (var site in week.Settlements)
        {
            if (site.IsRuin || week.Residents(site.Id) <= 0)
                continue;
            // 第一次有建築紀錄（開局聚落、新建的村）：已經住著的人有棲身之處（營地是棚屋、會基礎建築的是住宅）。
            var holding = holdings.GetValueOrDefault(site.Id) ?? DemoHolding.EmptyFor(site.Id) with
            {
                Dwellings = (int)((week.Residents(site.Id) + HouseCapacity - 1) / HouseCapacity)
            };
            var worked = Work(holding, site, week, ref nextId);
            if (!ReferenceEquals(worked, holding) || !holdings.ContainsKey(site.Id))
                holdings[site.Id] = worked;
        }
        return new DemoBuildingsState(holdings.Values.OrderBy(h => h.SettlementId).ToArray(), nextId);
    }

    // ── 破壞 ─────────────────────────────────────────────────────────────

    /// <summary>【TEMP】一件事的破壞（null＝沒影響）：城牆擋在前面；天災不分；成為遺跡時全部毀損。</summary>
    private static DemoHolding? Damage(DemoSettlementEvent e, DemoHolding h, DemoBuildingWeek week)
    {
        if (!Damaging.Contains(e.Kind))
            return null; // 出生、老死等每週大量的事件不碰建築
        var draw = 0UL;
        ulong Next() => Draw(week.Rng, e.SettlementId, (ulong)e.EventId * 32UL + draw++);
        int Roll(int min, int max) => min + (int)(Next() % (ulong)(max - min + 1));
        var buildings = h.Buildings.ToList();
        var ruinedHouses = 0;

        void Hit(Func<DemoBuilding, bool> which, int count, int min, int max)
        {
            for (var k = 0; k < count; k++)
            {
                var targets = Enumerable.Range(0, buildings.Count).Where(j => buildings[j].Status != DemoBuildingStatus.Ruined && which(buildings[j])).ToList();
                if (targets.Count == 0)
                    return;
                var i = targets[(int)(Next() % (ulong)targets.Count)];
                buildings[i] = Wear(buildings[i], Roll(min, max), week.Week);
            }
        }
        void HitEach(Func<DemoBuilding, bool> which, int percent, int min, int max)
        {
            for (var i = 0; i < buildings.Count; i++)
                if (buildings[i].Status != DemoBuildingStatus.Ruined && which(buildings[i]) && (int)(Next() % 100UL) < percent)
                    buildings[i] = Wear(buildings[i], Roll(min, max), week.Week);
        }
        void Houses(int permille)
        {
            var houses = h.Dwellings + h.LargeDwellings;
            var exact = houses * permille;
            ruinedHouses = exact / 1000 + ((int)(Next() % 1000UL) < exact % 1000 ? 1 : 0);
        }
        bool Walled() => buildings.Any(b => b.Status == DemoBuildingStatus.Standing && b.DefId is WallWood or WallStone);
        static bool Wall(DemoBuilding b) => b.DefId is WallWood or WallStone;
        static bool Gate(DemoBuilding b) => b.DefId is GateWood or GateStone;
        static bool Inside(DemoBuilding b) => !IsWallwork(b.DefId);

        switch (e.Kind)
        {
            case DemoSettlementEventKind.SettlementRuined:
                buildings = buildings.Select(b => b.Status == DemoBuildingStatus.Ruined ? b : b with { Status = DemoBuildingStatus.Ruined, Durability = 0, SinceWeek = week.Week }).ToList();
                return h with
                {
                    Buildings = buildings,
                    Dwellings = 0,
                    LargeDwellings = 0,
                    RuinedDwellings = h.RuinedDwellings + h.Dwellings + h.LargeDwellings,
                    DwellingProgress = 0
                };
            case DemoSettlementEventKind.MonsterRaid:
                if (Walled())
                {
                    Hit(Wall, 1, 150, 300);
                    Hit(Gate, 1, 100, 200);
                }
                else
                {
                    Hit(Inside, 1, 200, 450);
                    Houses(30);
                }
                break;
            case DemoSettlementEventKind.MonsterSwarm:
                Hit(Wall, 1, 300, 600);
                Hit(Gate, 1, 300, 600);
                Hit(Inside, 2, 300, 700);
                Houses(Walled() ? 40 : 100);
                break;
            case DemoSettlementEventKind.SecessionBattle or DemoSettlementEventKind.ConquestBattle or DemoSettlementEventKind.CivilWar:
                Hit(Wall, 1, 100, 250);
                Hit(Gate, 1, 50, 150);
                Houses(10);
                break;
            case DemoSettlementEventKind.Occupied:
                Hit(Wall, 1, 150, 300);
                Hit(Inside, 1, 100, 300);
                break;
            case DemoSettlementEventKind.Conquered:
                Hit(b => b.DefId is "BUILD_CASTLE" or "BUILD_TOWN_HALL", 1, 200, 400);
                Hit(Inside, 1, 200, 400);
                Houses(30);
                break;
            case DemoSettlementEventKind.Earthquake:
                HitEach(_ => true, 40, 200, 600);
                Houses(80);
                break;
            case DemoSettlementEventKind.Tornado:
                Hit(_ => true, 2, 300, 700);
                Houses(50);
                break;
            case DemoSettlementEventKind.Flood:
                HitEach(b => b.DefId is "BUILD_FARM" or "BUILD_GRANARY" or "BUILD_WAREHOUSE" or "BUILD_PORT" or "BUILD_MARKET", 100, 200, 500);
                Houses(40);
                break;
            case DemoSettlementEventKind.Meteorite:
                Hit(_ => true, 1, 1000, 1000);
                Houses(20);
                break;
            default:
                return null;
        }
        ruinedHouses = Math.Min(ruinedHouses, h.Dwellings + h.LargeDwellings);
        var large = Math.Min(h.LargeDwellings, ruinedHouses / 3);
        return h with
        {
            Buildings = buildings,
            Dwellings = h.Dwellings - (ruinedHouses - large),
            LargeDwellings = h.LargeDwellings - large,
            RuinedDwellings = h.RuinedDwellings + ruinedHouses
        };
    }

    /// <summary>這種事件會打壞建築（第 08-35 章：建築毀損的直接原因）。</summary>
    public static bool IsDamaging(DemoSettlementEventKind kind) => Damaging.Contains(kind);

    private static readonly HashSet<DemoSettlementEventKind> Damaging = new()
    {
        DemoSettlementEventKind.SettlementRuined, DemoSettlementEventKind.MonsterRaid, DemoSettlementEventKind.MonsterSwarm,
        DemoSettlementEventKind.SecessionBattle, DemoSettlementEventKind.ConquestBattle, DemoSettlementEventKind.CivilWar,
        DemoSettlementEventKind.Occupied, DemoSettlementEventKind.Conquered, DemoSettlementEventKind.Earthquake,
        DemoSettlementEventKind.Tornado, DemoSettlementEventKind.Flood, DemoSettlementEventKind.Meteorite
    };

    /// <summary>扣耐久度；施工中的退回三成進度；歸零＝毀損。</summary>
    private static DemoBuilding Wear(DemoBuilding b, int amount, int week)
    {
        if (b.Status == DemoBuildingStatus.Building)
            return b with { Progress = b.Progress * 70 / 100 };
        var left = b.Durability - amount;
        return left > 0 ? b with { Durability = left } : b with { Status = DemoBuildingStatus.Ruined, Durability = 0, SinceWeek = week };
    }

    // ── 施工 ─────────────────────────────────────────────────────────────

    private static DemoHolding Work(DemoHolding h, DemoSettlement site, DemoBuildingWeek week, ref int nextId)
    {
        var adults = week.Adults(site.Id);
        var builders = adults >= 2 ? Math.Max(1, adults * TempBuilderPercent / 100) : 0;
        var budget = builders * TempHoursPerBuilder;
        if (budget <= 0)
            return h;
        var tier = week.Tier(site.Id);
        var buildings = h.Buildings.ToList();
        var (wood, stone, iron) = (h.Wood, h.Stone, h.Iron);
        var dwellings = h.Dwellings;
        var large = h.LargeDwellings;
        var ruinedHouses = h.RuinedDwellings;
        var houseProgress = h.DwellingProgress;
        var nearby = week.Nearby(site);

        // ① 修理（耐久度最低的先修）。
        var damaged = Enumerable.Range(0, buildings.Count).Where(j => buildings[j].Status == DemoBuildingStatus.Standing && buildings[j].Durability < 1000)
            .OrderBy(j => buildings[j].Durability).ThenBy(j => buildings[j].Id).ToList();
        foreach (var i in damaged)
        {
            if (budget <= 0)
                break;
            var b = buildings[i];
            var full = Math.Max(1, b.Def.Hours * TempRepairPercent / 100);
            var need = Math.Max(1, full * (1000 - b.Durability) / 1000);
            var spend = (int)Math.Min(budget, need);
            budget -= spend;
            buildings[i] = b with { Durability = Math.Min(1000, b.Durability + Math.Max(1, spend * 1000 / full)) };
        }

        // ② 接著蓋：住宅（正在蓋的那一棟）、施工中的建築（先開工的先蓋）。
        var houseDef = HouseFor(tier, id => week.Knows(site.Id, id));
        if (houseProgress > 0 && budget > 0)
        {
            var spend = (int)Math.Min(budget, houseDef.Hours - houseProgress);
            budget -= spend;
            houseProgress += spend;
            if (houseProgress >= houseDef.Hours)
            {
                houseProgress = 0;
                if (houseDef.Id == HouseLarge) large++; else dwellings++;
                if (ruinedHouses > 0) ruinedHouses--;
            }
        }
        var underway = Enumerable.Range(0, buildings.Count).Where(j => buildings[j].Status == DemoBuildingStatus.Building)
            .OrderBy(j => buildings[j].SinceWeek).ThenBy(j => buildings[j].Id).ToList();
        foreach (var i in underway)
        {
            if (budget <= 0)
                break;
            var b = buildings[i];
            var spend = (int)Math.Min(budget, b.Def.Hours - b.Progress);
            budget -= spend;
            var progress = b.Progress + spend;
            buildings[i] = progress >= b.Def.Hours
                ? b with { Status = DemoBuildingStatus.Standing, Progress = b.Def.Hours, Durability = 1000, SinceWeek = week.Week }
                : b with { Progress = progress };
        }

        // ③ 開新工程（同時開工數有上限；缺料就改去採集）。
        var open = buildings.Count(b => b.Status == DemoBuildingStatus.Building) + (houseProgress > 0 ? 1 : 0);
        var idle = false;
        while (budget > 0 && open < Projects(tier))
        {
            var want = NextWant(buildings, dwellings, large, houseProgress > 0, site, tier, week);
            if (want is null)
            {
                idle = true;
                break;
            }
            var def = want == "DWELLING" ? houseDef : DemoBuildingCatalog.Get(want);
            var needIron = nearby.Ore ? def.Iron : 0;
            if (wood < def.Wood || stone < def.Stone || iron < needIron)
            {
                // 缺料：剩下的人手去採（木、石、鐵依序補到夠），本週不開工。
                budget = Gather(budget, def.Wood - wood, nearby.Forest ? TempWoodHours : TempWoodHoursFar, ref wood);
                budget = Gather(budget, def.Stone - stone, nearby.Hills ? TempStoneHours : TempStoneHoursFar, ref stone);
                if (needIron > 0)
                    Gather(budget, needIron - iron, TempIronHours, ref iron);
                break;
            }
            wood -= def.Wood;
            stone -= def.Stone;
            iron -= needIron;
            open++;
            var spend = (int)Math.Min(budget, def.Hours);
            budget -= spend;
            if (want == "DWELLING")
            {
                houseProgress = spend;
                if (houseProgress >= def.Hours)
                {
                    houseProgress = 0;
                    if (def.Id == HouseLarge) large++; else dwellings++;
                    if (ruinedHouses > 0) ruinedHouses--;
                    open--;
                }
                continue;
            }
            var done = spend >= def.Hours;
            var status = done ? DemoBuildingStatus.Standing : DemoBuildingStatus.Building;
            // 重建廢墟或升級：沿用同一個編號（城牆、城門升級成石造、毀損後重建也是同一圈、同一座）。
            var reuse = buildings.FindIndex(b => Family(b.DefId) == Family(want)
                && (b.Status == DemoBuildingStatus.Ruined || (IsWallwork(want) && b.DefId != want)));
            var built = new DemoBuilding(reuse >= 0 ? buildings[reuse].Id : nextId, want, status, spend, done ? 1000 : 0, week.Week);
            if (reuse >= 0)
                buildings[reuse] = built;
            else
            {
                buildings.Add(built);
                nextId++;
            }
            if (done)
                open--;
        }

        // ④ 沒有要蓋的：閒時備料（木、石各補到等級的上限）。
        if (idle && budget > 0)
        {
            var cap = TempStockPerTier * ((int)tier + 1);
            budget = Gather(budget, cap - wood, nearby.Forest ? TempWoodHours : TempWoodHoursFar, ref wood);
            Gather(budget, cap - stone, nearby.Hills ? TempStoneHours : TempStoneHoursFar, ref stone);
        }

        var same = buildings.Count == h.Buildings.Count && buildings.Zip(h.Buildings).All(p => ReferenceEquals(p.First, p.Second))
            && (wood, stone, iron, dwellings, large, ruinedHouses, houseProgress) == (h.Wood, h.Stone, h.Iron, h.Dwellings, h.LargeDwellings, h.RuinedDwellings, h.DwellingProgress);
        return same ? h : new DemoHolding(h.SettlementId, buildings.ToArray(), dwellings, large, ruinedHouses, houseProgress, wood, stone, iron);
    }

    /// <summary>第 08-36 章：同一種（城牆木造、石造算同一種；城門也是）。</summary>
    public static bool SameFamily(string a, string b) => Family(a) == Family(b);

    /// <summary>同一圈城牆、同一座城門（木造、石造算同一個）。</summary>
    private static string Family(string defId) => defId is WallWood or WallStone ? "WALL" : defId is GateWood or GateStone ? "GATE" : defId;

    /// <summary>用人手採集到補足 <paramref name="lacking"/>（每單位 <paramref name="hoursEach"/> 小時）；回傳剩下的工時。</summary>
    private static long Gather(long budget, long lacking, int hoursEach, ref long stock)
    {
        if (budget <= 0 || lacking <= 0)
            return budget;
        var units = Math.Min(lacking, budget / hoursEach);
        stock += units;
        return budget - units * hoursEach;
    }

    /// <summary>【TEMP】新住宅：營地搭棚屋（臨時收容所）；會基礎建築的蓋普通住宅；鎮以上、會木骨架與石工的蓋大型住宅。</summary>
    public static DemoBuildingDef HouseFor(DemoSettlementTier tier, Func<string, bool> knows)
    {
        if (tier >= DemoSettlementTier.Town && DemoBuildingCatalog.Get(HouseLarge).Techs.All(knows))
            return DemoBuildingCatalog.Get(HouseLarge);
        return DemoBuildingCatalog.Get(HouseBasic).Techs.All(knows) ? DemoBuildingCatalog.Get(HouseBasic) : DemoBuildingCatalog.Get(Shelter);
    }

    /// <summary>
    /// 【TEMP】下一個要開工的（"DWELLING"＝住宅；null＝沒有）。依序：住宅不夠住、等級設施的建築、城牆與城門（石造升級）、
    /// 糧倉、農舍、兵營、市場、塔樓、資源建築、其他（旅店、倉庫、診療所、馬廄、靶場、港口）；住宅、城牆、城門以外不超過建築格。
    /// </summary>
    private static string? NextWant(List<DemoBuilding> buildings, int dwellings, int large, bool houseBusy, DemoSettlement site, DemoSettlementTier tier,
        DemoBuildingWeek week)
    {
        var id = site.Id;
        bool Knows(string tech) => week.Knows(id, tech);
        int Have(string defId) => buildings.Count(b => b.DefId == defId && b.Status != DemoBuildingStatus.Ruined);
        var housing = (long)dwellings * HouseCapacity + (long)large * LargeHouseCapacity;
        if (!houseBusy && housing < week.Residents(id))
            return "DWELLING"; // 住宅一次蓋一棟
        var used = buildings.Count(b => !IsWallwork(b.DefId));
        // 第 08-36 章：升下一級要的本級設施建築先保留建築格；廢墟原地重建不必另有空格。
        var reserve = Reserve(buildings, tier, f => week.HasFacility(id, f));
        string? Want(string defId, int count, bool techCheck = true, bool reserved = false)
        {
            if (Have(defId) >= count || !DemoBuildingCatalog.Contains(defId))
                return null;
            if (techCheck && !DemoBuildingCatalog.Get(defId).Techs.All(Knows))
                return null;
            var rebuild = buildings.Any(b => b.DefId == defId && b.Status == DemoBuildingStatus.Ruined);
            return IsWallwork(defId) || rebuild || used < Slots(tier) - (reserved ? 0 : reserve) ? defId : null;
        }

        // 等級設施（各級都算：鎮也保有村的設施）；本級的可以用保留的建築格（第 08-36 章）。
        var facilityWants = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var t = DemoSettlementTier.Camp; t <= tier; t++)
            foreach (var f in DemoSettlementHistory.FacilitiesOf(t))
                if (week.HasFacility(id, f) && FacilityBuildings.TryGetValue(f.Name, out var map))
                    facilityWants[map.DefId] = Math.Max(facilityWants.GetValueOrDefault(map.DefId), map.Count);
        foreach (var t in new[] { DemoSettlementTier.Camp, DemoSettlementTier.Village, DemoSettlementTier.Town, DemoSettlementTier.City })
            foreach (var f in DemoSettlementHistory.FacilitiesOf(t))
                if (FacilityBuildings.TryGetValue(f.Name, out var map) && facilityWants.TryGetValue(map.DefId, out var count) && map.DefId != WallWood
                    && Want(map.DefId, count, techCheck: false, reserved: t == tier && week.HasFacility(id, f)) is { } facility)
                    return facility;

        // 城牆與城門（會石工與築城、城市以上改成石造）。
        if (facilityWants.ContainsKey(WallWood))
        {
            var stone = tier >= DemoSettlementTier.City && DemoBuildingCatalog.Get(WallStone).Techs.All(Knows);
            if (stone && buildings.Any(b => b.DefId == WallWood))
                return WallStone;
            if (stone && buildings.Any(b => b.DefId == GateWood) && buildings.Any(b => b.DefId == WallStone && b.Status == DemoBuildingStatus.Standing))
                return GateStone;
            if (Have(WallWood) + Have(WallStone) == 0)
                return stone ? WallStone : WallWood;
            if (Have(GateWood) + Have(GateStone) == 0)
                return stone && Have(WallStone) > 0 ? GateStone : GateWood;
        }
        var walled = buildings.Any(b => b.DefId is WallWood or WallStone && b.Status == DemoBuildingStatus.Standing);
        var soldiers = week.Soldiers(id);
        var nearby = week.Nearby(site);
        var candidates = new (string DefId, int Count, bool When)[]
        {
            ("BUILD_GRANARY", tier >= DemoSettlementTier.City ? 2 : 1, tier >= DemoSettlementTier.Village && week.FoodStock(id) >= week.Residents(id) * 6),
            ("BUILD_FARM", (week.Fields(id) + 5) / 6, week.Fields(id) > 0),
            ("BUILD_BARRACK", (int)((soldiers + 149) / 150), soldiers > 0),
            ("BUILD_MARKET", 1, week.IsMarket(id)),
            (Outpost, tier >= DemoSettlementTier.City ? 4 : 2, walled && tier >= DemoSettlementTier.Town && !DemoBuildingCatalog.Get(StoneTower).Techs.All(Knows)),
            (StoneTower, tier >= DemoSettlementTier.Capital ? 4 : 2, walled && tier >= DemoSettlementTier.City),
            ("BUILD_LUMBERYARD", 1, nearby.Forest && tier >= DemoSettlementTier.Village && Knows(DemoEnvironment.LoggingTech)),
            ("BUILD_QUARRY", 1, nearby.Hills && tier >= DemoSettlementTier.Village),
            ("BUILD_MINE", 1, nearby.Ore && Knows(DemoEnvironment.MiningTech)),
            ("BUILD_INN", 1, tier >= DemoSettlementTier.Town && week.IsMarket(id)),
            ("BUILD_WAREHOUSE", 1, tier >= DemoSettlementTier.City),
            ("BUILD_CLINIC", 1, tier >= DemoSettlementTier.Village),
            ("BUILD_STABLE", 1, soldiers >= 50),
            ("BUILD_ARCHERY_RANGE", 1, soldiers >= 50),
            ("BUILD_PORT", 1, nearby.Coast && tier >= DemoSettlementTier.Town)
        };
        foreach (var (defId, count, when) in candidates)
            if (when && count > 0 && Want(defId, count) is { } next)
                return next;
        return null;
    }

    /// <summary>附近 2 格（切比雪夫距離）有沒有森林、丘陵、礦脈（含隕石礦），1 格內有沒有海（第 08-33 章領地面板也用）。</summary>
    public static DemoBuildingNearby NearbyOf(DemoHexWorld map, DemoSettlement site, IReadOnlySet<DemoCellRef>? extraOre = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(site);
        bool forest = false, hills = false, ore = false, coast = false;
        for (var dr = -2; dr <= 2; dr++)
            for (var dc = -2; dc <= 2; dc++)
            {
                int c = site.Column + dc, r = site.Row + dr;
                if (!map.Contains(c, r))
                    continue;
                var cell = map.GetCell(c, r);
                forest |= cell.Terrain == DemoTerrain.Forest;
                hills |= cell.Terrain == DemoTerrain.Hills;
                ore |= cell.Ore || extraOre?.Contains(new DemoCellRef(c, r)) == true;
                coast |= cell.Sea && Math.Max(Math.Abs(dr), Math.Abs(dc)) <= 1;
            }
        return new DemoBuildingNearby(forest, hills, ore, coast);
    }

    /// <summary>
    /// 第 08-35 章：重要建築——建成、毀損會寫進聚落小史與世界編年史
    /// （城牆、城堡、市政廳、神殿、學校、高等學院、魔法學院、醫院、國家銀行、港口）。
    /// </summary>
    public static bool IsLandmark(string defId) => defId is WallWood or WallStone or "BUILD_CASTLE" or "BUILD_TOWN_HALL" or "BUILD_CHURCH"
        or "BUILD_SCHOOL" or "BUILD_ACADEMY" or "BUILD_MAGIC_ACADEMY" or "BUILD_HOSPITAL" or "BUILD_BANK" or "BUILD_PORT";

    /// <summary>
    /// 第 08-35 章：這一週的建設大事——重要建築建成（含重建、木牆改建的石牆）與毀損（聚落成為遺跡時的全部毀損不另記）。
    /// 事件編號從 <paramref name="firstEventId"/> 起連續；毀損的直接原因＝同週同聚落第一件打壞建築的事件（怪物、戰爭、天災）。
    /// </summary>
    public static IReadOnlyList<DemoSettlementEvent> Milestones(DemoBuildingsState before, DemoBuildingsState after, int week, TimeSpan worldTime,
        IEnumerable<DemoSettlement> settlements, Func<int, DemoSettlementTier> tier, IReadOnlyList<DemoSettlementEvent> weekEvents, long firstEventId)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(tier);
        ArgumentNullException.ThrowIfNull(weekEvents);
        var events = new List<DemoSettlementEvent>();
        var nextId = firstEventId;
        var byId = settlements.ToDictionary(s => s.Id);
        foreach (var h in after.Holdings)
        {
            if (!byId.TryGetValue(h.SettlementId, out var site) || !h.Buildings.Any(b => IsLandmark(b.DefId)))
                continue;
            var old = before.Of(h.SettlementId)?.Buildings.ToDictionary(b => b.Id);
            DemoSettlementEvent? cause = null;
            var causeKnown = false;
            foreach (var b in h.Buildings.Where(b => IsLandmark(b.DefId)).OrderBy(b => b.Id))
            {
                var was = old?.GetValueOrDefault(b.Id);
                var name = DemoHoldingReport.Name(b.DefId, tier(h.SettlementId));
                if (b.Status == DemoBuildingStatus.Standing && (was is null || was.Status != DemoBuildingStatus.Standing || was.DefId != b.DefId))
                {
                    events.Add(new DemoSettlementEvent(week, worldTime, site.Id, site.Name, DemoSettlementEventKind.BuildingCompleted, b.Id,
                        $"建成{name}", EventId: nextId++));
                }
                else if (b.Status == DemoBuildingStatus.Ruined && was is not null && was.Status != DemoBuildingStatus.Ruined)
                {
                    if (!causeKnown)
                    {
                        cause = weekEvents.FirstOrDefault(e => e.SettlementId == site.Id && IsDamaging(e.Kind));
                        causeKnown = true;
                    }
                    if (cause is { Kind: DemoSettlementEventKind.SettlementRuined })
                        continue; // 成為遺跡時全部毀損，遺跡事件已寫
                    events.Add(new DemoSettlementEvent(week, worldTime, site.Id, site.Name, DemoSettlementEventKind.BuildingRuined, b.Id,
                        $"{name}{RuinedHow(cause?.Kind)}", EventId: nextId++, CauseEventId: cause?.EventId ?? 0));
                }
            }
        }
        return events;
    }

    /// <summary>毀損的說法（依打壞它的事件）。</summary>
    private static string RuinedHow(DemoSettlementEventKind? kind) => kind switch
    {
        DemoSettlementEventKind.MonsterSwarm => "被獸潮攻破",
        DemoSettlementEventKind.MonsterRaid => "被怪物打毀",
        DemoSettlementEventKind.SecessionBattle or DemoSettlementEventKind.ConquestBattle or DemoSettlementEventKind.CivilWar
            or DemoSettlementEventKind.Occupied or DemoSettlementEventKind.Conquered => "毀於戰火",
        DemoSettlementEventKind.Earthquake => "在地震中倒塌",
        DemoSettlementEventKind.Tornado => "被龍捲風摧毀",
        DemoSettlementEventKind.Flood => "被洪水沖毀",
        DemoSettlementEventKind.Meteorite => "被隕石擊毀",
        _ => "毀損"
    };

    /// <summary>【TEMP】每個聚落附近的農田格數（農田算給 3 格內最近的聚落；同距離取編號小的）。</summary>
    public static IReadOnlyDictionary<int, int> FieldsNear(DemoLandDevelopmentMap land, IEnumerable<DemoSettlement> settlements)
    {
        ArgumentNullException.ThrowIfNull(land);
        ArgumentNullException.ThrowIfNull(settlements);
        var at = new Dictionary<(int Column, int Row), int>();
        foreach (var site in settlements.OrderBy(s => s.Id))
            at.TryAdd((site.Column, site.Row), site.Id);
        var counts = new Dictionary<int, int>();
        foreach (var cell in land.FarmlandCells())
            for (var d = 0; d <= 3; d++)
            {
                var found = int.MaxValue;
                for (var dr = -d; dr <= d; dr++)
                    for (var dc = -d; dc <= d; dc++)
                        if (Math.Max(Math.Abs(dr), Math.Abs(dc)) == d && at.TryGetValue((cell.Column + dc, cell.Row + dr), out var id))
                            found = Math.Min(found, id);
                if (found == int.MaxValue)
                    continue;
                counts[found] = counts.GetValueOrDefault(found) + 1;
                break;
            }
        return counts;
    }

    private static ulong Draw(RngRegistry rng, int settlementId, ulong index)
        => EntityLocalRandom.Create(rng, EntityId.From((ulong)Math.Max(1, settlementId)), RngStreams.Event, Namespace).GetUInt64(index);
}

/// <summary>聚落附近 2 格內有沒有森林、丘陵、礦脈、海（採集建材與資源建築用）。</summary>
public readonly record struct DemoBuildingNearby(bool Forest, bool Hills, bool Ore, bool Coast);

/// <summary>第 08-32 章：建築推進一週需要的世界資訊（全部唯讀；事件＝本週已發生的）。</summary>
public sealed record DemoBuildingWeek(
    int Week,
    DemoHexWorld Map,
    IReadOnlyList<DemoSettlement> Settlements,
    Func<int, long> Residents,
    Func<int, long> Adults,
    Func<int, DemoSettlementTier> Tier,
    Func<int, string, bool> Knows,
    Func<int, DemoTierFacility, bool> HasFacility,
    Func<int, long> Soldiers,
    Func<int, int> Fields,
    Func<int, long> FoodStock,
    Func<int, bool> IsMarket,
    IReadOnlyList<DemoSettlementEvent> Events,
    RngRegistry Rng,
    IReadOnlySet<DemoCellRef>? ExtraOre = null)
{
    private readonly Dictionary<int, DemoBuildingNearby> _nearby = new();

    /// <summary>附近 2 格（切比雪夫距離）。</summary>
    public DemoBuildingNearby Nearby(DemoSettlement site)
    {
        if (_nearby.TryGetValue(site.Id, out var known))
            return known;
        return _nearby[site.Id] = DemoBuildings.NearbyOf(Map, site, ExtraOre);
    }
}
