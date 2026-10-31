using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 07-1 章 a 節：一個年齡群——某地（聚落，或途中隊伍）在某一年出生、某性別的人數。
/// <see cref="BatchId"/>＝0 表示定居在 <see cref="SettlementId"/>；大於 0 表示在該遷徙隊伍裡（SettlementId 為出發聚落）。
/// 出生年＝世界年索引（第 0～364 日為第 0 年，開局的人可為負）。
/// </summary>
public sealed record DemoCohortGroup(int SettlementId, int BatchId, int BirthYear, DemoTempSex Sex, long Count);

/// <summary>第 07-1 章 a 節：某地在某週到期的懷孕數。</summary>
public sealed record DemoPregnancyGroup(int SettlementId, int BatchId, int DueWeek, long Count);

/// <summary>
/// 第 07-1 章 b 節：代表人物的身分。第 08-5 章（使用者 2026-10-23）：只有權力者（村長）與持有專業知識的人
/// （木匠、鐵匠…，隨知識樹出現）才算代表人物；採集、漁撈不是專業知識，不再設代表。
/// </summary>
public enum DemoRepresentativeRole
{
    /// <summary>村長（權力者）。</summary>
    Leader = 0,

    /// <summary>第 08-6 章：農師（農耕知識持有者）。</summary>
    Farmer = 1,

    /// <summary>第 08-6 章 d 節：木匠（木工知識持有者；聚落升為村莊的條件）。</summary>
    Carpenter = 2,

    /// <summary>第 08-6 章 e 節：鐵匠（鍛造知識持有者）。</summary>
    Smith = 3,

    /// <summary>第 08-9 章 A2（08-7b）：獵人（弓箭）。</summary>
    Hunter = 4,

    /// <summary>第 08-9 章 A2（08-7b）：巫祝（巫祝；專家，兼醫者）。</summary>
    Shaman = 5,

    /// <summary>第 08-9 章 A2（08-7b 第 3 節）：學者（抄寫法起補任；所有領域研究加速）。</summary>
    Scholar = 6
}

/// <summary>
/// 第 07-1 章 b 節：有名有姓的代表人物（使用者 2026-10-22：只需要知道村子重要的人）。
/// 他本人也算在所屬年齡群的人數裡（具名化不是出生）；實體 ID 由世界唯一分配器發號。
/// </summary>
public sealed record DemoRepresentative(long Id, int SettlementId, DemoRepresentativeRole Role, string Name, DemoTempSex Sex,
    int BirthYear, int AppointedWeek)
{
    public static string RoleName(DemoRepresentativeRole role) => role switch
    {
        DemoRepresentativeRole.Leader => "村長",
        DemoRepresentativeRole.Farmer => "農師",
        DemoRepresentativeRole.Carpenter => "木匠",
        DemoRepresentativeRole.Smith => "鐵匠",
        DemoRepresentativeRole.Hunter => "獵人",
        DemoRepresentativeRole.Shaman => "巫祝",
        DemoRepresentativeRole.Scholar => "學者",
        _ => "代表"
    };

    /// <summary>第 08-6 章：專業知識持有者需要的技術（權力者為 null）。</summary>
    public static DemoTechnology? TechnologyOf(DemoRepresentativeRole role) => role switch
    {
        DemoRepresentativeRole.Farmer => DemoTechnology.CropCultivation,
        DemoRepresentativeRole.Carpenter => DemoTechnology.BasicCarpentry,
        DemoRepresentativeRole.Smith => DemoTechnology.BasicForging,
        DemoRepresentativeRole.Hunter => DemoTechnology.Of("TECH_ARCHERY"),
        DemoRepresentativeRole.Shaman => DemoTechnology.Of("TECH_SHAMANISM"),
        DemoRepresentativeRole.Scholar => DemoTechnology.Of("TECH_SCRIBAL_METHOD"),
        _ => null
    };
}

/// <summary>第 07-1 章 b 節：代表人物卸任（老死、聚落無人）。</summary>
public sealed record DemoRepresentativeExit(long Id, int SettlementId, string Reason);

/// <summary>第 07-1 章 a 節：群體人口快照（存檔與讀檔比對用）；b 節加在任的代表人物。</summary>
public sealed record DemoCohortSnapshot(int LastWeek, ImmutableArray<DemoCohortGroup> Groups, ImmutableArray<DemoPregnancyGroup> Pregnancies)
{
    public ImmutableArray<DemoRepresentative> Representatives { get; init; } = ImmutableArray<DemoRepresentative>.Empty;
}

/// <summary>第 07-1 章 a 節：某地的人口結構統計。</summary>
public sealed record DemoCohortStats(long Total, long Children, long Adults, long Elders, long FertileWomen, long FertileMen,
    long PartnerMen, long Pregnant)
{
    /// <summary>【TEMP】可受孕的伴侶數＝育齡女性與可配對男性（成年未滿老年）的較小者。</summary>
    public long Couples => Math.Min(FertileWomen, PartnerMen);

    /// <summary>未配對的育齡女性（男性不夠）。</summary>
    public long UnpairedWomen => Math.Max(0, FertileWomen - PartnerMen);

    /// <summary>未配對的育齡男性（育齡女性不夠）。</summary>
    public long UnpairedMen => Math.Max(0, FertileMen - FertileWomen);
}

/// <summary>
/// 第 07-1 章 a 節：群體人口（使用者 2026-10-22 定案：絕大多數人只是統計數字）。
/// 人口依地點、出生年、性別分組記人數；懷孕依地點與到期週記數。不建立個體、不配實體 ID。
/// 不可變：每次變動回傳新物件。人口只因合法出生、死亡、遷徙改變（總綱第 4 節守恆）。
/// </summary>
public sealed class DemoCohortDemography
{
    public const int DaysPerYear = 365;

    private readonly ImmutableSortedDictionary<(int Settlement, int Batch, int BirthYear, DemoTempSex Sex), long> _groups;
    private readonly ImmutableSortedDictionary<(int Settlement, int Batch, int DueWeek), long> _pregnancies;

    public int LastWeek { get; }

    /// <summary>第 07-1 章 b 節：在任的代表人物（依 ID）。</summary>
    public ImmutableList<DemoRepresentative> Representatives { get; }

    private DemoCohortDemography(int lastWeek,
        ImmutableSortedDictionary<(int, int, int, DemoTempSex), long> groups,
        ImmutableSortedDictionary<(int, int, int), long> pregnancies,
        ImmutableList<DemoRepresentative>? representatives = null)
    {
        LastWeek = lastWeek;
        _groups = groups;
        _pregnancies = pregnancies;
        Representatives = representatives ?? ImmutableList<DemoRepresentative>.Empty;
    }

    /// <summary>換上新的代表人物名單（依 ID 排序）；每位都必須在所屬聚落有對應的年齡群人數。</summary>
    public DemoCohortDemography WithRepresentatives(IEnumerable<DemoRepresentative> representatives)
    {
        var list = representatives.OrderBy(r => r.Id).ToImmutableList();
        foreach (var g in list.GroupBy(r => (r.SettlementId, 0, r.BirthYear, r.Sex)))
        {
            if (_groups.GetValueOrDefault(g.Key) < g.Count())
                throw new InvalidOperationException("A representative is not counted in a settled cohort.");
        }
        if (list.Select(r => r.Id).Distinct().Count() != list.Count
            || list.GroupBy(r => (r.SettlementId, r.Role)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate representative identity or role.");
        return new DemoCohortDemography(LastWeek, _groups, _pregnancies, list);
    }

    /// <summary>某年齡群裡已是代表人物的人數（外移時不帶走他們）。</summary>
    public long RepresentativesIn(int settlementId, int birthYear, DemoTempSex sex)
        => (_repIndex ??= Representatives.GroupBy(r => (r.SettlementId, r.BirthYear, r.Sex)).ToDictionary(g => g.Key, g => (long)g.Count()))
            .GetValueOrDefault((settlementId, birthYear, sex));

    // 第 08-13 章第 5 節：依地點（聚落、隊伍）索引年齡群、懷孕與代表人物（物件不可變，第一次用到時建立；結果與逐一掃描相同）。
    private Dictionary<(int Settlement, int BirthYear, DemoTempSex Sex), long>? _repIndex;
    private Dictionary<(int Settlement, int Batch), List<KeyValuePair<(int Settlement, int Batch, int BirthYear, DemoTempSex Sex), long>>>? _placeIndex;
    private Dictionary<(int Settlement, int Batch), long>? _pregIndex;
    private Dictionary<int, long>? _batchIndex;

    private IReadOnlyList<KeyValuePair<(int Settlement, int Batch, int BirthYear, DemoTempSex Sex), long>> GroupsOfPlace(int settlementId, int batchId)
    {
        _placeIndex ??= _groups.GroupBy(kv => (kv.Key.Settlement, kv.Key.Batch)).ToDictionary(g => g.Key, g => g.ToList());
        return _placeIndex.TryGetValue((settlementId, batchId), out var list)
            ? list : Array.Empty<KeyValuePair<(int Settlement, int Batch, int BirthYear, DemoTempSex Sex), long>>();
    }

    public static int YearOfDay(int day) => (int)Math.Floor(day / (double)DaysPerYear);

    /// <summary>出生年 <paramref name="birthYear"/> 的人在第 <paramref name="day"/> 日的年齡（年）。</summary>
    public static int AgeAt(int birthYear, int day) => Math.Max(0, YearOfDay(day) - birthYear);

    public long Total => _groups.Values.Sum();

    public IEnumerable<DemoCohortGroup> Groups
        => _groups.Select(kv => new DemoCohortGroup(kv.Key.Settlement, kv.Key.Batch, kv.Key.BirthYear, kv.Key.Sex, kv.Value));

    public IEnumerable<DemoPregnancyGroup> Pregnancies
        => _pregnancies.Select(kv => new DemoPregnancyGroup(kv.Key.Settlement, kv.Key.Batch, kv.Key.DueWeek, kv.Value));

    public DemoCohortSnapshot Snapshot() => new(LastWeek, Groups.ToImmutableArray(), Pregnancies.ToImmutableArray())
    {
        Representatives = Representatives.ToImmutableArray()
    };

    /// <summary>從快照還原；拒絕非正數、重複、懷孕不在育齡女性所在地或已到期等不合法內容。</summary>
    public static DemoCohortDemography Restore(DemoCohortSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LastWeek < 0 || snapshot.Groups.IsDefault || snapshot.Pregnancies.IsDefault)
            throw new InvalidOperationException("Malformed cohort snapshot.");
        var groups = ImmutableSortedDictionary.CreateBuilder<(int, int, int, DemoTempSex), long>();
        foreach (var g in snapshot.Groups)
        {
            if (g is null || g.SettlementId < 1 || g.BatchId < 0 || g.Count <= 0 || !Enum.IsDefined(g.Sex)
                || g.BirthYear > YearOfDay(snapshot.LastWeek * 7) || !groups.TryAdd((g.SettlementId, g.BatchId, g.BirthYear, g.Sex), g.Count))
                throw new InvalidOperationException("Invalid cohort group.");
        }
        var pregnancies = ImmutableSortedDictionary.CreateBuilder<(int, int, int), long>();
        foreach (var p in snapshot.Pregnancies)
        {
            if (p is null || p.SettlementId < 1 || p.BatchId < 0 || p.Count <= 0 || p.DueWeek <= snapshot.LastWeek
                || !pregnancies.TryAdd((p.SettlementId, p.BatchId, p.DueWeek), p.Count))
                throw new InvalidOperationException("Invalid pregnancy group.");
        }
        var restored = new DemoCohortDemography(snapshot.LastWeek, groups.ToImmutable(), pregnancies.ToImmutable());
        return snapshot.Representatives.IsDefaultOrEmpty ? restored : restored.WithRepresentatives(snapshot.Representatives);
    }

    /// <summary>
    /// 第 07-1 章 a 節：混齡開局。每個聚落沿用第 07 章／08-1 章的開局規則（前六人是三對育齡建村夫婦，其餘依年齡層比例；
    /// 年齡、性別由世界種子 ECOLOGY_INIT 流的局部亂數抽出），抽完後只記入各年齡群的人數。
    /// 抽樣鍵＝聚落 ID×1000＋人序（不配實體 ID）。
    /// </summary>
    public static DemoCohortDemography Open(IReadOnlyDictionary<int, int> settlements, DemoTempDemographyRules rules,
        DemoOpeningDraws draws)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(draws);
        rules.Validate();
        var groups = ImmutableSortedDictionary.CreateBuilder<(int, int, int, DemoTempSex), long>();
        foreach (var (settlement, count) in settlements.OrderBy(kv => kv.Key))
        {
            if (settlement <= 0 || count < 0 || count >= 1000)
                throw new ArgumentOutOfRangeException(nameof(settlements));
            for (var slot = 0; slot < count; slot++)
            {
                var (sex, age) = OpeningPerson(checked(settlement * 1000L + slot + 1), slot, draws, rules);
                var key = (settlement, 0, -age, sex); // 第 0 年時剛好 age 歲
                groups[key] = groups.GetValueOrDefault(key) + 1;
            }
        }
        return new DemoCohortDemography(0, groups.ToImmutable(), ImmutableSortedDictionary<(int, int, int), long>.Empty);
    }

    private static (DemoTempSex Sex, int Age) OpeningPerson(long key, int slot, DemoOpeningDraws draws, DemoTempDemographyRules rules)
    {
        var bands = DemoDemographyTempSettings.Current.OpeningAgeBands;
        var raw = draws.Age(key);
        var pick = (int)(raw % 100);
        var band = bands[^1];
        var cumulative = 0;
        foreach (var b in bands)
        {
            cumulative += b.Percent;
            if (pick < cumulative) { band = b; break; }
        }
        // 第 08-1 章【TEMP】：前六人是三對建村夫婦。
        if (slot < 6)
            band = bands.First(b => b.MinAge >= rules.AdultAgeYears && b.MaxAge < rules.FertileAgeMaxYears);
        var span = (ulong)(band.MaxAge - band.MinAge + 1);
        var rest = raw / 100;
        var age = band.MinAge + (int)(rest % span);
        var sex = slot switch
        {
            0 or 2 or 4 => DemoTempSex.Female,
            1 or 3 or 5 => DemoTempSex.Male,
            _ => draws.IsFemale(key) ? DemoTempSex.Female : DemoTempSex.Male
        };
        return (sex, age);
    }

    /// <summary>某地的人口結構（<paramref name="batchId"/>＝0 為定居者）。</summary>
    public DemoCohortStats StatsOf(int settlementId, int batchId, int day, DemoTempDemographyRules rules)
    {
        long total = 0, children = 0, adults = 0, elders = 0, fw = 0, fm = 0, pm = 0;
        var elder = DemoSurvivalTempSettings.Current.ElderAgeYears;
        foreach (var (key, n) in GroupsOfPlace(settlementId, batchId))
        {
            var age = AgeAt(key.BirthYear, day);
            total += n;
            if (age < rules.AdultAgeYears) { children += n; continue; }
            if (age >= elder) elders += n; else adults += n;
            var fertile = age < rules.FertileAgeMaxYears;
            if (key.Sex == DemoTempSex.Female && fertile) fw += n;
            if (key.Sex == DemoTempSex.Male)
            {
                if (fertile) fm += n;
                if (age < elder) pm += n;
            }
        }
        var pregnant = (_pregIndex ??= _pregnancies.GroupBy(kv => (kv.Key.Settlement, kv.Key.Batch)).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value)))
            .GetValueOrDefault((settlementId, batchId));
        return new DemoCohortStats(total, children, adults, elders, fw, fm, pm, pregnant);
    }

    /// <summary>全世界的人口結構（定居＋途中）。</summary>
    public DemoCohortStats WorldStats(int day, DemoTempDemographyRules rules)
    {
        var places = _groups.Keys.Select(k => (k.Settlement, k.Batch)).Distinct().ToList();
        var all = places.Select(p => StatsOf(p.Settlement, p.Batch, day, rules)).ToList();
        return new DemoCohortStats(all.Sum(s => s.Total), all.Sum(s => s.Children), all.Sum(s => s.Adults), all.Sum(s => s.Elders),
            all.Sum(s => s.FertileWomen), all.Sum(s => s.FertileMen), all.Sum(s => s.PartnerMen), _pregnancies.Values.Sum());
    }

    /// <summary>本週（最後一週）定居在某聚落的成年人數（採集與取水勞力）。</summary>
    public long AdultsIn(int settlementId, DemoTempDemographyRules rules)
    {
        var day = LastWeek * 7;
        return _groups.Where(kv => kv.Key.Settlement == settlementId && kv.Key.Batch == 0
            && AgeAt(kv.Key.BirthYear, day) >= rules.AdultAgeYears).Sum(kv => kv.Value);
    }

    public long ResidentsOf(int settlementId) => GroupsOfPlace(settlementId, 0).Sum(kv => kv.Value);

    public long MembersOf(int batchId) => batchId == 0 ? _groups.Where(kv => kv.Key.Batch == 0).Sum(kv => kv.Value)
        : (_batchIndex ??= _groups.Where(kv => kv.Key.Batch != 0).GroupBy(kv => kv.Key.Batch).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value)))
            .GetValueOrDefault(batchId);

    /// <summary>某地各年齡群（依出生年、性別）。</summary>
    public IReadOnlyList<DemoCohortGroup> GroupsAt(int settlementId, int batchId)
        => Groups.Where(g => g.SettlementId == settlementId && g.BatchId == batchId).ToList();

    /// <summary>
    /// 出發：定居者依 <paramref name="take"/>（出生年、性別 → 人數）加入隊伍 <paramref name="batchId"/>，
    /// 另有 <paramref name="pregnancies"/> 個懷孕（最早到期者先）隨隊。
    /// </summary>
    /// <summary>
    /// 第 08-6 章 b 節【TEMP 規則】餓死（開拓時代起）：週帳已決定人數，這裡決定是哪些年齡群——
    /// 先老人（年長者先），再兒童（年幼者先），再成年人（年長者先）；代表人物最後才輪到（同一年齡群依 ID）。
    /// 定居者 <paramref name="batchId"/>＝0；途中隊伍為隊伍編號。不抽亂數。
    /// </summary>
    public (DemoCohortDemography Population, IReadOnlyList<DemoRepresentative> StarvedRepresentatives, long Children, long Elders) Starve(
        int settlementId, int batchId, long deaths, int day, DemoTempDemographyRules rules, int elderAge)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (deaths <= 0)
            return (this, Array.Empty<DemoRepresentative>(), 0, 0);
        var mine = _groups.Where(kv => kv.Key.Batch == batchId && (batchId > 0 || kv.Key.Settlement == settlementId))
            .Select(kv => (kv.Key, kv.Value, Age: AgeAt(kv.Key.BirthYear, day))).ToList();
        if (mine.Sum(g => g.Value) < deaths)
            throw new InvalidOperationException("Starving more people than the cohorts hold.");
        var ordered = mine.Where(g => g.Age >= elderAge).OrderBy(g => g.Key.BirthYear).ThenBy(g => g.Key.Sex)
            .Concat(mine.Where(g => g.Age < rules.AdultAgeYears).OrderByDescending(g => g.Key.BirthYear).ThenBy(g => g.Key.Sex))
            .Concat(mine.Where(g => g.Age >= rules.AdultAgeYears && g.Age < elderAge).OrderBy(g => g.Key.BirthYear).ThenBy(g => g.Key.Sex))
            .ToList();
        long RepsIn((int Settlement, int Batch, int BirthYear, DemoTempSex Sex) key)
            => key.Batch == 0 ? RepresentativesIn(key.Settlement, key.BirthYear, key.Sex) : 0;
        var take = new Dictionary<(int, int, int, DemoTempSex), long>();
        var left = deaths;
        foreach (var g in ordered)
        {
            if (left == 0) break;
            var n = Math.Min(left, g.Value - RepsIn(g.Key));
            if (n > 0) { take[g.Key] = n; left -= n; }
        }
        var starvedReps = new List<DemoRepresentative>();
        foreach (var g in ordered)
        {
            if (left == 0) break;
            foreach (var r in Representatives.Where(r => g.Key.Batch == 0 && r.SettlementId == g.Key.Settlement
                         && r.BirthYear == g.Key.BirthYear && r.Sex == g.Key.Sex).OrderBy(r => r.Id))
            {
                if (left == 0) break;
                take[g.Key] = take.GetValueOrDefault(g.Key) + 1;
                starvedReps.Add(r);
                left--;
            }
        }
        var groups = _groups.ToBuilder();
        long children = 0, elders = 0;
        foreach (var (key, n) in take)
        {
            Set(groups, key, groups[key] - n);
            var age = AgeAt(key.Item3, day);
            if (age < rules.AdultAgeYears) children += n;
            else if (age >= elderAge) elders += n;
        }
        var reps = Representatives.Where(r => starvedReps.All(x => x.Id != r.Id)).ToImmutableList();
        return (new DemoCohortDemography(LastWeek, groups.ToImmutable(), _pregnancies, reps), starvedReps, children, elders);
    }

    public DemoCohortDemography Depart(int settlementId, IReadOnlyDictionary<(int BirthYear, DemoTempSex Sex), long> take,
        long pregnancies, int batchId)
    {
        ArgumentNullException.ThrowIfNull(take);
        if (batchId < 1 || pregnancies < 0)
            throw new InvalidOperationException("Invalid cohort departure.");
        var groups = _groups.ToBuilder();
        foreach (var ((year, sex), n) in take)
        {
            if (n <= 0) continue;
            var from = (settlementId, 0, year, sex);
            var have = groups.GetValueOrDefault(from);
            if (have < n) throw new InvalidOperationException("Departing more people than the cohort holds.");
            Set(groups, from, have - n);
            var to = (settlementId, batchId, year, sex);
            Set(groups, to, groups.GetValueOrDefault(to) + n);
        }
        var preg = _pregnancies.ToBuilder();
        var left = pregnancies;
        foreach (var (key, n) in _pregnancies.Where(kv => kv.Key.Settlement == settlementId && kv.Key.Batch == 0).ToList())
        {
            if (left == 0) break;
            var move = Math.Min(left, n);
            Set(preg, key, n - move);
            var to = (settlementId, batchId, key.DueWeek);
            Set(preg, to, preg.GetValueOrDefault(to) + move);
            left -= move;
        }
        if (left != 0) throw new InvalidOperationException("Departing more pregnancies than the settlement has.");
        return new DemoCohortDemography(LastWeek, groups.ToImmutable(), preg.ToImmutable(), Representatives);
    }

    /// <summary>隊伍全體（含懷孕）抵達並定居 <paramref name="settlementId"/>。</summary>
    public DemoCohortDemography Arrive(int batchId, int settlementId)
    {
        if (batchId < 1 || settlementId < 1)
            throw new InvalidOperationException("Invalid cohort arrival.");
        var groups = _groups.ToBuilder();
        foreach (var (key, n) in _groups.Where(kv => kv.Key.Batch == batchId))
        {
            groups.Remove(key);
            var to = (settlementId, 0, key.BirthYear, key.Sex);
            groups[to] = groups.GetValueOrDefault(to) + n;
        }
        var preg = _pregnancies.ToBuilder();
        foreach (var (key, n) in _pregnancies.Where(kv => kv.Key.Batch == batchId))
        {
            preg.Remove(key);
            var to = (settlementId, 0, key.DueWeek);
            preg[to] = preg.GetValueOrDefault(to) + n;
        }
        return new DemoCohortDemography(LastWeek, groups.ToImmutable(), preg.ToImmutable(), Representatives);
    }

    private static void Set<TKey>(ImmutableSortedDictionary<TKey, long>.Builder b, TKey key, long value) where TKey : notnull
    {
        if (value < 0) throw new InvalidOperationException("Negative cohort count.");
        if (value == 0) b.Remove(key); else b[key] = value;
    }

    /// <summary>
    /// 推進一週：① 自然死亡（每個年齡群依 39C 年齡風險抽二項分布；希望時代只有老年）→ ② 到期分娩（性別各半抽二項）
    /// → ③ 流產 → ④ 受孕（只在定居聚落；可受孕伴侶＝伴侶數－已懷孕，數量不超過 <paramref name="conceptionRoom"/>）。
    /// 每一項都以地點與年齡群為鍵向 POP 流的局部亂數抽一次（與處理順序無關、可重現）。
    /// </summary>
    public DemoCohortOutcome AdvanceWeek(int week, Func<int, long> conceptionRoom, DemoTempDemographyRules rules, DemoPopulationDraws draws,
        IReadOnlyDictionary<(int Settlement, int Batch, int BirthYear, DemoTempSex Sex), long>? minimumDeaths = null)
    {
        ArgumentNullException.ThrowIfNull(conceptionRoom);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(draws);
        if (week != LastWeek + 1)
            throw new InvalidOperationException("Cohort weeks must be consecutive.");
        var day = checked(week * 7);
        var thisYear = YearOfDay(day);
        var groups = _groups.ToBuilder();
        var preg = _pregnancies.ToBuilder();
        var events = new List<DemoCohortEvent>();

        // ① 自然死亡。
        foreach (var (key, n) in _groups)
        {
            var age = AgeAt(key.BirthYear, day);
            var chance = rules.WeeklyNaturalDeathChance(age);
            if (chance <= 0) continue;
            var dead = DemoBinomial.Draw(n, chance, draws.Raw(CohortKey(key.Settlement, key.Batch, key.BirthYear, key.Sex), week, DemoPopulationAction.NaturalDeath));
            // 第 07-1 章 b 節：代表人物本人老死時，所屬年齡群至少死這麼多人（他就是其中之一）。
            var repDead = minimumDeaths?.GetValueOrDefault(key) ?? 0;
            dead = Math.Min(n, Math.Max(dead, repDead));
            // 存活的代表人物不在年齡群的死亡人數裡（死亡人數上限＝人數－存活代表人物數）。
            var repAlive = key.Batch == 0 ? Math.Max(0, RepresentativesIn(key.Settlement, key.BirthYear, key.Sex) - repDead) : 0;
            dead = Math.Min(dead, Math.Max(0, n - repAlive));
            if (dead == 0) continue;
            Set(groups, key, n - dead);
            events.Add(new DemoCohortEvent(DemoTempDemographicKind.NaturalDeath, key.Settlement, key.Batch, dead, age));
        }

        // 第 08-12 章：途中隊伍全員死亡（途中餓死或本週老死）後，隨隊的懷孕跟著消失——
        // 否則孩子會「出生」在已解散、沒有隊伍帳的隊伍裡，群體人口比世界人口多一人（長測種子 7 第 85 年）。
        var aliveBatches = groups.Where(g => g.Key.Batch > 0 && g.Value > 0).Select(g => g.Key.Batch).ToHashSet();
        foreach (var key in preg.Keys.Where(k => k.Item2 > 0 && !aliveBatches.Contains(k.Item2)).ToList())
            preg.Remove(key);

        // ② 到期分娩、③ 流產。
        foreach (var (key, n) in _pregnancies)
        {
            if (key.DueWeek == week)
            {
                preg.Remove(key);
                var girls = DemoBinomial.Draw(n, 0.5, draws.Raw(PregnancyKey(key.Settlement, key.Batch, key.DueWeek), week, DemoPopulationAction.NewbornSex));
                foreach (var (sex, count) in new[] { (DemoTempSex.Female, girls), (DemoTempSex.Male, n - girls) })
                {
                    if (count == 0) continue;
                    var to = (key.Settlement, key.Batch, thisYear, sex);
                    groups[to] = groups.GetValueOrDefault(to) + count;
                }
                events.Add(new DemoCohortEvent(DemoTempDemographicKind.Birth, key.Settlement, key.Batch, n, 0));
                continue;
            }
            var lost = DemoBinomial.Draw(n, rules.WeeklyMiscarriageChance, draws.Raw(PregnancyKey(key.Settlement, key.Batch, key.DueWeek), week, DemoPopulationAction.Miscarriage));
            if (lost > 0)
            {
                Set(preg, key, n - lost);
                events.Add(new DemoCohortEvent(DemoTempDemographicKind.Miscarriage, key.Settlement, key.Batch, lost, 0));
            }
        }

        // ④ 受孕：只在定居聚落（途中不受孕【TEMP】），依本週死亡、分娩之後的人口結構。
        var after = new DemoCohortDemography(week, groups.ToImmutable(), preg.ToImmutable(), Representatives);
        foreach (var settlement in groups.Keys.Where(k => k.Batch == 0).Select(k => k.Settlement).Distinct().ToList())
        {
            var stats = after.StatsOf(settlement, 0, day, rules);
            var eligible = Math.Max(0, stats.Couples - stats.Pregnant);
            if (eligible == 0) continue;
            var room = Math.Max(0, conceptionRoom(settlement));
            if (room == 0) continue;
            var conceived = Math.Min(room, DemoBinomial.Draw(eligible, rules.WeeklyConceptionChance,
                draws.Raw(PregnancyKey(settlement, 0, 0), week, DemoPopulationAction.Conception)));
            if (conceived == 0) continue;
            var due = (settlement, 0, checked(week + rules.GestationWeeks));
            preg[due] = preg.GetValueOrDefault(due) + conceived;
            events.Add(new DemoCohortEvent(DemoTempDemographicKind.Conception, settlement, 0, conceived, 0));
        }

        var result = new DemoCohortDemography(week, groups.ToImmutable(), preg.ToImmutable(), Representatives);
        var births = events.Where(e => e.Kind == DemoTempDemographicKind.Birth).Sum(e => e.Count);
        var deaths = events.Where(e => e.Kind == DemoTempDemographicKind.NaturalDeath).Sum(e => e.Count);
        if (result.Total != Total + births - deaths)
            throw new InvalidOperationException("Cohort births and deaths failed conservation.");
        return new DemoCohortOutcome(result, events);
    }

    /// <summary>年齡群的抽樣鍵（地點、出生年、性別；恆正、互不重複）。</summary>
    internal static long CohortKey(int settlement, int batch, int birthYear, DemoTempSex sex)
    {
        var place = batch > 0 ? (1L << 31) + batch : settlement;
        return checked(1 + ((place * 4096 + (birthYear + 2048)) * 2 + (sex == DemoTempSex.Male ? 1 : 0)));
    }

    /// <summary>懷孕群（地點、到期週）的抽樣鍵；到期週 0＝該地本週受孕。</summary>
    internal static long PregnancyKey(int settlement, int batch, int dueWeek)
    {
        var place = batch > 0 ? (1L << 31) + batch : settlement;
        return checked(1 + (1L << 61) / 4 + place * (1L << 20) + dueWeek);
    }
}

/// <summary>第 07-1 章 a 節：一週的群體生死紀錄（Count 人；Age＝自然死亡者的年齡）。</summary>
public sealed record DemoCohortEvent(DemoTempDemographicKind Kind, int SettlementId, int BatchId, long Count, int Age);

public sealed record DemoCohortOutcome(DemoCohortDemography Population, IReadOnlyList<DemoCohortEvent> Events);

/// <summary>
/// 第 07-1 章 a 節：以一個均勻亂數抽二項分布（可重現）。n·p 小時逆累積（精確），大時常態近似（四捨五入並截在 0～n）。
/// </summary>
public static class DemoBinomial
{
    private const double TwoPow53 = 9007199254740992d;

    public static long Draw(long n, double p, ulong raw)
    {
        if (n <= 0 || double.IsNaN(p) || p <= 0) return 0;
        if (p >= 1) return n;
        var u = (raw >> 11) / TwoPow53;
        if (n * p < 50 && n <= 5000)
        {
            var q = 1 - p;
            var pk = Math.Pow(q, n);
            var cdf = pk;
            long k = 0;
            while (u > cdf && k < n)
            {
                pk = pk * (n - k) / (k + 1) * p / q;
                k++;
                cdf += pk;
                if (pk == 0) break;
            }
            return k;
        }
        var mean = n * p;
        var sd = Math.Sqrt(n * p * (1 - p));
        var z = InverseNormal(Math.Clamp(u, 1e-12, 1 - 1e-12));
        return Math.Clamp((long)Math.Round(mean + z * sd), 0, n);
    }

    /// <summary>標準常態分布反函數（Acklam 近似）。</summary>
    private static double InverseNormal(double p)
    {
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double low = 0.02425;
        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > 1 - low)
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        var r = p - 0.5;
        var s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }
}
