namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 05-2 章：家族（參考十字軍之王的家族）——由人物登記表的父母欄推得，不另存（第 08-10g 章）。
/// 一位沒有父母紀錄的領袖開創一個家族；世襲的繼承人、領袖子女出身的英雄都屬於父母的家族。
/// 士兵出身的英雄與其他職位（農師、木匠…）不屬於任何家族。
/// </summary>
public static class DemoHouses
{
    /// <summary>沿父母欄往上找到最早的一代（找不到父母紀錄就停）。</summary>
    public static long FounderOf(IReadOnlyDictionary<long, DemoPersonRecord> people, long id)
    {
        ArgumentNullException.ThrowIfNull(people);
        var at = id;
        for (var guard = 0; guard < 256 && people.TryGetValue(at, out var p) && p.ParentId > 0 && people.ContainsKey(p.ParentId); guard++)
            at = p.ParentId;
        return at;
    }

    /// <summary>第幾代（開創者＝1）。</summary>
    public static int GenerationOf(IReadOnlyDictionary<long, DemoPersonRecord> people, long id)
    {
        ArgumentNullException.ThrowIfNull(people);
        var generation = 1;
        var at = id;
        for (var guard = 0; guard < 256 && people.TryGetValue(at, out var p) && p.ParentId > 0 && people.ContainsKey(p.ParentId); guard++)
        {
            at = p.ParentId;
            generation++;
        }
        return generation;
    }

    /// <summary>這個人屬於的家族（開創者的人物編號）；不屬於任何家族＝null。</summary>
    public static long? HouseOf(IReadOnlyDictionary<long, DemoPersonRecord> people, long id)
    {
        ArgumentNullException.ThrowIfNull(people);
        if (!people.TryGetValue(id, out var p) || p.Role is not (DemoRepresentativeRole.Leader or DemoRepresentativeRole.Hero))
            return null;
        var founder = FounderOf(people, id);
        return people.TryGetValue(founder, out var f) && f.Role == DemoRepresentativeRole.Leader ? founder : null;
    }

    /// <summary>家族的成員（領袖與英雄，依上任先後）。</summary>
    public static IReadOnlyList<DemoPersonRecord> Members(IReadOnlyDictionary<long, DemoPersonRecord> people, long founder)
    {
        ArgumentNullException.ThrowIfNull(people);
        return people.Values.Where(p => p.Role is DemoRepresentativeRole.Leader or DemoRepresentativeRole.Hero && FounderOf(people, p.Id) == founder)
            .OrderBy(p => p.AppointedWeek).ThenBy(p => p.Id).ToList();
    }
}

/// <summary>
/// 第 05-2 章：家族頁（十字軍之王的家族頁）。「家族」頁：家族的來歷、歷代、現況、英雄與傳家寶；「成員」頁：每位成員的人像；「家譜」頁：樹狀的世系。
/// 只讀世界、不改世界。不是家族開創者的編號回傳 null。
/// </summary>
public static class DemoHouseDetail
{
    public static DemoPlayerDetail? Of(DemoObserverWorld world, long founderId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        if (!people.TryGetValue(founderId, out var founder) || DemoHouses.HouseOf(people, founderId) != founderId)
            return null;
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));
        var members = DemoHouses.Members(people, founderId);
        var leaders = members.Where(p => p.Role == DemoRepresentativeRole.Leader).ToList();
        var heroes = members.Where(p => p.Role == DemoRepresentativeRole.Hero).ToList();
        var family = DemoNames.FamilyOf(founder.Name);
        var site = world.Sites.Settlements.FirstOrDefault(s => s.Id == founder.SettlementId);
        var seat = site is null ? "某地" : DemoSettlementHistory.TitledName(site, world.ChronicleOf(site.Id).Tier);
        var from = founder.AppointedWeek < 1 ? "開局" : $"第 {Year(founder.AppointedWeek)} 年";
        var head = leaders[^1];
        var lines = new List<string>
        {
            $"{seat}的{family}家",
            $"開創者：{founder.Name}（{from}掌權）",
            $"傳 {leaders.Count} 代：{string.Join("→", leaders.Select(p => DemoNames.GivenOf(p.Name)))}"
        };
        if (head.InOffice)
            lines.Add($"現任家主：{head.Name}（{from}至今掌權）");
        else
        {
            var successor = people.Values.Where(p => p.Role == DemoRepresentativeRole.Leader && p.SettlementId == head.SettlementId
                    && p.AppointedWeek >= head.ExitWeek && p.ParentId != head.Id)
                .OrderBy(p => p.AppointedWeek).ThenBy(p => p.Id).FirstOrDefault();
            lines.Add($"掌權：{from}～第 {Year(head.ExitWeek)} 年");
            lines.Add(successor is null
                ? $"第 {Year(head.ExitWeek)} 年，末代家主{head.Name}{DemoSagaCast.ExitText(head.ExitReason)}，{family}家就此失去權位"
                : $"第 {Year(head.ExitWeek)} 年，末代家主{head.Name}{DemoSagaCast.ExitText(head.ExitReason)}，{DemoNames.FamilyOf(successor.Name)}家的{successor.Name}接掌");
        }
        lines.Add(heroes.Count == 0 ? "還沒有出過英雄" : $"出過 {heroes.Count} 位英雄：{string.Join("、", heroes.Select(h => h.Name))}");
        foreach (var legend in DemoHeroes.Legends(world).Where(l => l.Item.Length > 0 && heroes.Any(h => h.Id == l.Hero.Id)))
            lines.Add($"傳家寶：「{legend.Item}」（{legend.Hero.Name}第 {Year(legend.Week)} 年{legend.Deed.Text}所得；"
                + (legend.Hero.InOffice ? $"由{legend.Hero.Name}持有）" : "由家族收藏）"));

        var links = new List<DemoDetailLink>();
        if (site is not null)
            links.Add(new DemoDetailLink($"發源地：{seat}", DemoLinkKind.Settlement, site.Id));
        links.Add(new DemoDetailLink($"開創者：{founder.Name}", DemoLinkKind.Person, founder.Id));
        if (head.Id != founder.Id)
            links.Add(new DemoDetailLink($"{(head.InOffice ? "現任家主" : "末代家主")}：{head.Name}", DemoLinkKind.Person, head.Id));

        var day = world.Snapshot().DaysElapsed;
        var entries = members.Select(m => DemoPersonDetail.Entry(world, m, day,
            $"第 {DemoHouses.GenerationOf(people, m.Id)} 代　{DemoRepresentative.RoleName(m.Role)}　{m.Name}", DemoPersonDetail.Term(m))).ToList();

        // 家譜：開創者在最上面，子女依出生先後縮排在父母下面。
        var tree = new List<string>();
        string Label(DemoPersonRecord p) => $"{p.Name}（{DemoRepresentative.RoleName(p.Role)}；{DemoPersonDetail.Term(p)}）";
        void Walk(DemoPersonRecord p, string indent, bool last, bool root)
        {
            tree.Add(root ? Label(p) : indent + (last ? "└ " : "├ ") + Label(p));
            var children = members.Where(c => c.ParentId == p.Id).OrderBy(c => c.BirthYear).ThenBy(c => c.Id).ToList();
            for (var i = 0; i < children.Count; i++)
                Walk(children[i], root ? "" : indent + (last ? "　 " : "│ "), i == children.Count - 1, false);
        }
        Walk(founder, "", true, true);

        return new DemoPlayerDetail($"{family}家（{seat}）", new[]
        {
            new DemoDetailTab("家族", lines) { Links = links },
            new DemoDetailTab("成員", entries.SelectMany(e => e.Lines).ToList()) { People = entries },
            new DemoDetailTab("家譜", tree)
        }, founder.SettlementId);
    }
}
