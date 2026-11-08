namespace WorldSimulation.Core.World.Demo;

/// <summary>人口金字塔的一層：年齡段（例如「20～29 歲」）、男、女。</summary>
public sealed record DemoPyramidBand(string Label, long Male, long Female)
{
    public long Total => Male + Female;
}

/// <summary>
/// 第 08-18b 章第 5 節介面第二批：聚落頁的人口金字塔與排行（參考十字軍之王、Dwarf Fortress 的人口頁）。
/// 金字塔依住在聚落裡的年齡群（不含路上的隊伍）每 <see cref="TempBandYears"/> 歲一層、<see cref="TempTopBand"/> 歲以上併成一層，由年輕到年老；
/// 排行依有人的聚落比人口、技術數與建立的早晚。純函式，只讀世界，不改世界。
/// </summary>
public static class DemoPopulationPyramid
{
    /// <summary>【TEMP】每幾歲一層、幾歲以上併成最上一層。</summary>
    public const int TempBandYears = 10, TempTopBand = 70;

    public static IReadOnlyList<DemoPyramidBand> Of(IEnumerable<DemoCohortGroup> groups, int settlementId, int day)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var bands = Enumerable.Range(0, TempTopBand / TempBandYears + 1).Select(_ => (Male: 0L, Female: 0L)).ToArray();
        foreach (var g in groups.Where(g => g.SettlementId == settlementId && g.BatchId == 0))
        {
            var band = Math.Min(bands.Length - 1, DemoCohortDemography.AgeAt(g.BirthYear, day) / TempBandYears);
            bands[band] = g.Sex == DemoTempSex.Male ? (bands[band].Male + g.Count, bands[band].Female) : (bands[band].Male, bands[band].Female + g.Count);
        }
        return bands.Select((b, i) => new DemoPyramidBand(
            i == bands.Length - 1 ? $"{TempTopBand} 歲以上" : $"{i * TempBandYears}～{i * TempBandYears + TempBandYears - 1} 歲", b.Male, b.Female)).ToList();
    }

    /// <summary>聚落在全世界有人的聚落裡的名次（人口、技術、建立早晚）；遺跡或沒人回傳空。</summary>
    public static IReadOnlyList<string> Rankings(DemoObserverWorld world, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var living = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        if (living.All(s => s.Id != settlementId))
            return Array.Empty<string>();
        int Rank(Func<DemoSettlement, long> key, bool high) => 1 + living.Count(s => s.Id != settlementId
            && (high ? key(s) > key(living.First(x => x.Id == settlementId)) : key(s) < key(living.First(x => x.Id == settlementId))));
        var total = living.Count;
        return new[]
        {
            $"人口：全世界第 {Rank(s => world.SettlementPopulation(s.Id), true)} 名（共 {total} 個聚落）",
            $"技術：全世界第 {Rank(s => world.Knowledge.KnownBy(s.Id).Count, true)} 名",
            $"歷史：全世界第 {Rank(s => s.Id, false)} 古老"
        };
    }
}
