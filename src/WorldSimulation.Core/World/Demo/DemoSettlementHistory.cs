namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-11 章：居住地五級（使用者 2026-10-28 裁決：參考世紀帝國「升時代要兩棟本時代建築」）。
/// 營地→村莊→城鎮→城市→首都。數字隨存檔保存，不可改動既有值。
/// </summary>
public enum DemoSettlementTier
{
    Camp = 0,
    Village = 1,
    Town = 2,
    City = 3,
    Capital = 4
}

/// <summary>第 08-11 章：一種等級設施（建築系統出現前，以對應的專家或技術代表）。</summary>
public sealed record DemoTierFacility(string Name, DemoRepresentativeRole? Role, string? TechId);

/// <summary>
/// 第 08-3 章：聚落小史——建立至今幾年，以及依年份列出的里程碑（開局或開拓建立、人口第一次達到規模門檻、
/// 分出開拓隊建立的新聚落、人口高峰、成為遺跡）。由聚落小史（<see cref="DemoSettlementChronicle"/>，第 04-1 章）與聚落資料推得，只讀、不改世界。
/// 年份＝世界日 ÷ 365（總綱第 4 節：365 日／年）。
/// </summary>
public static class DemoSettlementHistory
{
    /// <summary>【TEMP】人口第一次達到這些規模時記一筆。</summary>
    public static readonly IReadOnlyList<long> TempMilestones = new long[] { 10, 25, 50, 100 };

    /// <summary>【TEMP】第 08-11 章：升到該等級的人口門檻（營地 0、村莊 10、城鎮 60、城市 200、首都 1000）。</summary>
    public static long TempUpPopulation(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => 10,
        DemoSettlementTier.Town => 60,
        DemoSettlementTier.City => 200,
        DemoSettlementTier.Capital => 1000,
        _ => 0
    };

    /// <summary>【TEMP】保留該等級的人口下限（約升級門檻的八成，避免在門檻上下反覆升降）。</summary>
    public static long TempKeepPopulation(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => 8,
        DemoSettlementTier.Town => 50,
        DemoSettlementTier.City => 170,
        DemoSettlementTier.Capital => 850,
        _ => 0
    };

    /// <summary>【TEMP】升下一級要有的本級設施種數（世紀帝國：兩棟本時代建築）。</summary>
    public const int TempFacilitiesNeeded = 2;

    /// <summary>首都另需的技術（王權）。</summary>
    public const string CapitalTechId = "TECH_KINGSHIP";

    /// <summary>第 08-11 章：各等級的設施（升到下一級要任兩種）。首都級設施待第 10 章。</summary>
    public static IReadOnlyList<DemoTierFacility> FacilitiesOf(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => new DemoTierFacility[]
        {
            new("木工坊", DemoRepresentativeRole.Carpenter, null), new("磨坊", DemoRepresentativeRole.Farmer, null),
            new("獵屋", DemoRepresentativeRole.Hunter, null), new("祭壇", DemoRepresentativeRole.Shaman, null)
        },
        DemoSettlementTier.Village => new DemoTierFacility[]
        {
            new("市集", null, "TECH_CURRENCY"), new("鐵匠鋪", DemoRepresentativeRole.Smith, null),
            new("抄寫所", DemoRepresentativeRole.Scholar, null), new("陶窯", null, "TECH_POTTERY_FIRING")
        },
        DemoSettlementTier.Town => new DemoTierFacility[]
        {
            new("城牆", null, "TECH_DEFENSIVE_WORKS"), new("神殿", null, "TECH_ORGANIZED_RELIGION"),
            new("學校", null, "TECH_FORMAL_SCHOOLING"), new("市政廳", null, "TECH_CODIFIED_LAW")
        },
        _ => Array.Empty<DemoTierFacility>()
    };

    /// <summary>
    /// 第 08-11 章【TEMP 規則】依人口與設施決定等級，每週最多升或降一級：
    /// 目前等級的人口低於保留下限、或撐起目前等級的前一級設施不足兩種 → 降一級；
    /// 否則本級設施有兩種、人口達下一級門檻（首都另需王權）→ 升一級。
    /// </summary>
    public static DemoSettlementTier TierFor(long population, Func<DemoTierFacility, bool> has, DemoSettlementTier current,
        Func<string, bool>? knows = null)
    {
        ArgumentNullException.ThrowIfNull(has);
        if (current > DemoSettlementTier.Camp
            && (population < TempKeepPopulation(current) || FacilitiesOf(current - 1).Count(has) < TempFacilitiesNeeded))
            return current - 1;
        var next = current + 1;
        if (current < DemoSettlementTier.Capital && FacilitiesOf(current).Count(has) >= TempFacilitiesNeeded
            && population >= TempUpPopulation(next)
            && (next != DemoSettlementTier.Capital || knows?.Invoke(CapitalTechId) == true))
            return next;
        return current;
    }

    public static string TierName(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => "村莊",
        DemoSettlementTier.Town => "城鎮",
        DemoSettlementTier.City => "城市",
        DemoSettlementTier.Capital => "首都",
        _ => "營地"
    };

    public const int DaysPerYear = 365;

    public static int YearOfDay(int day) => Math.Max(0, day - 1) / DaysPerYear + 1;

    private static int YearOfWeek(int week) => YearOfDay(checked(week * 7));

    /// <summary>規模名稱（遺跡／村莊／聚落；第 08-6 章 d 節起依聚落小史記的等級）。</summary>
    public static string TierName(DemoSettlement settlement, DemoSettlementTier tier)
        => settlement.IsRuin ? "遺跡" : TierName(tier);

    /// <summary>點格詳情用的各行：第一行是規模與建立年數，其後每行一筆里程碑（依時間）。</summary>
    public static IReadOnlyList<string> Lines(DemoObserverWorld world, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var settlement = world.Sites.Settlements.Single(s => s.Id == settlementId);
        var today = world.Snapshot().DaysElapsed;
        var foundedDay = settlement.IsFounded ? checked(settlement.FoundedWeek * 7) : 0;
        var age = (today - foundedDay) / DaysPerYear;
        var population = world.SettlementPopulation(settlementId);
        var head = new[]
        {
            $"規模：{TierName(settlement, world.ChronicleOf(settlementId).Tier)}｜人口 {population}｜建立至今 {age} 年",
            "聚落小史："
        };
        var events = new List<(int Year, string Text)>();
        void Add(int year, string text) => events.Add((year, $"  第 {year} 年　{text}"));

        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        if (settlement.IsFounded)
            Add(YearOfWeek(settlement.FoundedWeek), $"{names.GetValueOrDefault(settlement.FoundedFromSettlementId, "未知聚落")}的開拓隊移居此地，建立聚落");
        else
            Add(1, "開局時已有人定居");

        // 第 04-1 章：里程碑、人口高峰與代表人物由聚落自己的小史提供（每週提交時更新、隨存檔保存）。
        var chronicle = world.ChronicleOf(settlementId);
        foreach (var line in chronicle.Milestones)
            Add(YearOfWeek(line.Week), line.Text);

        foreach (var child in world.Sites.Settlements.Where(s => s.IsFounded && s.FoundedFromSettlementId == settlementId)
                     .OrderBy(s => s.FoundedWeek).ThenBy(s => s.Id))
            Add(YearOfWeek(child.FoundedWeek), $"分出開拓隊，建立{child.Name}");

        // 第 07-1 章 b 節：代表人物的就任與卸任。
        foreach (var line in chronicle.Representatives)
            Add(YearOfWeek(line.Week), line.Text);

        if (chronicle.Peak > 0)
            Add(YearOfWeek(chronicle.PeakWeek), $"人口高峰 {chronicle.Peak} 人");
        if (settlement.IsRuin)
            Add(YearOfWeek(settlement.RuinedWeek), "人口歸零，成為遺跡");

        // 依年份排序（同年保持加入順序；OrderBy 是穩定排序）。
        return head.Concat(events.OrderBy(e => e.Year).Select(e => e.Text)).ToArray();
    }
}
