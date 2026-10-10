using System.Collections.Immutable;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #110: TEMP source-traceable demographic kernel only. These tests do NOT claim that
/// DemoWeeklySettlement, Save v2 or the playable Godot world has births yet.
/// </summary>
public sealed class DemoTempDemographyTests
{
    private static readonly DemoTempDemographyRules Rules = DemoTempDemographyRules.HumanFixture;

    [Fact]
    public void OpeningPopulationHasRealAdultSexesAndNoImaginaryBirths()
    {
        var pop = DemoTempDemography.Open(42, new Dictionary<int, int> { [1] = 8, [2] = 3 }, Rules);
        Assert.Equal(11, pop.Count);
        Assert.Equal(11, pop.AdultsIn(1, Rules) + pop.AdultsIn(2, Rules));
        Assert.Equal(6, pop.People.Count(p => p.Sex == DemoTempSex.Female));
        Assert.Equal(5, pop.People.Count(p => p.Sex == DemoTempSex.Male));
        Assert.Equal(11, pop.People.Select(p => p.Id).Distinct().Count());
        Assert.All(pop.People, p => Assert.Null(p.PregnancyDueWeek));
    }

    [Fact]
    public void NoAdultManOrNoFoodMeansNoConceptionOrBirth()
    {
        var solitary = DemoTempDemography.Restore(
            new DemoTempDemographicSnapshot(4, 0, 2,
                ImmutableArray.Create(new DemoTempPerson(1, 1, DemoTempSex.Female, -19 * 365))), Rules);
        var hungry = DemoTempDemography.Open(42, new Dictionary<int, int> { [1] = 6 }, Rules);
        for (int w = 1; w <= 300; w++)
        {
            var singleOutcome = solitary.AdvanceWeek(w, _ => true, Rules);
            var hungerOutcome = hungry.AdvanceWeek(w, _ => false, Rules);
            Assert.Empty(singleOutcome.Events.Where(e => e.Kind is DemoTempDemographicKind.Birth
                or DemoTempDemographicKind.Conception));
            Assert.Empty(hungerOutcome.Events.Where(e => e.Kind is DemoTempDemographicKind.Birth
                or DemoTempDemographicKind.Conception));
            solitary = singleOutcome.Population;
            hungry = hungerOutcome.Population;
        }
        Assert.Equal(1, solitary.Count);
        Assert.Equal(6, hungry.Count);
    }

    [Fact]
    public void BirthsNeedParentsAndGestationAndBabiesCannotWork()
    {
        var pop = DemoTempDemography.Open(42, new Dictionary<int, int> { [1] = 8 }, Rules);
        var pregnancies = new Dictionary<long, int>();
        var births = new List<DemoTempDemographicEvent>();
        for (var week = 1; week <= 160; week++)
        {
            var result = pop.AdvanceWeek(week, _ => true, Rules);
            foreach (var e in result.Events)
            {
                if (e.Kind == DemoTempDemographicKind.Conception)
                {
                    Assert.NotNull(e.FatherId);
                    Assert.Contains(pop.People, p => p.Id == e.MotherId
                        && p.Sex == DemoTempSex.Female && p.IsAdultAt(week * 7, Rules));
                    Assert.Contains(pop.People, p => p.Id == e.FatherId
                        && p.Sex == DemoTempSex.Male && p.IsAdultAt(week * 7, Rules));
                    pregnancies[e.PersonId] = week;
                }
                if (e.Kind == DemoTempDemographicKind.Birth)
                {
                    births.Add(e);
                    Assert.True(pregnancies.TryGetValue(e.MotherId!.Value, out var start));
                    Assert.Equal(Rules.GestationWeeks, week - start);
                    Assert.Contains(result.Population.People, p => p.Id == e.PersonId
                        && !p.IsAdultAt(week * 7, Rules) && p.BornDay == week * 7);
                }
                if (e.Kind == DemoTempDemographicKind.Miscarriage)
                    pregnancies.Remove(e.MotherId!.Value);
            }
            pop = result.Population;
        }
        Assert.NotEmpty(births);
        Assert.Equal(8 + births.Count, pop.Count); // Opening adults are too young for TEMP natural death here.
        Assert.Equal(8, pop.AdultsIn(1, Rules)); // Every newborn is younger than 18.
    }

    [Fact]
    public void OldAgeHasRealNaturalDeathsNotAnExactForcedLifespan()
    {
        var old = DemoTempDemography.Restore(new DemoTempDemographicSnapshot(42, 0, 2,
            ImmutableArray.Create(new DemoTempPerson(1, 1, DemoTempSex.Male, -100 * 365))), Rules);
        var deaths = new List<DemoTempDemographicEvent>();
        for (var week = 1; week <= 260; week++)
        {
            var result = old.AdvanceWeek(week, _ => true, Rules);
            deaths.AddRange(result.Events.Where(e => e.Kind == DemoTempDemographicKind.NaturalDeath));
            old = result.Population;
        }
        Assert.Single(deaths);
        Assert.Equal(0, old.Count);
        Assert.Equal(1L, deaths[0].PersonId);
        Assert.Equal(0, Rules.WeeklyNaturalDeathChance(40));
        Assert.True(Rules.WeeklyNaturalDeathChance(100) > Rules.WeeklyNaturalDeathChance(75));
    }

    [Fact]
    public void SnapshotRestoreAndSeededRunsYieldIdenticalBirthsAndDeaths()
    {
        var x = DemoTempDemography.Open(17, new Dictionary<int, int> { [1] = 7 }, Rules);
        var y = DemoTempDemography.Open(17, new Dictionary<int, int> { [1] = 7 }, Rules);
        for (var w = 1; w <= 75; w++)
            x = x.AdvanceWeek(w, _ => true, Rules).Population;
        x = DemoTempDemography.Restore(x.Snapshot(), Rules);
        for (var w = 1; w <= 200; w++)
        {
            var outcomeY = y.AdvanceWeek(w, _ => true, Rules);
            y = outcomeY.Population;
            if (w <= 75) continue;
            var outcomeX = x.AdvanceWeek(w, _ => true, Rules);
            x = outcomeX.Population;
            Assert.Equal(outcomeY.Events.ToArray(), outcomeX.Events.ToArray());
            Assert.Equal(y.People.ToArray(), x.People.ToArray());
        }
        Assert.Equal(y.Snapshot().NextId, x.Snapshot().NextId);
        Assert.Equal(y.Snapshot().LastWeek, x.Snapshot().LastWeek);
        Assert.Equal(y.People.ToArray(), x.People.ToArray());
    }

    [Fact]
    public void MigrationKeepsPregnancyAndStarvationCannotCreateOrCopyPeople()
    {
        var pop = DemoTempDemography.Open(42, new Dictionary<int, int> { [1] = 8 }, Rules);
        DemoTempPerson? expecting = null;
        int conceivedWeek = 0;
        for (var week = 1; week <= 130; week++)
        {
            pop = pop.AdvanceWeek(week, _ => true, Rules).Population;
            expecting = pop.People.FirstOrDefault(p => p.PregnancyDueWeek.HasValue);
            if (expecting is null) continue;
            conceivedWeek = week;
            break;
        }
        Assert.NotNull(expecting);
        var baselineCount = pop.Count;
        Assert.Throws<InvalidOperationException>(() =>
            pop.Transfer(new long[] { expecting!.Id, expecting.Id }, 2));
        Assert.Throws<InvalidOperationException>(() => pop.Transfer(new long[] { 999999 }, 2));
        pop = pop.Transfer(new long[] { expecting!.Id }, 2);
        Assert.Equal(baselineCount, pop.Count);
        Assert.Equal(expecting.PregnancyDueWeek, pop.People.Single(p => p.Id == expecting.Id).PregnancyDueWeek);
        var father = expecting.FatherId!.Value;
        var starvation = pop.RemoveStarvation(new long[] { father }, inTransit: false);
        pop = starvation.Population;
        Assert.Single(starvation.Events);
        Assert.Equal(DemoTempDemographicKind.StarvationDeath, starvation.Events[0].Kind);
        Assert.Equal(baselineCount - 1, pop.Count);
        Assert.Throws<InvalidOperationException>(() => pop.RemoveStarvation(new long[] { father }, false));

        var births = new List<DemoTempDemographicEvent>();
        var expectedDue = expecting.PregnancyDueWeek!.Value;
        for (var week = conceivedWeek + 1; week <= expectedDue; week++)
        {
            var result = pop.AdvanceWeek(week, _ => true, Rules);
            births.AddRange(result.Events.Where(e => e.Kind == DemoTempDemographicKind.Birth
                && e.MotherId == expecting.Id));
            pop = result.Population;
        }
        Assert.Single(births);
        Assert.Equal(2, births[0].SettlementId);
        Assert.Equal(father, births[0].FatherId); // Father can die after verified conception.
        Assert.Equal(baselineCount, pop.Count); // 1 verified death and 1 verified birth.
    }

    [Fact]
    public void RejectMalformedOrForgedDemographicIdentitySnapshots()
    {
        var pop = DemoTempDemography.Open(1, new Dictionary<int, int> { [1] = 4 }, Rules);
        var good = pop.Snapshot();
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { People = good.People.Add(good.People[0]) }, Rules));
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { NextId = 2 }, Rules));
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { People = good.People.SetItem(1, good.People[1] with { PregnancyDueWeek = 39, FatherId = 4 }) },
            Rules)); // Male cannot be pregnant.
    }
}
