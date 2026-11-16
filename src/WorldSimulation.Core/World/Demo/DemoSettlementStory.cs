namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10d 章 C：聚落小史的一個時期。<see cref="Title"/>＝這個時期是什麼（草創、興起、衰退、廢棄），
/// <see cref="Summary"/>＝一句總結（只數這段時間裡發生的事），<see cref="Lines"/>＝這段時間的每一筆紀錄（依時間）。
/// </summary>
public sealed record DemoSettlementPhase(int FromYear, int ToYear, string Title, string Summary, IReadOnlyList<string> Lines)
{
    /// <summary>「第 1～23 年　草創：營地」。</summary>
    public string Heading => FromYear == ToYear ? $"第 {FromYear} 年　{Title}" : $"第 {FromYear}～{ToYear} 年　{Title}";
}

/// <summary>第 08-10d 章 C：分期後的聚落小史——以什麼聞名，加上依時間排列的各個時期。</summary>
public sealed record DemoSettlementStoryView(string Renown, IReadOnlyList<DemoSettlementPhase> Phases);

/// <summary>
/// 第 08-10d 章 C：把聚落小史（<see cref="DemoSettlementHistory.Lines"/> 的同一批紀錄）依等級升降切成時期，每個時期一句總結，
/// 並用現況說出這個聚落以什麼聞名。只讀既有紀錄（聚落資料、聚落自己的小史），不改世界、不抽亂數；同一世界讀檔前後結果相同。
/// 只組合已經發生的事實，不添加原因或評語。
/// </summary>
public static class DemoSettlementStory
{
    /// <summary>【TEMP】分出幾個聚落以上稱為「母城」；最多說幾項聞名的事。</summary>
    public const int TempMotherChildren = 3, TempRenownTraits = 2;

    private enum Kind { Founding, TierUp, TierDown, Population, Tech, Farm, FarmLost, Era, Event, Child, Leader, Peak, Ruin }

    private static int YearOf(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));

    private static Kind KindOf(DemoChronicleLine line)
    {
        if (DemoSettlementChronicle.IsTierChange(line))
            return line.Text.Contains(DemoSettlementChronicle.TierUpMarker, StringComparison.Ordinal) ? Kind.TierUp : Kind.TierDown;
        if (DemoSettlementChronicle.IsPopulationMilestone(line))
            return Kind.Population;
        if (line.Text.StartsWith("學會", StringComparison.Ordinal))
            return Kind.Tech;
        if (line.Text == "開墾一塊農田")
            return Kind.Farm;
        if (line.Text.StartsWith("一塊農田無人耕作", StringComparison.Ordinal))
            return Kind.FarmLost;
        return line.Text.StartsWith("率先讓世界進入", StringComparison.Ordinal) ? Kind.Era : Kind.Event;
    }

    /// <summary>等級升降那一筆之後是哪一級（「…發展為村莊」「…，降為營地」）；看不出來回傳 null。</summary>
    private static string? TierAfter(string text)
    {
        foreach (var marker in new[] { DemoSettlementChronicle.TierUpMarker, DemoSettlementChronicle.TierDownMarker })
        {
            var at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
                return text[(at + marker.Length)..];
        }
        return null;
    }

    public static DemoSettlementStoryView Build(DemoObserverWorld world, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var settlement = world.Sites.Settlements.Single(s => s.Id == settlementId);
        var chronicle = world.ChronicleOf(settlementId);
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var children = world.Sites.Settlements.Where(s => s.IsFounded && s.FoundedFromSettlementId == settlementId)
            .OrderBy(s => s.FoundedWeek).ThenBy(s => s.Id).ToList();

        // 與 DemoSettlementHistory.Lines 同一批紀錄，依週次排列（同一週保持加入順序）。
        var items = new List<(int Week, Kind Kind, string Text)>
        {
            settlement.IsFounded
                ? (settlement.FoundedWeek, Kind.Founding, $"{names.GetValueOrDefault(settlement.FoundedFromSettlementId, "未知聚落")}的開拓隊移居此地，建立聚落")
                : (0, Kind.Founding, "開局時已有人定居")
        };
        items.AddRange(chronicle.Milestones.Select(l => (l.Week, KindOf(l), l.Text)));
        items.AddRange(children.Select(c => (c.FoundedWeek, Kind.Child, $"分出開拓隊，建立{c.Name}")));
        items.AddRange(chronicle.Representatives.Select(l => (l.Week, Kind.Leader, l.Text)));
        if (chronicle.Peak > 0)
            items.Add((chronicle.PeakWeek, Kind.Peak, $"人口高峰 {chronicle.Peak} 人"));
        if (settlement.IsRuin)
            items.Add((settlement.RuinedWeek, Kind.Ruin, "人口歸零，成為遺跡"));
        // 建立一定排最前面、成為遺跡一定排最後面（同一週的其他紀錄夾在中間）。
        var ordered = items.Select((x, i) => (x.Week, x.Kind, x.Text, Index: i))
            .OrderBy(x => x.Kind == Kind.Founding ? 0 : x.Kind == Kind.Ruin ? 2 : 1).ThenBy(x => x.Week).ThenBy(x => x.Index).ToList();

        var thisYear = Math.Max(1, DemoSettlementHistory.YearOfDay(world.Snapshot().DaysElapsed));
        var lastYear = settlement.IsRuin ? YearOf(settlement.RuinedWeek) : thisYear;
        var phases = new List<DemoSettlementPhase>();
        var title = "草創：營地";
        var fromYear = YearOf(ordered[0].Week);
        var current = new List<(int Week, Kind Kind, string Text)>();
        void Close(int toYear)
        {
            if (current.Count > 0)
                phases.Add(new DemoSettlementPhase(fromYear, Math.Max(fromYear, toYear), title, Summarize(current),
                    current.Select(x => $"第 {YearOf(x.Week)} 年　{x.Text}").ToArray()));
            current = new List<(int Week, Kind Kind, string Text)>();
        }
        foreach (var x in ordered)
        {
            if (x.Kind is Kind.TierUp or Kind.TierDown or Kind.Ruin)
            {
                Close(YearOf(x.Week));
                fromYear = YearOf(x.Week);
                title = x.Kind switch
                {
                    Kind.TierUp => $"興起：發展為{TierAfter(x.Text) ?? "更大的聚落"}",
                    Kind.TierDown => $"衰退：降為{TierAfter(x.Text) ?? "較小的聚落"}",
                    _ => "廢棄"
                };
            }
            current.Add((x.Week, x.Kind, x.Text));
        }
        Close(lastYear);
        return new DemoSettlementStoryView(Renown(world, settlement, chronicle, children.Count, thisYear), phases.AsReadOnly());
    }

    /// <summary>一個時期的一句總結：只數這段時間裡的紀錄（分界那一筆——升級、降級、成為遺跡——已經寫在標題）。</summary>
    private static string Summarize(IReadOnlyList<(int Week, Kind Kind, string Text)> items)
    {
        int Count(Kind kind) => items.Count(x => x.Kind == kind);
        var parts = new List<string>();
        if (items.LastOrDefault(x => x.Kind == Kind.Population).Text is { } population)
            parts.Add(population);
        if (items.FirstOrDefault(x => x.Kind == Kind.Peak).Text is { } peak)
            parts.Add(peak);
        if (Count(Kind.Era) > 0)
            parts.Add(items.First(x => x.Kind == Kind.Era).Text);
        if (Count(Kind.Tech) is var techs and > 0)
            parts.Add($"學會 {techs} 項技術");
        if (Count(Kind.Farm) is var farms and > 0)
            parts.Add($"開墾 {farms} 塊農田");
        if (Count(Kind.Child) is var born and > 0)
            parts.Add($"分出 {born} 個聚落");
        if (items.Count(x => x.Kind == Kind.Leader && x.Text.Contains(" 成為", StringComparison.Ordinal)) is var leaders and > 0)
            parts.Add($"{leaders} 位代表人物就任");
        if (Count(Kind.Event) is var events and > 0)
            parts.Add($"歷經 {events} 件大事");
        if (parts.Count > 0)
            return string.Join("、", parts) + "。";
        // 只有分界那一筆（剛升級、剛降級、成為遺跡）：標題已經說完了。
        return items.All(x => x.Kind is Kind.TierUp or Kind.TierDown or Kind.Ruin) ? "" : "沒有留下特別的紀錄。";
    }

    /// <summary>以什麼聞名：依現況挑最多 <see cref="TempRenownTraits"/> 項（率先開啟時代、人口最多、技術最多、母城、擅長的領域）；都沒有就說建立幾年、什麼等級。</summary>
    private static string Renown(DemoObserverWorld world, DemoSettlement settlement, DemoSettlementChronicle chronicle, int children, int thisYear)
    {
        if (settlement.IsRuin)
            return "已成遺跡" + (chronicle.Peak > 0 ? $"，全盛時有 {chronicle.Peak} 人" : "") + (children > 0 ? $"，曾分出 {children} 個聚落" : "");
        var traits = new List<string>();
        if (chronicle.Milestones.FirstOrDefault(l => KindOf(l) == Kind.Era) is { } era)
            traits.Add(era.Text);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        if (alive.Count > 1)
        {
            var people = world.SettlementPopulation(settlement.Id);
            if (people > 0 && people == alive.Max(s => world.SettlementPopulation(s.Id)))
                traits.Add("世界上人口最多的聚落");
            var techs = world.Knowledge.KnownBy(settlement.Id).Count;
            if (techs > 0 && techs == alive.Max(s => world.Knowledge.KnownBy(s.Id).Count))
                traits.Add("掌握最多技術的聚落");
        }
        if (children >= TempMotherChildren)
            traits.Add($"{children} 個聚落的母城");
        if (world.Knowledge.LeadingDomain(settlement.Id) is { } domain)
            traits.Add($"以{DemoPlayerDetail.DomainName(domain)}見長");
        if (traits.Count > 0)
            return string.Join("、", traits.Take(TempRenownTraits));
        var foundedYear = settlement.IsFounded ? YearOf(settlement.FoundedWeek) : 1;
        return $"建立 {Math.Max(0, thisYear - foundedYear)} 年的{DemoSettlementHistory.TierName(chronicle.Tier)}";
    }

    /// <summary>詳情頁「歷史」用的各行：第一行「聞名」，之後每個時期一行標題與總結（【…】開頭），底下是那段時間的紀錄。</summary>
    public static IReadOnlyList<string> Lines(DemoObserverWorld world, int settlementId)
    {
        var story = Build(world, settlementId);
        var lines = new List<string> { $"聞名：{story.Renown}" };
        foreach (var phase in story.Phases)
        {
            lines.Add($"【{phase.Heading}】{phase.Summary}");
            lines.AddRange(phase.Lines);
        }
        return lines.AsReadOnly();
    }
}
