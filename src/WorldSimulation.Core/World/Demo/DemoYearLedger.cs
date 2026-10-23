namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08 章 b 節：由「已提交的週報」彙整年帳（觀察者年度圖表與 CLI 年報共用的資料來源）。
/// 只讀週報，不讀也不改世界狀態：
/// 期初＝上一年最後一次週結算的期末（第 1 年＝第 1 週的期初）；期末＝本年最後一次週結算的期末；
/// 週結算以外的日子不改聚落、資源點或隊伍（見 <see cref="DemoCenturyRun"/>），所以與年界當日的世界狀態相同。
/// 週結算歸屬結算日所在年份（第 7N 日）。進行中的年份也列出，<see cref="DemoYearSummary.LastDay"/>＝目前世界日。
/// 輸出與 <see cref="DemoCenturyRun.Run"/> 逐年逐欄相同（DemoYearLedgerTests 比對），
/// 所以圖表數字＝無畫面長測年報數字。
/// </summary>
public static class DemoYearLedger
{
    /// <summary>
    /// 彙整第 1 年到 <paramref name="daysElapsed"/> 所在年份的年帳。
    /// <paramref name="daysElapsed"/> 為 0 或沒有任何週報時回傳空清單。
    /// </summary>
    public static IReadOnlyList<DemoYearSummary> FromWeeks(IReadOnlyList<DemoWeekReport> weeks, int daysPerYear, int daysElapsed)
        => FromWeeks(weeks, daysPerYear, daysElapsed, firstYear: 1);

    /// <summary>
    /// 第 04-1 章：從第 <paramref name="firstYear"/> 年起彙整（讀檔後只保留近期週報，較早的年帳另存）。
    /// <paramref name="weeks"/> 必須從該年第一次週結算開始；期初＝第一份週報的期初。
    /// </summary>
    public static IReadOnlyList<DemoYearSummary> FromWeeks(IReadOnlyList<DemoWeekReport> weeks, int daysPerYear, int daysElapsed, int firstYear)
    {
        if (firstYear < 1)
            throw new ArgumentOutOfRangeException(nameof(firstYear));
        ArgumentNullException.ThrowIfNull(weeks);
        if (daysPerYear < 1)
            throw new ArgumentOutOfRangeException(nameof(daysPerYear), "Days per year must be positive.");
        if (daysElapsed <= 0 || weeks.Count == 0)
            return Array.Empty<DemoYearSummary>();

        var first = weeks[0];
        var state = new Totals(
            first.Settlements.Sum(l => l.PopulationOpening),
            first.Settlements.Sum(l => l.FoodOpening),
            first.Settlements.Sum(l => l.ReserveOpening),
            0, 0, first.Settlements.Count(l => l.PopulationOpening > 0), first.Settlements.Count);

        var years = (daysElapsed - 1) / daysPerYear + 1;
        var result = new List<DemoYearSummary>(Math.Max(0, years - firstYear + 1));
        var index = 0;
        for (var year = firstYear; year <= years; year++)
        {
            var firstDay = checked((year - 1) * daysPerYear + 1);
            var lastDay = Math.Min(checked(year * daysPerYear), daysElapsed);
            var opening = state;
            var a = new Accumulator();
            while (index < weeks.Count && weeks[index].WorldTime.TotalDays <= lastDay)
            {
                var report = weeks[index++];
                a.Add(report);
                state = Totals.After(report);
            }

            result.Add(new DemoYearSummary(
                year, lastDay, a.Weeks,
                opening.Population, a.Births, a.Deaths, state.Population,
                opening.Food, a.Produced, a.Consumed, a.Shortage, state.Food,
                opening.Reserve, a.Regenerated, a.Harvested, state.Reserve,
                a.HungerEvents, a.StarvationEvents, a.DeathsInEvents, a.RecoveryEvents,
                state.Inhabited,
                a.Emigrants, a.Immigrants, state.InTransit,
                a.EmigrationEvents, a.ArrivalEvents, a.Founded, state.Settlements,
                firstDay,
                a.NaturalDeaths, a.BirthEvents, a.NaturalDeathEvents,
                a.Spoiled, a.Other));
        }

        return result.AsReadOnly();
    }

    /// <summary>某年是否已完整結束（進行中的年份 LastDay 小於年末）。</summary>
    public static bool IsComplete(DemoYearSummary year, int daysPerYear)
    {
        ArgumentNullException.ThrowIfNull(year);
        return year.LastDay == (long)year.Year * daysPerYear;
    }

    /// <summary>世界總量（各聚落＋途中隊伍只計一次；途中人口另列）。</summary>
    private readonly record struct Totals(long Population, long Food, long Reserve, long InTransit, long FoodInTransit,
        int Inhabited, int Settlements)
    {
        public static Totals After(DemoWeekReport report)
        {
            var travelling = report.Migrations.Where(m => !m.Arrived).ToList();
            var transit = travelling.Sum(m => m.PopulationClosing);
            var transitFood = travelling.Sum(m => m.FoodClosing);
            return new Totals(
                checked(report.Settlements.Sum(l => l.PopulationClosing) + transit),
                checked(report.Settlements.Sum(l => l.FoodClosing) + transitFood),
                report.Settlements.Sum(l => l.ReserveClosing),
                transit, transitFood,
                report.Settlements.Count(l => l.PopulationClosing > 0),
                report.Settlements.Count);
        }
    }

    /// <summary>一年內各週流量的加總（與 <see cref="DemoCenturyRun"/> 同一套欄位）。</summary>
    private sealed class Accumulator
    {
        public int Weeks, HungerEvents, StarvationEvents, RecoveryEvents, EmigrationEvents, ArrivalEvents, Founded;
        public long Births, Deaths, NaturalDeaths, Produced, Consumed, Shortage, Spoiled, Other, Regenerated, Harvested;
        public long Emigrants, Immigrants, DeathsInEvents, BirthEvents, NaturalDeathEvents;

        public void Add(DemoWeekReport report)
        {
            Weeks++;
            foreach (var l in report.Settlements)
            {
                Births = checked(Births + l.Births);
                Deaths = checked(Deaths + l.Deaths);
                NaturalDeaths = checked(NaturalDeaths + l.NaturalDeaths);
                Produced = checked(Produced + l.FoodProduced);
                Consumed = checked(Consumed + l.FoodConsumed);
                Shortage = checked(Shortage + l.FoodShortage);
                Spoiled = checked(Spoiled + l.FoodSpoiled);
                Other = checked(Other + l.FoodFromDead + l.FoodFromHunting); // 第 08-9 章 B3b、C1
                Regenerated = checked(Regenerated + l.Regenerated);
                Harvested = checked(Harvested + l.Harvested);
                Emigrants = checked(Emigrants + l.Emigrants);
                Immigrants = checked(Immigrants + l.Immigrants);
            }

            foreach (var m in report.Migrations)
            {
                Births = checked(Births + m.Births);
                Deaths = checked(Deaths + m.Deaths + m.NaturalDeaths);
                NaturalDeaths = checked(NaturalDeaths + m.NaturalDeaths);
                Consumed = checked(Consumed + m.FoodConsumed);
                Shortage = checked(Shortage + m.FoodShortage);
            }

            foreach (var e in report.Events)
            {
                switch (e.Kind)
                {
                    case DemoSettlementEventKind.Hunger:
                        HungerEvents++;
                        break;
                    case DemoSettlementEventKind.StarvationDeath:
                    case DemoSettlementEventKind.MigrantStarvationDeath:
                        StarvationEvents++;
                        DeathsInEvents = checked(DeathsInEvents + e.Count);
                        break;
                    case DemoSettlementEventKind.HungerRecovered:
                        RecoveryEvents++;
                        break;
                    case DemoSettlementEventKind.Emigration:
                        EmigrationEvents++;
                        break;
                    case DemoSettlementEventKind.Arrival:
                        ArrivalEvents++;
                        break;
                    case DemoSettlementEventKind.SettlementFounded:
                        Founded++;
                        break;
                    case DemoSettlementEventKind.Birth:
                        BirthEvents = checked(BirthEvents + e.Count);
                        break;
                    case DemoSettlementEventKind.NaturalDeath:
                        NaturalDeathEvents = checked(NaturalDeathEvents + e.Count);
                        break;
                }
            }
        }
    }
}
