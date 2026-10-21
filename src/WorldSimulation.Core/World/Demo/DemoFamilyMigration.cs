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

    /// <summary>不超過 <paramref name="limit"/> 人、整戶出發的成員 ID（遞增排序）。</summary>
    public static long[] Choose(IEnumerable<DemoTempPerson> residents, long limit, int day, DemoTempDemographyRules rules)
    {
        var chosen = new List<long>();
        var left = limit;
        foreach (var family in Families(residents, day, rules))
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
