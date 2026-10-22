namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-3 章：聚落小史——建立至今幾年，以及依年份列出的里程碑（開局或開拓建立、人口第一次達到規模門檻、
/// 分出開拓隊建立的新聚落、人口高峰、成為遺跡）。全部由已提交的週報與聚落資料推得，只讀、不改世界。
/// 年份＝世界日 ÷ 365（總綱第 4 節：365 日／年）。
/// </summary>
public static class DemoSettlementHistory
{
    /// <summary>【TEMP】人口第一次達到這些規模時記一筆；10 人起稱為「村莊」，未滿稱「聚落」。</summary>
    public static readonly IReadOnlyList<long> TempMilestones = new long[] { 10, 25, 50, 100 };

    /// <summary>【TEMP】人口 ≥ 這個數稱為村莊。</summary>
    public const long TempVillagePopulation = 10;

    public const int DaysPerYear = 365;

    public static int YearOfDay(int day) => Math.Max(0, day - 1) / DaysPerYear + 1;

    private static int YearOfWeek(int week) => YearOfDay(checked(week * 7));

    /// <summary>規模名稱（遺跡／村莊／聚落）。</summary>
    public static string TierName(DemoSettlement settlement, long population)
        => settlement.IsRuin ? "遺跡" : population >= TempVillagePopulation ? "村莊" : "聚落";

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
            $"規模：{TierName(settlement, population)}｜建立至今 {age} 年（TEMP：{TempVillagePopulation} 人以上稱村莊）",
            "聚落小史："
        };
        var events = new List<(int Year, string Text)>();
        void Add(int year, string text) => events.Add((year, $"  第 {year} 年　{text}"));

        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        if (settlement.IsFounded)
            Add(YearOfWeek(settlement.FoundedWeek), $"{names.GetValueOrDefault(settlement.FoundedFromSettlementId, "未知聚落")}的開拓隊移居此地，建立聚落");
        else
            Add(1, "開局時已有人定居");

        var next = 0;
        long peak = 0;
        var peakWeek = 0;
        foreach (var report in world.WeekReports())
        {
            var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == settlementId);
            if (ledger is null) continue;
            var pop = ledger.PopulationClosing;
            while (next < TempMilestones.Count && pop >= TempMilestones[next])
            {
                var label = TempMilestones[next] == TempVillagePopulation ? "，由聚落發展為村莊" : "";
                Add(YearOfWeek(report.Week), $"人口首次達到 {TempMilestones[next]} 人{label}");
                next++;
            }
            if (pop > peak)
            {
                peak = pop;
                peakWeek = report.Week;
            }
        }

        foreach (var child in world.Sites.Settlements.Where(s => s.IsFounded && s.FoundedFromSettlementId == settlementId)
                     .OrderBy(s => s.FoundedWeek).ThenBy(s => s.Id))
            Add(YearOfWeek(child.FoundedWeek), $"分出開拓隊，建立{child.Name}");

        // 第 07-1 章 b 節：代表人物的就任與卸任。
        var known = new Dictionary<long, DemoRepresentative>();
        foreach (var report in world.WeekReports())
        {
            foreach (var r in report.Appointments.Where(r => r.SettlementId == settlementId))
            {
                known[r.Id] = r;
                Add(YearOfWeek(report.Week), $"{r.Name} 成為{DemoRepresentative.RoleName(r.Role)}");
            }
            foreach (var x in report.RepresentativeExits.Where(x => x.SettlementId == settlementId))
                if (known.TryGetValue(x.Id, out var who))
                    Add(YearOfWeek(report.Week), $"{DemoRepresentative.RoleName(who.Role)} {who.Name} 卸任：{x.Reason}");
        }

        if (peak > 0)
            Add(YearOfWeek(peakWeek), $"人口高峰 {peak} 人");
        if (settlement.IsRuin)
            Add(YearOfWeek(settlement.RuinedWeek), "人口歸零，成為遺跡");

        // 依年份排序（同年保持加入順序；OrderBy 是穩定排序）。
        return head.Concat(events.OrderBy(e => e.Year).Select(e => e.Text)).ToArray();
    }
}
