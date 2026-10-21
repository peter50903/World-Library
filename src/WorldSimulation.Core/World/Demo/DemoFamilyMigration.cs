namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 07 章 b 節：遷徙以整個家庭為單位（伴侶與未成年子女同行）。純函式、不抽亂數。
/// 家庭＝一對伴侶（或沒有伴侶的成年人）＋同住、未成年、父或母在其中的子女。
/// 父母都不在本聚落的未成年人不單獨出發【TEMP】。
/// 挑選順序【TEMP】：家中最年輕成年人較年輕的家庭先走（同齡依最小 ID）；依序整戶加入，
/// 放不下的家庭跳過，直到人數達到上限。結果可能少於上限（不拆散家庭），甚至為 0。
/// </summary>
public static class DemoFamilyMigration
{
    /// <summary>【TEMP】第 08-1 章：開拓隊至少要帶的成年伴侶對數。</summary>
    public const int TempFoundingCouples = 2;

    /// <summary>
    /// 第 08-1 章【TEMP 規則】婚嫁遷移：本聚落最年輕的「找不到伴侶的育齡單身者」——
    /// 育齡、沒有伴侶、沒有帶著未成年子女，而且聚落裡沒有任何未配對、非近親的成年異性。沒有就是 null。
    /// </summary>
    public static DemoTempPerson? MateSeeker(IEnumerable<DemoTempPerson> residents, int day, DemoTempDemographyRules rules)
    {
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(rules);
        var people = residents.Where(p => p.IsSettled).ToList();
        return people
            .Where(p => p.PartnerId is null && p.IsAdultAt(day, rules) && p.AgeYearsAt(day) < rules.FertileAgeMaxYears
                && !people.Any(c => !c.IsAdultAt(day, rules) && (c.MotherId == p.Id || c.BirthFatherId == p.Id))
                && !people.Any(o => o.Sex != p.Sex && o.PartnerId is null && o.IsAdultAt(day, rules) && !p.IsCloseKinOf(o)))
            .OrderByDescending(p => p.BornDay).ThenBy(p => p.Id)
            .FirstOrDefault();
    }

    /// <summary>第 08-1 章：本聚落未配對的成年人數（依性別；婚嫁遷移選目的地用）。</summary>
    public static int Unpaired(IEnumerable<DemoTempPerson> residents, DemoTempSex sex, int day, DemoTempDemographyRules rules)
        => residents.Count(p => p.IsSettled && p.Sex == sex && p.PartnerId is null && p.IsAdultAt(day, rules));

    public static IReadOnlyList<IReadOnlyList<DemoTempPerson>> Families(
        IEnumerable<DemoTempPerson> residents, int day, DemoTempDemographyRules rules)
    {
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(rules);
        var people = residents.OrderBy(p => p.Id).ToList();
        var assigned = new HashSet<long>();
        var families = new List<List<DemoTempPerson>>();
        var byId = people.ToDictionary(p => p.Id);
        foreach (var adult in people.Where(p => p.IsAdultAt(day, rules)))
        {
            if (assigned.Contains(adult.Id)) continue;
            var family = new List<DemoTempPerson> { adult };
            assigned.Add(adult.Id);
            if (adult.PartnerId is long partnerId && byId.TryGetValue(partnerId, out var partner) && assigned.Add(partner.Id))
                family.Add(partner);
            families.Add(family);
        }
        foreach (var child in people.Where(p => !p.IsAdultAt(day, rules)))
        {
            var family = families.FirstOrDefault(f => f.Any(a => a.Id == child.MotherId || a.Id == child.BirthFatherId));
            if (family is null) continue; // 父母都不在：留在聚落
            family.Add(child);
            assigned.Add(child.Id);
        }
        return families
            .OrderByDescending(f => f.Where(p => p.IsAdultAt(day, rules)).Max(p => p.BornDay))
            .ThenBy(f => f.Min(p => p.Id))
            .Select(f => (IReadOnlyList<DemoTempPerson>)f.OrderBy(p => p.Id).ToArray())
            .ToArray();
    }

    /// <summary>
    /// 不超過 <paramref name="limit"/> 人、整戶出發的成員 ID（遞增排序）。
    /// 第 08-1 章【TEMP 規則】：<paramref name="requireCouple"/>（開拓隊）時，最先放入兩戶成年伴侶（兩個不同家庭），
    /// 放不下兩對就不出發（回傳空）——只有一個家庭的新聚落，下一代都是手足、無法通婚。
    /// </summary>
    public static long[] Choose(IEnumerable<DemoTempPerson> residents, long limit, int day, DemoTempDemographyRules rules,
        bool requireCouple = false)
    {
        var chosen = new List<long>();
        var left = limit;
        var families = Families(residents, day, rules).ToList();
        if (requireCouple)
        {
            var couples = new List<IReadOnlyList<DemoTempPerson>>();
            var room = left;
            foreach (var f in families.Where(f => f.Count(p => p.IsAdultAt(day, rules)) == 2))
            {
                if (f.Count > room) continue;
                couples.Add(f);
                room -= f.Count;
                if (couples.Count == TempFoundingCouples) break;
            }
            if (couples.Count < TempFoundingCouples)
                return Array.Empty<long>();
            foreach (var c in couples)
                families.Remove(c);
            families.InsertRange(0, couples);
        }
        foreach (var family in families)
        {
            if (family.Count > left) continue;
            chosen.AddRange(family.Select(p => p.Id));
            left -= family.Count;
            if (left == 0) break;
        }
        chosen.Sort();
        return chosen.ToArray();
    }
}
