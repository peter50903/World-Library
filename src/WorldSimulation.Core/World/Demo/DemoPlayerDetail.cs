namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-11 章：詳情的一個分頁籤。<see cref="Badges"/> 不為空時，介面把技術畫成 RPG 技能那樣的方形小圖示
/// （<see cref="Lines"/> 仍是同內容的文字版，給 CLI 與測試）。
/// </summary>
public sealed record DemoDetailTab(string Title, IReadOnlyList<string> Lines)
{
    public IReadOnlyList<DemoTechBadge> Badges { get; init; } = Array.Empty<DemoTechBadge>();

    /// <summary>第 08-16 章（知識樹收斂顯示）：前幾個徽章是「關鍵技術」，介面放最上面並加標題；其餘收合。</summary>
    public int KeyBadgeCount { get; init; }

    public string KeyTitle { get; init; } = "";

    /// <summary>第 08-16 章：代表人物分頁的每一人（人像＋文字）；介面有素材就畫人像。</summary>
    public IReadOnlyList<DemoPersonEntry> People { get; init; } = Array.Empty<DemoPersonEntry>();

    /// <summary>第 08-16 章：現況分頁上方的文化紋章與信仰符號（沒有＝null）。</summary>
    public DemoEmblem? Emblem { get; init; }

    public string? FaithSymbol { get; init; }

    public int FaithId { get; init; }
}

/// <summary>第 08-16 章：代表人物分頁的一人（人像部件＋原本的文字與小傳）。</summary>
public sealed record DemoPersonEntry(long Id, DemoPortrait Portrait, IReadOnlyList<string> Lines);

/// <summary>
/// 第 08-11 章（使用者 2026-10-28：技術用 RPG 技能那樣的方形小圖示，不同圖案與數字代表領域與等級）。
/// <see cref="LearnedYear"/>＝習得年份（研究中為 null）；<see cref="Percent"/>＝研究進度（已習得 100）。
/// </summary>
public sealed record DemoTechBadge(string Id, string Name, string Domain, string DomainName, string Glyph, int Tier, int? LearnedYear, long Percent,
    bool Opening = false);

/// <summary>
/// 第 08-11 章（使用者 2026-10-28 試玩回饋 6、8）：給玩家看的詳情——聚落分成「現況｜代表人物｜技術｜研究｜歷史」五個分頁籤；
/// 其他格只寫地名、土地狀態與經過的遷徙隊伍。不顯示座標、路線、來源供應量等數值（那些留在開發用的 <see cref="DemoCellView"/>）。
/// 只讀世界，不改狀態、不抽亂數。
/// </summary>
public sealed record DemoPlayerDetail(string Title, IReadOnlyList<DemoDetailTab> Tabs, int? SettlementId)
{
    /// <summary>全部分頁的文字（分頁標題一行、內容逐行）；介面冒煙測試比對用。</summary>
    public string Text => string.Join("\n", Tabs.SelectMany(t => new[] { $"【{t.Title}】" }.Concat(t.Lines)));

    /// <summary>領域顯示名稱（10A 中文名稱；新領域見 10R）。</summary>
    public static string DomainName(string domain) => domain switch
    {
        "10B" => "農業與畜牧",
        "10C" => "採礦與冶金",
        "10D" => "建築與土木",
        "10E" => "木工與造船",
        "10F" => "紡織與皮革",
        "10G" => "食品與保存",
        "10H" => "醫療與藥學",
        "10I" => "軍事工程",
        "10J" => "書寫與教育",
        "10K" => "魔法工藝",
        "10L" => "陶瓷與玻璃",
        "10M" => "珠寶與精工",
        "10N" => "運輸車輛",
        "HUNT" => "狩獵與求生",
        "SOCIETY" => "社會與制度",
        "FAITH" => "信仰與文化",
        "OPENING" => "開局知識",
        _ => domain
    };

    /// <summary>領域的圖示字（技能圖示素材交付前，方形小圖示上畫這個字）。</summary>
    public static string DomainGlyph(string domain) => domain switch
    {
        "10B" => "農", "10C" => "礦", "10D" => "建", "10E" => "木", "10F" => "織", "10G" => "食", "10H" => "醫",
        "10I" => "軍", "10J" => "書", "10K" => "魔", "10L" => "陶", "10M" => "珠", "10N" => "車",
        "HUNT" => "獵", "SOCIETY" => "法", "FAITH" => "祭", "OPENING" => "火",
        _ => "？"
    };

    private static DemoTechBadge Badge(DemoTechnology t, int? year, long percent)
        => new(t.Id, DemoKnowledge.Name(t), t.Node.Domain, DomainName(t.Node.Domain), DomainGlyph(t.Node.Domain), t.Node.Tier, year, percent,
            DemoKnowledge.IsOpening(t));

    /// <summary>
    /// 第 08-16 章：某等級升下一級所需設施對應的技術（設施本身要技術，或要某職業的代表人物、而那個職業要技術）；
    /// 城市升首都另需王權。首都＝沒有下一級。
    /// </summary>
    public static IReadOnlyList<DemoTechnology> KeyTechnologies(DemoSettlementTier tier)
    {
        var list = DemoSettlementHistory.FacilitiesOf(tier)
            .Select(f => f.TechId is { } id ? DemoTechnology.Of(id) : f.Role is { } role ? DemoRepresentative.TechnologyOf(role) : null)
            .Where(t => t is not null).Select(t => t!.Value).Distinct().ToList();
        if (tier == DemoSettlementTier.City)
            list.Add(DemoTechnology.Of(DemoSettlementHistory.CapitalTechId));
        return list;
    }

    /// <summary>研究分頁最多列幾項（進度高的在前）。</summary>
    public const int MaxResearchLines = 10;

    /// <summary>拿掉給開發者看的 TEMP 標記（規則仍是暫定，但玩家不需要看到）。</summary>
    public static string Clean(string text)
        => text.Replace("【TEMP】", "", StringComparison.Ordinal).Replace("（TEMP）", "", StringComparison.Ordinal)
            .Replace("TEMP ", "", StringComparison.Ordinal).Replace("TEMP", "", StringComparison.Ordinal);

    public static DemoPlayerDetail Of(DemoObserverWorld world, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(world);
        var cell = world.Map.GetCell(column, row);
        var settlement = world.Sites.SettlementAt(column, row);
        if (settlement is not null && DemoRuins.IsFaded(world, settlement))
        {
            // 第 08-11 章：消失的遺跡只顯示原本的地形，另記一行舊址。
            var place = Place(world, cell, ignoreSettlement: true);
            var year = DemoSettlementHistory.YearOfDay(checked(settlement.RuinedWeek * 7));
            var lines = place.Tabs[0].Lines.Append($"舊址：{settlement.Name}（第 {year} 年荒廢）").ToList();
            return place with { Tabs = new[] { new DemoDetailTab("地點", lines) } };
        }
        return settlement is null ? Place(world, cell) : Settlement(world, settlement);
    }

    private static DemoPlayerDetail Place(DemoObserverWorld world, DemoHexCell cell, bool ignoreSettlement = false)
    {
        var lines = new List<string> { DemoCellQuery.ShortName(world.Map, ignoreSettlement ? null : world.Sites, cell) };
        if (world.Land.ParcelAt(new DemoCellRef(cell.Column, cell.Row)) is { } parcel)
            lines.Add(parcel.State == DemoLandDevelopment.Farmland ? "農田" : $"開墾中（{parcel.DevelopmentPercent}%）");
        if (world.MeteoriteOre.Contains(new DemoCellRef(cell.Column, cell.Row)))
            lines.Add("隕石留下的礦脈"); // 第 12-1 章
        if (world.Dungeons.FirstOrDefault(d => d.Cell.Column == cell.Column && d.Cell.Row == cell.Row) is { } dungeon)
            lines.Add($"地下城：{dungeon.Name}（{dungeon.Layers} 層，第 {DemoSettlementHistory.YearOfDay(checked(dungeon.AppearedWeek * 7))} 年出現）"); // 第 12-1 章 C4
        if (DemoWildlife.CellNote(world.Wildlife, world.Map, cell) is { } note)
            lines.Add(note); // 第 12-1 章 C1
        foreach (var batch in world.MigrationsInTransit().Where(b => b.Cell.Column == cell.Column && b.Cell.Row == cell.Row))
            lines.Add($"{(batch.Founding ? "開拓隊" : "遷徙隊伍")}：{batch.People} 人，從{batch.FromName}前往{batch.ToName}");
        return new DemoPlayerDetail(lines[0], new[] { new DemoDetailTab("地點", lines) }, null);
    }

    private static DemoPlayerDetail Settlement(DemoObserverWorld world, DemoSettlement settlement)
    {
        var day = world.Snapshot().DaysElapsed;
        int YearOfWeek(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var tier = DemoSettlementHistory.TierName(settlement, world.ChronicleOf(settlement.Id).Tier);

        // 現況
        var now = new List<string>
        {
            $"{settlement.Name}（{tier}）",
            settlement.IsFounded
                ? $"第 {YearOfWeek(settlement.FoundedWeek)} 年由{names.GetValueOrDefault(settlement.FoundedFromSettlementId, "未知聚落")}的開拓隊建立"
                : "開局時已有人定居",
            $"家系：{names.GetValueOrDefault(DemoLineageMap.LineageRoot(world.Sites, settlement.Id), "未知")}"
        };
        if (!settlement.IsRuin)
        {
            now.Add($"人口 {world.SettlementPopulation(settlement.Id)}");
            if (world.PopulationStats(settlement.Id) is { Total: > 0 } st)
                now.Add($"兒童 {st.Children}｜成年 {st.Adults}｜老人 {st.Elders}");
            now.Add(world.IsStarving(settlement.Id) ? "糧食：短缺，有人挨餓" : "糧食：充足");
            // 第 12-1 章 C1：附近獵物多寡（不揭示數字）。
            if (!world.Wildlife.Prey.IsDefaultOrEmpty)
                now.Add("附近獵物：" + DemoWildlife.PreyLevel(world.Wildlife, world.Map, settlement) switch { 2 => "豐富", 1 => "普通", _ => "稀少" });
            // 第 12-1 章 C4：附近的地下城。
            var nearDungeons = DemoDungeons.Near(world.Dungeons, world.Map, settlement).Select(d => d.Name).ToList();
            if (nearDungeons.Count > 0)
                now.Add("附近有地下城：" + string.Join("、", nearDungeons));
        }
        // 第 08-8 章 B1：文化比例（由多到少）與主要文化的特性。
        var shares = world.Culture.SharesOf(settlement.Id).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).ToList();
        if (shares.Count > 0)
        {
            now.Add("文化：" + string.Join("、", shares.Select(s => $"{world.Culture.Culture(s.CultureId)?.Name ?? "？"} {(s.Permille + 5) / 10}%")));
            if (world.Culture.Culture(shares[0].CultureId) is { } main)
                now.Add($"{main.Name}的傳統：" + string.Join("、", main.Traits.Select(DemoCultures.TraitName)));
            // 第 08-8 章 B3：信仰比例與主要信仰的信條、教義。
            var faith = world.Culture.Faith;
            now.Add("信仰：" + string.Join("、", faith.SharesOf(settlement.Id).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
                .Select(s => $"{(s.CultureId == 0 ? "無信仰" : faith.Faith(s.CultureId)?.Name ?? "？")} {(s.Permille + 5) / 10}%")));
            if (faith.MainFaithOf(settlement.Id) is { } mf && faith.Faith(mf) is { } f)
            {
                now.Add($"{f.Name}的信條：" + string.Join("、", f.Tenets.Select(DemoFaiths.EntryName)));
                now.Add($"{f.Name}的教義：" + string.Join("、", f.Doctrines.Select(DemoFaiths.EntryName)));
            }
        }
        else
        {
            now.Add($"第 {YearOfWeek(settlement.RuinedWeek)} 年起無人居住");
        }

        // 代表人物（第 08-16 章：每人下面接兩三行小傳）
        var allEvents = world.SettlementEvents();
        var entries = world.RepresentativesOf(settlement.Id)
            .OrderBy(r => r.Role)
            .Select(r => new DemoPersonEntry(r.Id,
                DemoHeraldry.PortraitOf(r, DemoCohortDemography.AgeAt(r.BirthYear, day), world.Culture.CultureOfRepresentative(r.Id) ?? 0),
                new[]
                {
                    $"{DemoRepresentative.RoleName(r.Role)}　{r.Name}（{(r.Sex == DemoTempSex.Female ? "女" : "男")}，"
                    + $"{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲，第 {YearOfWeek(r.AppointedWeek)} 年上任"
                    + (world.Culture.CultureOfRepresentative(r.Id) is { } c && world.Culture.Culture(c) is { } culture ? $"，{culture.Name}" : "")
                    + (world.Culture.Faith.Faith(world.Culture.Faith.FaithOfRepresentative(r.Id)) is { } rf ? $"，信{rf.Name}" : "") + "）"
                }.Concat(DemoStories.Biography(world, r, allEvents)).ToList()))
            .ToList();
        var people = entries.SelectMany(e => e.Lines).ToList();
        if (people.Count == 0)
            people.Add(settlement.IsRuin ? "已無人居住" : "目前沒有代表人物");

        // 技術（依領域分組，開局知識另列一行）
        var records = world.Knowledge.Records.Where(r => r.SettlementId == settlement.Id).ToList();
        var known = records.Where(r => r.Learned).OrderBy(r => r.Technology).ToList();
        var techs = new List<string>
        {
            "開局知識：" + string.Join("、", DemoTechnology.All.Where(DemoKnowledge.IsOpening).Select(DemoKnowledge.Name))
        };
        foreach (var group in known.GroupBy(r => r.Technology.Node.Domain).OrderBy(g => g.Min(r => r.Technology)))
            techs.Add($"{DomainName(group.Key)}：" + string.Join("、", group.Select(r => $"{DemoKnowledge.Name(r.Technology)}（第 {YearOfWeek(r.LearnedWeek)} 年）")));
        if (known.Count == 0)
            techs.Add("尚未研究出新技術");

        // 第 08-16 章（世紀帝國式收斂顯示）：關鍵技術＝讓本聚落升下一級的設施所需的技術，放最上面；其餘收合。
        var keyTechs = KeyTechnologies(world.ChronicleOf(settlement.Id).Tier);
        var keyBadges = keyTechs.Select(t => records.FirstOrDefault(r => r.Technology == t) is { } rec
                ? rec.Learned ? Badge(t, YearOfWeek(rec.LearnedWeek), 100) : Badge(t, null, rec.Progress * 100 / DemoKnowledge.ThresholdOf(t))
                : Badge(t, null, 0)).ToList();
        var keySet = keyTechs.ToHashSet();
        var knownBadges = keyBadges
            .Concat(DemoTechnology.All.Where(DemoKnowledge.IsOpening).Select(t => Badge(t, null, 100)))
            .Concat(known.Where(r => !keySet.Contains(r.Technology)).Select(r => Badge(r.Technology, YearOfWeek(r.LearnedWeek), 100))).ToList();
        var nextTier = world.ChronicleOf(settlement.Id).Tier + 1;
        var keyTitle = keyTechs.Count == 0 ? "" : settlement.IsRuin ? "關鍵技術"
            : $"升為{DemoSettlementHistory.TierName(nextTier)}的關鍵技術（有其中兩項設施才能升級）";
        if (keyTechs.Count > 0)
            techs.Insert(0, keyTitle + "：" + string.Join("、", keyTechs.Select(t =>
                DemoKnowledge.Name(t) + (known.Any(r => r.Technology == t) ? "（已會）" : ""))));

        // 研究
        var researching = records.Where(r => !r.Learned && r.Progress > 0)
            .Select(r => (Record: r, Percent: r.Progress * 100 / DemoKnowledge.ThresholdOf(r.Technology)))
            .OrderByDescending(x => x.Percent).ThenBy(x => x.Record.Technology)
            .Take(MaxResearchLines)
            .ToList();
        var researchBadges = researching.Select(x => Badge(x.Record.Technology, null, x.Percent)).ToList();
        var research = researching
            .Select(x => $"{DemoKnowledge.Name(x.Record.Technology)}　{x.Percent}%（{DomainName(x.Record.Technology.Node.Domain)}）")
            .ToList();
        if (research.Count == 0)
            research.Add(settlement.IsRuin ? "已無人研究" : "目前沒有進行中的研究");

        // 歷史（聚落小史，去掉開頭的規模摘要）
        var history = DemoSettlementHistory.Lines(world, settlement.Id)
            .SkipWhile(l => !l.StartsWith("聚落小史", StringComparison.Ordinal)).Skip(1)
            .Select(l => Clean(l.Trim()))
            .ToList();

        return new DemoPlayerDetail($"{settlement.Name}（{tier}）", new[]
        {
            new DemoDetailTab("現況", now)
            {
                Emblem = world.Culture.MainCultureOf(settlement.Id) is { } emblemCulture && world.Culture.Culture(emblemCulture) is { } mainCulture ? DemoHeraldry.EmblemOf(mainCulture) : null,
                FaithSymbol = world.Culture.Faith.MainFaithOf(settlement.Id) is { } symbolFaith && world.Culture.Faith.Faith(symbolFaith) is { } mainFaith ? DemoHeraldry.FaithSymbolOf(mainFaith) : null,
                FaithId = world.Culture.Faith.MainFaithOf(settlement.Id) ?? 0
            },
            new DemoDetailTab("代表人物", people) { People = entries },
            new DemoDetailTab("技術", techs) { Badges = knownBadges, KeyBadgeCount = keyBadges.Count, KeyTitle = keyTitle },
            new DemoDetailTab("研究", research) { Badges = researchBadges },
            new DemoDetailTab("歷史", history)
        }, settlement.Id);
    }
}
