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

/// <summary>
/// 人族生死規則。第 07 章 a 節起由 <see cref="DemoHumanRulesSource"/> 從 02_種族、39C 與 config/demo 載入：
/// 自然死亡依 39C LIFE_DEATH_BANDS（年齡÷典型壽命 分段的年度機率，【TEMP】數值未定案），不再用自創曲線。
/// </summary>
public sealed record DemoTempDemographyRules(
    int AdultAgeYears,
    int TypicalLifespanYears,
    double FertilityMonthly,
    double MiscarriageMonthly,
    int GestationWeeks)
{
    /// <summary>人族：02 成年 18／典型壽命 75／月受孕 0.08；39C 流產 0.5%／月、死亡分段；孕期 39 週【TEMP】。</summary>
    public static DemoTempDemographyRules HumanFixture { get; } = DemoHumanRulesSource.Load();

    /// <summary>39C LIFE_DEATH_BANDS：(年齡÷典型壽命 的上界（不含）, 年度機率)。</summary>
    public IReadOnlyList<(double UpperRatio, double Annual)> DeathBands { get; init; } = Array.Empty<(double, double)>();

    /// <summary>育齡上限【TEMP】（config/demo）。</summary>
    public int FertileAgeMaxYears { get; init; } = 45;

    public void Validate()
    {
        if (AdultAgeYears < 1 || TypicalLifespanYears <= AdultAgeYears
            || FertilityMonthly is < 0 or > 1 || double.IsNaN(FertilityMonthly)
            || MiscarriageMonthly is < 0 or > 1 || double.IsNaN(MiscarriageMonthly)
            || GestationWeeks < 1 || DeathBands.Count == 0 || FertileAgeMaxYears <= AdultAgeYears)
            throw new ArgumentOutOfRangeException(nameof(DemoTempDemographyRules));
    }

    // TEMP: calendar uses 365 days/year and 12 months/year, 7 days per settlement week.
    public double WeeklyConceptionChance => 1 - Math.Pow(1 - FertilityMonthly, 84.0 / 365);
    public double WeeklyMiscarriageChance => 1 - Math.Pow(1 - MiscarriageMonthly, 84.0 / 365);

    /// <summary>39C 年度自然死亡機率（依年齡÷典型壽命 分段）；不是壽命到期強制死亡。</summary>
    public double AnnualNaturalDeathChance(int ageYears)
    {
        var ratio = (double)Math.Max(0, ageYears) / TypicalLifespanYears;
        foreach (var (upper, annual) in DeathBands)
            if (ratio < upper)
                return annual;
        return DeathBands[^1].Annual;
    }

    public double WeeklyNaturalDeathChance(int ageYears)
        => 1 - Math.Pow(1 - AnnualNaturalDeathChance(ageYears), 7.0 / 365);
}

public sealed record DemoTempPerson(
    long Id,
    int SettlementId,
    DemoTempSex Sex,
    int BornDay,
    int? PregnancyDueWeek = null,
    long? FatherId = null,
    long? MotherId = null,
    long? BirthFatherId = null,
    long? PartnerId = null)
{
    /// <summary>
    /// 第 07 章 a 節：可追溯的近親（父母、子女、同父或同母的手足）不得配對（config/demo KinExclusion）。
    /// 開局人口沒有記錄父母，彼此不算近親。
    /// </summary>
    public bool IsCloseKinOf(DemoTempPerson other)
        => other.Id == MotherId || other.Id == BirthFatherId
        || other.MotherId == Id || other.BirthFatherId == Id
        || (MotherId is not null && MotherId == other.MotherId)
        || (BirthFatherId is not null && BirthFatherId == other.BirthFatherId);

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
    public int LastWeek { get; }
    public long NextId { get; }
    public int Count => _people.Count;
    public IReadOnlyList<DemoTempPerson> People => _people.Values.ToArray();

    private DemoTempDemography(int week, long nextId,
        ImmutableSortedDictionary<long, DemoTempPerson> people)
    {
        LastWeek = week;
        NextId = nextId;
        _people = people;
    }

    /// <summary>
    /// 第 07 章 a 節：混齡開局。每人的年齡層、年齡（含年內出生日）、性別由 <paramref name="draws"/>
    /// （世界種子 ECOLOGY_INIT 流的實體局部亂數）抽出；年齡層比例取自 config/demo/demography-temp.json【TEMP】。
    /// 不傳 <paramref name="draws"/> 時沿用舊的全成人開局（18～22 歲、男女交替，只供單元測試）。
    /// </summary>
    /// <remarks>
    /// C04：傳入 <paramref name="ids"/>（世界唯一實體 ID 分配器）時，每個人的 ID 依聚落、人序向它取號，
    /// <see cref="NextId"/> 等於分配器取號後的下一號；不傳時沿用獨立序號（只供單元測試）。
    /// </remarks>
    public static DemoTempDemography Open(IReadOnlyDictionary<int, int> settlements,
        DemoTempDemographyRules rules, EntityIdAllocator? ids = null, DemoOpeningDraws? draws = null)
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
                if (ids is not null) id = DemoEntityIds.NextLong(ids);
                people.Add(id, draws is null
                    ? new DemoTempPerson(id, settlement, i % 2 == 0 ? DemoTempSex.Female : DemoTempSex.Male,
                        checked(-(rules.AdultAgeYears + i % 5) * 365))
                    : OpeningPerson(id, settlement, draws));
                id = ids is null ? checked(id + 1) : DemoEntityIds.NextValue(ids.Capture());
            }
        }
        return Restore(new DemoTempDemographicSnapshot(0, id,
            people.Values.ToImmutableArray()), rules);
    }

    /// <summary>第 07 章 a 節：依年齡層比例抽一個開局人（年齡層→年齡→年內出生日；性別各半）。</summary>
    internal static DemoTempPerson OpeningPerson(long id, int settlement, DemoOpeningDraws draws)
    {
        var bands = DemoDemographyTempSettings.Current.OpeningAgeBands;
        var raw = draws.Age(id);
        var pick = (int)(raw % 100);
        var band = bands[^1];
        var cumulative = 0;
        foreach (var b in bands)
        {
            cumulative += b.Percent;
            if (pick < cumulative) { band = b; break; }
        }
        var span = (ulong)(band.MaxAge - band.MinAge + 1);
        var rest = raw / 100;
        var age = band.MinAge + (int)(rest % span);
        var dayInYear = (int)(rest / span % 365);
        var sex = draws.IsFemale(id) ? DemoTempSex.Female : DemoTempSex.Male;
        return new DemoTempPerson(id, settlement, sex, checked(-(age * 365 + dayInYear)));
    }

    public DemoTempDemographicSnapshot Snapshot()
        => new(LastWeek, NextId, _people.Values.ToImmutableArray());

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
            if (p.MotherId is <= 0 || p.MotherId >= p.Id || p.BirthFatherId is <= 0 || p.BirthFatherId >= p.Id
                || p.MotherId.HasValue != p.BirthFatherId.HasValue || p.PartnerId is <= 0 || p.PartnerId == p.Id)
                throw new InvalidOperationException("Invalid TEMP parent or partner identity.");
            if (builder.ContainsKey(p.Id))
                throw new InvalidOperationException("Duplicate TEMP population identity.");
            builder.Add(p.Id, p);
        }
        // 第 07 章 a 節：伴侶必須在名冊上、互為伴侶、異性、皆成年且不是近親。
        foreach (var p in builder.Values.Where(p => p.PartnerId is not null))
        {
            if (!builder.TryGetValue(p.PartnerId!.Value, out var partner) || partner.PartnerId != p.Id
                || partner.Sex == p.Sex || !p.IsAdultAt(snapshot.LastWeek * 7, rules) || p.IsCloseKinOf(partner))
                throw new InvalidOperationException("Invalid TEMP partnership.");
        }
        return new DemoTempDemography(snapshot.LastWeek, snapshot.NextId,
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
        return new DemoTempDemography(LastWeek, NextId, next.ToImmutable());
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
            new DemoTempDemography(LastWeek, NextId, next.ToImmutable()), events.ToImmutable());
    }

    /// <summary>Advance exactly one 7-day week. Nutritional status must come from real Core food accounts.</summary>
    /// <remarks>
    /// C04：傳入 <paramref name="ids"/> 時新生兒向世界唯一分配器取號（依母親 ID 順序）；不傳時沿用獨立序號（只供單元測試）。
    /// 第 04 章 a 節：自然死亡、新生兒性別、流產、受孕全部由 <paramref name="draws"/>（歷史種子的 POP 流、實體局部亂數）決定；
    /// 新生兒性別用已發出的新生兒 ID 抽。
    /// </remarks>
    public DemoTempDemographicOutcome AdvanceWeek(int week,
        Func<int, bool> settlementFed, DemoTempDemographyRules rules, DemoPopulationDraws draws, EntityIdAllocator? ids = null)
    {
        ArgumentNullException.ThrowIfNull(settlementFed);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(draws);
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
            if (!draws.Hit(p.Id, week, DemoPopulationAction.NaturalDeath, chance)) continue;
            next.Remove(p.Id);
            events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.NaturalDeath,
                p.Id, p.SettlementId));
        }

        Pair(next, day, rules);

        foreach (var p in _people.Values.Where(p => p.Sex == DemoTempSex.Female))
        {
            if (!next.TryGetValue(p.Id, out var mother)) continue;
            if (mother.PregnancyDueWeek is int due)
            {
                if (due == week)
                {
                    if (ids is not null) nextId = DemoEntityIds.NextLong(ids);
                    var newborn = new DemoTempPerson(nextId, mother.SettlementId,
                        draws.Hit(nextId, week, DemoPopulationAction.NewbornSex, 0.5) ? DemoTempSex.Female : DemoTempSex.Male, day,
                        MotherId: mother.Id, BirthFatherId: mother.FatherId);
                    next.Add(nextId, newborn);
                    events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Birth,
                        nextId, mother.SettlementId, mother.Id, mother.FatherId));
                    nextId = ids is null ? checked(nextId + 1) : DemoEntityIds.NextValue(ids.Capture());
                    next[mother.Id] = mother with { PregnancyDueWeek = null, FatherId = null };
                }
                else if (draws.Hit(mother.Id, week, DemoPopulationAction.Miscarriage, rules.WeeklyMiscarriageChance))
                {
                    events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Miscarriage,
                        mother.Id, mother.SettlementId, mother.Id, mother.FatherId));
                    next[mother.Id] = mother with { PregnancyDueWeek = null, FatherId = null };
                }
                continue;
            }

            // 第 07 章 a 節：只與穩定伴侶受孕；育齡上限【TEMP】；聚落本週真實糧帳不足時不受孕。
            if (mother.AgeYearsAt(day) < rules.AdultAgeYears || mother.AgeYearsAt(day) >= rules.FertileAgeMaxYears
                || !settlementFed(mother.SettlementId) || mother.PartnerId is not long partnerId
                || !next.TryGetValue(partnerId, out var father))
                continue;
            if (!draws.Hit(mother.Id, week, DemoPopulationAction.Conception, rules.WeeklyConceptionChance))
                continue;
            next[mother.Id] = mother with { PregnancyDueWeek = checked(week + rules.GestationWeeks), FatherId = father.Id };
            events.Add(new DemoTempDemographicEvent(week, DemoTempDemographicKind.Conception,
                mother.Id, mother.SettlementId, mother.Id, father.Id));
        }

        // 出生之後清除（尚未成年的新生兒不會配對；死者已在配對前清除）。
        var after = new DemoTempDemography(week, nextId, next.ToImmutable());
        if (after.Count != Count + events.Count(x => x.Kind == DemoTempDemographicKind.Birth)
            - events.Count(x => x.Kind == DemoTempDemographicKind.NaturalDeath))
            throw new InvalidOperationException("TEMP births and natural deaths failed conservation.");
        return new DemoTempDemographicOutcome(after, events.ToImmutable());
    }

    /// <summary>
    /// 第 07 章 a 節：穩定配對【TEMP】。先解除伴侶已不在名冊或不在同聚落的配對；
    /// 再依 ID 順序，讓每位未配對的成年女性配給同聚落、ID 最小、未配對、非近親的成年男性。不用亂數。
    /// </summary>
    private static void Pair(ImmutableSortedDictionary<long, DemoTempPerson>.Builder people, int day, DemoTempDemographyRules rules)
    {
        foreach (var p in people.Values.Where(p => p.PartnerId is not null).ToArray())
        {
            if (!people.TryGetValue(p.PartnerId!.Value, out var partner) || partner.SettlementId != p.SettlementId)
                people[p.Id] = p with { PartnerId = null };
        }
        var freeMen = people.Values.Where(m => m.Sex == DemoTempSex.Male && m.PartnerId is null && m.IsAdultAt(day, rules))
            .ToList();
        foreach (var woman in people.Values.Where(w => w.Sex == DemoTempSex.Female && w.PartnerId is null
            && w.IsAdultAt(day, rules)).ToArray())
        {
            var man = freeMen.FirstOrDefault(m => m.SettlementId == woman.SettlementId && !woman.IsCloseKinOf(m));
            if (man is null) continue;
            freeMen.Remove(man);
            people[woman.Id] = woman with { PartnerId = man.Id };
            people[man.Id] = man with { PartnerId = woman.Id };
        }
    }
}
