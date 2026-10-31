namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10b 章：編年史條目的重要度（預設只顯示重大）。</summary>
public enum DemoChronicleLevel
{
    Major = 0,
    Normal = 1
}

/// <summary>第 08-10b 章：編年史條目的類型（介面上每一類可個別開關，參考 FMS 歷史面板）。</summary>
public enum DemoChronicleCategory
{
    Era = 0,
    Knowledge = 1,
    Founding = 2,
    Ruin = 3,
    Tier = 4,
    Farmland = 5,
    Famine = 6,
    Migration = 7,
    Hunger = 8,
    Representative = 9,
    Population = 10,
    Culture = 11,
    Faith = 12,
    Wilds = 13
}

/// <summary>條目文字的一段；<see cref="SettlementId"/>＞0 的段落是可點擊的聚落名稱（參考 CK3 名稱底線）。</summary>
public sealed record DemoChronicleSegment(string Text, int SettlementId = 0);

/// <summary>編年史的一筆（同一年內依週次排列）。</summary>
public sealed record DemoChronicleEntry(
    int Week,
    DemoChronicleLevel Level,
    DemoChronicleCategory Category,
    IReadOnlyList<DemoChronicleSegment> Segments,
    long EventId = 0)
{
    public string Text => string.Concat(Segments.Select(s => s.Text));

    /// <summary>點這一筆時定位的聚落（第一個名稱）；沒有＝0。</summary>
    public int SettlementId => Segments.FirstOrDefault(s => s.SettlementId > 0)?.SettlementId ?? 0;
}

/// <summary>編年史的一年：當年條目、出生與老死合計（統計級，每年一行；可展開各聚落）、年底時代。</summary>
public sealed record DemoChronicleYear(
    int Year,
    DemoEra Era,
    bool EraStarted,
    IReadOnlyList<DemoChronicleEntry> Entries,
    long Births,
    long NaturalDeaths,
    IReadOnlyDictionary<int, (long Births, long NaturalDeaths)> BySettlement)
{
    /// <summary>每年一行的統計（全世界合計）。</summary>
    public string StatisticLine => $"本年出生 {Births} 人、老死 {NaturalDeaths} 人";
}

/// <summary>
/// 第 08-10b 章：世界編年史。只讀既有事件（含讀檔前的較早事件）、聚落小史與代表人物紀錄，
/// 不改世界；同一世界讀檔前後結果相同。
/// </summary>
public static class DemoWorldChronicle
{
    /// <summary>【TEMP】超過幾年的舊年份在介面上預設折疊成十年摘要。</summary>
    public const int TempFoldAfterYears = 10;

    /// <summary>從第 1 年到目前年份，每年一筆（沒有條目的年份也列出，方便看出空白）。</summary>
    public static IReadOnlyList<DemoChronicleYear> Build(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var parents = world.Sites.Settlements.Where(s => s.IsFounded).ToDictionary(s => s.Id, s => s.FoundedFromSettlementId);
        DemoChronicleSegment Name(int id) => new(names.GetValueOrDefault(id, $"#{id}"), id);
        static DemoChronicleSegment T(string text) => new(text);
        static int YearOf(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));

        var entries = new List<(int Year, DemoChronicleEntry Entry)>();
        void Add(int week, DemoChronicleLevel level, DemoChronicleCategory category, long eventId, params DemoChronicleSegment[] segments)
            => entries.Add((YearOf(week), new DemoChronicleEntry(week, level, category, segments, eventId)));

        var events = world.SettlementEvents();
        var knownTechs = new HashSet<DemoTechnology>();
        var eraStarts = new Dictionary<int, DemoEra>();
        foreach (var e in events.OrderBy(e => e.Week).ThenBy(e => e.EventId))
        {
            switch (e.Kind)
            {
                case DemoSettlementEventKind.EraBegan:
                    eraStarts[YearOf(e.Week)] = (DemoEra)e.Count;
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Era, e.EventId,
                        T($"世界進入{DemoKnowledge.EraName((DemoEra)e.Count)}（"), Name(e.SettlementId), T(" 率先）"));
                    break;
                case DemoSettlementEventKind.KnowledgeLearned:
                    var tech = (DemoTechnology)e.Count;
                    var first = knownTechs.Add(tech);
                    Add(e.Week, first ? DemoChronicleLevel.Major : DemoChronicleLevel.Normal, DemoChronicleCategory.Knowledge, e.EventId,
                        Name(e.SettlementId), T($" 學會{DemoKnowledge.Name(tech)}{(first ? "（世界首次）" : "")}"));
                    break;
                case DemoSettlementEventKind.SettlementFounded:
                    var from = parents.GetValueOrDefault(e.SettlementId);
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Founding, e.EventId,
                        from > 0 ? new[] { Name(from), T(" 的開拓隊建立 "), Name(e.SettlementId), T($"（{e.Count} 人）") }
                                 : new[] { T("開拓隊建立 "), Name(e.SettlementId), T($"（{e.Count} 人）") });
                    break;
                case DemoSettlementEventKind.SettlementRuined:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Ruin, e.EventId,
                        Name(e.SettlementId), T(" 人口歸零，成為遺跡"));
                    break;
                case DemoSettlementEventKind.FarmlandCleared:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Farmland, e.EventId,
                        Name(e.SettlementId), T(" 開墾一塊農田"));
                    break;
                case DemoSettlementEventKind.FarmlandAbandoned:
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Farmland, e.EventId,
                        Name(e.SettlementId), T(" 附近的一塊農田無人耕作，開始荒廢"));
                    break;
                case DemoSettlementEventKind.FaithFounded:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Faith, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.FaithExtinct:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Faith, e.EventId, T(DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.BeastAttack:
                    // 第 12-1 章 C2：隊伍全數罹難是重大事件，其他為一般。
                    Add(e.Week, e.Cause.Contains("全數罹難", StringComparison.Ordinal) ? DemoChronicleLevel.Major : DemoChronicleLevel.Normal,
                        DemoChronicleCategory.Wilds, e.EventId, Name(e.SettlementId), T(" 的" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.CultureSplit:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Culture, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
            }
        }

        // 每年每聚落合計：餓死（重大）、缺糧週數、外移與遷入（一般）。
        foreach (var g in events.Where(e => e.Kind is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath
                         or DemoSettlementEventKind.Hunger or DemoSettlementEventKind.Emigration or DemoSettlementEventKind.Arrival)
                     .GroupBy(e => (Year: YearOf(e.Week), e.SettlementId)))
        {
            var lastWeek = g.Max(e => e.Week);
            var starved = g.Where(e => e.Kind is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath).Sum(e => e.Count);
            if (starved > 0)
                Add(lastWeek, DemoChronicleLevel.Major, DemoChronicleCategory.Famine, 0, Name(g.Key.SettlementId), T($" 本年餓死 {starved} 人"));
            var hungerWeeks = g.Count(e => e.Kind == DemoSettlementEventKind.Hunger);
            if (hungerWeeks > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Hunger, 0, Name(g.Key.SettlementId), T($" 本年缺糧 {hungerWeeks} 週"));
            var left = g.Where(e => e.Kind == DemoSettlementEventKind.Emigration).Sum(e => e.Count);
            if (left > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Migration, 0, Name(g.Key.SettlementId), T($" 本年外移 {left} 人"));
            var arrived = g.Where(e => e.Kind == DemoSettlementEventKind.Arrival).Sum(e => e.Count);
            if (arrived > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Migration, 0, Name(g.Key.SettlementId), T($" 本年遷入 {arrived} 人"));
        }

        // 聚落小史：等級升降（重大）、人口里程碑與代表人物就任／卸任（一般）。
        foreach (var s in world.Sites.Settlements)
        {
            var chronicle = world.ChronicleOf(s.Id);
            foreach (var line in chronicle.Milestones)
            {
                if (DemoSettlementChronicle.IsTierChange(line))
                    Add(line.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Tier, 0, Name(s.Id), T($" {line.Text}"));
                else if (DemoSettlementChronicle.IsPopulationMilestone(line))
                    Add(line.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Population, 0, Name(s.Id), T($" {line.Text}"));
            }
            foreach (var line in chronicle.Representatives)
                Add(line.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Representative, 0, Name(s.Id), T($"：{line.Text}"));
        }

        var stats = events.Where(e => e.Kind is DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath)
            .GroupBy(e => YearOf(e.Week))
            .ToDictionary(g => g.Key, g => g.GroupBy(e => e.SettlementId).ToDictionary(
                s => s.Key,
                s => (Births: s.Where(e => e.Kind == DemoSettlementEventKind.Birth).Sum(e => e.Count),
                      NaturalDeaths: s.Where(e => e.Kind == DemoSettlementEventKind.NaturalDeath).Sum(e => e.Count))));

        var lastYear = Math.Max(1, DemoSettlementHistory.YearOfDay(world.Snapshot().DaysElapsed));
        var byYear = entries.ToLookup(x => x.Year, x => x.Entry);
        var era = DemoEra.Hope;
        var result = new List<DemoChronicleYear>(lastYear);
        for (var year = 1; year <= lastYear; year++)
        {
            var started = eraStarts.TryGetValue(year, out var newEra);
            if (started) era = newEra;
            var bySettlement = stats.GetValueOrDefault(year) ?? new Dictionary<int, (long Births, long NaturalDeaths)>();
            result.Add(new DemoChronicleYear(
                year, era, started,
                byYear[year].OrderBy(e => e.Week).ThenBy(e => e.Level).ThenBy(e => e.Category).ToList().AsReadOnly(),
                bySettlement.Values.Sum(v => v.Births), bySettlement.Values.Sum(v => v.NaturalDeaths), bySettlement));
        }
        return result.AsReadOnly();
    }

    /// <summary>十年摘要（舊年份折疊時顯示）：只數重大條目。</summary>
    public static string DecadeSummary(IReadOnlyList<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        if (years.Count == 0) return "";
        var major = years.SelectMany(y => y.Entries).Where(e => e.Level == DemoChronicleLevel.Major).ToList();
        int Count(DemoChronicleCategory c) => major.Count(e => e.Category == c);
        var parts = new List<string>();
        void Part(DemoChronicleCategory c, string unit) { var n = Count(c); if (n > 0) parts.Add($"{unit} {n}"); }
        Part(DemoChronicleCategory.Era, "時代更替");
        Part(DemoChronicleCategory.Knowledge, "技術首創");
        Part(DemoChronicleCategory.Founding, "建立聚落");
        Part(DemoChronicleCategory.Ruin, "成為遺跡");
        Part(DemoChronicleCategory.Tier, "等級升降");
        Part(DemoChronicleCategory.Farmland, "開墾農田");
        Part(DemoChronicleCategory.Famine, "餓死紀錄");
        Part(DemoChronicleCategory.Culture, "文化分化");
        Part(DemoChronicleCategory.Faith, "信仰興衰");
        Part(DemoChronicleCategory.Wilds, "野外險境");
        var head = $"第 {years[0].Year}～{years[^1].Year} 年：";
        return head + (parts.Count == 0 ? "沒有重大事件" : string.Join("、", parts))
               + $"｜出生 {years.Sum(y => y.Births)}、老死 {years.Sum(y => y.NaturalDeaths)}";
    }

    /// <summary>介面預設折疊的十年分段：除了最近 <see cref="TempFoldAfterYears"/> 年，其餘每 10 年一段。</summary>
    public static IReadOnlyList<(int FromYear, int ToYear)> FoldedDecades(int currentYear)
    {
        var list = new List<(int, int)>();
        for (var from = 1; from + 9 <= currentYear - TempFoldAfterYears; from += 10)
            list.Add((from, from + 9));
        return list;
    }
}
