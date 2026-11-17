namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10i 章：一件傳說物品與它的來歷——哪一年、哪個聚落的哪位英雄殺光了哪個地下城的怪物而得到。
/// 由英雄的登記紀錄與「地下城沉寂」事件推得，不另存。
/// </summary>
public sealed record DemoLegend(string Item, DemoPersonRecord Hero, int Week, string Dungeon);

/// <summary>
/// 第 08-10i 章【TEMP 規則】英雄與傳說物品（使用者 2026-11-17：英雄來自成名的士兵與領袖的家族成員）。
/// <list type="bullet">
/// <item><b>成名</b>：一個聚落的人馬殺光一個地下城的怪物（地下城沉寂）的下一週，帶隊的人具名成為這個聚落的英雄；每個聚落同時最多一位在世的英雄。</item>
/// <item><b>出身</b>：聚落的領袖有子女輩的成年人時，<see cref="TempKinPercent"/>% 是領袖的子女（沿用家族的姓、記下父母）；否則是士兵出身（最年輕的成年人，另取姓名）。</item>
/// <item><b>效果</b>：英雄在世時，本聚落士兵的等級 +1（第 12-2c 節）。英雄與一般人一樣會老死、餓死、死於災禍。</item>
/// <item><b>傳說物品</b>：地下城核心留下的裝備（第 12-1 章）。名稱由地下城名稱與事件編號決定；英雄在世時由英雄持有，
///   領袖家族出身的英雄死後由家族收藏，士兵出身的英雄死後下落不明。</item>
/// </list>
/// </summary>
public static class DemoHeroes
{
    /// <summary>【TEMP】領袖有子女輩成年人時，英雄是領袖子女的機率。</summary>
    public const int TempKinPercent = 50;

    private static readonly string[] Kinds = { "劍", "槍", "弓", "盾", "冠", "甲", "杖", "斧" };

    private const string ClearedMarker = "的怪物被殺光";

    /// <summary>
    /// 上一週殺光地下城怪物的聚落（下一週各出一位英雄）：每個「地下城沉寂」事件，取同一週在同一個地下城最後一筆擊殺的聚落；
    /// 找不到擊殺紀錄時取事件記的鄰近聚落。
    /// </summary>
    public static IReadOnlySet<int> Deeds(IReadOnlyList<DemoSettlementEvent>? events)
    {
        var result = new HashSet<int>();
        if (events is null)
            return result;
        foreach (var cleared in events.Where(e => e.Kind == DemoSettlementEventKind.DungeonCleared))
            result.Add(SlayerOf(cleared, events));
        return result;
    }

    private static int SlayerOf(DemoSettlementEvent cleared, IReadOnlyList<DemoSettlementEvent> events)
        => events.LastOrDefault(e => e.Kind == DemoSettlementEventKind.MonsterSlain && e.Week == cleared.Week
            && DemoMonsters.DecodeKills(e.Count).CellId == cleared.Count)?.SettlementId ?? cleared.SettlementId;

    /// <summary>傳說物品的名稱：「灰燼洞窟之劍」。</summary>
    public static string ItemName(string dungeon, long eventId)
        => dungeon + "之" + Kinds[(int)(Math.Abs(eventId) % Kinds.Length)];

    /// <summary>「地下城沉寂」事件裡的地下城名稱。</summary>
    public static string DungeonOf(DemoSettlementEvent cleared)
    {
        ArgumentNullException.ThrowIfNull(cleared);
        var text = DemoPlayerDetail.Clean(cleared.Cause);
        var at = text.IndexOf(ClearedMarker, StringComparison.Ordinal);
        return at > 0 ? text[..at] : "地下城";
    }

    /// <summary>世界上所有的傳說物品（依得到的時間）：每位英雄對應他成名前一週、由他的聚落殺光的地下城。</summary>
    public static IReadOnlyList<DemoLegend> Legends(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var events = world.SettlementEvents();
        var cleared = events.Where(e => e.Kind == DemoSettlementEventKind.DungeonCleared).ToList();
        var byWeek = events.Where(e => e.Kind is DemoSettlementEventKind.DungeonCleared or DemoSettlementEventKind.MonsterSlain)
            .GroupBy(e => e.Week).ToDictionary(g => g.Key, g => (IReadOnlyList<DemoSettlementEvent>)g.ToList());
        var legends = new List<DemoLegend>();
        foreach (var hero in world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Hero).OrderBy(p => p.AppointedWeek).ThenBy(p => p.Id))
        {
            var deed = cleared.Where(e => e.Week == hero.AppointedWeek - 1 && SlayerOf(e, byWeek[e.Week]) == hero.SettlementId)
                .OrderBy(e => e.EventId).FirstOrDefault();
            if (deed is null)
                continue;
            var dungeon = DungeonOf(deed);
            legends.Add(new DemoLegend(ItemName(dungeon, deed.EventId), hero, deed.Week, dungeon));
        }
        return legends;
    }
}
