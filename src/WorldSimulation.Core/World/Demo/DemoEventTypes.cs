using WorldSimulation.Core.History;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 03 章 b 節（Claude 定案，TEMP）：聚落事件種類 → 正式事件類型的單一對照表。
/// 出生→正式 EV_BIRTH；餓死、途中餓死、自然死亡→正式 EV_DEATH（死因放根因與摘要）；
/// 24 表尚無的 5 種用 TEMP_EV_*（登記在 TEMP 清單）。根因：飢餓、遷徙、建村＝資源；出生、自然死亡＝生命週期【TEMP】。
/// 本節不給重要度、不做 L0～L4 對照（第 13 章）。
/// </summary>
public static class DemoEventTypes
{
    public sealed record Entry(string TypeId, string Name, CauseRootCategory Root)
    {
        /// <summary>24 表尚無正式列的 TEMP 類型。</summary>
        public bool IsTemp => TypeId.StartsWith("TEMP_", StringComparison.Ordinal);

        /// <summary>畫面用：「EV_DEATH 死亡」；TEMP 類型加【TEMP】。</summary>
        public string Display => IsTemp ? $"【TEMP】{TypeId} {Name}" : $"{TypeId} {Name}";
    }

    private static readonly Dictionary<DemoSettlementEventKind, Entry> ByKind = new()
    {
        [DemoSettlementEventKind.Hunger] = new("TEMP_EV_HUNGER", "飢餓", CauseRootCategory.Resource),
        [DemoSettlementEventKind.StarvationDeath] = new("EV_DEATH", "死亡", CauseRootCategory.Resource),
        [DemoSettlementEventKind.HungerRecovered] = new("TEMP_EV_HUNGER_RECOVERED", "飢餓解除", CauseRootCategory.Resource),
        [DemoSettlementEventKind.Emigration] = new("TEMP_EV_EMIGRATION", "外移", CauseRootCategory.Resource),
        [DemoSettlementEventKind.Arrival] = new("TEMP_EV_ARRIVAL", "抵達", CauseRootCategory.Resource),
        [DemoSettlementEventKind.MigrantStarvationDeath] = new("EV_DEATH", "死亡", CauseRootCategory.Resource),
        [DemoSettlementEventKind.SettlementFounded] = new("TEMP_EV_SETTLEMENT_FOUNDED", "建立新聚落", CauseRootCategory.Resource),
        [DemoSettlementEventKind.Birth] = new("EV_BIRTH", "出生", CauseRootCategory.Lifecycle),
        [DemoSettlementEventKind.NaturalDeath] = new("EV_DEATH", "死亡", CauseRootCategory.Lifecycle),
        [DemoSettlementEventKind.SettlementRuined] = new("TEMP_EV_SETTLEMENT_RUINED", "聚落成為遺跡", CauseRootCategory.Resource),
        [DemoSettlementEventKind.KnowledgeLearned] = new("TEMP_EV_KNOWLEDGE_LEARNED", "習得技術", CauseRootCategory.Knowledge),
        [DemoSettlementEventKind.EraBegan] = new("TEMP_EV_ERA_BEGAN", "時代開始", CauseRootCategory.Knowledge),
        [DemoSettlementEventKind.FarmlandCleared] = new("TEMP_EV_FARMLAND_CLEARED", "開墾農田", CauseRootCategory.Knowledge),
        [DemoSettlementEventKind.FarmlandAbandoned] = new("TEMP_EV_FARMLAND_ABANDONED", "農田荒廢", CauseRootCategory.Resource),
        [DemoSettlementEventKind.CultureSplit] = new("TEMP_EV_CULTURE_SPLIT", "文化分化", CauseRootCategory.Culture),
        [DemoSettlementEventKind.FaithFounded] = new("TEMP_EV_FAITH_FOUNDED", "創立信仰", CauseRootCategory.Culture),
        [DemoSettlementEventKind.FaithExtinct] = new("TEMP_EV_FAITH_EXTINCT", "信仰失傳", CauseRootCategory.Culture),
        [DemoSettlementEventKind.BeastAttack] = new("TEMP_EV_BEAST_ATTACK", "野獸襲擊", CauseRootCategory.Resource),
        [DemoSettlementEventKind.Earthquake] = new("TEMP_EV_EARTHQUAKE", "地震", CauseRootCategory.Disaster),
        [DemoSettlementEventKind.Tornado] = new("TEMP_EV_TORNADO", "龍捲風", CauseRootCategory.Disaster),
        [DemoSettlementEventKind.Meteorite] = new("TEMP_EV_METEORITE", "隕石", CauseRootCategory.Disaster),
        [DemoSettlementEventKind.Flood] = new("TEMP_EV_FLOOD", "洪水", CauseRootCategory.Disaster)
    };

    private static readonly HashSet<string> Known = ByKind.Values.Select(e => e.TypeId).ToHashSet(StringComparer.Ordinal);

    public static Entry Of(DemoSettlementEventKind kind)
        => ByKind.TryGetValue(kind, out var entry) ? entry : throw new ArgumentOutOfRangeException(nameof(kind), kind, "未登錄的聚落事件種類。");

    public static bool IsKnown(string typeId) => Known.Contains(typeId);

    public static IReadOnlyCollection<string> TypeIds => Known;

    public static string Name(string typeId) => ByKind.Values.FirstOrDefault(e => e.TypeId == typeId)?.Name ?? typeId;

    public static string Display(string typeId)
        => ByKind.Values.FirstOrDefault(e => e.TypeId == typeId)?.Display ?? typeId;

    public static string RootName(CauseRootCategory root) => root switch
    {
        CauseRootCategory.Resource => "資源",
        CauseRootCategory.Lifecycle => "【TEMP】生命週期",
        CauseRootCategory.Knowledge => "【TEMP】知識",
        _ => root.ToString()
    };
}
