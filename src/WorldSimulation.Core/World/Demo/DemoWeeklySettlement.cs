using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// TEMP（暫定）每週結算示範設定：資源點初始存量、存量上限、每週再生量、每人每週產量／消耗、
/// 聚落初始糧食。這些值沒有定案，只供 V0-04A 示範畫面與測試；不得寫回正式設定檔或 Notion 規則。
/// </summary>
public sealed record DemoWeeklyTempConfig(
    long InitialReservePerPoint,
    long ReserveCapPerPoint,
    long FarmlandRegenPerWeek,
    long WoodlandRegenPerWeek,
    long FisheryRegenPerWeek,
    long YieldPerWorkerPerWeek,
    long FoodPerPersonPerWeek,
    long InitialFoodPerPerson)
{
    /// <summary>TEMP 範例值（未定案）。</summary>
    public static DemoWeeklyTempConfig Sample { get; } = new(
        InitialReservePerPoint: 60,
        ReserveCapPerPoint: 120,
        FarmlandRegenPerWeek: 30,
        WoodlandRegenPerWeek: 20,
        FisheryRegenPerWeek: 25,
        YieldPerWorkerPerWeek: 5,
        FoodPerPersonPerWeek: 4,
        InitialFoodPerPerson: 7);

    internal void Validate()
    {
        if (InitialReservePerPoint < 0 || ReserveCapPerPoint < InitialReservePerPoint
            || FarmlandRegenPerWeek < 0 || WoodlandRegenPerWeek < 0 || FisheryRegenPerWeek < 0
            || YieldPerWorkerPerWeek < 0 || FoodPerPersonPerWeek < 0 || InitialFoodPerPerson < 0)
            throw new ArgumentOutOfRangeException(nameof(DemoWeeklyTempConfig), "Invalid TEMP weekly settlement config.");
    }

    internal long RegenFor(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => FarmlandRegenPerWeek,
        DemoResourceKind.Woodland => WoodlandRegenPerWeek,
        DemoResourceKind.Fishery => FisheryRegenPerWeek,
        _ => throw new InvalidOperationException("Unknown TEMP resource kind.")
    };
}

/// <summary>
/// 一個示範聚落一週的帳目（全部是純量，可直接比較相等）。
/// 資源點存量帳：期初＋再生－採收＝期末。
/// 糧食帳：期初＋生產－消耗＝期末；生產必須等於採收（無魔法糧食）。
/// 人口帳：期初＋出生－死亡＝期末（V0-04A 尚無出生／死亡規則，兩欄固定為 0）。
/// </summary>
public sealed record DemoSettlementWeekLedger(
    int Week,
    int SettlementId,
    string SettlementName,
    long ReserveOpening,
    long Regenerated,
    long Harvested,
    long ReserveClosing,
    long FoodOpening,
    long FoodProduced,
    long FoodConsumed,
    long FoodClosing,
    long FoodShortage,
    long PopulationOpening,
    long Births,
    long Deaths,
    long PopulationClosing)
{
    public bool ReserveBalanced => ReserveOpening + Regenerated - Harvested == ReserveClosing;

    public bool FoodBalanced => FoodOpening + FoodProduced - FoodConsumed == FoodClosing;

    /// <summary>M1_NO_MAGIC_FOOD：糧食只能來自資源點的實際採收。</summary>
    public bool NoMagicFood => FoodProduced == Harvested;

    /// <summary>M1_RES：聚落總資源（資源點存量＋糧食）只因再生增加、只因消耗減少。</summary>
    public bool TotalResourceBalanced
        => ReserveOpening + FoodOpening + Regenerated - FoodConsumed == ReserveClosing + FoodClosing;

    /// <summary>M1_POP 的人口帳部分。</summary>
    public bool PopulationBalanced => PopulationOpening + Births - Deaths == PopulationClosing;

    public bool NonNegative
        => ReserveOpening >= 0 && Regenerated >= 0 && Harvested >= 0 && ReserveClosing >= 0
        && FoodOpening >= 0 && FoodProduced >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && PopulationOpening >= 0 && Births >= 0 && Deaths >= 0 && PopulationClosing >= 0;

    public bool IsBalanced
        => ReserveBalanced && FoodBalanced && NoMagicFood && TotalResourceBalanced && PopulationBalanced && NonNegative;
}

/// <summary>一次週結算的結果：第幾週、結算時的世界時間、各聚落帳目（依聚落編號排序）。</summary>
public sealed record DemoWeekReport(int Week, TimeSpan WorldTime, IReadOnlyList<DemoSettlementWeekLedger> Settlements);

/// <summary>週結算的候選結果：報告與結算後的新狀態。在發布前不會寫入任何世界狀態。</summary>
internal sealed record DemoWeeklyOutcome(
    DemoWeekReport Report,
    ImmutableSortedDictionary<int, long> Reserves,
    ImmutableSortedDictionary<int, long> SettlementFood);

/// <summary>
/// V0-04A 每週 L2 聚落結算（TEMP 示範）：資源再生 → 勞動與生產 → 消耗，並建立資源帳與人口帳。
/// 純函式：只讀入目前狀態、回傳候選結果，不改動任何輸入。飢餓與死亡留給 V0-04B。
/// 不使用亂數，因此不影響 RESOURCE 流（V0-01 每日收穫）的抽樣次數。
/// </summary>
internal static class DemoWeeklySettlement
{
    public static DemoWeeklyOutcome Settle(
        int week,
        TimeSpan worldTime,
        DemoWorldSites sites,
        ImmutableSortedDictionary<int, long> reserves,
        ImmutableSortedDictionary<int, long> settlementFood,
        ImmutableSortedDictionary<int, long> population,
        DemoWeeklyTempConfig config)
    {
        var nextReserves = reserves.ToBuilder();
        var nextFood = settlementFood.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(sites.Settlements.Count);

        foreach (var settlement in sites.Settlements.OrderBy(s => s.Id))
        {
            var people = population[settlement.Id];
            long reserveOpening = 0, regenerated = 0, harvested = 0, reserveClosing = 0;

            // ① 資源再生，② 勞動與生產：勞力依資源點編號順序採收，不能超過再生後的存量。
            var laborLeft = checked(people * config.YieldPerWorkerPerWeek);
            foreach (var point in sites.ResourcePointsOf(settlement.Id).OrderBy(p => p.Id))
            {
                var opening = reserves[point.Id];
                var regen = Math.Max(0, Math.Min(config.RegenFor(point.Kind), config.ReserveCapPerPoint - opening));
                var afterRegen = checked(opening + regen);
                var take = Math.Min(laborLeft, afterRegen);
                laborLeft -= take;
                var closing = afterRegen - take;

                reserveOpening = checked(reserveOpening + opening);
                regenerated = checked(regenerated + regen);
                harvested = checked(harvested + take);
                reserveClosing = checked(reserveClosing + closing);
                nextReserves[point.Id] = closing;
            }

            // ③ 消耗：糧食只來自本週採收；不足部分記為缺糧（飢餓／死亡由 V0-04B 處理）。
            var foodOpening = settlementFood[settlement.Id];
            var available = checked(foodOpening + harvested);
            var demand = checked(people * config.FoodPerPersonPerWeek);
            var consumed = Math.Min(available, demand);
            var foodClosing = available - consumed;
            nextFood[settlement.Id] = foodClosing;

            ledgers.Add(new DemoSettlementWeekLedger(
                week, settlement.Id, settlement.Name,
                reserveOpening, regenerated, harvested, reserveClosing,
                foodOpening, harvested, consumed, foodClosing, demand - consumed,
                people, Births: 0, Deaths: 0, PopulationClosing: people));
        }

        var report = new DemoWeekReport(week, worldTime, Array.AsReadOnly(ledgers.ToArray()));
        Verify(report);
        return new DemoWeeklyOutcome(report, nextReserves.ToImmutable(), nextFood.ToImmutable());
    }

    /// <summary>任何一個聚落帳目不平衡就丟出例外；呼叫端必須在發布任何狀態之前呼叫。</summary>
    public static void Verify(DemoWeekReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var ledger in report.Settlements)
        {
            if (!ledger.IsBalanced)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 帳目不平衡（ledger imbalance）。");
        }
    }
}
