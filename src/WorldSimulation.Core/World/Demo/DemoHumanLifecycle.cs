using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #105 TEMP 人類生死與週帳整合。第 07-1 章 a 節起人口是群體統計（<see cref="DemoCohortDemography"/>）：
/// 出生只來自到期的懷孕、死亡只依年齡風險（希望時代只有老年），遷徙依 <see cref="DemoCohortMigration"/> 規劃的年齡群同行。
/// 結算（DemoWeeklySettlement）先決定糧食、遷徙人數，這裡再套用到群體人口並補上出生／自然死亡的週帳與事件。
/// </summary>
internal static class DemoHumanLifecycle
{
    /// <summary>
    /// 人族規則。第 08-2 章：人類繁衍目前只出現在希望時代，只有老年（TEMP 老年起算年齡，survival-temp.json）才會自然死亡；
    /// 39C 年輕族群的自然死亡風險在希望時代不套用。
    /// </summary>
    public static DemoTempDemographyRules Rules { get; } = DemoTempDemographyRules.HumanFixture with
    {
        NaturalDeathMinAgeYears = DemoSurvivalTempSettings.Current.ElderAgeYears
    };

    private static DemoEmigrationMode ModeOf(DemoMigrationWeekLedger leg)
        => leg.MateSeeking ? DemoEmigrationMode.Mate : leg.Founding ? DemoEmigrationMode.Founding : DemoEmigrationMode.Shortage;

    /// <returns>生死後的週結算與群體人口（第 07-1 章起沒有個體當事人；事件只記聚落與人數）。</returns>
    public static (DemoWeeklyOutcome Outcome, DemoCohortDemography Population) Apply(
        DemoWeeklyOutcome weekly, DemoCohortDemography opening, DemoPopulationDraws draws, long firstNewEventId = 1)
    {
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(draws);
        var report = weekly.Report;
        if (opening.LastWeek + 1 != report.Week)
            throw new InvalidOperationException("Cohort lifecycle must advance one committed week.");
        var day = report.Week * 7;
        if (report.Settlements.Any(l => l.Births != 0 || l.NaturalDeaths != 0 || l.Deaths != 0)
            || report.Migrations.Any(m => m.Births != 0 || m.NaturalDeaths != 0 || m.Deaths != 0))
            throw new InvalidOperationException("Cohort lifecycle expects a pre-demography Hope-era ledger (no starvation deaths).");

        // ① 出發：與結算時同一份規劃。② 抵達：隊伍的年齡群與懷孕併入目的聚落。
        var moved = opening;
        foreach (var leg in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
        {
            var plan = DemoCohortMigration.Plan(moved, leg.FromSettlementId, leg.PopulationOpening, ModeOf(leg), day, Rules);
            if (plan.People != leg.PopulationOpening)
                throw new InvalidOperationException("Departing cohorts do not match the migration ledger.");
            moved = moved.Depart(leg.FromSettlementId, plan.Take, plan.Pregnancies, leg.BatchId);
        }
        foreach (var leg in report.Migrations.Where(m => m.Arrived).OrderBy(m => m.BatchId))
        {
            if (moved.MembersOf(leg.BatchId) != leg.PopulationClosing)
                throw new InvalidOperationException("Arriving migrants do not match the migration ledger.");
            moved = moved.Arrive(leg.BatchId, leg.ToSettlementId);
        }
        foreach (var leg in report.Migrations.Where(m => !m.Arrived))
        {
            if (moved.MembersOf(leg.BatchId) != leg.PopulationClosing)
                throw new InvalidOperationException("Travelling migrants do not match the migration ledger.");
        }

        // ③ 生死：受孕只在本週真實糧帳寬裕的聚落，數量不超過可預留的口糧（第 08-3 章）。
        var outcome = moved.AdvanceWeek(report.Week, id =>
        {
            var ledger = report.Settlements.SingleOrDefault(s => s.SettlementId == id);
            if (ledger is null || ledger.HungerDebtClosing != 0 || ledger.PopulationClosing <= 0
                || !DemoCrowding.Ample(report.Sources.Count > 0, ledger.FoodShortage, ledger.FoodConsumed + ledger.FoodShortage, ledger.SourceSlack))
                return 0;
            return report.Sources.Count == 0 ? long.MaxValue
                : DemoCrowding.ConceptionRoom(ledger, moved.StatsOf(id, 0, day, Rules).Pregnant);
        }, Rules, draws);

        long Sum(DemoTempDemographicKind kind, int settlement, int batch)
            => outcome.Events.Where(e => e.Kind == kind && e.SettlementId == settlement && e.BatchId == batch).Sum(e => e.Count);

        var next = weekly.Population.ToBuilder();
        var nextDebt = weekly.HungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(report.Settlements.Count);
        foreach (var ledger in report.Settlements)
        {
            var births = Sum(DemoTempDemographicKind.Birth, ledger.SettlementId, 0);
            var natural = Sum(DemoTempDemographicKind.NaturalDeath, ledger.SettlementId, 0);
            var closing = checked(ledger.PopulationClosing + births - natural);
            if (closing < 0 || outcome.Population.ResidentsOf(ledger.SettlementId) != closing)
                throw new InvalidOperationException("Cohort births/deaths do not match world population.");
            var excessDebt = closing == 0 ? ledger.HungerDebtClosing : 0L;
            var modified = ledger with
            {
                Births = births,
                Deaths = checked(ledger.Deaths + natural),
                NaturalDeaths = natural,
                PopulationClosing = closing,
                HungerDebtRemovedByNaturalDeath = excessDebt,
                HungerDebtClosing = ledger.HungerDebtClosing - excessDebt
            };
            ledgers.Add(modified);
            next[ledger.SettlementId] = closing;
            nextDebt[ledger.SettlementId] = modified.HungerDebtClosing;
        }

        // 途中隊伍帳：出生、自然死亡；全員自然死亡的隊伍剩餘飢餓債沒有承擔者，隨死者移除，隊伍解散。
        var legs = new List<DemoMigrationWeekLedger>(report.Migrations.Count);
        var batches = weekly.Migrations.ToBuilder();
        foreach (var leg in report.Migrations)
        {
            if (leg.Arrived) { legs.Add(leg); continue; }
            var births = Sum(DemoTempDemographicKind.Birth, leg.FromSettlementId, leg.BatchId);
            var natural = Sum(DemoTempDemographicKind.NaturalDeath, leg.FromSettlementId, leg.BatchId);
            if (births == 0 && natural == 0) { legs.Add(leg); continue; }
            var closing = checked(leg.PopulationClosing + births - natural);
            if (closing < 0 || outcome.Population.MembersOf(leg.BatchId) != closing)
                throw new InvalidOperationException("Travel birth/death cohorts do not match the migration ledger.");
            var excessDebt = closing == 0 ? leg.HungerDebtClosing : 0L;
            var revisedLeg = leg with
            {
                Births = births, NaturalDeaths = natural, PopulationClosing = closing,
                HungerDebtRemovedByNaturalDeath = excessDebt, HungerDebtClosing = leg.HungerDebtClosing - excessDebt
            };
            legs.Add(revisedLeg);
            var index = batches.FindIndex(b => b.Id == leg.BatchId);
            if (index >= 0)
            {
                if (closing == 0) batches.RemoveAt(index);
                else batches[index] = batches[index] with { People = closing, HungerDebt = revisedLeg.HungerDebtClosing };
            }
        }

        // 事件：每位新生兒、每位自然死亡者各一筆（第 07 章 b 節的對帳規則不變；群體版沒有個體編號）。
        var events = report.Events.ToList();
        long eventId = events.Count == 0 ? firstNewEventId : checked(events[^1].EventId + 1);
        foreach (var e in outcome.Events.Where(e => e.Kind is DemoTempDemographicKind.NaturalDeath or DemoTempDemographicKind.Birth)
                     .OrderBy(e => e.Kind == DemoTempDemographicKind.Birth ? 1 : 0).ThenBy(e => e.BatchId).ThenBy(e => e.SettlementId).ThenByDescending(e => e.Age))
        {
            var site = weekly.Sites.Settlements.Single(s => s.Id == e.SettlementId);
            var travel = e.BatchId > 0 ? $"（遷徙隊伍 #{e.BatchId} 途中）" : "";
            var (kind, cause) = e.Kind == DemoTempDemographicKind.Birth
                ? (DemoSettlementEventKind.Birth, $"TEMP人族：懷孕 {Rules.GestationWeeks} 週到期，新生兒出生{travel}")
                : (DemoSettlementEventKind.NaturalDeath, $"TEMP人族：年齡 {e.Age}，依種族典型壽命 {Rules.TypicalLifespanYears} 的自然死亡風險{travel}");
            for (var i = 0; i < e.Count; i++)
                events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, site.Id, site.Name,
                    kind, 1, cause, MigrationBatchId: e.BatchId, EventId: eventId++));
        }

        var revised = report with
        {
            Settlements = ledgers.AsReadOnly(), Events = events.AsReadOnly(), Migrations = legs.AsReadOnly()
        };
        DemoWeeklySettlement.Verify(revised);
        return (weekly with
        {
            Report = revised, Population = next.ToImmutable(), HungerDebt = nextDebt.ToImmutable(),
            Migrations = batches.ToImmutable()
        }, outcome.Population);
    }

    /// <summary>結算時的外移人數規劃——與 <see cref="Apply"/> 同一份 <see cref="DemoCohortMigration.Plan"/>。</summary>
    public static long PlanEmigrants(DemoCohortDemography opening, int week, int settlementId, long limit,
        DemoEmigrationMode mode = DemoEmigrationMode.Shortage)
    {
        ArgumentNullException.ThrowIfNull(opening);
        return DemoCohortMigration.Plan(opening, settlementId, limit, mode, week * 7, Rules).People;
    }

    /// <summary>第 08-1 章：各聚落婚嫁市場（找不到伴侶的單身者性別、未配對育齡男女數）。</summary>
    public static IReadOnlyDictionary<int, DemoMateMarket> MateMarkets(DemoCohortDemography population, DemoWorldSites sites, int week)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(sites);
        var day = week * 7;
        return sites.Settlements.ToDictionary(s => s.Id, s =>
        {
            var st = population.StatsOf(s.Id, 0, day, Rules);
            DemoTempSex? seeker = st.UnpairedWomen > 0 && st.UnpairedMen == 0 ? DemoTempSex.Female
                : st.UnpairedMen > 0 && st.UnpairedWomen == 0 ? DemoTempSex.Male : null;
            return new DemoMateMarket(seeker, checked((int)st.UnpairedWomen), checked((int)st.UnpairedMen));
        });
    }

    /// <summary>讀檔：由開局群體與已驗證的週帳鏈逐週重播生死，任何出生／自然死亡帳或事件被竄改都拒絕。</summary>
    public static DemoCohortDemography Replay(DemoCohortDemography opening, IReadOnlyList<DemoWeekReport> reports,
        DemoWorldSites sites, DemoPopulationDraws draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        var current = opening;
        long nextEventId = 1;
        foreach (var stored in reports)
        {
            if (stored.Settlements.Any(l => l.AdultWorkersOpening != current.AdultsIn(l.SettlementId, Rules)))
                throw new InvalidDataException("TEMP adult workers differ from the cohort ages.");
            var stripped = stored with
            {
                Migrations = stored.Migrations.Select(m => m with
                {
                    Births = 0,
                    NaturalDeaths = 0,
                    PopulationClosing = m.PopulationClosing - m.Births + m.NaturalDeaths,
                    HungerDebtClosing = m.HungerDebtClosing + m.HungerDebtRemovedByNaturalDeath,
                    HungerDebtRemovedByNaturalDeath = 0
                }).ToArray(),
                Events = stored.Events.Where(e => e.Kind is not
                    (DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath)).ToArray(),
                Settlements = stored.Settlements.Select(l => l with
                {
                    Births = 0,
                    Deaths = l.Deaths - l.NaturalDeaths,
                    NaturalDeaths = 0,
                    PopulationClosing = l.PopulationClosing - l.Births + l.NaturalDeaths,
                    HungerDebtClosing = l.HungerDebtClosing + l.HungerDebtRemovedByNaturalDeath,
                    HungerDebtRemovedByNaturalDeath = 0
                }).ToArray()
            };
            var temp = new DemoWeeklyOutcome(stripped,
                ImmutableSortedDictionary<int, long>.Empty,
                ImmutableSortedDictionary<int, long>.Empty,
                stripped.Settlements.ToImmutableSortedDictionary(l => l.SettlementId, l => l.PopulationClosing),
                stripped.Settlements.ToImmutableSortedDictionary(l => l.SettlementId, l => l.HungerDebtClosing),
                ImmutableList<DemoMigrationBatch>.Empty,
                new EntityIdAllocatorSnapshot(EntityIdAllocatorSnapshot.CurrentVersion,
                    checked((ulong)stripped.EntityIdNextClosing), false), sites);
            var trial = Apply(temp, current, draws, nextEventId);
            if (!trial.Outcome.Report.Settlements.SequenceEqual(stored.Settlements)
                || !trial.Outcome.Report.Migrations.SequenceEqual(stored.Migrations)
                || !trial.Outcome.Report.Events.SequenceEqual(stored.Events))
                throw new InvalidDataException("TEMP birth/death events do not match the reconstructed cohorts.");
            current = trial.Population;
            if (stored.Events.Count > 0) nextEventId = checked(stored.Events[^1].EventId + 1);
        }
        return current;
    }
}
