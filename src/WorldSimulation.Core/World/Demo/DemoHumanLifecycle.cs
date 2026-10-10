using System.Collections.Immutable;

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

    public static (DemoWeeklyOutcome Outcome, DemoTempDemography Population) Apply(
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
        }

        // Pregnancy begins only with food that the Core's TRUE weekly account
        // actually covered. Birth and natural death occur at this week's end.
        var result = afterStarvation.AdvanceWeek(report.Week, id =>
        {
            var ledger = report.Settlements.Single(s => s.SettlementId == id);
            return ledger.FoodShortage == 0 && ledger.HungerDebtClosing == 0 && ledger.PopulationClosing > 0;
        }, Rules);
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
            }
            else
            {
                kind = DemoSettlementEventKind.NaturalDeath;
                var departed = afterStarvation.People.Single(p => p.Id == e.PersonId);
                cause = $"TEMP人族：人口 #{e.PersonId} 年齡 {departed.AgeYearsAt(report.Week * 7)}，依種族典型壽命 {Rules.TypicalLifespanYears} 的自然死亡風險";
            }
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, site.Id, site.Name,
                kind, 1, cause, EventId: eventId++));
        }

        var revised = report with { Settlements = ledgers.AsReadOnly(), Events = events.AsReadOnly() };
        DemoWeeklySettlement.Verify(revised);
        return (weekly with
        {
            Report = revised, Population = next.ToImmutable(), HungerDebt = nextDebt.ToImmutable()
        }, result.Population);
    }

    /// <summary>Reconstruct pregnancy and age from the entire independently checked
    /// weekly ledger chain, and reject tampered births/deaths in a TEMP v2 save.</summary>
    public static DemoTempDemography Replay(
        DemoTempDemography opening, IReadOnlyList<DemoWeekReport> reports, DemoWorldSites sites)
    {
        var current = opening;
        long nextEventId = 1;
        foreach (var stored in reports)
        {
            if (stored.Settlements.Any(l => l.AdultWorkersOpening !=
                current.AdultsIn(l.SettlementId, Rules)))
                throw new InvalidDataException("TEMP adult workers differ from actual human ages.");
            var stripped = stored with
            {
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
                ImmutableList<DemoMigrationBatch>.Empty, 1, sites);
            var trial = Apply(temp, current, nextEventId);
            if (!trial.Outcome.Report.Settlements.SequenceEqual(stored.Settlements)
                || !trial.Outcome.Report.Events.SequenceEqual(stored.Events))
                throw new InvalidDataException("TEMP birth/death events do not match reconstructed pregnancies and age.");
            current = trial.Population;
            if (stored.Events.Count > 0) nextEventId = checked(stored.Events[^1].EventId + 1);
        }
        return current;
    }
}
