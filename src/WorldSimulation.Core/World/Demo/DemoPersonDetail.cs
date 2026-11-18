namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19c 章：人物頁（參考十字軍之王的角色頁、WorldBox 的生物頁）。點代表人物或地圖上的領袖時打開。
/// 「人物」頁：人像、職位、性別年齡、所屬聚落與勢力、文化與信仰、任期；「生平」頁：小傳（第 08-16 章）；「同僚」頁：同一聚落的其他代表人物。
/// 第 05-2 章：已卸任、已過世的人也打得開（由人物登記表推得）；領袖與英雄多「家人」頁（父母、手足、子女）與家族的連結。
/// 只讀世界、不改世界。登記表上沒有這個人時回傳 null。
/// </summary>
public static class DemoPersonDetail
{
    private static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));

    public static DemoPlayerDetail? Of(DemoObserverWorld world, long representativeId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var person = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).FirstOrDefault(r => r.Id == representativeId);
        if (person is null)
            return world.PersonRecord(representativeId) is { } record ? Past(world, record) : null;
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        var day = world.Snapshot().DaysElapsed;
        var age = DemoCohortDemography.AgeAt(person.BirthYear, day);
        var site = world.Sites.Settlements.First(s => s.Id == person.SettlementId);
        var tier = world.ChronicleOf(site.Id).Tier;
        var home = DemoSettlementHistory.TitledName(site, tier);
        var role = DemoRepresentative.RoleName(person.Role, tier); // 第 08-10l 節：稱號依聚落等級
        var cultureId = world.Culture.CultureOfRepresentative(person.Id);
        var year = DemoSettlementHistory.YearOfDay(day);

        var lines = new List<string>
        {
            $"{role}　{person.Name}",
            $"{(person.Sex == DemoTempSex.Female ? "女" : "男")}，{age} 歲" + (person.BirthYear >= 1 ? $"（第 {person.BirthYear} 年生）" : "（開局前出生）"),
            $"所屬：{home}（{DemoSettlementHistory.TierName(tier)}）",
            $"任期：第 {Year(person.AppointedWeek)} 年上任，已 {Math.Max(0, year - Year(person.AppointedWeek))} 年"
        };
        AddBelief(lines, world, person.Id, cultureId);
        lines.Add(Duty(person.Role));
        AddFamily(lines, world, person, people);
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
                new[] { $"{DemoRepresentative.RoleName(r.Role, tier)}　{r.Name}（{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲）" }))
            .ToList();
        var peerLines = peers.SelectMany(p => p.Lines).ToList();
        if (peerLines.Count == 0)
            peerLines.Add($"{home}沒有其他代表人物");
        var tabs = new List<DemoDetailTab>
        {
            new("人物", lines) { People = new[] { entry }, Links = Links(person, people) },
            new("生平", life),
            new("同僚", peerLines) { People = peers }
        };
        if (people.TryGetValue(person.Id, out var self) && FamilyTab(world, self, people, day) is { } family)
            tabs.Add(family);
        return new DemoPlayerDetail($"{role} {person.Name}", tabs, site.Id);
    }

    /// <summary>第 05-2 章：已卸任或已過世的人（登記表上的紀錄）。</summary>
    private static DemoPlayerDetail Past(DemoObserverWorld world, DemoPersonRecord record)
    {
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        var person = DemoPersonRegister.AsRepresentative(record);
        var age = DemoCohortDemography.AgeAt(record.BirthYear, checked(Math.Max(1, record.ExitWeek) * 7));
        var site = world.Sites.Settlements.FirstOrDefault(s => s.Id == record.SettlementId);
        var home = site is null ? "某地" : DemoSettlementHistory.TitledName(site, world.ChronicleOf(site.Id).Tier);
        var role = Title(world, record);
        var status = Died(record) ? "已故" : "已卸任";
        var cultureId = world.Culture.CultureOfRepresentative(record.Id);
        var start = record.AppointedWeek < 1 ? "開局" : $"第 {Year(record.AppointedWeek)} 年";
        var lines = new List<string>
        {
            $"{role}　{record.Name}（{status}）",
            $"{(record.Sex == DemoTempSex.Female ? "女" : "男")}，" + (record.BirthYear >= 1 ? $"第 {record.BirthYear} 年生" : "開局前出生")
                + $"，第 {Year(record.ExitWeek)} 年{DemoSagaCast.ExitText(record.ExitReason)}",
            $"所屬：{home}",
            $"任期：{start}～第 {Year(record.ExitWeek)} 年（{Math.Max(0, Year(record.ExitWeek) - Year(record.AppointedWeek))} 年）"
        };
        AddBelief(lines, world, record.Id, cultureId);
        AddFamily(lines, world, person, people);
        var entry = new DemoPersonEntry(record.Id, DemoHeraldry.PortraitOf(person, age, cultureId ?? 0), lines);
        var life = DemoStories.Biography(world, person, null, record.ExitWeek).ToList();
        if (life.Count == 0)
            life.Add("沒有留下值得記下的事");
        var tabs = new List<DemoDetailTab>
        {
            new("人物", lines) { People = new[] { entry }, Links = Links(person, people) },
            new("生平", life)
        };
        if (FamilyTab(world, record, people, world.Snapshot().DaysElapsed) is { } family)
            tabs.Add(family);
        return new DemoPlayerDetail($"{role} {record.Name}（{status}）", tabs, record.SettlementId);
    }

    /// <summary>卸任的原因是不是死亡（老死、餓死、死於天災或戰事）。</summary>
    public static bool Died(DemoPersonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.ExitReason.StartsWith("老死", StringComparison.Ordinal) || record.ExitReason.StartsWith("餓死", StringComparison.Ordinal)
            || record.ExitReason.StartsWith("死於", StringComparison.Ordinal);
    }

    private static void AddBelief(List<string> lines, DemoObserverWorld world, long id, int? cultureId)
    {
        var culture = cultureId is { } c ? world.Culture.Culture(c) : null;
        var faith = world.Culture.Faith.Faith(world.Culture.Faith.FaithOfRepresentative(id));
        lines.Add(culture is null ? "文化：未知" : $"文化：{culture.Name}" + (culture.Traits.Count == 0 ? "" : $"（{string.Join("、", culture.Traits.Select(DemoCultures.TraitName))}）"));
        lines.Add(faith is null ? "信仰：無" : $"信仰：{faith.Name}");
    }

    /// <summary>第 08-10g～08-10i 章：家族與父母、英雄的事蹟、性格（領袖與英雄）。</summary>
    /// <summary>第 08-10l 節：這個人的職稱；領袖依聚落在他在任最後那一週（在任＝現在）的等級。</summary>
    internal static string Title(DemoObserverWorld world, DemoPersonRecord p)
        => DemoRepresentative.RoleName(p.Role, world.ChronicleOf(p.SettlementId).TierAt(p.InOffice ? world.CommittedWeeks : p.ExitWeek));

    private static void AddFamily(List<string> lines, DemoObserverWorld world, DemoRepresentative person, IReadOnlyDictionary<long, DemoPersonRecord> people)
    {
        if (person.Role is not (DemoRepresentativeRole.Leader or DemoRepresentativeRole.Hero))
            return;
        lines.Add($"家族：{DemoNames.FamilyOf(person.Name)}家" + (person.ParentId > 0 && people.TryGetValue(person.ParentId, out var parent)
            ? $"（{(parent.Sex == DemoTempSex.Female ? "母親" : "父親")}是{Title(world, parent)}{parent.Name}）"
            : person.Role == DemoRepresentativeRole.Hero ? "（士兵出身）" : "（這一家的第一代）"));
        // 第 08-10i 章：英雄的事蹟與傳說物品。
        if (person.Role == DemoRepresentativeRole.Hero && DemoHeroes.Legends(world).FirstOrDefault(l => l.Hero.Id == person.Id) is { } legend)
            lines.Add($"事蹟：第 {Year(legend.Week)} 年{legend.Deed.Text}" + (legend.Item.Length > 0 ? $"，得到「{legend.Item}」" : ""));
        if (person.Traits != DemoTrait.None)
        {
            lines.Add("性格：" + DemoTraits.Names(person.Traits));
            foreach (var trait in DemoTraits.Everything(person.Traits)) // 第 08-10h 章：每項性格一句說明；第 08-22c 章：傳說特性與身體特質
                lines.Add($"　{DemoTraits.Name(trait)}：{DemoTraits.Effect(trait)}");
        }
    }

    /// <summary>第 05-2 章：人物頁的連結——所屬的家族、父母。</summary>
    private static IReadOnlyList<DemoDetailLink> Links(DemoRepresentative person, IReadOnlyDictionary<long, DemoPersonRecord> people)
    {
        var links = new List<DemoDetailLink>();
        if (DemoHouses.HouseOf(people, person.Id) is { } founder && people.TryGetValue(founder, out var head))
            links.Add(new DemoDetailLink($"家族：{DemoNames.FamilyOf(head.Name)}家", DemoLinkKind.House, founder));
        if (person.ParentId > 0 && people.TryGetValue(person.ParentId, out var parent))
            links.Add(new DemoDetailLink($"{(parent.Sex == DemoTempSex.Female ? "母親" : "父親")}：{parent.Name}", DemoLinkKind.Person, parent.Id));
        return links;
    }

    /// <summary>第 05-2 章：「家人」頁——父母、手足、子女（登記表上有紀錄的人）；都沒有＝null。</summary>
    private static DemoDetailTab? FamilyTab(DemoObserverWorld world, DemoPersonRecord self, IReadOnlyDictionary<long, DemoPersonRecord> people, int day)
    {
        var entries = new List<DemoPersonEntry>();
        void Add(DemoPersonRecord p, string relation)
            => entries.Add(Entry(world, p, day, $"{relation}　{Title(world, p)}　{p.Name}", Term(p)));
        if (self.ParentId > 0 && people.TryGetValue(self.ParentId, out var parent))
        {
            Add(parent, parent.Sex == DemoTempSex.Female ? "母親" : "父親");
            foreach (var sibling in people.Values.Where(p => p.ParentId == self.ParentId && p.Id != self.Id).OrderBy(p => p.BirthYear).ThenBy(p => p.Id))
                Add(sibling, sibling.BirthYear < self.BirthYear
                    ? (sibling.Sex == DemoTempSex.Female ? "姊姊" : "哥哥")
                    : (sibling.Sex == DemoTempSex.Female ? "妹妹" : "弟弟"));
        }
        foreach (var child in people.Values.Where(p => p.ParentId == self.Id).OrderBy(p => p.BirthYear).ThenBy(p => p.Id))
            Add(child, child.Sex == DemoTempSex.Female ? "女兒" : "兒子");
        return entries.Count == 0 ? null : new DemoDetailTab("家人", entries.SelectMany(e => e.Lines).ToList()) { People = entries };
    }

    /// <summary>第 05-2 章：一個人在清單裡的一列（人像＋兩行字）；年齡是現在的年齡，已卸任的人是卸任時的年齡。</summary>
    internal static DemoPersonEntry Entry(DemoObserverWorld world, DemoPersonRecord p, int day, string title, string term)
    {
        var age = DemoCohortDemography.AgeAt(p.BirthYear, p.InOffice ? day : checked(Math.Max(1, p.ExitWeek) * 7));
        return new DemoPersonEntry(p.Id, DemoHeraldry.PortraitOf(DemoPersonRegister.AsRepresentative(p), age, world.Culture.CultureOfRepresentative(p.Id) ?? 0),
            new[] { title, "　" + term });
    }

    /// <summary>第 05-2 章：任期一句——「第 12 年上任，至今」「第 12 年上任，第 40 年壽終（63 歲）」（英雄寫「成名」）。</summary>
    internal static string Term(DemoPersonRecord p)
    {
        var start = p.AppointedWeek < 1 ? "開局" : $"第 {Year(p.AppointedWeek)} 年";
        var verb = p.Role == DemoRepresentativeRole.Hero ? "成名" : "上任";
        return p.InOffice ? $"{start}{verb}，至今" : $"{start}{verb}，第 {Year(p.ExitWeek)} 年{DemoSagaCast.ExitText(p.ExitReason)}";
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
        DemoRepresentativeRole.Hero => "職責：帶領士兵對付怪物，在世時本地士兵的等級 +1",
        _ => "職責：代表聚落"
    };
}
