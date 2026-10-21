using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #105 TEMP human cohort integration. A verified 39-week pregnancy is the ONLY
/// source of a new person; deaths are tracked independently as starvation vs age.
/// 第 07 章 b 節：遷徙隊伍以家庭為單位攜帶身分；途中餓死、出生、自然死亡都對應真實個體。
/// </summary>
internal static class DemoHumanLifecycle
{
    public static DemoTempDemographyRules Rules => DemoTempDemographyRules.HumanFixture;

    /// <returns>
    /// 生死後的週結算、人口名冊，以及第 03 章 b 節的「事件 ID → 當事人」：出生＝新生兒＋母＋父；
    /// 自然死亡＝死者；本週聚落餓死事件＝本週被移除的餓死者（受孕、流產不入史）。
    /// </returns>
    public static (DemoWeeklyOutcome Outcome, DemoTempDemography Population, IReadOnlyDictionary<long, IReadOnlyList<long>> Participants) Apply(
        DemoWeeklyOutcome weekly, DemoTempDemography opening, DemoPopulationDraws draws, long firstNewEventId = 1)
    {
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(draws);
        var report = weekly.Report;
        if (opening.LastWeek + 1 != report.Week)
            throw new InvalidOperationException("TEMP pregnancies must advance one committed week.");
        var day = report.Week * 7;

        // ① 途中餓死（第 07 章 b 節）：隊伍帳的死亡人數＝從隊伍成員中依年齡權重抽出的真實身分。
        var afterStarvation = opening;
        var starvedByBatch = new Dictionary<int, long[]>();
        foreach (var leg in report.Migrations)
        {
            if (leg.Births != 0 || leg.NaturalDeaths != 0 || leg.HungerDebtRemovedByNaturalDeath != 0)
                throw new InvalidOperationException("Expected pre-demography migration ledger.");
            if (leg.Departed || leg.Deaths == 0) continue;
            var ids = StarvedAmong(afterStarvation.People.Where(p => p.BatchId == leg.BatchId), leg.Deaths, report.Week, draws);
            afterStarvation = afterStarvation.RemoveStarvation(ids, inTransit: true).Population;
            starvedByBatch[leg.BatchId] = ids;
        }

        // ② 聚落餓死：已有的週帳提供真實證據，只移除對應的真實身分，不憑空生出倖存者。
        var starvedBySettlement = new Dictionary<int, long[]>();
        foreach (var ledger in report.Settlements)
        {
            if (ledger.Births != 0 || ledger.NaturalDeaths != 0 || ledger.ChildStarvationDeaths != 0 || ledger.ElderStarvationDeaths != 0)
                throw new InvalidOperationException("Expected pre-demography weekly food ledger.");
            if (ledger.Deaths > int.MaxValue)
                throw new InvalidOperationException("Too many people for TEMP identity cohort.");
            var ids = StarvedIn(afterStarvation, ledger.SettlementId, ledger.Deaths, report.Week, draws);
            afterStarvation = afterStarvation.RemoveStarvation(ids, inTransit: false).Population;
            if (ids.Length > 0)
                starvedBySettlement[ledger.SettlementId] = ids;
        }

        // ③ 出發：整戶挑選（與結算時的人數規劃同一函式），身分加入隊伍。④ 抵達：隊伍成員定居目的聚落。
        var moved = afterStarvation;
        foreach (var leg in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
        {
            var ids = DemoFamilyMigration.Choose(moved.People.Where(p => p.IsSettled && p.SettlementId == leg.FromSettlementId),
                leg.PopulationOpening, day, Rules, requireCouple: leg.Founding);
            if (ids.Length != leg.PopulationOpening)
                throw new InvalidOperationException("Departing families do not match the migration ledger.");
            moved = moved.Depart(ids, leg.BatchId);
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

        // ⑤ 受孕只在本週真實糧帳足夠的聚落；出生與自然死亡在週末（定居者記聚落帳，途中者記隊伍帳）。
        // C04：新生兒接在本週結算的配號之後，向同一個私有分配器取號。
        var entityAllocator = EntityIdAllocator.Restore(weekly.EntityIds);
        var result = moved.AdvanceWeek(report.Week, id =>
        {
            var ledger = report.Settlements.Single(s => s.SettlementId == id);
            return ledger.FoodShortage == 0 && ledger.HungerDebtClosing == 0 && ledger.PopulationClosing > 0;
        }, Rules, draws, entityAllocator);
        var next = weekly.Population.ToBuilder();
        var nextDebt = weekly.HungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(report.Settlements.Count);
        foreach (var ledger in report.Settlements)
        {
            var births = result.Events.Count(e => e.SettlementId == ledger.SettlementId && e.BatchId is null
                && e.Kind == DemoTempDemographicKind.Birth);
            var natural = result.Events.Count(e => e.SettlementId == ledger.SettlementId && e.BatchId is null
                && e.Kind == DemoTempDemographicKind.NaturalDeath);
            var closing = checked(ledger.PopulationClosing + births - natural);
            if (closing < 0 || result.Population.ResidentsOf(ledger.SettlementId) != closing)
                throw new InvalidOperationException("Birth/death identities do not match world population.");
            var excessDebt = closing == 0 ? ledger.HungerDebtClosing : 0L;
            var starved = starvedBySettlement.GetValueOrDefault(ledger.SettlementId) ?? Array.Empty<long>();
            var starvedPeople = opening.People.Where(p => starved.Contains(p.Id)).ToArray();
            var modified = ledger with
            {
                ChildStarvationDeaths = starvedPeople.Count(p => !p.IsAdultAt(day, Rules)),
                ElderStarvationDeaths = starvedPeople.Count(p => p.AgeYearsAt(day) >= DemoSurvivalTempSettings.Current.ElderAgeYears),
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
            var births = result.Events.Count(e => e.BatchId == leg.BatchId && e.Kind == DemoTempDemographicKind.Birth);
            var natural = result.Events.Count(e => e.BatchId == leg.BatchId && e.Kind == DemoTempDemographicKind.NaturalDeath);
            if (births == 0 && natural == 0) { legs.Add(leg); continue; }
            var closing = checked(leg.PopulationClosing + births - natural);
            if (closing < 0 || result.Population.MembersOf(leg.BatchId) != closing)
                throw new InvalidOperationException("Travel birth/death identities do not match the migration ledger.");
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

        var events = report.Events.ToList();
        var participants = new Dictionary<long, IReadOnlyList<long>>();
        foreach (var starved in events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath))
        {
            if (starvedBySettlement.Remove(starved.SettlementId, out var ids))
                participants[starved.EventId] = Array.AsReadOnly(ids.OrderBy(id => id).ToArray());
        }
        foreach (var starved in events.Where(e => e.Kind == DemoSettlementEventKind.MigrantStarvationDeath))
        {
            if (starvedByBatch.Remove(starved.MigrationBatchId, out var ids))
                participants[starved.EventId] = Array.AsReadOnly(ids.OrderBy(id => id).ToArray());
        }
        // 第 07 章 b 節：外移事件的當事人＝整戶出發的成員。
        foreach (var emigration in events.Where(e => e.Kind == DemoSettlementEventKind.Emigration))
            participants[emigration.EventId] = Array.AsReadOnly(moved.People.Where(p => p.BatchId == emigration.MigrationBatchId
                && report.Migrations.Any(m => m.Departed && m.BatchId == emigration.MigrationBatchId)).Select(p => p.Id).ToArray());
        long eventId = events.Count == 0 ? firstNewEventId : checked(events[events.Count - 1].EventId + 1);
        foreach (var e in result.Events.Where(e => e.Kind is
            DemoTempDemographicKind.Birth or DemoTempDemographicKind.NaturalDeath))
        {
            var site = weekly.Sites.Settlements.Single(s => s.Id == e.SettlementId);
            var travel = e.BatchId is int batchId ? $"（遷徙隊伍 #{batchId} 途中）" : "";
            string cause;
            DemoSettlementEventKind kind;
            if (e.Kind == DemoTempDemographicKind.Birth)
            {
                kind = DemoSettlementEventKind.Birth;
                cause = $"TEMP人族：雙親 #{e.MotherId}／#{e.FatherId} 已經受孕與懷孕 {Rules.GestationWeeks} 週，新生人口 #{e.PersonId}{travel}";
                participants[eventId] = new[] { e.PersonId, e.MotherId ?? 0, e.FatherId ?? 0 }.Where(id => id > 0).ToArray();
            }
            else
            {
                kind = DemoSettlementEventKind.NaturalDeath;
                var departed = moved.People.Single(p => p.Id == e.PersonId);
                cause = $"TEMP人族：人口 #{e.PersonId} 年齡 {departed.AgeYearsAt(day)}，依種族典型壽命 {Rules.TypicalLifespanYears} 的自然死亡風險{travel}";
                participants[eventId] = new[] { e.PersonId };
            }
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, site.Id, site.Name,
                kind, 1, cause, MigrationBatchId: e.BatchId ?? 0, EventId: eventId++));
        }

        var entityIds = entityAllocator.Capture();
        var revised = report with
        {
            Settlements = ledgers.AsReadOnly(), Events = events.AsReadOnly(), Migrations = legs.AsReadOnly(),
            EntityIdNextClosing = DemoEntityIds.NextValue(entityIds)
        };
        DemoWeeklySettlement.Verify(revised);
        return (weekly with
        {
            Report = revised, Population = next.ToImmutable(), HungerDebt = nextDebt.ToImmutable(),
            EntityIds = entityIds, Migrations = batches.ToImmutable()
        }, result.Population, participants);
    }

    /// <summary>
    /// 第 07 章 b 節：結算時的整戶外移人數規劃——與 <see cref="Apply"/> 同一套規則（先依本週餓死人數移除餓死者，
    /// 再整戶挑選），保證結算的出發人數就是名冊實際出發的人數。
    /// </summary>
    public static long PlanEmigrants(DemoTempDemography opening, int week, int settlementId, long deaths, long limit,
        DemoPopulationDraws draws, bool founding = false)
    {
        ArgumentNullException.ThrowIfNull(opening);
        var starved = StarvedIn(opening, settlementId, deaths, week, draws).ToHashSet();
        var residents = opening.People.Where(p => p.IsSettled && p.SettlementId == settlementId && !starved.Contains(p.Id));
        return DemoFamilyMigration.Choose(residents, limit, week * 7, Rules, requireCouple: founding).Length;
    }

    /// <summary>第 06 章 d 節：聚落餓死名單（定居者，依年齡權重）。</summary>
    private static long[] StarvedIn(DemoTempDemography population, int settlementId, long deaths, int week, DemoPopulationDraws draws)
        => StarvedAmong(population.People.Where(p => p.IsSettled && p.SettlementId == settlementId), deaths, week, draws);

    /// <summary>
    /// 第 06 章 d 節：餓死名單以 POP 流（實體局部亂數）依年齡權重抽取——兒童與老人權重較高【TEMP】；
    /// 鍵值＝原始抽樣值÷權重（以整數交叉相乘比較，可重現），取最小的 deaths 人。
    /// </summary>
    private static long[] StarvedAmong(IEnumerable<DemoTempPerson> candidates, long deaths, int week, DemoPopulationDraws draws)
    {
        var day = week * 7;
        var ids = candidates
            .Select(p => (Person: p, Raw: draws.Raw(p.Id, week, DemoPopulationAction.StarvationSelection),
                Weight: (ulong)StarvationWeight(p, day)))
            .OrderBy(x => x, StarvationOrder.Instance)
            .Take(checked((int)deaths)).Select(x => x.Person.Id).ToArray();
        if (ids.Length != deaths)
            throw new InvalidOperationException("Unverifiable starvation identity loss.");
        return ids;
    }

    /// <summary>Reconstruct pregnancy and age from the entire independently checked
    /// weekly ledger chain, and reject tampered births/deaths in a TEMP v2 save.</summary>
    public static (DemoTempDemography Population, IReadOnlyDictionary<long, IReadOnlyList<long>> Participants) Replay(
        DemoTempDemography opening, IReadOnlyList<DemoWeekReport> reports, DemoWorldSites sites, DemoPopulationDraws draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        var current = opening;
        var participants = new Dictionary<long, IReadOnlyList<long>>();
        long nextEventId = 1;
        foreach (var stored in reports)
        {
            if (stored.Settlements.Any(l => l.AdultWorkersOpening !=
                current.AdultsIn(l.SettlementId, Rules)))
                throw new InvalidDataException("TEMP adult workers differ from actual human ages.");
            // C04：新生兒的號碼位於本週配號區間的尾端；剝掉出生後，分配器回到出生前的位置重新發號。
            var births = stored.Settlements.Sum(l => l.Births) + stored.Migrations.Sum(m => m.Births);
            var stripped = stored with
            {
                // 第 07 章 b 節：途中隊伍帳也剝掉出生／自然死亡。
                Migrations = stored.Migrations.Select(m => m with
                {
                    Births = 0,
                    NaturalDeaths = 0,
                    PopulationClosing = m.PopulationClosing - m.Births + m.NaturalDeaths,
                    HungerDebtClosing = m.HungerDebtClosing + m.HungerDebtRemovedByNaturalDeath,
                    HungerDebtRemovedByNaturalDeath = 0
                }).ToArray(),
                EntityIdNextClosing = checked(stored.EntityIdNextClosing - births),
                Events = stored.Events.Where(e => e.Kind is not
                    (DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath)).ToArray(),
                Settlements = stored.Settlements.Select(l => l with
                {
                    ChildStarvationDeaths = 0,
                    ElderStarvationDeaths = 0,
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
                throw new InvalidDataException("TEMP birth/death events do not match reconstructed pregnancies and age.");
            current = trial.Population;
            foreach (var (id, people) in trial.Participants)
                participants[id] = people;
            if (stored.Events.Count > 0) nextEventId = checked(stored.Events[^1].EventId + 1);
        }
        return (current, participants);
    }

    /// <summary>第 06 章 d 節：餓死抽樣權重（兒童、成人、老人；config/demo/survival-temp.json【TEMP】）。</summary>
    private static int StarvationWeight(DemoTempPerson person, int day)
    {
        var settings = DemoSurvivalTempSettings.Current;
        if (!person.IsAdultAt(day, Rules)) return settings.ChildStarvationWeight;
        return person.AgeYearsAt(day) >= settings.ElderAgeYears ? settings.ElderStarvationWeight : settings.AdultStarvationWeight;
    }

    /// <summary>鍵值 Raw÷Weight 由小到大（以 UInt128 交叉相乘比較），同值依人 ID。</summary>
    private sealed class StarvationOrder : IComparer<(DemoTempPerson Person, ulong Raw, ulong Weight)>
    {
        public static readonly StarvationOrder Instance = new();

        public int Compare((DemoTempPerson Person, ulong Raw, ulong Weight) a, (DemoTempPerson Person, ulong Raw, ulong Weight) b)
        {
            var left = (UInt128)a.Raw * b.Weight;
            var right = (UInt128)b.Raw * a.Weight;
            var c = left.CompareTo(right);
            return c != 0 ? c : a.Person.Id.CompareTo(b.Person.Id);
        }
    }
}
