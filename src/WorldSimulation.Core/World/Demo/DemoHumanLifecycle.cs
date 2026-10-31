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
        DemoWeeklyOutcome weekly, DemoCohortDemography opening, DemoPopulationDraws draws, DemoNames names, long firstNewEventId = 1,
        DemoKnowledgeState? knowledge = null, Func<int, bool>? strongRule = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(draws);
        var report = weekly.Report;
        if (opening.LastWeek + 1 != report.Week)
            throw new InvalidOperationException("Cohort lifecycle must advance one committed week.");
        var day = report.Week * 7;
        if (report.Settlements.Any(l => l.Births != 0 || l.NaturalDeaths != 0)
            || report.Migrations.Any(m => m.Births != 0 || m.NaturalDeaths != 0))
            throw new InvalidOperationException("Cohort lifecycle expects a pre-demography ledger (no births or natural deaths yet).");

        // ⓪ 第 08-6 章 b 節：開拓時代起的餓死（週帳已決定人數；這裡決定是哪些年齡群，先老幼、後成年、代表人物最後）。
        var moved = opening;
        var elderAge = DemoSurvivalTempSettings.Current.ElderAgeYears;
        var starvedReps = new List<DemoRepresentative>();
        var starvedSplit = new Dictionary<int, (long Children, long Elders)>();
        foreach (var ledger in report.Settlements.Where(l => l.Deaths > 0))
        {
            var r = moved.Starve(ledger.SettlementId, 0, ledger.Deaths, day, Rules, elderAge);
            moved = r.Population;
            starvedReps.AddRange(r.StarvedRepresentatives);
            starvedSplit[ledger.SettlementId] = (r.Children, r.Elders);
        }

        // ① 出發：與結算時同一份規劃。② 抵達：隊伍的年齡群與懷孕併入目的聚落。
        foreach (var leg in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
        {
            var plan = DemoCohortMigration.Plan(moved, leg.FromSettlementId, leg.PopulationOpening, ModeOf(leg), day, Rules);
            if (plan.People != leg.PopulationOpening)
                throw new InvalidOperationException("Departing cohorts do not match the migration ledger.");
            moved = moved.Depart(leg.FromSettlementId, plan.Take, plan.Pregnancies, leg.BatchId);
        }
        // 途中餓死（開拓時代起）：在抵達之前，從隊伍的年齡群移除。
        foreach (var leg in report.Migrations.Where(m => !m.Departed && m.Deaths > 0).OrderBy(m => m.BatchId))
            moved = moved.Starve(leg.FromSettlementId, leg.BatchId, leg.Deaths, day, Rules, elderAge).Population;
        // 第 12-1 章 C2：遭野獸襲擊而死的人也從隊伍的年齡群移除（同餓死的挑選順序，簡化）。
        foreach (var leg in report.Migrations.Where(m => !m.Departed && m.AttackDeaths > 0).OrderBy(m => m.BatchId))
            moved = moved.Starve(leg.FromSettlementId, leg.BatchId, leg.AttackDeaths, day, Rules, elderAge).Population;
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

        // 第 07-1 章 b 節：代表人物本人的老死（以本人的實體 ID 抽 POP 流局部亂數），所屬年齡群至少死這麼多人。
        var repDeaths = moved.Representatives
            .Where(r => draws.Hit(r.Id, report.Week, DemoPopulationAction.NaturalDeath,
                Rules.WeeklyNaturalDeathChance(DemoCohortDemography.AgeAt(r.BirthYear, day))))
            .ToList();
        var minimumDeaths = repDeaths.GroupBy(r => (r.SettlementId, 0, r.BirthYear, r.Sex))
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // ③ 生死：受孕只在本週真實糧帳寬裕的聚落，數量不超過可預留的口糧（第 08-3 章）。
        var outcome = moved.AdvanceWeek(report.Week, id =>
        {
            var ledger = report.Settlements.SingleOrDefault(s => s.SettlementId == id);
            if (ledger is null || ledger.HungerDebtClosing != 0 || ledger.PopulationClosing <= 0
                || !DemoCrowding.Ample(report.Sources.Count > 0, ledger.FoodShortage, ledger.FoodConsumed + ledger.FoodShortage, ledger.SourceSlack))
                return 0;
            return report.Sources.Count == 0 ? long.MaxValue
                : DemoCrowding.ConceptionRoom(ledger, moved.StatsOf(id, 0, day, Rules).Pregnant);
        }, Rules, draws, minimumDeaths);

        // ④ 第 07-1 章 b 節：代表人物卸任（老死、聚落無人）與補任。
        var entityAllocator = EntityIdAllocator.Restore(weekly.EntityIds);
        var (population, appointments, exits) = Representatives(outcome.Population, repDeaths, report, weekly.Sites, day, entityAllocator, names,
            knowledge ?? DemoKnowledgeState.Initial, starvedReps, strongRule);

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
            if (closing < 0 || population.ResidentsOf(ledger.SettlementId) != closing)
                throw new InvalidOperationException("Cohort births/deaths do not match world population.");
            var excessDebt = closing == 0 ? ledger.HungerDebtClosing : 0L;
            var split = starvedSplit.GetValueOrDefault(ledger.SettlementId);
            var modified = ledger with
            {
                ChildStarvationDeaths = split.Children,
                ElderStarvationDeaths = split.Elders,
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
            if (closing < 0 || population.MembersOf(leg.BatchId) != closing)
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

        // 事件：第 04-2 章（使用者 2026-10-22：大多數人只是統計數字）每個聚落（或途中隊伍）每週的出生、自然死亡各合計一筆，
        // 人數＝事件人數（週帳對帳規則改為「有出生就恰好一筆、人數相等」）。
        var events = report.Events.ToList();
        long eventId = events.Count == 0 ? firstNewEventId : checked(events[^1].EventId + 1);
        foreach (var g in outcome.Events.Where(e => e.Kind is DemoTempDemographicKind.NaturalDeath or DemoTempDemographicKind.Birth)
                     .GroupBy(e => (Birth: e.Kind == DemoTempDemographicKind.Birth, e.BatchId, e.SettlementId))
                     .OrderBy(g => g.Key.Birth ? 1 : 0).ThenBy(g => g.Key.BatchId).ThenBy(g => g.Key.SettlementId))
        {
            var count = g.Sum(e => e.Count);
            if (count == 0) continue;
            var site = weekly.Sites.Settlements.Single(s => s.Id == g.Key.SettlementId);
            var travel = g.Key.BatchId > 0 ? $"（遷徙隊伍 #{g.Key.BatchId} 途中）" : "";
            var youngest = g.Min(e => e.Age);
            var oldest = g.Max(e => e.Age);
            var ages = youngest == oldest ? $"{youngest}" : $"{youngest}～{oldest}";
            var (kind, cause) = g.Key.Birth
                ? (DemoSettlementEventKind.Birth, $"TEMP人族：懷孕 {Rules.GestationWeeks} 週到期，新生兒出生{travel}")
                : (DemoSettlementEventKind.NaturalDeath, $"TEMP人族：年齡 {ages}，依種族典型壽命 {Rules.TypicalLifespanYears} 的自然死亡風險{travel}");
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, site.Id, site.Name,
                kind, count, cause, MigrationBatchId: g.Key.BatchId, EventId: eventId++));
        }

        var entityIds = entityAllocator.Capture();
        var revised = report with
        {
            Settlements = ledgers.AsReadOnly(), Events = events.AsReadOnly(), Migrations = legs.AsReadOnly(),
            Appointments = appointments, RepresentativeExits = exits,
            EntityIdNextClosing = DemoEntityIds.NextValue(entityIds)
        };
        DemoWeeklySettlement.Verify(revised);
        return (weekly with
        {
            Report = revised, Population = next.ToImmutable(), HungerDebt = nextDebt.ToImmutable(),
            Migrations = batches.ToImmutable(), EntityIds = entityIds
        }, population);
    }

    /// <summary>
    /// 第 07-1 章 b 節【TEMP 規則】代表人物：每個有人的聚落一位村長（最年長的成年人）。第 08-5 章：採集、漁撈代表取消
    /// （使用者：只有權力者與持有專業知識的人才算）。老死或聚落無人就卸任，下週結算由同規則補任。
    /// 補任者從年齡群中具名化（不是出生、不改人數），實體 ID 由世界唯一分配器依聚落、職位順序發號，名字依 ID 抽。
    /// </summary>
    private static (DemoCohortDemography Population, IReadOnlyList<DemoRepresentative> Appointments, IReadOnlyList<DemoRepresentativeExit> Exits)
        Representatives(DemoCohortDemography population, IReadOnlyList<DemoRepresentative> died, DemoWeekReport report,
            DemoWorldSites sites, int day, EntityIdAllocator ids, DemoNames names, DemoKnowledgeState knowledge,
            IReadOnlyList<DemoRepresentative> starved, Func<int, bool>? strongRule = null)
    {
        var exits = starved.Select(r => new DemoRepresentativeExit(r.Id, r.SettlementId, $"餓死（{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲）")).ToList();
        var current = new List<DemoRepresentative>();
        var diedIds = died.Select(r => r.Id).ToHashSet();
        foreach (var r in population.Representatives)
        {
            if (diedIds.Contains(r.Id))
                exits.Add(new DemoRepresentativeExit(r.Id, r.SettlementId, $"老死（{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲）"));
            else if (population.ResidentsOf(r.SettlementId) == 0)
                exits.Add(new DemoRepresentativeExit(r.Id, r.SettlementId, "聚落無人"));
            else
                current.Add(r);
        }

        var appointments = new List<DemoRepresentative>();
        foreach (var settlement in sites.Settlements.Where(s => !s.IsRuin && population.ResidentsOf(s.Id) > 0).OrderBy(s => s.Id))
        {
            long Free(DemoCohortGroup g) => g.Count - current.Count(r => r.SettlementId == settlement.Id && r.BirthYear == g.BirthYear && r.Sex == g.Sex);
            var adults = population.GroupsAt(settlement.Id, 0)
                .Where(g => DemoCohortDemography.AgeAt(g.BirthYear, day) >= Rules.AdultAgeYears).ToList();
            foreach (var role in Enum.GetValues<DemoRepresentativeRole>())
            {
                if (current.Any(r => r.SettlementId == settlement.Id && r.Role == role)) continue;
                // 第 08-6 章：專業知識持有者只在已習得該技術的聚落（農師＝農耕、木匠＝木工）。
                if (DemoRepresentative.TechnologyOf(role) is { } needed && !knowledge.Knows(settlement.Id, needed)) continue;
                var candidates = adults.Where(g => Free(g) > 0).ToList();
                // 第 08-8 章 B2：主要文化「強人統治」——村長改由最壯年者（未滿老年的成年人中最年長者）擔任；長老議事（預設）＝最年長者。
                var strong = role == DemoRepresentativeRole.Leader && strongRule?.Invoke(settlement.Id) == true
                    && candidates.Any(g => DemoCohortDemography.AgeAt(g.BirthYear, day) < DemoSurvivalTempSettings.Current.ElderAgeYears);
                if (role != DemoRepresentativeRole.Leader || strong)
                    candidates = candidates.Where(g => DemoCohortDemography.AgeAt(g.BirthYear, day) < DemoSurvivalTempSettings.Current.ElderAgeYears).ToList();
                var pick = role == DemoRepresentativeRole.Leader
                    ? candidates.OrderBy(g => g.BirthYear).ThenBy(g => g.Sex).FirstOrDefault()
                    : candidates.OrderByDescending(Free).ThenBy(g => g.BirthYear).ThenBy(g => g.Sex).FirstOrDefault();
                if (pick is null) continue;
                var id = DemoEntityIds.NextLong(ids);
                var person = new DemoRepresentative(id, settlement.Id, role, names.Person(id, pick.Sex), pick.Sex, pick.BirthYear, report.Week);
                current.Add(person);
                appointments.Add(person);
            }
        }
        return (population.WithRepresentatives(current), appointments, exits);
    }

    /// <summary>結算時的外移人數規劃——與 <see cref="Apply"/> 同一份 <see cref="DemoCohortMigration.Plan"/>。</summary>
    public static long PlanEmigrants(DemoCohortDemography opening, int week, int settlementId, long limit,
        DemoEmigrationMode mode = DemoEmigrationMode.Shortage, long starvationDeaths = 0)
    {
        ArgumentNullException.ThrowIfNull(opening);
        // 第 08-6 章 b 節：本週有人餓死時，從餓死後的年齡群規劃（與生死套用同一順序）。
        var survivors = starvationDeaths > 0
            ? opening.Starve(settlementId, 0, starvationDeaths, week * 7, Rules, DemoSurvivalTempSettings.Current.ElderAgeYears).Population
            : opening;
        return DemoCohortMigration.Plan(survivors, settlementId, limit, mode, week * 7, Rules).People;
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
        DemoWorldSites sites, DemoPopulationDraws draws, DemoNames names)
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
                Appointments = Array.Empty<DemoRepresentative>(),
                RepresentativeExits = Array.Empty<DemoRepresentativeExit>(),
                EntityIdNextClosing = checked(stored.EntityIdNextClosing - stored.Appointments.Count),
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
            var trial = Apply(temp, current, draws, names, nextEventId);
            if (!trial.Outcome.Report.Settlements.SequenceEqual(stored.Settlements)
                || !trial.Outcome.Report.Appointments.SequenceEqual(stored.Appointments)
                || !trial.Outcome.Report.RepresentativeExits.SequenceEqual(stored.RepresentativeExits)
                || trial.Outcome.Report.EntityIdNextClosing != stored.EntityIdNextClosing
                || !trial.Outcome.Report.Migrations.SequenceEqual(stored.Migrations)
                || !trial.Outcome.Report.Events.SequenceEqual(stored.Events))
                throw new InvalidDataException("TEMP birth/death events do not match the reconstructed cohorts.");
            current = trial.Population;
            if (stored.Events.Count > 0) nextEventId = checked(stored.Events[^1].EventId + 1);
        }
        return current;
    }
}
