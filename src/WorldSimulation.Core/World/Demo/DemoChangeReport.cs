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
    DemoEra Era)
{
    /// <summary>這一段的重大事件數（08-10b 的重大級）。</summary>
    public int Major => Founded + Ruined + TierUps + TierDowns + FirstLearned + Farmland + EraChanges;

    public string Describe()
        => $"第 {FromYear}～{ToYear} 年｜重大 {Major}（建村 {Founded}、遺跡 {Ruined}、升村莊 {TierUps}、降級 {TierDowns}、"
           + $"技術首創 {FirstLearned}、開墾 {Farmland}、時代 {EraChanges}）｜傳入 {SpreadLearned}"
           + $"｜期末 人口 {Population}、有人聚落 {LivingSettlements}、村莊 {Villages}、家系 {Lineages}（最大 {LargestLineage} 聚落）"
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
    private int _founded, _ruined, _ups, _downs, _first, _spread, _farmland, _eras;

    public IReadOnlyList<DemoChangeDecade> Decades => _decades;

    /// <summary>第一項技術在世界上首次習得的年份；尚未出現＝null。</summary>
    public int? FirstTechnologyYear { get; private set; }

    /// <summary>開拓時代開始的年份；尚未開始＝null。</summary>
    public int? PioneerEraYear { get; private set; }

    /// <summary>某聚落的家系源頭：沿「由哪個聚落的開拓隊建立」往上追到開局聚落。</summary>
    public static int LineageRoot(DemoWorldSites sites, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(sites);
        var byId = sites.Settlements.ToDictionary(s => s.Id);
        var id = settlementId;
        var guard = 0;
        while (guard++ <= byId.Count
               && byId.TryGetValue(id, out var current)
               && current.IsFounded
               && byId.ContainsKey(current.FoundedFromSettlementId))
        {
            id = current.FoundedFromSettlementId;
        }
        return id;
    }

    /// <summary>每年年底呼叫一次（year 從 1 起連續）。</summary>
    public void ObserveYearEnd(DemoObserverWorld world, int year)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (year != _lastYear + 1)
            throw new ArgumentOutOfRangeException(nameof(year), "年份必須從 1 起連續。");
        _lastYear = year;

        foreach (var week in world.WeekReports().Where(w => w.Week > _lastWeek))
        {
            foreach (var e in week.Events)
            {
                switch (e.Kind)
                {
                    case DemoSettlementEventKind.SettlementFounded: _founded++; break;
                    case DemoSettlementEventKind.SettlementRuined: _ruined++; break;
                    case DemoSettlementEventKind.FarmlandCleared: _farmland++; break;
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
            var before = _tiers.GetValueOrDefault(s.Id, DemoSettlementTier.Settlement);
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
            living.Count(s => world.ChronicleOf(s.Id).Tier == DemoSettlementTier.Village),
            lineages.Count, lineages.Count == 0 ? 0 : lineages.Max(), world.Era));
        _fromYear = toYear + 1;
        _founded = _ruined = _ups = _downs = _first = _spread = _farmland = _eras = 0;
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
