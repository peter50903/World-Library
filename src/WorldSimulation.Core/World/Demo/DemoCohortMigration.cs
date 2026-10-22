namespace WorldSimulation.Core.World.Demo;

/// <summary>第 07-1 章 a 節：一次出發的組成（各年齡群人數與隨隊懷孕數）。</summary>
public sealed record DemoCohortDeparture(IReadOnlyDictionary<(int BirthYear, DemoTempSex Sex), long> Take, long Pregnancies)
{
    public static readonly DemoCohortDeparture None = new(new Dictionary<(int, DemoTempSex), long>(), 0);

    public long People => Take.Values.Sum();
}

/// <summary>
/// 第 07-1 章 a 節：群體人口的外移規劃（取代第 07 章「整戶挑選」）。純函式、不抽亂數，結算規劃與生死套用共用同一份。
/// 規則【TEMP】：
/// <list type="bullet">
/// <item>原聚落有育齡女性與可配對男性時，至少各留一人（第 08-2 章「留下一對育齡夫婦」的群體版）。</item>
/// <item>開拓：先帶兩名育齡女性、兩名可配對男性（最年輕者先），其餘名額依各年齡群人數比例分配（最大餘數法，同餘數依出生年、性別）；湊不齊兩對就不出發。</item>
/// <item>缺糧／擁擠外移：名額依各年齡群人數比例分配。</item>
/// <item>婚嫁：一名找不到伴侶的育齡單身者（最年輕的育齡年齡群）。</item>
/// <item>懷孕隨育齡女性等比例同行（向下取整）。</item>
/// </list>
/// </summary>
public static class DemoCohortMigration
{
    /// <summary>第 08-1 章規則沿用：開拓隊至少要帶的伴侶對數。</summary>
    public const int TempFoundingCouples = 2;

    public static DemoCohortDeparture Plan(DemoCohortDemography population, int settlementId, long limit,
        DemoEmigrationMode mode, int day, DemoTempDemographyRules rules)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(rules);
        if (limit <= 0) return DemoCohortDeparture.None;
        var elder = DemoSurvivalTempSettings.Current.ElderAgeYears;
        var groups = population.GroupsAt(settlementId, 0)
            .OrderBy(g => g.BirthYear).ThenBy(g => g.Sex).ToList();
        var stats = population.StatsOf(settlementId, 0, day, rules);
        bool FertileWoman(DemoCohortGroup g) => g.Sex == DemoTempSex.Female && Age(g) >= rules.AdultAgeYears && Age(g) < rules.FertileAgeMaxYears;
        bool PartnerMan(DemoCohortGroup g) => g.Sex == DemoTempSex.Male && Age(g) >= rules.AdultAgeYears && Age(g) < elder;
        int Age(DemoCohortGroup g) => DemoCohortDemography.AgeAt(g.BirthYear, day);

        var take = new Dictionary<(int, DemoTempSex), long>();
        var room = groups.ToDictionary(g => (g.BirthYear, g.Sex), g => g.Count);
        void Take((int, DemoTempSex) key, long n)
        {
            take[key] = take.GetValueOrDefault(key) + n;
            room[key] -= n;
        }
        // 最年輕的 n 人（依出生年遞減），只取 match 的年齡群。
        long TakeYoungest(Func<DemoCohortGroup, bool> match, long n)
        {
            foreach (var g in groups.Where(match).OrderByDescending(g => g.BirthYear))
            {
                if (n == 0) break;
                var k = Math.Min(n, room[(g.BirthYear, g.Sex)]);
                if (k > 0) { Take((g.BirthYear, g.Sex), k); n -= k; }
            }
            return n;
        }

        // 原聚落要留的育齡女性與可配對男性。
        var keepWomen = stats.FertileWomen > 0 ? 1 : 0;
        var keepMen = stats.PartnerMen > 0 ? 1 : 0;

        if (mode == DemoEmigrationMode.Mate)
        {
            var sex = stats.UnpairedWomen > 0 && stats.UnpairedMen == 0 ? DemoTempSex.Female
                : stats.UnpairedMen > 0 && stats.UnpairedWomen == 0 ? DemoTempSex.Male
                : (DemoTempSex?)null;
            if (sex is null) return DemoCohortDeparture.None;
            var left = TakeYoungest(g => g.Sex == sex && Age(g) >= rules.AdultAgeYears && Age(g) < rules.FertileAgeMaxYears, 1);
            return left == 0 ? new DemoCohortDeparture(take, 0) : DemoCohortDeparture.None;
        }

        if (mode == DemoEmigrationMode.Founding)
        {
            if (limit < 2 * TempFoundingCouples
                || stats.FertileWomen - keepWomen < TempFoundingCouples || stats.PartnerMen - keepMen < TempFoundingCouples)
                return DemoCohortDeparture.None;
            TakeYoungest(FertileWoman, TempFoundingCouples);
            TakeYoungest(PartnerMan, TempFoundingCouples);
        }

        // 其餘名額依各年齡群剩餘人數比例（最大餘數法）。
        var rest = limit - take.Values.Sum();
        var pool = room.Where(kv => kv.Value > 0).ToList();
        var total = pool.Sum(kv => kv.Value);
        if (rest > 0 && total > 0)
        {
            rest = Math.Min(rest, total);
            var shares = pool.Select(kv => (kv.Key, Floor: kv.Value * rest / total, Rem: kv.Value * rest % total)).ToList();
            foreach (var s in shares.Where(s => s.Floor > 0))
                Take(s.Key, s.Floor);
            var extra = rest - shares.Sum(s => s.Floor);
            foreach (var s in shares.OrderByDescending(s => s.Rem).ThenBy(s => s.Key.Item1).ThenBy(s => s.Key.Item2))
            {
                if (extra == 0) break;
                if (room[s.Key] == 0) continue;
                Take(s.Key, 1);
                extra--;
            }
        }

        // 原聚落至少留一名育齡女性、一名可配對男性：多帶走的退回（最年長的先退）。
        void KeepAtLeast(Func<DemoCohortGroup, bool> match, long keep)
        {
            var remaining = groups.Where(match).Sum(g => room[(g.BirthYear, g.Sex)]);
            foreach (var g in groups.Where(match).OrderBy(g => g.BirthYear))
            {
                if (remaining >= keep) break;
                var key = (g.BirthYear, g.Sex);
                var back = Math.Min(take.GetValueOrDefault(key), keep - remaining);
                if (back <= 0) continue;
                take[key] -= back;
                room[key] += back;
                remaining += back;
            }
        }
        KeepAtLeast(FertileWoman, keepWomen);
        KeepAtLeast(PartnerMan, keepMen);

        var clean = take.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (mode == DemoEmigrationMode.Founding)
        {
            long Count(Func<DemoCohortGroup, bool> match) => groups.Where(match).Sum(g => clean.GetValueOrDefault((g.BirthYear, g.Sex)));
            if (Count(FertileWoman) < TempFoundingCouples || Count(PartnerMan) < TempFoundingCouples)
                return DemoCohortDeparture.None;
        }
        var movedWomen = groups.Where(FertileWoman).Sum(g => clean.GetValueOrDefault((g.BirthYear, g.Sex)));
        var pregnancies = stats.FertileWomen == 0 ? 0 : stats.Pregnant * movedWomen / stats.FertileWomen;
        return new DemoCohortDeparture(clean, pregnancies);
    }
}
