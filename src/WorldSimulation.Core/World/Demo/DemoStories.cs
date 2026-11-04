namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-16 章第 4 節之 3（讓紀錄成故事，十字軍之王那一半）：把已提交事件的因果鏈與前後文說成一句話。
/// 只讀事件（含讀檔前的較早事件），不改世界、不抽亂數；同一世界讀檔前後結果相同。
/// </summary>
public static class DemoStories
{
    /// <summary>【TEMP】聚落成為遺跡時，回看幾週內發生的事當作原因；人物小傳最多列幾件事。</summary>
    public const int TempRuinLookbackWeeks = 104;
    public const int TempBiographyItems = 3;

    /// <summary>沿直接原因往回走到根因（由近到遠，不含自己；防環最多 16 步）。</summary>
    public static IReadOnlyList<DemoSettlementEvent> CauseChain(DemoSettlementEvent e, IReadOnlyDictionary<long, DemoSettlementEvent> byId)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(byId);
        var chain = new List<DemoSettlementEvent>();
        var at = e;
        for (var k = 0; k < 16 && at.CauseEventId > 0 && byId.TryGetValue(at.CauseEventId, out var parent); k++)
        {
            chain.Add(parent);
            at = parent;
        }
        return chain;
    }

    /// <summary>建立新聚落的起因（一句話，接在編年史條目後面）；說不出來＝null。</summary>
    public static string? WhyFounded(DemoSettlementEvent founded, IReadOnlyDictionary<long, DemoSettlementEvent> byId)
    {
        var chain = CauseChain(founded, byId);
        var emigration = chain.FirstOrDefault(x => x.Kind == DemoSettlementEventKind.Emigration);
        if (emigration is null)
            return null;
        if (chain.Any(x => x.Kind == DemoSettlementEventKind.Heatwave))
            return "熱浪讓家鄉鬧饑荒，一群人帶著存糧出走";
        if (chain.Any(x => x.Kind == DemoSettlementEventKind.Hunger))
            return "家鄉缺糧，一群人出走尋找新天地";
        if (emigration.Cause.StartsWith("人口接近", StringComparison.Ordinal))
            return "家鄉人口漸多，一戶人家分出去開墾";
        return null;
    }

    /// <summary>聚落成為遺跡前兩年內發生的事（一句話）；沒有特別的事＝null。</summary>
    public static string? WhyRuined(DemoSettlementEvent ruined, IReadOnlyList<DemoSettlementEvent> events)
    {
        ArgumentNullException.ThrowIfNull(ruined);
        ArgumentNullException.ThrowIfNull(events);
        var before = events.Where(x => x.SettlementId == ruined.SettlementId && x.Week <= ruined.Week
            && x.Week > ruined.Week - TempRuinLookbackWeeks).ToList();
        var parts = new List<string>();
        foreach (var d in before.Where(x => DemoDisasters.IsDisaster(x.Kind) || DemoEnvironment.IsSpell(x.Kind)).Select(x => DemoEventTypes.Of(x.Kind).Name).Distinct())
            parts.Add(d);
        if (before.Any(x => x.Kind == DemoSettlementEventKind.StarvationDeath))
            parts.Add("饑荒");
        if (before.Any(x => x.Kind == DemoSettlementEventKind.Emigration))
            parts.Add("居民陸續離開");
        return parts.Count == 0 ? null : string.Join("、", parts.Take(3)) + "之後";
    }

    /// <summary>
    /// 代表人物小傳（兩三行）：出身與上任、在任期間聚落經歷的大事（世界首次的技術、天災、建立的子村、饑荒），玩家用語、不含數字以外的開發資訊。
    /// </summary>
    public static IReadOnlyList<string> Biography(DemoObserverWorld world, DemoRepresentative r, IReadOnlyList<DemoSettlementEvent>? allEvents = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(r);
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));
        var events = allEvents ?? world.SettlementEvents();
        var during = events.Where(e => e.Week >= r.AppointedWeek).ToList();
        var here = during.Where(e => e.SettlementId == r.SettlementId).ToList();
        var items = new List<(int Week, string Text)>();
        foreach (var e in here.Where(e => e.Kind == DemoSettlementEventKind.KnowledgeLearned))
            items.Add((e.Week, $"第 {Year(e.Week)} 年帶領村人學會{DemoKnowledge.Name((DemoTechnology)e.Count)}"));
        foreach (var e in here.Where(e => DemoDisasters.IsDisaster(e.Kind) || DemoEnvironment.IsSpell(e.Kind)))
            items.Add((e.Week, $"第 {Year(e.Week)} 年經歷{DemoEventTypes.Of(e.Kind).Name}"));
        var children = world.Sites.Settlements.Where(s => s.IsFounded && s.FoundedFromSettlementId == r.SettlementId && s.FoundedWeek >= r.AppointedWeek).ToList();
        foreach (var s in children)
            items.Add((s.FoundedWeek, $"第 {Year(s.FoundedWeek)} 年送出開拓隊建立{s.Name}"));
        var famineYears = here.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath).Select(e => Year(e.Week)).Distinct().ToList();
        if (famineYears.Count > 0)
            items.Add((here.First(e => e.Kind == DemoSettlementEventKind.StarvationDeath).Week,
                famineYears.Count == 1 ? $"第 {famineYears[0]} 年熬過一場饑荒" : $"熬過 {famineYears.Count} 次饑荒"));
        // 重要的在前：天災與建村、饑荒、技術；同類依時間。
        int Rank(string t) => t.Contains("建立", StringComparison.Ordinal) ? 0 : t.Contains("經歷", StringComparison.Ordinal) ? 1 : t.Contains("饑荒", StringComparison.Ordinal) ? 2 : 3;
        var picked = items.OrderBy(i => Rank(i.Text)).ThenBy(i => i.Week).Take(TempBiographyItems).OrderBy(i => i.Week).Select(i => i.Text).ToList();
        var lines = new List<string>();
        if (picked.Count > 0)
            lines.Add("　" + string.Join("；", picked));
        var predecessors = world.ChronicleOf(r.SettlementId).Representatives
            .Count(l => l.Week < r.AppointedWeek && l.Text.EndsWith("成為" + DemoRepresentative.RoleName(r.Role), StringComparison.Ordinal));
        if (predecessors > 0)
            lines.Add($"　本村第 {predecessors + 1} 任{DemoRepresentative.RoleName(r.Role)}");
        return lines;
    }
}
