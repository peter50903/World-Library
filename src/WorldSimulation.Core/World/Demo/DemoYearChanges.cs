namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10a 章 V2：某一年年底的聚落與技術規模（年度圖表「聚落變化」系列）。</summary>
public sealed record DemoYearChange(int Year, long Settlements, long Villages, long KnowledgeLearned)
{
    public static DemoYearChange Empty(int year) => new(year, 0, 0, 0);
}

/// <summary>
/// 第 08-10a 章 V2：由聚落建立與遺跡週次、聚落小史的等級升降、知識樹的習得週次，算出每一年年底的
/// 有人聚落數（未成遺跡）、村莊數與技術習得累計（聚落×技術）。全部是既有存檔資料推得，存檔格式不變。
/// </summary>
public static class DemoYearChanges
{
    public static IReadOnlyDictionary<int, DemoYearChange> Of(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        static int YearOf(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));
        var lastYear = Math.Max(1, DemoSettlementHistory.YearOfDay(world.Snapshot().DaysElapsed));
        var settlements = world.Sites.Settlements;
        var tierLines = settlements.ToDictionary(s => s.Id,
            s => world.ChronicleOf(s.Id).Milestones.Where(DemoSettlementChronicle.IsTierChange)
                .Select(l => (Year: YearOf(l.Week), Up: l.Text.Contains(DemoSettlementChronicle.TierUpMarker, StringComparison.Ordinal)))
                .ToList());
        var learned = world.Knowledge.Records.Where(r => r.Learned).Select(r => YearOf(r.LearnedWeek)).ToList();

        var result = new Dictionary<int, DemoYearChange>();
        for (var year = 1; year <= lastYear; year++)
        {
            bool Alive(DemoSettlement s)
                => (!s.IsFounded || YearOf(s.FoundedWeek) <= year) && (!s.IsRuin || YearOf(s.RuinedWeek) > year);
            var alive = settlements.Where(Alive).ToList();
            // 第 08-11 章：五級——年底等級＝升級次數－降級次數；村莊以上都算。
            var villages = alive.Count(s => tierLines[s.Id].Where(l => l.Year <= year).Sum(l => l.Up ? 1 : -1) >= (int)DemoSettlementTier.Village);
            result[year] = new DemoYearChange(year, alive.Count, villages, learned.Count(y => y <= year));
        }
        return result;
    }
}
