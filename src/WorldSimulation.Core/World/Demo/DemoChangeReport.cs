namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10a 章 V0：每 10 年一段的世界變化統計（參考 WorldBox 統計比較）。只讀世界，不改規則。
/// 重大事件依 08-10b：時代開始、技術在世界首次習得、聚落建立、成為遺跡、等級升降、開墾農田；
/// 其他聚落之後才習得同一技術記為「傳入」（另列，不算重大）。
/// </summary>
public sealed record DemoChangeDecade(
    int FromYear,
    int ToYear,
    int Founded,
    int Ruined,
    int TierUps,
    int TierDowns,
    int FirstLearned,
    int SpreadLearned,
    int Farmland,
    int EraChanges,
    long Population,
    int LivingSettlements,
    int Villages,
    int Lineages,
    int LargestLineage,
    DemoEra Era,
    int Towns = 0,
    long LargestPopulation = 0,
    int CultureSplits = 0,
    int Cultures = 0)
{
    /// <summary>這一段的重大事件數（08-10b 的重大級）。</summary>
    public int Major => Founded + Ruined + TierUps + TierDowns + FirstLearned + Farmland + EraChanges + CultureSplits;

    public string Describe()
        => $"第 {FromYear}～{ToYear} 年｜重大 {Major}（建村 {Founded}、遺跡 {Ruined}、升村莊 {TierUps}、降級 {TierDowns}、"
           + $"技術首創 {FirstLearned}、開墾 {Farmland}、時代 {EraChanges}、文化分化 {CultureSplits}）｜傳入 {SpreadLearned}"
           + $"｜期末 人口 {Population}、有人聚落 {LivingSettlements}、村莊 {Villages}（城鎮以上 {Towns}、最大聚落 {LargestPopulation} 人）、家系 {Lineages}（最大 {LargestLineage} 聚落）、文化 {Cultures}"
           + $"｜{DemoKnowledge.EraName(Era)}";
}

/// <summary>第 08-10a 章 V0：逐年觀察世界、每 10 年結算一段 <see cref="DemoChangeDecade"/>。</summary>
public sealed class DemoChangeTracker
{
    /// <summary>每段年數。</summary>
    public const int DecadeYears = 10;

    private readonly Dictionary<int, DemoSettlementTier> _tiers = new();
    private readonly HashSet<DemoTechnology> _known = new();
    private readonly List<DemoChangeDecade> _decades = new();
    private int _lastWeek;
    private int _fromYear = 1;
    private int _lastYear;
    private int _founded, _ruined, _ups, _downs, _first, _spread, _farmland, _eras, _splits;

    // 第 08-11 章：遺跡成因——每個聚落每年的人口流動，聚落成為遺跡時回看最後 10 年。
    private sealed class YearFlow
    {
        public long Opening = -1, Births, NaturalDeaths, StarvationDeaths, Immigrants, Mate, Crowding, Shortage, Founding, OtherOut;
        public int ShortageWeeks, WaterShortageWeeks;
    }

    private readonly Dictionary<int, SortedDictionary<int, YearFlow>> _flows = new();
    private readonly List<string> _ruinCauses = new();

    /// <summary>第 08-11 章：每個成為遺跡的聚落一行（最後 10 年的出生、死亡、遷出原因與缺糧週數）。</summary>
    public IReadOnlyList<string> RuinCauses => _ruinCauses;

    private YearFlow Flow(int settlementId, int year)
    {
        if (!_flows.TryGetValue(settlementId, out var years))
            _flows[settlementId] = years = new SortedDictionary<int, YearFlow>();
        if (!years.TryGetValue(year, out var flow))
            years[year] = flow = new YearFlow();
        return flow;
    }

    public IReadOnlyList<DemoChangeDecade> Decades => _decades;

    /// <summary>第一項技術在世界上首次習得的年份；尚未出現＝null。</summary>
    public int? FirstTechnologyYear { get; private set; }

    /// <summary>開拓時代開始的年份；尚未開始＝null。</summary>
    public int? PioneerEraYear { get; private set; }

    /// <summary>某聚落的家系源頭：沿「由哪個聚落的開拓隊建立」往上追到開局聚落。</summary>
    public static int LineageRoot(DemoWorldSites sites, int settlementId) => DemoLineageMap.LineageRoot(sites, settlementId);

    /// <summary>每年年底呼叫一次（year 從 1 起連續）。</summary>
    public void ObserveYearEnd(DemoObserverWorld world, int year)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (year != _lastYear + 1)
            throw new ArgumentOutOfRangeException(nameof(year), "年份必須從 1 起連續。");
        _lastYear = year;

        foreach (var week in world.WeekReports().Where(w => w.Week > _lastWeek))
        {
            foreach (var l in week.Settlements)
            {
                var f = Flow(l.SettlementId, year);
                if (f.Opening < 0) f.Opening = l.PopulationOpening;
                f.Births += l.Births;
                f.NaturalDeaths += l.NaturalDeaths;
                f.StarvationDeaths += Math.Max(0, l.Deaths - l.NaturalDeaths);
                f.Immigrants += l.Immigrants;
                if (l.FoodShortage > 0) f.ShortageWeeks++;
                if (l.WaterShortage > 0) f.WaterShortageWeeks++;
            }
            foreach (var e in week.Events)
            {
                if (e.Kind == DemoSettlementEventKind.Emigration)
                {
                    var f = Flow(e.SettlementId, year);
                    if (e.Cause.StartsWith("婚嫁", StringComparison.Ordinal)) f.Mate += e.Count;
                    else if (e.Cause.StartsWith("人口接近", StringComparison.Ordinal)) f.Crowding += e.Count;
                    else if (e.Cause.Contains("缺糧", StringComparison.Ordinal)) f.Shortage += e.Count;
                    else if (e.Cause.Contains("開拓", StringComparison.Ordinal)) f.Founding += e.Count;
                    else f.OtherOut += e.Count;
                }
                if (e.Kind == DemoSettlementEventKind.SettlementRuined)
                    _ruinCauses.Add(DescribeRuin(world, e.SettlementId, year));
                switch (e.Kind)
                {
                    case DemoSettlementEventKind.SettlementFounded: _founded++; break;
                    case DemoSettlementEventKind.SettlementRuined: _ruined++; break;
                    case DemoSettlementEventKind.FarmlandCleared: _farmland++; break;
                    case DemoSettlementEventKind.CultureSplit: _splits++; break;
                    case DemoSettlementEventKind.EraBegan:
                        _eras++;
                        PioneerEraYear ??= year;
                        break;
                    case DemoSettlementEventKind.KnowledgeLearned:
                        if (_known.Add((DemoTechnology)e.Count))
                        {
                            _first++;
                            FirstTechnologyYear ??= year;
                        }
                        else
                        {
                            _spread++;
                        }
                        break;
                }
            }
            _lastWeek = week.Week;
        }

        // 等級升降：與去年底比較（年內又升又降只看得到淨變化）。
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
        {
            var tier = world.ChronicleOf(s.Id).Tier;
            var before = _tiers.GetValueOrDefault(s.Id, DemoSettlementTier.Camp);
            if (tier > before) _ups++;
            else if (tier < before) _downs++;
            _tiers[s.Id] = tier;
        }

        if (year % DecadeYears == 0)
            Close(world, year);
    }

    /// <summary>結束時把未滿 10 年的最後一段也結算。</summary>
    public void Finish(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_lastYear >= _fromYear)
            Close(world, _lastYear);
    }

    private void Close(DemoObserverWorld world, int toYear)
    {
        var living = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        var lineages = living.GroupBy(s => LineageRoot(world.Sites, s.Id)).Select(g => g.Count()).ToList();
        _decades.Add(new DemoChangeDecade(
            _fromYear, toYear, _founded, _ruined, _ups, _downs, _first, _spread, _farmland, _eras,
            world.WorldPopulation, living.Count,
            living.Count(s => world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.Village),
            lineages.Count, lineages.Count == 0 ? 0 : lineages.Max(), world.Era,
            living.Count(s => world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.Town),
            living.Count == 0 ? 0 : living.Max(s => world.SettlementPopulation(s.Id)), _splits,
            living.Select(s => world.Culture.MainCultureOf(s.Id)).Where(c => c is not null).Distinct().Count()));
        _splits = 0;
        _fromYear = toYear + 1;
        _founded = _ruined = _ups = _downs = _first = _spread = _farmland = _eras = 0;
    }

    private string DescribeRuin(DemoObserverWorld world, int settlementId, int year)
    {
        var site = world.Sites.Settlements.First(s => s.Id == settlementId);
        var years = _flows.GetValueOrDefault(settlementId)?.Where(kv => kv.Key > year - 10).Select(kv => kv.Value).ToList() ?? new List<YearFlow>();
        long Sum(Func<YearFlow, long> f) => years.Sum(f);
        var founded = site.IsFounded ? DemoSettlementHistory.YearOfDay(checked(site.FoundedWeek * 7)) : 0;
        var cell = world.Map.GetCell(site.Column, site.Row);
        var water = DemoMigrationRoutes.HasWaterAccess(world.Map, new DemoCellRef(site.Column, site.Row)) ? "鄰水" : "不鄰水";
        return $"遺跡 {site.Name}（#{site.Id}，第 {year} 年；{(site.IsFounded ? $"第 {founded} 年由 #{site.FoundedFromSettlementId} 開拓" : "開局聚落")}；{cell.Terrain}、{water}）"
               + $"｜最後 10 年期初 {years.FirstOrDefault(y => y.Opening >= 0)?.Opening ?? 0} 人"
               + $"｜出生 {Sum(y => y.Births)}、老死 {Sum(y => y.NaturalDeaths)}、餓死 {Sum(y => y.StarvationDeaths)}、遷入 {Sum(y => y.Immigrants)}"
               + $"｜遷出 婚嫁 {Sum(y => y.Mate)}、擁擠 {Sum(y => y.Crowding)}、缺糧 {Sum(y => y.Shortage)}、開拓 {Sum(y => y.Founding)}、其他 {Sum(y => y.OtherOut)}"
               + $"｜缺糧週 {years.Sum(y => y.ShortageWeeks)}、缺水週 {years.Sum(y => y.WaterShortageWeeks)}";
    }

    /// <summary>總結：第一項技術與開拓時代的年份、每段重大事件的平均與最少。</summary>
    public string Summary()
    {
        static string Year(int? y) => y is int v ? $"第 {v} 年" : "未出現";
        var full = _decades.Where(d => d.ToYear - d.FromYear + 1 == DecadeYears).ToList();
        var stats = full.Count == 0 ? "（不足 10 年）"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"平均 {full.Average(d => d.Major):F1}、最少 {full.Min(d => d.Major)}");
        return $"總結：第一項技術 {Year(FirstTechnologyYear)}｜開拓時代 {Year(PioneerEraYear)}｜每 10 年重大事件 {stats}";
    }
}
