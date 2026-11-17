namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10i 章：一件成名的事蹟——哪個聚落、哪個事件、怎麼說（「帶隊殺光灰燼洞窟的怪物」）、得到什麼傳說物品（沒有＝空字串）。
/// </summary>
public sealed record DemoHeroDeed(int SettlementId, DemoSettlementEvent Event, string Text, string Item);

/// <summary>第 08-10i 章：一位英雄與他成名的事蹟（由英雄的登記紀錄與事件推得，不另存）。</summary>
public sealed record DemoLegend(DemoPersonRecord Hero, DemoHeroDeed Deed)
{
    public int Week => Deed.Event.Week;

    /// <summary>傳說物品（沒有＝空字串）。</summary>
    public string Item => Deed.Item;
}

/// <summary>
/// 第 08-10i 章【TEMP 規則】英雄與傳說物品（使用者 2026-11-17：英雄來自成名的士兵與領袖的家族成員）。
/// <list type="bullet">
/// <item><b>成名的事蹟</b>（發生的下一週，那個聚落出一位英雄；每個聚落同時最多一位在世的英雄）：
///   殺光一個地下城的怪物；打贏征服戰爭（不戰而降不算）；打贏獨立戰爭；擊退來犯的大軍；擊潰叛軍。</item>
/// <item><b>出身</b>：聚落的領袖有子女輩的成年人時，<see cref="TempKinPercent"/>% 是領袖的子女（沿用家族的姓、記下父母）；否則是士兵出身（最年輕的成年人，另取姓名）。</item>
/// <item><b>效果</b>：英雄在世時，本聚落士兵的等級 +1（第 12-2c 節）。英雄與一般人一樣會老死、餓死、死於災禍。</item>
/// <item><b>傳說物品</b>：殺光地下城得到核心留下的裝備（第 12-1 章）；打贏征服戰爭奪得對方的寶物。名稱＝來源的名稱＋「之」＋器物（由事件編號決定）。
///   英雄在世由英雄持有；領袖家族出身的英雄死後由家族收藏；士兵出身的英雄死後下落不明。其他事蹟沒有物品。</item>
/// </list>
/// </summary>
public static class DemoHeroes
{
    /// <summary>【TEMP】領袖有子女輩成年人時，英雄是領袖子女的機率。</summary>
    public const int TempKinPercent = 50;

    private static readonly string[] Kinds = { "劍", "槍", "弓", "盾", "冠", "甲", "杖", "斧" };

    private const string ClearedMarker = "的怪物被殺光", ConquestMarker = "征服了", RebelWinMarker = "在獨立戰爭中擊敗", RepelledMarker = "敗退", CrushedMarker = "擊潰";

    /// <summary>
    /// 一週的事件裡成名的事蹟（每個聚落最多一件，依事件順序取第一件）。<paramref name="nameOf"/>＝聚落名稱（只用來寫事蹟的句子）。
    /// 殺光地下城的功勞算給同一週在那個地下城最後一筆擊殺的聚落；找不到擊殺紀錄時算給事件記的鄰近聚落。
    /// </summary>
    public static IReadOnlyList<DemoHeroDeed> DeedsOf(IReadOnlyList<DemoSettlementEvent>? events, Func<int, string>? nameOf = null)
    {
        var deeds = new List<DemoHeroDeed>();
        if (events is null)
            return deeds;
        string Name(long id) => nameOf?.Invoke((int)id) ?? "";
        foreach (var e in events)
        {
            var cause = DemoPlayerDetail.Clean(e.Cause);
            DemoHeroDeed? deed = e.Kind switch
            {
                DemoSettlementEventKind.DungeonCleared
                    => new DemoHeroDeed(SlayerOf(e, events), e, $"帶隊殺光{DungeonOf(e)}的怪物", ItemName(DungeonOf(e), e.EventId)),
                DemoSettlementEventKind.Conquered when cause.Contains(ConquestMarker, StringComparison.Ordinal)
                    => new DemoHeroDeed((int)e.Count, e, $"率軍攻下{e.SettlementName}", e.SettlementName.Length > 0 ? ItemName(e.SettlementName, e.EventId) : ""),
                DemoSettlementEventKind.Independence when cause.Contains(RebelWinMarker, StringComparison.Ordinal)
                    => new DemoHeroDeed(e.SettlementId, e, $"率眾擊敗{Name(e.Count)}，為家鄉爭得獨立", ""),
                DemoSettlementEventKind.WarEnded when cause.Contains(RepelledMarker, StringComparison.Ordinal)
                    => new DemoHeroDeed((int)e.Count, e, $"擊退{e.SettlementName}來犯的大軍", ""),
                DemoSettlementEventKind.SecessionFailed when cause.Contains(CrushedMarker, StringComparison.Ordinal)
                    => new DemoHeroDeed((int)e.Count, e, $"擊潰{e.SettlementName}的叛軍", ""),
                _ => null
            };
            if (deed is not null && deeds.All(d => d.SettlementId != deed.SettlementId))
                deeds.Add(deed);
        }
        return deeds;
    }

    /// <summary>上一週立下事蹟的聚落（下一週各出一位英雄）。</summary>
    public static IReadOnlySet<int> Deeds(IReadOnlyList<DemoSettlementEvent>? events)
        => DeedsOf(events).Select(d => d.SettlementId).ToHashSet();

    private static int SlayerOf(DemoSettlementEvent cleared, IReadOnlyList<DemoSettlementEvent> events)
        => events.LastOrDefault(e => e.Kind == DemoSettlementEventKind.MonsterSlain && e.Week == cleared.Week
            && DemoMonsters.DecodeKills(e.Count).CellId == cleared.Count)?.SettlementId ?? cleared.SettlementId;

    /// <summary>傳說物品的名稱：「灰燼洞窟之劍」「東塔林之冠」。</summary>
    public static string ItemName(string source, long eventId)
        => source + "之" + Kinds[(int)(Math.Abs(eventId) % Kinds.Length)];

    /// <summary>「地下城沉寂」事件裡的地下城名稱。</summary>
    public static string DungeonOf(DemoSettlementEvent cleared)
    {
        ArgumentNullException.ThrowIfNull(cleared);
        var text = DemoPlayerDetail.Clean(cleared.Cause);
        var at = text.IndexOf(ClearedMarker, StringComparison.Ordinal);
        return at > 0 ? text[..at] : "地下城";
    }

    /// <summary>世界上所有的英雄與事蹟（依成名的時間）：每位英雄對應他成名前一週、他的聚落立下的事蹟。</summary>
    public static IReadOnlyList<DemoLegend> Legends(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var heroes = world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Hero).OrderBy(p => p.AppointedWeek).ThenBy(p => p.Id).ToList();
        if (heroes.Count == 0)
            return Array.Empty<DemoLegend>();
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var weeks = heroes.Select(h => h.AppointedWeek - 1).ToHashSet();
        var byWeek = world.SettlementEvents().Where(e => weeks.Contains(e.Week)).GroupBy(e => e.Week)
            .ToDictionary(g => g.Key, g => DeedsOf(g.ToList(), id => names.GetValueOrDefault(id, "")));
        var legends = new List<DemoLegend>();
        foreach (var hero in heroes)
            if (byWeek.TryGetValue(hero.AppointedWeek - 1, out var deeds) && deeds.FirstOrDefault(d => d.SettlementId == hero.SettlementId) is { } deed)
                legends.Add(new DemoLegend(hero, deed));
        return legends;
    }
}
