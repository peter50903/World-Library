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

    /// <summary>第 08-18b 章第 5 節：人口金字塔（由年輕到年老；沒有＝不畫）。</summary>
    public IReadOnlyList<DemoPyramidBand> Pyramid { get; init; } = Array.Empty<DemoPyramidBand>();

    /// <summary>第 08-16 章：現況分頁上方的文化紋章與信仰符號（沒有＝null）。</summary>
    public DemoEmblem? Emblem { get; init; }

    public string? FaithSymbol { get; init; }

    public int FaithId { get; init; }

    /// <summary>第 08-16 章第 15 節：現況分頁的主要文化（介面放「查看文化」連結；沒有＝0）。</summary>
    public int CultureId { get; init; }
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
        // 第 08-16 章第 14 節：伐木與採礦。
        var at = new DemoCellRef(cell.Column, cell.Row);
        var week = world.CommittedWeeks;
        var spells = world.Wildlife.Spells;
        var mining = DemoEnvironment.MiningAt(spells, at, week);
        if (mining > 1f)
            lines.Add("礦脈已挖空，只剩廢棄的礦坑");
        else if (mining >= 0f && spells.First(x => x.Kind == DemoSettlementEventKind.MiningBegan && x.Cells.Contains(at)) is var dig)
            lines.Add($"{NameOf(world, dig.SettlementId)}正在開採這處礦脈（已開採 {(int)(mining * 100)}%）");
        var logged = DemoEnvironment.LoggedAt(spells, at, week);
        if (logged >= 0f)
            lines.Add(logged < 0.5f ? "森林剛被砍伐，只剩樹樁與幼苗" : "砍過的森林，樹木正慢慢長回");
        if (world.Dungeons.FirstOrDefault(d => d.Cell.Column == cell.Column && d.Cell.Row == cell.Row) is { } dungeon)
        {
            lines.Add($"地下城：{dungeon.Name}（{dungeon.Layers} 層，第 {DemoSettlementHistory.YearOfDay(checked(dungeon.AppearedWeek * 7))} 年出現）"); // 第 12-1 章 C4
            // 第 12-2 章：怪物數與勢力範圍（沉寂時寫還要多久再生）。
            if (world.MonsterNests(world.CommittedWeeks).FirstOrDefault(n => n.Dungeon.EventId == dungeon.EventId) is { } nest)
                lines.Add(nest.Dormant
                    ? $"怪物已被殺光，地下城沉寂中（約 {Math.Max(0, DemoMonsters.TempDormantWeeks - (world.CommittedWeeks - nest.DormantSinceWeek)) / 52 + 1} 年內再生）"
                    : $"怪物：{nest.Monsters} 隻（{DemoCombat.MonsterLevel(nest.Dungeon)} 級）｜佔據 {nest.Cells.Count} 格（會襲擊鄰近的聚落，也會被討伐；範圍內不能建村）");
        }
        if (DemoWildlife.CellNote(world.Wildlife, world.Map, cell) is { } note)
            lines.Add(note); // 第 12-1 章 C1
        foreach (var batch in world.MigrationsInTransit().Where(b => b.Cell.Column == cell.Column && b.Cell.Row == cell.Row))
            lines.Add($"{(batch.Founding ? "開拓隊" : "遷徙隊伍")}：{batch.People} 人，從{batch.FromName}前往{batch.ToName}");
        return new DemoPlayerDetail(lines[0], new[] { new DemoDetailTab("地點", lines) }, null);
    }

    /// <summary>
    /// 第 08-18i 章：聚落現況的頁首四塊（RimWorld 的狀態列、十字軍之王的主要數值）：
    /// 人口與近一年的增減（出生、死亡、遷入遷出）、糧食、隸屬、危機（挨餓、戰爭與請願、獨立派系、怪物、近一年的天災；沒有寫「無」）。
    /// </summary>
    public static IReadOnlyList<string> Glance(DemoObserverWorld world, DemoSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settlement);
        var id = settlement.Id;
        var recent = world.WeekReports().Where(w => w.Week > world.CommittedWeeks - 52).ToList();
        var ledgers = recent.SelectMany(w => w.Settlements).Where(l => l.SettlementId == id).ToList();
        var births = ledgers.Sum(l => l.Births);
        var deaths = ledgers.Sum(l => l.Deaths + l.DisasterDeaths);
        var moved = ledgers.Sum(l => l.Immigrants - l.Emigrants);
        var net = births - deaths + moved;
        string Signed(long v) => v > 0 ? $"+{v}" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"【人口】{world.SettlementPopulation(id)}（近一年 {Signed(net)}：出生 {births}、死亡 {deaths}、遷徙 {Signed(moved)}）",
            world.IsStarving(id) ? $"【糧食】短缺，有人挨餓（存糧 {world.SettlementFood(id)}）" : $"【糧食】充足（存糧 {world.SettlementFood(id)}）"
        };
        var territory = world.Territory();
        string Name(int x) => world.Sites.Settlements.FirstOrDefault(s => s.Id == x) is { } s ? DemoSettlementHistory.TitledName(s, world.ChronicleOf(x).Tier) : $"#{x}";
        var vassals = territory.VassalsOf(id).Count;
        lines.Add(territory.LiegeOf(id) is { } liege
            ? $"【隸屬】{Name(liege)}" + (vassals > 0 ? $"（轄下 {vassals} 個聚落）" : "")
            : vassals > 0 ? $"【隸屬】獨立，統領 {vassals} 個聚落" : "【隸屬】獨立");
        var crises = new List<string>();
        if (world.IsStarving(id))
            crises.Add("挨餓");
        if (world.Realms().StruggleOf(id) is { } struggle)
            crises.Add(struggle.Armed ? "獨立戰爭" : "請願抗議");
        else if (world.Realms().Faction.ContainsKey(id))
            crises.Add("加入了獨立派系");
        var here = new DemoCellRef(settlement.Column, settlement.Row);
        if (world.MonsterNests(world.CommittedWeeks).Any(n => n.Cells.Count > 0 && n.Cells.Min(c => DemoHexWorld.Distance(c, here)) <= DemoMonsters.TempRaidReach))
            crises.Add("緊鄰怪物地盤");
        var disasters = recent.SelectMany(w => w.Events).Where(e => e.SettlementId == id && DemoDisasters.IsDisaster(e.Kind))
            .Select(e => DemoDisasters.Name(e.Kind)).Distinct().ToList();
        if (disasters.Count > 0)
            crises.Add("近一年遭遇" + string.Join("、", disasters));
        lines.Add(crises.Count == 0 ? "【危機】無" : "【危機】" + string.Join("、", crises));
        return lines;
    }

    private static string NameOf(DemoObserverWorld world, int settlementId)
        => world.Sites.Settlements.FirstOrDefault(x => x.Id == settlementId)?.Name ?? "某聚落";

    private static DemoPlayerDetail Settlement(DemoObserverWorld world, DemoSettlement settlement)
    {
        var day = world.Snapshot().DaysElapsed;
        int YearOfWeek(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var tier = DemoSettlementHistory.TierName(settlement, world.ChronicleOf(settlement.Id).Tier);

        // 現況
        var now = new List<string>
        {
            $"{DemoSettlementHistory.TitledName(settlement, world.ChronicleOf(settlement.Id).Tier)}（{tier}）",
            settlement.IsFounded
                ? $"第 {YearOfWeek(settlement.FoundedWeek)} 年由{names.GetValueOrDefault(settlement.FoundedFromSettlementId, "未知聚落")}的開拓隊建立"
                : "開局時已有人定居",
            $"家系：{names.GetValueOrDefault(DemoLineageMap.LineageRoot(world.Sites, settlement.Id), "未知")}"
        };
        if (!settlement.IsRuin)
        {
            // 第 08-18i 章（GPT 試玩回饋 03：先回答「過得好不好」）：頁首四塊——人口與一年增減、糧食、隸屬、危機；其餘是細節。
            now.InsertRange(1, Glance(world, settlement));
            now.Add("── 詳細 ──");
            // 第 08-19 章：勢力（獨立或隸屬於誰、轄下有哪些聚落）。
            DemoSettlementTier TierOf(int id) => world.ChronicleOf(id).Tier;
            now.Add(DemoTerritory.Describe(world.Territory(), world.Sites, settlement.Id, TierOf));
            // 第 08-19b 章：對上級的忠誠度明細（WorldBox 的城市忠誠）、派系與進行中的鬥爭。
            now.AddRange(world.LoyaltyLines(settlement.Id));
            if (world.Realms().StruggleOf(settlement.Id) is { } struggle)
                now.Add(DemoRealms.Describe(struggle, world.Sites, world.CommittedWeeks));
            if (world.Realms().WarOf(settlement.Id) is { } war) // 第 08-19e 章
                now.Add(DemoRealms.DescribeWar(war, world.Sites, world.CommittedWeeks));
            now.Add(DemoCombat.Describe(world.CombatForceOf(settlement.Id))); // 第 12-2c 節
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
            // 第 12-2a 節：緊鄰怪物佔據的範圍（會被襲擊）。
            var here = new DemoCellRef(settlement.Column, settlement.Row);
            var threats = world.MonsterNests(world.CommittedWeeks).Where(n => n.Cells.Count > 0
                && n.Cells.Min(c => DemoHexWorld.Distance(c, here)) <= DemoMonsters.TempRaidReach).Select(n => $"{n.Dungeon.Name}（{n.Monsters} 隻）").ToList();
            if (threats.Count > 0)
                now.Add("⚔ 緊鄰怪物的地盤：" + string.Join("、", threats));
        }
        // 第 08-8 章 B1：文化比例（由多到少）與主要文化的特性。
        var shares = world.Culture.SharesOf(settlement.Id).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).ToList();
        if (shares.Count > 0)
        {
            now.Add("文化：" + string.Join("、", shares.Select(s => $"{world.Culture.Culture(s.CultureId)?.Name ?? "？"} {(s.Permille + 5) / 10}%")));
            // 第 08-18i 章：完整的傳統、信條與教義收到文化頁、信仰頁（現況上方有「查看文化」「查看信仰」連結）。
            // 第 08-8 章 B3：信仰比例與主要信仰的信條、教義。
            var faith = world.Culture.Faith;
            now.Add("信仰：" + string.Join("、", faith.SharesOf(settlement.Id).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
                .Select(s => $"{(s.CultureId == 0 ? "無信仰" : faith.Faith(s.CultureId)?.Name ?? "？")} {(s.Permille + 5) / 10}%")));
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
        {
            research.Add(settlement.IsRuin ? "已無人研究" : "目前沒有進行中的研究");
            if (!settlement.IsRuin)
            {
                // 第 08-18i 章（GPT 試玩回饋 07：看不出下一步會發生什麼）：前置都已具備、還沒開始累積的技術（依知識樹的前置規則）。
                bool Has(string techId) => DemoTechCatalog.Contains(techId)
                    && (DemoKnowledge.IsOpening(DemoTechnology.Of(techId)) || known.Any(r => r.Technology.Id == techId));
                var ready = DemoTechnology.All.Where(t => !DemoKnowledge.IsOpening(t) && !known.Any(r => r.Technology == t) && t.Node.Parents.All(Has))
                    .OrderBy(t => t.Node.Tier).ThenBy(t => t).Take(MaxResearchLines).ToList();
                research.Add(ready.Count == 0
                    ? "還沒有前置都具備的新技術"
                    : "前置都已具備、居民實踐後就會開始研究：" + string.Join("、", ready.Select(DemoKnowledge.Name)));
            }
        }

        // 歷史（聚落小史，去掉開頭的規模摘要）
        var history = DemoSettlementHistory.Lines(world, settlement.Id)
            .SkipWhile(l => !l.StartsWith("聚落小史", StringComparison.Ordinal)).Skip(1)
            .Select(l => Clean(l.Trim()))
            .ToList();

        return new DemoPlayerDetail($"{DemoSettlementHistory.TitledName(settlement, world.ChronicleOf(settlement.Id).Tier)}（{tier}）", new[]
        {
            new DemoDetailTab("現況", now)
            {
                Emblem = world.Culture.MainCultureOf(settlement.Id) is { } emblemCulture && world.Culture.Culture(emblemCulture) is { } mainCulture ? DemoHeraldry.EmblemOf(mainCulture) : null,
                FaithSymbol = world.Culture.Faith.MainFaithOf(settlement.Id) is { } symbolFaith && world.Culture.Faith.Faith(symbolFaith) is { } mainFaith ? DemoHeraldry.FaithSymbolOf(mainFaith) : null,
                FaithId = world.Culture.Faith.MainFaithOf(settlement.Id) ?? 0,
                CultureId = world.Culture.MainCultureOf(settlement.Id) ?? 0
            },
            new DemoDetailTab("代表人物", people) { People = entries },
            new DemoDetailTab("技術", techs) { Badges = knownBadges, KeyBadgeCount = keyBadges.Count, KeyTitle = keyTitle },
            new DemoDetailTab("研究", research) { Badges = researchBadges },
            new DemoDetailTab("歷史", history),
            PopulationTab(world, settlement)
        }, settlement.Id);
    }

    /// <summary>第 08-18b 章第 5 節介面第二批：「人口」頁——排行與人口金字塔（有年齡群時；畫面另畫成橫條圖）。</summary>
    private static DemoDetailTab PopulationTab(DemoObserverWorld world, DemoSettlement settlement)
    {
        var lines = DemoPopulationPyramid.Rankings(world, settlement.Id).ToList();
        var pyramid = world.HumanPopulationSnapshot is { } snapshot
            ? DemoPopulationPyramid.Of(snapshot.Groups, settlement.Id, world.Snapshot().DaysElapsed)
            : Array.Empty<DemoPyramidBand>();
        if (pyramid.Any(b => b.Total > 0))
        {
            lines.Add("");
            lines.Add("人口金字塔（男｜女）");
            lines.AddRange(Enumerable.Reverse(pyramid).Select(b => $"{b.Label}：{b.Male}｜{b.Female}"));
        }
        if (lines.Count == 0)
            lines.Add(settlement.IsRuin ? "已無人居住" : "還沒有人口資料");
        return new DemoDetailTab("人口", lines) { Pyramid = pyramid };
    }
}
