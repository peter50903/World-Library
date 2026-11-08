namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19c 章：人物頁（參考十字軍之王的角色頁、WorldBox 的生物頁）。點代表人物或地圖上的領袖時打開。
/// 「人物」頁：人像、職位、性別年齡、所屬聚落與勢力、文化與信仰、任期；「生平」頁：小傳（第 08-16 章）；「同僚」頁：同一聚落的其他代表人物。
/// 只讀世界、不改世界。卸任或過世的人物找不到時回傳 null。
/// </summary>
public static class DemoPersonDetail
{
    public static DemoPlayerDetail? Of(DemoObserverWorld world, long representativeId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var person = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).FirstOrDefault(r => r.Id == representativeId);
        if (person is null)
            return null;
        var day = world.Snapshot().DaysElapsed;
        var age = DemoCohortDemography.AgeAt(person.BirthYear, day);
        var site = world.Sites.Settlements.First(s => s.Id == person.SettlementId);
        var tier = world.ChronicleOf(site.Id).Tier;
        var home = DemoSettlementHistory.TitledName(site, tier);
        var role = DemoRepresentative.RoleName(person.Role);
        var cultureId = world.Culture.CultureOfRepresentative(person.Id);
        var culture = cultureId is { } c ? world.Culture.Culture(c) : null;
        var faith = world.Culture.Faith.Faith(world.Culture.Faith.FaithOfRepresentative(person.Id));
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));
        var year = DemoSettlementHistory.YearOfDay(day);

        var lines = new List<string>
        {
            $"{role}　{person.Name}",
            $"{(person.Sex == DemoTempSex.Female ? "女" : "男")}，{age} 歲" + (person.BirthYear >= 1 ? $"（第 {person.BirthYear} 年生）" : "（開局前出生）"),
            $"所屬：{home}（{DemoSettlementHistory.TierName(tier)}）",
            $"任期：第 {Year(person.AppointedWeek)} 年上任，已 {Math.Max(0, year - Year(person.AppointedWeek))} 年",
            culture is null ? "文化：未知" : $"文化：{culture.Name}" + (culture.Traits.Count == 0 ? "" : $"（{string.Join("、", culture.Traits.Select(DemoCultures.TraitName))}）"),
            faith is null ? "信仰：無" : $"信仰：{faith.Name}",
            Duty(person.Role)
        };
        // 勢力：聚落屬於哪個勢力；領袖另寫他統領的勢力與獨立派系。
        var territory = world.Territory();
        var top = territory.RealmOf(site.Id);
        if (top != site.Id && world.Sites.Settlements.FirstOrDefault(s => s.Id == top) is { } realm)
            lines.Add($"效忠：{DemoSettlementHistory.TitledName(realm, world.ChronicleOf(realm.Id).Tier)}的勢力");
        else if (person.Role == DemoRepresentativeRole.Leader && territory.VassalsOf(site.Id).Count > 0)
            lines.Add($"統領：轄下 {territory.VassalsOf(site.Id).Count} 個聚落");
        if (person.Role == DemoRepresentativeRole.Leader && world.Realms().Faction.ContainsKey(site.Id))
            lines.Add("立場：帶領聚落加入了獨立派系");

        var entry = new DemoPersonEntry(person.Id, DemoHeraldry.PortraitOf(person, age, cultureId ?? 0), lines);
        var life = DemoStories.Biography(world, person).ToList();
        if (life.Count == 0)
            life.Add("還沒有值得記下的事");
        var peers = world.RepresentativesOf(site.Id).Where(r => r.Id != person.Id)
            .Select(r => new DemoPersonEntry(r.Id, DemoHeraldry.PortraitOf(r, DemoCohortDemography.AgeAt(r.BirthYear, day), world.Culture.CultureOfRepresentative(r.Id) ?? 0),
                new[] { $"{DemoRepresentative.RoleName(r.Role)}　{r.Name}（{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲）" }))
            .ToList();
        var peerLines = peers.SelectMany(p => p.Lines).ToList();
        if (peerLines.Count == 0)
            peerLines.Add($"{home}沒有其他代表人物");
        return new DemoPlayerDetail($"{role} {person.Name}", new[]
        {
            new DemoDetailTab("人物", lines) { People = new[] { entry } },
            new DemoDetailTab("生平", life),
            new DemoDetailTab("同僚", peerLines) { People = peers }
        }, site.Id);
    }

    /// <summary>職位的作用（玩家看得懂的一句）。</summary>
    public static string Duty(DemoRepresentativeRole role) => role switch
    {
        DemoRepresentativeRole.Leader => "職責：帶領聚落，決定遷徙與開拓",
        DemoRepresentativeRole.Farmer => "職責：傳授農耕，聚落的農田靠他維持",
        DemoRepresentativeRole.Carpenter => "職責：蓋房子與工具，聚落升級需要他",
        DemoRepresentativeRole.Smith => "職責：鍛造工具與武器，讓收成與戰力提升",
        DemoRepresentativeRole.Hunter => "職責：帶領狩獵，也是對抗野獸與怪物的好手",
        DemoRepresentativeRole.Shaman => "職責：主持祭祀、照顧病人",
        DemoRepresentativeRole.Scholar => "職責：抄寫與研究，加快所有領域的研究",
        _ => "職責：代表聚落"
    };
}
