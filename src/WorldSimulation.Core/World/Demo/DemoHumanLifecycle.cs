using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #105 TEMP human cohort integration. A verified 39-week pregnancy is the ONLY
/// source of a new person; deaths are tracked independently as starvation vs age.
/// This first playable scenario deliberately disables migration until identity
/// tokens can travel with migration batches (other legacy migration modes remain).
/// </summary>
internal static class DemoHumanLifecycle
{
    public static DemoTempDemographyRules Rules => DemoTempDemographyRules.HumanFixture;

    /// <returns>
    /// 生死後的週結算、人口名冊，以及第 03 章 b 節的「事件 ID → 當事人」：出生＝新生兒＋母＋父；
    /// 自然死亡＝死者；本週聚落餓死事件＝本週被移除的餓死者（受孕、流產不入史）。
    /// </returns>
    public static (DemoWeeklyOutcome Outcome, DemoTempDemography Population, IReadOnlyDictionary<long, IReadOnlyList<long>> Participants) Apply(
        DemoWeeklyOutcome weekly, DemoTempDemography opening, long firstNewEventId = 1)
    {
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(opening);
        var report = weekly.Report;
        if (report.Migrations.Count != 0 || weekly.Migrations.Count != 0)
            throw new InvalidOperationException("TEMP pregnancy identities cannot be transported yet.");
        if (opening.LastWeek + 1 != report.Week)
            throw new InvalidOperationException("TEMP pregnancies must advance one committed week.");

        // The existing weekly food/death ledger provides actual starvation evidence.
        // Remove only the corresponding real identity tokens; never invent a survivor.
        var afterStarvation = opening;
        var starvedBySettlement = new Dictionary<int, long[]>();
        foreach (var ledger in report.Settlements)
        {
            if (ledger.Births != 0 || ledger.NaturalDeaths != 0)
                throw new InvalidOperationException("Expected pre-demography weekly food ledger.");
            if (ledger.Deaths > int.MaxValue)
                throw new InvalidOperationException("Too many people for TEMP identity cohort.");
            var ids = afterStarvation.People.Where(p => p.SettlementId == ledger.SettlementId)
                .OrderByDescending(p => p.AgeYearsAt(report.Week * 7))
                .ThenBy(p => p.Id)
                .Take((int)ledger.Deaths).Select(p => p.Id).ToArray();
            if (ids.Length != ledger.Deaths)
                throw new InvalidOperationException("Unverifiable starvation identity loss.");
            afterStarvation = afterStarvation.RemoveStarvation(ids, inTransit: false).Population;
            if (ids.Length > 0)
                starvedBySettlement[ledger.SettlementId] = ids;
        }

        // Pregnancy begins only with food that the Core's TRUE weekly account
        // actually covered. Birth and natural death occur at this week's end.
        // C04：新生兒接在本週結算的配號之後，向同一個私有分配器取號。
        var entityAllocator = EntityIdAllocator.Restore(weekly.EntityIds);
        var result = afterStarvation.AdvanceWeek(report.Week, id =>
        {
            var ledger = report.Settlements.Single(s => s.SettlementId == id);
            return ledger.FoodShortage == 0 && ledger.HungerDebtClosing == 0 && ledger.PopulationClosing > 0;
        }, Rules, entityAllocator);
        var next = weekly.Population.ToBuilder();
        var nextDebt = weekly.HungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(report.Settlements.Count);
        foreach (var ledger in report.Settlements)
        {
            var births = result.Events.Count(e => e.SettlementId == ledger.SettlementId
                && e.Kind == DemoTempDemographicKind.Birth);
            var natural = result.Events.Count(e => e.SettlementId == ledger.SettlementId
                && e.Kind == DemoTempDemographicKind.NaturalDeath);
            var closing = checked(ledger.PopulationClosing + births - natural);
            if (closing < 0 || result.Population.People.Count(p => p.SettlementId == ledger.SettlementId) != closing)
                throw new InvalidOperationException("Birth/death identities do not match world population.");
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

        var events = report.Events.ToList();
        var participants = new Dictionary<long, IReadOnlyList<long>>();
        foreach (var starved in events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath))
        {
            if (starvedBySettlement.Remove(starved.SettlementId, out var ids))
                participants[starved.EventId] = Array.AsReadOnly(ids.OrderBy(id => id).ToArray());
        }
        long eventId = events.Count == 0 ? firstNewEventId : checked(events[events.Count - 1].EventId + 1);
        foreach (var e in result.Events.Where(e => e.Kind is
            DemoTempDemographicKind.Birth or DemoTempDemographicKind.NaturalDeath))
        {
            var site = weekly.Sites.Settlements.Single(s => s.Id == e.SettlementId);
            string cause;
            DemoSettlementEventKind kind;
            if (e.Kind == DemoTempDemographicKind.Birth)
            {
                kind = DemoSettlementEventKind.Birth;
                cause = $"TEMP人族：雙親 #{e.MotherId}／#{e.FatherId} 已經受孕與懷孕 {Rules.GestationWeeks} 週，新生人口 #{e.PersonId}";
                participants[eventId] = new[] { e.PersonId, e.MotherId ?? 0, e.FatherId ?? 0 }.Where(id => id > 0).ToArray();
            }
            else
            {
                kind = DemoSettlementEventKind.NaturalDeath;
                var departed = afterStarvation.People.Single(p => p.Id == e.PersonId);
                cause = $"TEMP人族：人口 #{e.PersonId} 年齡 {departed.AgeYearsAt(report.Week * 7)}，依種族典型壽命 {Rules.TypicalLifespanYears} 的自然死亡風險";
                participants[eventId] = new[] { e.PersonId };
            }
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, site.Id, site.Name,
                kind, 1, cause, EventId: eventId++));
        }

        var entityIds = entityAllocator.Capture();
        var revised = report with
        {
            Settlements = ledgers.AsReadOnly(), Events = events.AsReadOnly(),
            EntityIdNextClosing = DemoEntityIds.NextValue(entityIds)
        };
        DemoWeeklySettlement.Verify(revised);
        return (weekly with
        {
            Report = revised, Population = next.ToImmutable(), HungerDebt = nextDebt.ToImmutable(),
            EntityIds = entityIds
        }, result.Population, participants);
    }

    /// <summary>Reconstruct pregnancy and age from the entire independently checked
    /// weekly ledger chain, and reject tampered births/deaths in a TEMP v2 save.</summary>
    public static (DemoTempDemography Population, IReadOnlyDictionary<long, IReadOnlyList<long>> Participants) Replay(
        DemoTempDemography opening, IReadOnlyList<DemoWeekReport> reports, DemoWorldSites sites)
    {
        var current = opening;
        var participants = new Dictionary<long, IReadOnlyList<long>>();
        long nextEventId = 1;
        foreach (var stored in reports)
        {
            if (stored.Settlements.Any(l => l.AdultWorkersOpening !=
                current.AdultsIn(l.SettlementId, Rules)))
                throw new InvalidDataException("TEMP adult workers differ from actual human ages.");
            // C04：新生兒的號碼位於本週配號區間的尾端；剝掉出生後，分配器回到出生前的位置重新發號。
            var births = stored.Settlements.Sum(l => l.Births);
            var stripped = stored with
            {
                EntityIdNextClosing = checked(stored.EntityIdNextClosing - births),
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
            var trial = Apply(temp, current, nextEventId);
            if (!trial.Outcome.Report.Settlements.SequenceEqual(stored.Settlements)
                || !trial.Outcome.Report.Events.SequenceEqual(stored.Events))
                throw new InvalidDataException("TEMP birth/death events do not match reconstructed pregnancies and age.");
            current = trial.Population;
            foreach (var (id, people) in trial.Participants)
                participants[id] = people;
            if (stored.Events.Count > 0) nextEventId = checked(stored.Events[^1].EventId + 1);
        }
        return (current, participants);
    }
}
