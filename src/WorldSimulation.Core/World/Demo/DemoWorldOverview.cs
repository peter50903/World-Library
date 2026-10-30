using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-2 章：觀察者「世界總覽」——一眼看清目前世界的人口、村莊、遷徙、年齡結構、糧食與今年的生死。
/// 全部讀已提交狀態與已提交週報（今年＝<see cref="DemoYearLedger"/> 的最後一年，可能進行中）；只讀、不改世界。
/// Godot 右側清單上方與 CLI 共用 <see cref="Lines"/>。
/// </summary>
public sealed record DemoWorldOverview(
    string? EraName,
    int Year,
    long Population,
    long Settled,
    long InTransit,
    int Batches,
    int Villages,
    int FoundedVillages,
    int Ruins,
    long Capacity,
    long Children,
    long Adults,
    long Elders,
    long Couples,
    long Pregnant,
    long FoodStored,
    long FoodInTransit,
    int ShortVillages,
    long BirthsThisYear,
    long OldAgeDeathsThisYear,
    long StarvationDeathsThisYear,
    long EmigrantsThisYear,
    int FoundedThisYear)
{
    public static DemoWorldOverview Of(DemoObserverWorld world, int daysPerYear)
    {
        ArgumentNullException.ThrowIfNull(world);
        var day = world.Snapshot().DaysElapsed;
        // 第 04-1 章：365 日年用世界保存的年帳（讀檔後也完整）；其他曆法（測試用）由保留的週報彙整。
        var years = daysPerYear == DemoSettlementHistory.DaysPerYear
            ? world.YearSummaries()
            : DemoYearLedger.FromWeeks(world.WeekReports(), daysPerYear, day);
        var year = years.Count > 0 ? years[^1] : null;
        var active = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        var stats = world.WorldPopulationStats; // 第 07-1 章：群體人口統計
        var latest = world.LatestWeek;
        return new DemoWorldOverview(
            // 第 08-6 章：人類繁衍世界的時代來自知識樹（希望時代→開拓時代）。
            world.WeeklyConfig.EraName is null ? null : DemoKnowledge.EraName(world.Era),
            year?.Year ?? 1,
            world.WorldPopulation,
            world.SettledPopulation,
            world.PopulationInTransit,
            world.MigrationsInTransit().Count,
            active.Count,
            active.Count(s => s.IsFounded),
            world.Sites.Settlements.Count(s => s.IsRuin),
            world.MapCapacity.People,
            stats?.Children ?? 0,
            stats?.Adults ?? 0,
            stats?.Elders ?? 0,
            stats?.Couples ?? 0,
            stats?.Pregnant ?? 0,
            world.Sites.Settlements.Sum(s => world.SettlementFood(s.Id)),
            world.FoodInTransit,
            latest?.Settlements.Count(l => l.FoodShortage > 0) ?? 0,
            year?.Births ?? 0,
            year?.NaturalDeaths ?? 0,
            year?.StarvationDeaths ?? 0,
            year?.Emigrants ?? 0,
            year?.SettlementsFounded ?? 0)
        {
            // 第 08-11 章：各等級的有人聚落數（營地、村莊、城鎮、城市、首都）。
            Tiers = Enum.GetValues<DemoSettlementTier>().Select(t => active.Count(s => world.ChronicleOf(s.Id).Tier == t)).ToArray(),
            // 第 08-8 章 B1：各文化的估計人口（聚落人口×比例），由多到少。
            Cultures = world.Culture.Cultures
                .Select(c => (c.Name, People: active.Sum(s => world.SettlementPopulation(s.Id)
                    * (world.Culture.SharesOf(s.Id).FirstOrDefault(x => x.CultureId == c.Id)?.Permille ?? 0) / 1000)))
                .Where(x => x.People > 0).OrderByDescending(x => x.People).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray(),
            // 第 08-8 章 B3：各信仰的估計信徒（聚落人口×比例）與無信仰者。
            Faiths = world.Culture.Faith.Faiths.Where(f => f.ExtinctWeek == 0)
                .Select(f => (f.Name, People: active.Sum(s => world.SettlementPopulation(s.Id)
                    * (world.Culture.Faith.SharesOf(s.Id).FirstOrDefault(x => x.CultureId == f.Id)?.Permille ?? 0) / 1000)))
                .Where(x => x.People > 0).OrderByDescending(x => x.People).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray()
        };
    }

    /// <summary>第 08-11 章：各等級的有人聚落數，依 <see cref="DemoSettlementTier"/> 順序。</summary>
    public IReadOnlyList<int> Tiers { get; init; } = Array.Empty<int>();

    /// <summary>第 08-8 章 B1：各文化的估計人口（由多到少）。</summary>
    public IReadOnlyList<(string Name, long People)> Cultures { get; init; } = Array.Empty<(string, long)>();

    /// <summary>第 08-8 章 B3：各信仰的估計信徒（由多到少；失傳的不列）。</summary>
    public IReadOnlyList<(string Name, long People)> Faiths { get; init; } = Array.Empty<(string, long)>();

    /// <summary>總覽最多列幾個文化（信仰同）。</summary>
    public const int MaxCultureLines = 4;

    /// <summary>理論上限的達成百分比（沒有上限＝null）。</summary>
    public long? CapacityPercent => Capacity > 0 ? Population * 100 / Capacity : null;

    /// <summary>總覽各列（繁體中文；Godot 與 CLI 共用）。</summary>
    public IReadOnlyList<string> Lines()
    {
        string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var lines = new List<string>();
        lines.Add(EraName is null ? I($"第 {Year} 年") : I($"{EraName}｜第 {Year} 年"));
        lines.Add(Capacity > 0
            ? I($"世界人口 {Population}／理論上限 {Capacity}（{CapacityPercent}%）")
            : I($"世界人口 {Population}"));
        lines.Add(I($"定居 {Settled}｜遷徙途中 {InTransit}（{Batches} 支隊伍）"));
        lines.Add(I($"有人的聚落 {Villages}（其中新建 {FoundedVillages}）｜遺跡 {Ruins}"));
        if (Tiers.Count > 0)
            lines.Add(string.Join("｜", Enum.GetValues<DemoSettlementTier>()
                .Where(t => (int)t <= (int)DemoSettlementTier.Village || Tiers[(int)t] > 0)
                .Select(t => I($"{DemoSettlementHistory.TierName(t)} {Tiers[(int)t]}"))));
        if (Children + Adults + Elders > 0)
        {
            lines.Add(I($"兒童 {Children}｜成年 {Adults}｜老人 {Elders}"));
            lines.Add(I($"育齡伴侶約 {Couples} 對｜懷孕中 {Pregnant}"));
        }
        lines.Add(I($"存糧 {FoodStored}（途中攜帶 {FoodInTransit}）｜本週缺糧聚落 {ShortVillages}"));
        if (Cultures.Count > 0)
            lines.Add(I($"文化 {Cultures.Count} 種：") + string.Join("、", Cultures.Take(MaxCultureLines).Select(c => I($"{c.Name} 約 {c.People} 人")))
                + (Cultures.Count > MaxCultureLines ? "…" : ""));
        lines.Add(Faiths.Count == 0 ? "信仰：人人尚無信仰"
            : I($"信仰 {Faiths.Count} 種：") + string.Join("、", Faiths.Take(MaxCultureLines).Select(f => I($"{f.Name} 約 {f.People} 人")))
                + (Faiths.Count > MaxCultureLines ? "…" : ""));
        lines.Add(I($"今年：出生 {BirthsThisYear}｜老死 {OldAgeDeathsThisYear}｜餓死 {StarvationDeathsThisYear}｜外移 {EmigrantsThisYear}｜建村 {FoundedThisYear}"));
        return lines;
    }
}
