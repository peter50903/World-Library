using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #110: An isolated TEMP demographic slice, NOT wired into DemoObserverWorld yet.
/// Real settlement/migration ledgers remain the authority until #105 integration.
/// All demographic identities here are bookkeeping tokens, not complete L3 characters.
/// </summary>
public enum DemoTempSex { Female, Male }

public enum DemoTempDemographicKind
{
    Conception,
    Birth,
    Miscarriage,
    NaturalDeath,
    StarvationDeath,
    TravelStarvationDeath
}

/// <summary>Formal human age/fertility inputs must eventually be loaded from 02_種族;
/// gestation and the mortality curve below are TEMP, not approved world parameters.</summary>
public sealed record DemoTempDemographyRules(
    int AdultAgeYears,
    int TypicalLifespanYears,
    double FertilityMonthly,
    double MiscarriageMonthly,
    int GestationWeeks)
{
    /// <summary>Human fixture: age 18 / lifespan 75 / fertility .08 from 02_種族;
    /// miscarriage .005 from 29_數值參數 (unbalanced); gestation 39 weeks TEMP.</summary>
    public static DemoTempDemographyRules HumanFixture { get; } = new(18, 75, 0.08, 0.005, 39);

    public void Validate()
    {
        if (AdultAgeYears < 1 || TypicalLifespanYears <= AdultAgeYears
            || FertilityMonthly is < 0 or > 1 || double.IsNaN(FertilityMonthly)
            || MiscarriageMonthly is < 0 or > 1 || double.IsNaN(MiscarriageMonthly)
            || GestationWeeks < 1)
            throw new ArgumentOutOfRangeException(nameof(DemoTempDemographyRules));
    }

    // TEMP: calendar uses 365 days/year and 12 months/year, 7 days per settlement week.
    public double WeeklyConceptionChance => 1 - Math.Pow(1 - FertilityMonthly, 84.0 / 365);
    public double WeeklyMiscarriageChance => 1 - Math.Pow(1 - MiscarriageMonthly, 84.0 / 365);

    /// <summary>TEMP age-dependent natural-death hazard; not a forced death at typical lifespan.</summary>
    public double WeeklyNaturalDeathChance(int ageYears)
    {
        if (ageYears < TypicalLifespanYears - 15) return 0;
        var annual = Math.Min(0.95, ageYears < TypicalLifespanYears
            ? 0.008
            : 0.02 + 0.04 * (ageYears - TypicalLifespanYears));
        return 1 - Math.Pow(1 - annual, 7.0 / 365);
    }
}

public sealed record DemoTempPerson(
    long Id,
    int SettlementId,
    DemoTempSex Sex,
    int BornDay,
    int? PregnancyDueWeek = null,
    long? FatherId = null)
{
    public int AgeYearsAt(int day)
        => day < BornDay ? 0 : (day - BornDay) / 365;
    public bool IsAdultAt(int day, DemoTempDemographyRules rules)
        => AgeYearsAt(day) >= rules.AdultAgeYears;
}

public sealed record DemoTempDemographicEvent(
    int Week,
    DemoTempDemographicKind Kind,
    long PersonId,
    int SettlementId,
    long? MotherId = null,
    long? FatherId = null);

/// <summary>Serialized-shaped snapshot for an eventual atomic world save; not yet a save version.</summary>
public sealed record DemoTempDemographicSnapshot(
    ulong Seed,
    int LastWeek,
    long NextId,
    ImmutableArray<DemoTempPerson> People);

public sealed record DemoTempDemographicOutcome(
    DemoTempDemography Population,
    ImmutableArray<DemoTempDemographicEvent> Events)
{
    public int Births => Events.Count(e => e.Kind == DemoTempDemographicKind.Birth);
    public int NaturalDeaths => Events.Count(e => e.Kind == DemoTempDemographicKind.NaturalDeath);
    public int Deaths => Events.Count(e => e.Kind is DemoTempDemographicKind.NaturalDeath
        or DemoTempDemographicKind.StarvationDeath or DemoTempDemographicKind.TravelStarvationDeath);
}

/// <summary>
/// Deterministic, immutable TEMP human demography with source-traceable parents and age.
/// Newborns eat only when a later world-ledger integration counts them; they are not workers before adult age.
/// Population never increases without pregnancy and two adult prospective parents.
/// </summary>
public sealed class DemoTempDemography
{
    private readonly ImmutableSortedDictionary<long, DemoTempPerson> _people;
    public ulong Seed { get; }
    public int LastWeek { get; }
    public long NextId { get; }
    public int Count => _people.Count;
    public IReadOnlyList<DemoTempPerson> People => _people.Values.ToArray();

    private DemoTempDemography(ulong seed, int week, long nextId,
        ImmutableSortedDictionary<long, DemoTempPerson> people)
    {
        Seed = seed;
        LastWeek = week;
        NextId = nextId;
        _people = people;
    }

    /// <summary>Opening population (TEMP): all adults, alternating sex; age varies 18–22 years.</summary>
    /// <remarks>
    /// C04：傳入 <paramref name="ids"/>（世界唯一實體 ID 分配器）時，每個人的 ID 依聚落、人序向它取號，
    /// <see cref="NextId"/> 等於分配器取號後的下一號；不傳時沿用獨立序號（只供單元測試）。
    /// </remarks>
    public static DemoTempDemography Open(ulong seed, IReadOnlyDictionary<int, int> settlements,
        DemoTempDemographyRules rules, EntityIdAllocator? ids = null)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        var people = ImmutableSortedDictionary.CreateBuilder<long, DemoTempPerson>();
        long id = ids is null ? 1 : DemoEntityIds.NextValue(ids.Capture());
        foreach (var (settlement, count) in settlements.OrderBy(kv => kv.Key))
        {
            if (settlement <= 0 || count < 0)
                throw new ArgumentOutOfRangeException(nameof(settlements));
            for (var i = 0; i < count; i++)
            {
                var age = checked(rules.AdultAgeYears + i % 5);
                if (ids is not null) id = DemoEntityIds.NextLong(ids);
                people.Add(id, new DemoTempPerson(id, settlement,
                    i % 2 == 0 ? DemoTempSex.Female : DemoTempSex.Male,
                    checked(-age * 365)));
                id = ids is null ? checked(id + 1) : DemoEntityIds.NextValue(ids.Capture());
            }
        }
        return Restore(new DemoTempDemographicSnapshot(seed, 0, id,
            people.Values.ToImmutableArray()), rules);
    }

    public DemoTempDemographicSnapshot Snapshot()
        => new(Seed, LastWeek, NextId, _people.Values.ToImmutableArray());

    /// <summary>Reject duplicated IDs, malformed pregnancies, and impossible future births.</summary>
    public static DemoTempDemography Restore(DemoTempDemographicSnapshot snapshot,
        DemoTempDemographyRules rules)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (snapshot.LastWeek < 0 || snapshot.LastWeek > int.MaxValue / 7
            || snapshot.NextId < 1 || snapshot.People.IsDefault)
            throw new InvalidOperationException("Malformed TEMP demographic snapshot.");
        var builder = ImmutableSortedDictionary.CreateBuilder<long, DemoTempPerson>();
        foreach (var p in snapshot.People)
        {
            if (p is null || p.Id < 1 || p.Id >= snapshot.NextId
                || p.SettlementId < 1 || p.BornDay > snapshot.LastWeek * 7
                || !Enum.IsDefined(p.Sex)
                || (p.PregnancyDueWeek.HasValue != p.FatherId.HasValue)
                || (p.PregnancyDueWeek.HasValue && (p.Sex != DemoTempSex.Female
                    || p.PregnancyDueWeek.Value <= snapshot.LastWeek
                    || !p.IsAdultAt(snapshot.LastWeek * 7, rules)
                    || p.FatherId!.Value <= 0
                    || p.FatherId.Value >= snapshot.NextId)))
                throw new InvalidOperationException("Invalid TEMP demographic member or pregnancy.");
            if (builder.ContainsKey(p.Id))
                throw new InvalidOperationException("Duplicate TEMP population identity.");
            builder.Add(p.Id, p);
        }
        return new DemoTempDemography(snapshot.Seed, snapshot.LastWeek, snapshot.NextId,
            builder.ToImmutable());
    }

    public int AdultsIn(int settlementId, DemoTempDemographyRules rules)
        => _people.Values.Count(p => p.SettlementId == settlementId && p.IsAdultAt(LastWeek * 7, rules));

    /// <summary>Atomic transfer of specific identity tokens; does not by itself move actual world migrants.</summary>
    public DemoTempDemography Transfer(IReadOnlyCollection<long> ids, int destinationSettlement)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (destinationSettlement < 1 || ids.Count != ids.Distinct().Count()
            || ids.Any(id => !_people.ContainsKey(id)))
            throw new InvalidOperationException("Invalid TEMP demographic transfer.");
        var next = _people.ToBuilder();
        foreach (var id in ids)
            next[id] = next[id] with { SettlementId = destinationSettlement };
        return new DemoTempDemography(Seed, LastWeek, NextId, next.ToImmutable());
    }

    /// <summary>Externally evidenced starvation losses, selected by person ID; for future week-ledger integration.</summary>
    public DemoTempDemographicOutcome RemoveStarvation(IReadOnlyCollection<long> ids, bool inTransit)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count != ids.Distinct().Count() || ids.Any(id => !_people.ContainsKey(id)))
            throw new InvalidOperationException("Invalid TEMP starvation death identities.");
        var next = _people.ToBuilder();
        var events = ImmutableArray.CreateBuilder<DemoTempDemographicEvent>();
        foreach (var id in ids.Order())
        {
            var p = next[id];
            next.Remove(id);
            events.Add(new DemoTempDemographicEvent(LastWeek,
                inTransit ? DemoTempDemographicKind.TravelStarvationDeath : DemoTempDemographicKind.StarvationDeath,
                id, p.SettlementId));
        }
        return new DemoTempDemographicOutcome(
            new DemoTempDemography(Seed, LastWeek, NextId, next.ToImmutable()), events.ToImmutable());
    }

    /// <summary>Advance exactly one 7-day week. Nutritional status must come from real Core food accounts.</summary>
    /// <remarks>C04：傳入 <paramref name="ids"/> 時新生兒向世界唯一分配器取號（依母親 ID 順序）；不傳時沿用獨立序號（只供單元測試）。</remarks>
    public DemoTempDemographicOutcome AdvanceWeek(int week,
        Func<int, bool> settlementFed, DemoTempDemographyRules rules, EntityIdAllocator? ids = null)
    {
        ArgumentNullException.ThrowIfNull(settlementFed);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (week != LastWeek + 1 || week > int.MaxValue / 7)
            throw new InvalidOperationException("TEMP demographic weeks must be consecutive.");
        var day = checked(week * 7);
        var next = _people.ToBuilder();
        var events = ImmutableArray.CreateBuilder<DemoTempDemographicEvent>();
        long nextId = ids is null ? NextId : DemoEntityIds.NextValue(ids.Capture());
        if (ids is not null && nextId < NextId)
            throw new InvalidOperationException("The world entity allocator is behind the TEMP person IDs.");
        // Death first: a dead mother cannot give birth later in the same settlement week.
        foreach (var p in _people.Values)
        {
            var chance = rules.WeeklyNaturalDeathChance(p.AgeYearsAt(day));
            if (!Roll(Seed, p.Id, week, 1, chance)) continue;
            next.Remove(p.Id);
            events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.NaturalDeath,
                p.Id, p.SettlementId));
        }

        foreach (var p in _people.Values.Where(p => p.Sex == DemoTempSex.Female))
        {
            if (!next.TryGetValue(p.Id, out var mother)) continue;
            if (mother.PregnancyDueWeek is int due)
            {
                if (due == week)
                {
                    if (ids is not null) nextId = DemoEntityIds.NextLong(ids);
                    var newborn = new DemoTempPerson(nextId, mother.SettlementId,
                        Roll(Seed, nextId, week, 2, 0.5) ? DemoTempSex.Female : DemoTempSex.Male, day);
                    next.Add(nextId, newborn);
                    events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Birth,
                        nextId, mother.SettlementId, mother.Id, mother.FatherId));
                    nextId = ids is null ? checked(nextId + 1) : DemoEntityIds.NextValue(ids.Capture());
                    next[mother.Id] = mother with { PregnancyDueWeek = null, FatherId = null };
                }
                else if (Roll(Seed, mother.Id, week, 3, rules.WeeklyMiscarriageChance))
                {
                    events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Miscarriage,
                        mother.Id, mother.SettlementId, mother.Id, mother.FatherId));
                    next[mother.Id] = mother with { PregnancyDueWeek = null, FatherId = null };
                }
                continue;
            }

            // Temporary fertile-age cut-off: no fabricated pregnancy without a live adult male
            // at the same settlement, and no conception when real settlement food is insufficient.
            if (mother.AgeYearsAt(day) < rules.AdultAgeYears || mother.AgeYearsAt(day) >= 45
                || !settlementFed(mother.SettlementId))
                continue;
            var father = next.Values.FirstOrDefault(m => m.SettlementId == mother.SettlementId
                && m.Sex == DemoTempSex.Male && m.IsAdultAt(day, rules));
            if (father is null || !Roll(Seed, mother.Id, week, 4, rules.WeeklyConceptionChance))
                continue;
            next[mother.Id] = mother with { PregnancyDueWeek = checked(week + rules.GestationWeeks), FatherId = father.Id };
            events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Conception,
                mother.Id, mother.SettlementId, mother.Id, father.Id));
        }

        var after = new DemoTempDemography(Seed, week, nextId, next.ToImmutable());
        if (after.Count != Count + events.Count(x => x.Kind == DemoTempDemographicKind.Birth)
            - events.Count(x => x.Kind == DemoTempDemographicKind.NaturalDeath))
            throw new InvalidOperationException("TEMP births and natural deaths failed conservation.");
        return new DemoTempDemographicOutcome(after, events.ToImmutable());
    }

    private static bool Roll(ulong seed, long personId, int week, ulong channel, double chance)
    {
        if (chance <= 0) return false;
        if (chance >= 1) return true;
        // TEMP per-(seed, person, week, action) deterministic hash: replay and order independent.
        ulong z = unchecked(seed + (ulong)personId * 0x9E3779B97F4A7C15UL
            + (ulong)week * 0xBF58476D1CE4E5B9UL + channel * 0x94D049BB133111EBUL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)) < chance;
    }
}
