namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-6 章：聚落等級（使用者 2026-10-23：聚落→村莊→城邦→城市→首都；目前只有前兩級）。</summary>
public enum DemoSettlementTier
{
    Settlement = 0,
    Village = 1
}

/// <summary>
/// 第 08-3 章：聚落小史——建立至今幾年，以及依年份列出的里程碑（開局或開拓建立、人口第一次達到規模門檻、
/// 分出開拓隊建立的新聚落、人口高峰、成為遺跡）。由聚落小史（<see cref="DemoSettlementChronicle"/>，第 04-1 章）與聚落資料推得，只讀、不改世界。
/// 年份＝世界日 ÷ 365（總綱第 4 節：365 日／年）。
/// </summary>
public static class DemoSettlementHistory
{
    /// <summary>【TEMP】人口第一次達到這些規模時記一筆。</summary>
    public static readonly IReadOnlyList<long> TempMilestones = new long[] { 10, 25, 50, 100 };

    /// <summary>【TEMP】第 08-6 章 d 節：有木匠且人口 ≥ 這個數稱為村莊（使用者 2026-10-23：村莊要有木匠）。</summary>
    public const long TempVillagePopulation = 10;

    /// <summary>【TEMP】已是村莊的聚落，人口降到這個數以下才降級（避免人口在門檻上下來回時反覆升降）。</summary>
    public const long TempVillageKeepPopulation = 8;

    /// <summary>第 08-6 章 d 節：依人口與必要職位決定等級（<paramref name="current"/>＝目前等級，降級門檻較低）。</summary>
    public static DemoSettlementTier TierFor(long population, bool hasCarpenter, DemoSettlementTier current = DemoSettlementTier.Settlement)
    {
        var needed = current == DemoSettlementTier.Village ? TempVillageKeepPopulation : TempVillagePopulation;
        return hasCarpenter && population >= needed ? DemoSettlementTier.Village : DemoSettlementTier.Settlement;
    }

    public static string TierName(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => "村莊",
        _ => "聚落"
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
            $"規模：{TierName(settlement, world.ChronicleOf(settlementId).Tier)}｜人口 {population}｜建立至今 {age} 年（TEMP：有木匠且 {TempVillagePopulation} 人以上稱村莊）",
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
