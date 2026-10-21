using System.Collections.Immutable;
using WorldSimulation.Core.Rng;
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

    /// <summary>第 04 章 a 節：生死抽樣改由世界的 POP 流（原本傳給 Open 的種子改當世界種子）。</summary>
    private static DemoPopulationDraws Draws(ulong worldSeed) => new(RngRegistry.Create(worldSeed));
    private static DemoOpeningDraws Opening(ulong worldSeed) => new(RngRegistry.Create(worldSeed));

    [Fact]
    public void OpeningPopulationHasRealAdultSexesAndNoImaginaryBirths()
    {
        var pop = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 8, [2] = 3 }, Rules);
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
            new DemoTempDemographicSnapshot(0, 2,
                ImmutableArray.Create(new DemoTempPerson(1, 1, DemoTempSex.Female, -19 * 365))), Rules);
        var hungry = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 6 }, Rules);
        for (int w = 1; w <= 300; w++)
        {
            var singleOutcome = solitary.AdvanceWeek(w, _ => true, Rules, Draws(42));
            var hungerOutcome = hungry.AdvanceWeek(w, _ => false, Rules, Draws(42));
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
        var pop = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 8 }, Rules);
        var pregnancies = new Dictionary<long, int>();
        var births = new List<DemoTempDemographicEvent>();
        for (var week = 1; week <= 160; week++)
        {
            var result = pop.AdvanceWeek(week, _ => true, Rules, Draws(42));
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
        // 第 07 章 a 節：39C 死亡分段下年輕成人也有很小的自然死亡機率，所以只核對守恆。
        Assert.Equal(pop.Count, pop.People.Count(p => p.BornDay <= 0) + births.Count(b => pop.People.Any(p => p.Id == b.PersonId)));
        Assert.Equal(pop.People.Count(p => p.BornDay <= 0), pop.AdultsIn(1, Rules)); // Every newborn is younger than 18.
    }

    [Fact]
    public void OldAgeHasRealNaturalDeathsNotAnExactForcedLifespan()
    {
        var old = DemoTempDemography.Restore(new DemoTempDemographicSnapshot(0, 2,
            ImmutableArray.Create(new DemoTempPerson(1, 1, DemoTempSex.Male, -120 * 365))), Rules);
        var deaths = new List<DemoTempDemographicEvent>();
        for (var week = 1; week <= 52 * 25; week++)
        {
            var result = old.AdvanceWeek(week, _ => true, Rules, Draws(42));
            deaths.AddRange(result.Events.Where(e => e.Kind == DemoTempDemographicKind.NaturalDeath));
            old = result.Population;
        }
        Assert.Single(deaths);
        Assert.Equal(0, old.Count);
        Assert.Equal(1L, deaths[0].PersonId);
        // 第 07 章 a 節：39C LIFE_DEATH_BANDS（年度，依 年齡÷典型壽命 75）。
        Assert.Equal(0.0005, Rules.AnnualNaturalDeathChance(40), 10);   // 0.53 < 0.6
        Assert.Equal(0.002, Rules.AnnualNaturalDeathChance(45), 10);    // 0.6
        Assert.Equal(0.05, Rules.AnnualNaturalDeathChance(75), 10);     // 1.0
        Assert.Equal(0.35, Rules.AnnualNaturalDeathChance(113), 10);    // ≥1.5
        Assert.True(Rules.WeeklyNaturalDeathChance(40) > 0);
        Assert.True(Rules.WeeklyNaturalDeathChance(100) > Rules.WeeklyNaturalDeathChance(75));
    }

    [Fact]
    public void SnapshotRestoreAndSeededRunsYieldIdenticalBirthsAndDeaths()
    {
        var x = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 7 }, Rules);
        var y = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 7 }, Rules);
        for (var w = 1; w <= 75; w++)
            x = x.AdvanceWeek(w, _ => true, Rules, Draws(42)).Population;
        x = DemoTempDemography.Restore(x.Snapshot(), Rules);
        for (var w = 1; w <= 200; w++)
        {
            var outcomeY = y.AdvanceWeek(w, _ => true, Rules, Draws(42));
            y = outcomeY.Population;
            if (w <= 75) continue;
            var outcomeX = x.AdvanceWeek(w, _ => true, Rules, Draws(42));
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
        var noMiscarriage = Rules with { MiscarriageMonthly = 0 }; // Isolate transfer + provenance from a separate miscarriage chance.
        var pop = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 8 }, noMiscarriage);
        DemoTempPerson? expecting = null;
        int conceivedWeek = 0;
        for (var week = 1; week <= 130; week++)
        {
            pop = pop.AdvanceWeek(week, _ => true, noMiscarriage, Draws(42)).Population;
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
        var allBirths = 0;
        var allDeaths = 0;
        var expectedDue = expecting.PregnancyDueWeek!.Value;
        for (var week = conceivedWeek + 1; week <= expectedDue; week++)
        {
            var result = pop.AdvanceWeek(week, _ => true, noMiscarriage, Draws(42));
            allBirths += result.Births;
            allDeaths += result.Deaths;
            births.AddRange(result.Events.Where(e => e.Kind == DemoTempDemographicKind.Birth
                && e.MotherId == expecting.Id));
            pop = result.Population;
        }
        Assert.Single(births);
        Assert.Equal(2, births[0].SettlementId);
        Assert.Equal(father, births[0].FatherId); // Father can die after verified conception.
        Assert.Equal(baselineCount - 1 + allBirths - allDeaths, pop.Count); // All births/deaths accounted for; not only this mother.
    }

    [Fact]
    public void RejectMalformedOrForgedDemographicIdentitySnapshots()
    {
        var pop = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 4 }, Rules);
        var good = pop.Snapshot();
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { People = good.People.Add(good.People[0]) }, Rules));
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { NextId = 2 }, Rules));
        Assert.Throws<InvalidOperationException>(() => DemoTempDemography.Restore(
            good with { People = good.People.SetItem(1, good.People[1] with { PregnancyDueWeek = 39, FatherId = 4 }) },
            Rules)); // Male cannot be pregnant.
    }

    [Fact]
    public void HumanRulesComeFromRaceAndLifecycleSheets()
    {
        Assert.Equal(18, Rules.AdultAgeYears);
        Assert.Equal(75, Rules.TypicalLifespanYears);
        Assert.Equal(0.08, Rules.FertilityMonthly, 10);
        Assert.Equal(0.005, Rules.MiscarriageMonthly, 10);
        Assert.Equal(39, Rules.GestationWeeks);
        Assert.Equal(45, Rules.FertileAgeMaxYears);
        Assert.Equal(6, Rules.DeathBands.Count);
        Assert.True(double.IsPositiveInfinity(Rules.DeathBands[^1].UpperRatio));
        Assert.Throws<InvalidOperationException>(() => DemoHumanRulesSource.ParseDeathBands("Age/ExpectedLife:<0.6=1%"));
    }

    [Fact]
    public void MixedAgeOpeningFollowsTempBandsAndIsReproducible()
    {
        var a = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 400, [2] = 400 }, Rules, null, Opening(7));
        var b = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 400, [2] = 400 }, Rules, null, Opening(7));
        Assert.Equal(a.People.ToArray(), b.People.ToArray());
        var children = a.People.Count(p => !p.IsAdultAt(0, Rules));
        var elders = a.People.Count(p => p.AgeYearsAt(0) >= 60);
        Assert.InRange(children, 800 * 25 / 100, 800 * 45 / 100);   // TEMP 35%
        Assert.InRange(elders, 800 * 3 / 100, 800 * 14 / 100);      // TEMP 8%
        Assert.All(a.People, p => Assert.InRange(p.AgeYearsAt(0), 0, 79));
        Assert.InRange(a.People.Count(p => p.Sex == DemoTempSex.Female), 340, 460);
        Assert.True(a.People.Select(p => p.BornDay % 365).Distinct().Count() > 100); // 年內出生日分散
        Assert.NotEqual(a.People.ToArray(), DemoTempDemography.Open(
            new Dictionary<int, int> { [1] = 400, [2] = 400 }, Rules, null, Opening(8)).People.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PairingPrefersNonKinButAllowsSiblingsAndNeverParentChild(bool withStranger)
    {
        // 第 08-1 章：手足可配但優先非近親；親子永遠不配。
        var people = new List<DemoTempPerson>
        {
            new(1, 1, DemoTempSex.Female, -60 * 365),
            new(2, 1, DemoTempSex.Male, -61 * 365),
            new(3, 1, DemoTempSex.Female, -20 * 365, MotherId: 1, BirthFatherId: 2),
            new(4, 1, DemoTempSex.Male, -21 * 365, MotherId: 1, BirthFatherId: 2)
        };
        if (withStranger)
            people.Add(new DemoTempPerson(5, 1, DemoTempSex.Male, -30 * 365));
        var pop = DemoTempDemography.Restore(new DemoTempDemographicSnapshot(0, 6, people.ToImmutableArray()), Rules);
        var after = pop.AdvanceWeek(1, _ => true, Rules, Draws(11)).Population.People.ToDictionary(p => p.Id);
        Assert.Equal(withStranger ? 5 : 4, after[3].PartnerId);
        Assert.All(after.Values.Where(p => p.PartnerId is not null),
            p => Assert.False(p.IsParentOrChildOf(after[p.PartnerId!.Value])));
    }

    [Fact]
    public void ConceptionOnlyWithStablePartnerAndNeverCloseKin()
    {
        var pop = DemoTempDemography.Open(new Dictionary<int, int> { [1] = 12 }, Rules);
        var partners = new Dictionary<long, long>();
        for (var week = 1; week <= 52 * 60; week++)
        {
            var result = pop.AdvanceWeek(week, _ => true, Rules, Draws(11));
            foreach (var e in result.Events.Where(e => e.Kind == DemoTempDemographicKind.Conception))
            {
                var mother = result.Population.People.Single(p => p.Id == e.MotherId);
                Assert.Equal(mother.PartnerId, e.FatherId);
            }
            foreach (var p in result.Population.People.Where(p => p.PartnerId is not null))
            {
                var partner = result.Population.People.Single(q => q.Id == p.PartnerId);
                Assert.Equal(p.Id, partner.PartnerId);
                Assert.NotEqual(p.Sex, partner.Sex);
                Assert.False(p.IsParentOrChildOf(partner)); // 第 08-1 章：手足可配，親子不可
                Assert.True(p.IsAdultAt(week * 7, Rules));
                // 穩定：已存在的配對只會因死亡或分居解除，不換人。
                if (partners.TryGetValue(p.Id, out var before) && result.Population.People.Any(q => q.Id == before))
                    Assert.Equal(before, p.PartnerId);
                partners[p.Id] = p.PartnerId!.Value;
            }
            pop = DemoTempDemography.Restore(result.Population.Snapshot(), Rules);
        }
        Assert.Contains(pop.People, p => p.MotherId is not null && p.PartnerId is not null); // 第二代也配對
    }

    [Fact]
    public void RestoreRejectsBrokenPartnerships()
    {
        var oneSided = ImmutableArray.Create(
            new DemoTempPerson(1, 1, DemoTempSex.Female, -20 * 365, PartnerId: 2),
            new DemoTempPerson(2, 1, DemoTempSex.Male, -20 * 365));
        Assert.Throws<InvalidOperationException>(() =>
            DemoTempDemography.Restore(new DemoTempDemographicSnapshot(0, 3, oneSided), Rules));
        // 第 08-1 章：親子配對拒讀；手足配對可讀。
        var parentChild = ImmutableArray.Create(
            new DemoTempPerson(1, 1, DemoTempSex.Female, -60 * 365, PartnerId: 4),
            new DemoTempPerson(2, 1, DemoTempSex.Male, -60 * 365),
            new DemoTempPerson(4, 1, DemoTempSex.Male, -20 * 365, MotherId: 1, BirthFatherId: 2, PartnerId: 1));
        Assert.Throws<InvalidOperationException>(() =>
            DemoTempDemography.Restore(new DemoTempDemographicSnapshot(0, 5, parentChild), Rules));
        var siblings = ImmutableArray.Create(
            new DemoTempPerson(1, 1, DemoTempSex.Female, -60 * 365),
            new DemoTempPerson(2, 1, DemoTempSex.Male, -60 * 365),
            new DemoTempPerson(3, 1, DemoTempSex.Female, -20 * 365, MotherId: 1, BirthFatherId: 2, PartnerId: 4),
            new DemoTempPerson(4, 1, DemoTempSex.Male, -20 * 365, MotherId: 1, BirthFatherId: 2, PartnerId: 3));
        Assert.Equal(4, DemoTempDemography.Restore(new DemoTempDemographicSnapshot(0, 5, siblings), Rules).Count);
    }
}
