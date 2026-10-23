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
    int FoundedThisYear,
    int Cultures = 0,
    int Faiths = 0,
    long Believers = 0,
    long Grazers = 0,
    long Predators = 0)
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
            year?.SettlementsFounded ?? 0,
            world.Culture.Cultures.Count, // 第 08-9 章 B1
            world.Faith.Faiths.Count(f => !f.Extinct), // 第 08-9 章 B3：存續的信仰與估計信徒
            world.Sites.Settlements.Where(s => !s.IsRuin).Sum(s => world.SettlementPopulation(s.Id) * (1000 - world.Faith.PermilleOf(s.Id, 0)) / 1000),
            world.Wildlife.TotalGrazers, world.Wildlife.TotalPredators); // 第 08-9 章 C1
    }

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
        lines.Add(Cultures > 0
            ? I($"村莊 {Villages}（其中新建 {FoundedVillages}）｜遺跡 {Ruins}｜文化 {Cultures} 個｜信仰 {Faiths} 個（信徒約 {Believers} 人）")
            : I($"村莊 {Villages}（其中新建 {FoundedVillages}）｜遺跡 {Ruins}"));
        if (Children + Adults + Elders > 0)
        {
            lines.Add(I($"兒童 {Children}｜成年 {Adults}｜老人 {Elders}"));
            lines.Add(I($"育齡伴侶約 {Couples} 對｜懷孕中 {Pregnant}"));
        }
        if (Grazers + Predators > 0)
            lines.Add(I($"野生動物：草食獸 {Grazers} 頭｜掠食獸 {Predators} 頭"));
        lines.Add(I($"存糧 {FoodStored}（途中攜帶 {FoodInTransit}）｜本週缺糧村莊 {ShortVillages}"));
        lines.Add(I($"今年：出生 {BirthsThisYear}｜老死 {OldAgeDeathsThisYear}｜餓死 {StarvationDeathsThisYear}｜外移 {EmigrantsThisYear}｜建村 {FoundedThisYear}"));
        return lines;
    }
}
