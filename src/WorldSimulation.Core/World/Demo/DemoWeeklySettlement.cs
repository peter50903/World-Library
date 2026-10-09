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
    long InitialFoodPerPerson,
    long HungerDebtPerStarvationDeath = 12,
    long HungerRecoveryPerPersonPerWeek = 2)
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

    /// <summary>
    /// TEST／CI 用 TEMP 饑荒情境：資源點沒有存量也不再生、聚落沒有初始糧食，
    /// 用來在數週內重現飢餓與餓死事件。不是正式數值。
    /// </summary>
    public static DemoWeeklyTempConfig FamineSample { get; } = Sample with
    {
        InitialReservePerPoint = 0,
        FarmlandRegenPerWeek = 0,
        WoodlandRegenPerWeek = 0,
        FisheryRegenPerWeek = 0,
        InitialFoodPerPerson = 0
    };

    internal void Validate()
    {
        if (InitialReservePerPoint < 0 || ReserveCapPerPoint < InitialReservePerPoint
            || FarmlandRegenPerWeek < 0 || WoodlandRegenPerWeek < 0 || FisheryRegenPerWeek < 0
            || YieldPerWorkerPerWeek < 0 || FoodPerPersonPerWeek < 0 || InitialFoodPerPerson < 0
            || HungerDebtPerStarvationDeath <= 0 || HungerRecoveryPerPersonPerWeek < 0)
            throw new ArgumentOutOfRangeException(nameof(DemoWeeklyTempConfig), "Invalid TEMP weekly settlement config.");
    }

    internal long RegenFor(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => FarmlandRegenPerWeek,
        DemoResourceKind.Woodland => WoodlandRegenPerWeek,
        DemoResourceKind.Fishery => FisheryRegenPerWeek,
        _ => throw new InvalidOperationException("Unknown TEMP resource kind.")
    };

    /// <summary>
    /// M1_NO_MAGIC_FOOD 的合法來源（TEMP 對應）：農地＝農牧；林地（採集狩獵）與漁場（漁撈）＝採集。
    /// 沒有對應合法來源的資源種類不得產生糧食。
    /// </summary>
    internal static DemoFoodSource FoodSourceOf(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => DemoFoodSource.Farming,
        DemoResourceKind.Woodland => DemoFoodSource.Gathering,
        DemoResourceKind.Fishery => DemoFoodSource.Gathering,
        _ => throw new InvalidOperationException("TEMP resource kind has no legal food source.")
    };
}

/// <summary>糧食的合法來源（45G M1_NO_MAGIC_FOOD：採集／農牧；配方與神蹟尚未實作）。</summary>
public enum DemoFoodSource
{
    /// <summary>農牧。</summary>
    Farming = 0,

    /// <summary>採集（含林地採集狩獵、漁撈）。</summary>
    Gathering = 1
}

/// <summary>V0-04B 聚落事件種類。</summary>
public enum DemoSettlementEventKind
{
    /// <summary>飢餓：本週取得糧食低於需求，形成飢餓債（45C FOOD_UNMET、45D SURV_DEBT）。</summary>
    Hunger = 0,

    /// <summary>餓死：飢餓債越過 TEMP 門檻造成死亡，原因保留（45D SURV_DEATH）。</summary>
    StarvationDeath = 1,

    /// <summary>飢餓解除：需求被實際糧食滿足、飢餓債降為 0（45D SURV_RECOVERY）。</summary>
    HungerRecovered = 2
}

/// <summary>
/// V0-04B 帶原因的聚落事件（只追加、不改寫）。Count 為缺糧量、死亡人數或 0。
/// Cause 是玩家可讀的繁體中文原因，Describe() 給歷史列表顯示。
/// </summary>
public sealed record DemoSettlementEvent(
    int Week,
    TimeSpan WorldTime,
    int SettlementId,
    string SettlementName,
    DemoSettlementEventKind Kind,
    long Count,
    string Cause)
{
    public string Describe()
    {
        var day = (int)WorldTime.TotalDays;
        return Kind switch
        {
            DemoSettlementEventKind.Hunger => $"第 {day} 天  |  {SettlementName} 飢餓：缺糧 {Count}（{Cause}）",
            DemoSettlementEventKind.StarvationDeath => $"第 {day} 天  |  {SettlementName} {Count} 人餓死（原因：{Cause}）",
            DemoSettlementEventKind.HungerRecovered => $"第 {day} 天  |  {SettlementName} 飢餓解除（{Cause}）",
            _ => $"第 {day} 天  |  {SettlementName} 未知事件"
        };
    }
}

/// <summary>
/// 一個示範聚落一週的帳目（全部是純量，可直接比較相等）。
/// 資源點存量帳：期初＋再生－採收＝期末。
/// 糧食帳：期初＋生產－消耗＝期末；生產必須等於採收（無魔法糧食）。
/// 人口帳：期初＋出生－死亡＝期末（出生 V0-04B 仍固定為 0；死亡只來自餓死）。
/// 飢餓債帳（V0-04B）：期初＋本週缺糧－恢復－隨死者移除＝期末。
/// 糧食來源：農牧＋採集＝生產（無魔法糧食）。
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
    long PopulationClosing,
    long FoodFromFarming = 0,
    long FoodFromGathering = 0,
    long HungerDebtOpening = 0,
    long HungerDebtRecovered = 0,
    long HungerDebtRemovedByDeath = 0,
    long HungerDebtClosing = 0)
{
    public bool ReserveBalanced => ReserveOpening + Regenerated - Harvested == ReserveClosing;

    public bool FoodBalanced => FoodOpening + FoodProduced - FoodConsumed == FoodClosing;

    /// <summary>
    /// M1_NO_MAGIC_FOOD：糧食只能來自資源點的實際採收，且每一單位都歸屬合法來源（農牧／採集）。
    /// </summary>
    public bool NoMagicFood => FoodProduced == Harvested && FoodFromFarming + FoodFromGathering == FoodProduced;

    /// <summary>飢餓債帳：期初＋缺糧－恢復－隨死者移除＝期末；有死亡必有移除的飢餓債（死亡有原因）。</summary>
    public bool HungerDebtBalanced
        => HungerDebtOpening + FoodShortage - HungerDebtRecovered - HungerDebtRemovedByDeath == HungerDebtClosing
        && (Deaths == 0 || HungerDebtRemovedByDeath > 0)
        && (PopulationClosing > 0 || HungerDebtClosing == 0);

    /// <summary>M1_RES：聚落總資源（資源點存量＋糧食）只因再生增加、只因消耗減少。</summary>
    public bool TotalResourceBalanced
        => ReserveOpening + FoodOpening + Regenerated - FoodConsumed == ReserveClosing + FoodClosing;

    /// <summary>M1_POP 的人口帳部分。</summary>
    public bool PopulationBalanced => PopulationOpening + Births - Deaths == PopulationClosing;

    public bool NonNegative
        => ReserveOpening >= 0 && Regenerated >= 0 && Harvested >= 0 && ReserveClosing >= 0
        && FoodOpening >= 0 && FoodProduced >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && PopulationOpening >= 0 && Births >= 0 && Deaths >= 0 && PopulationClosing >= 0
        && FoodFromFarming >= 0 && FoodFromGathering >= 0 && HungerDebtOpening >= 0 && HungerDebtRecovered >= 0
        && HungerDebtRemovedByDeath >= 0 && HungerDebtClosing >= 0;

    public bool IsBalanced
        => ReserveBalanced && FoodBalanced && NoMagicFood && TotalResourceBalanced && PopulationBalanced
        && HungerDebtBalanced && NonNegative;
}

/// <summary>
/// 一次週結算的結果：第幾週、結算時的世界時間、各聚落帳目（依聚落編號排序），
/// 以及本週產生的帶原因事件（V0-04B，依聚落編號、事件順序排列）。
/// </summary>
public sealed record DemoWeekReport(
    int Week,
    TimeSpan WorldTime,
    IReadOnlyList<DemoSettlementWeekLedger> Settlements,
    IReadOnlyList<DemoSettlementEvent> Events)
{
    /// <summary>沒有事件的週報（V0-04A 相容）。</summary>
    public DemoWeekReport(int week, TimeSpan worldTime, IReadOnlyList<DemoSettlementWeekLedger> settlements)
        : this(week, worldTime, settlements, Array.Empty<DemoSettlementEvent>())
    {
    }
}

/// <summary>週結算的候選結果：報告與結算後的新狀態。在發布前不會寫入任何世界狀態。</summary>
internal sealed record DemoWeeklyOutcome(
    DemoWeekReport Report,
    ImmutableSortedDictionary<int, long> Reserves,
    ImmutableSortedDictionary<int, long> SettlementFood,
    ImmutableSortedDictionary<int, long> Population,
    ImmutableSortedDictionary<int, long> HungerDebt);

/// <summary>
/// V0-04A／V0-04B 每週 L2 聚落結算（TEMP 示範），順序依 44B：
/// ① 資源再生 → ② 勞動與生產 → ⑥ 消耗（形成飢餓債）→ ⑦ 健康後果（餓死）→ ⑨ 人口帳。
/// 純函式：只讀入目前狀態、回傳候選結果，不改動任何輸入。
/// 不使用亂數，因此不影響 RESOURCE 流（V0-01 每日收穫）的抽樣次數；餓死人數是確定性的。
/// 不做隱藏救濟（45D SURV_NO_RESCUE）：人口即將滅亡也不生成糧食或人口。
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
        ImmutableSortedDictionary<int, long> hungerDebt,
        DemoWeeklyTempConfig config)
    {
        var nextReserves = reserves.ToBuilder();
        var nextFood = settlementFood.ToBuilder();
        var nextPopulation = population.ToBuilder();
        var nextDebt = hungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(sites.Settlements.Count);
        var events = new List<DemoSettlementEvent>();

        foreach (var settlement in sites.Settlements.OrderBy(s => s.Id))
        {
            var people = population[settlement.Id];
            long reserveOpening = 0, regenerated = 0, harvested = 0, reserveClosing = 0;
            long fromFarming = 0, fromGathering = 0;

            // ① 資源再生，② 勞動與生產：勞力依資源點編號順序採收，不能超過再生後的存量。
            // 勞動力＝目前人口，所以死亡會降低下週產量（45D SURV_CASCADE）。
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

                if (DemoWeeklyTempConfig.FoodSourceOf(point.Kind) == DemoFoodSource.Farming)
                    fromFarming = checked(fromFarming + take);
                else
                    fromGathering = checked(fromGathering + take);
            }

            // ⑥ 消耗：糧食只來自庫存與本週採收；取得量低於需求的部分形成飢餓債。
            var foodOpening = settlementFood[settlement.Id];
            var available = checked(foodOpening + harvested);
            var demand = checked(people * config.FoodPerPersonPerWeek);
            var consumed = Math.Min(available, demand);
            var foodClosing = available - consumed;
            var shortage = demand - consumed;
            nextFood[settlement.Id] = foodClosing;

            var debtOpening = hungerDebt[settlement.Id];
            var debt = checked(debtOpening + shortage);

            // 恢復也需來源：只有本週需求被實際糧食完全滿足時，飢餓債才下降（TEMP 速率）。
            long recovered = 0;
            if (shortage == 0 && debt > 0)
            {
                recovered = Math.Min(debt, checked(people * config.HungerRecoveryPerPersonPerWeek));
                debt -= recovered;
            }

            // ⑦ 健康後果：每累積 TEMP 門檻的飢餓債造成 1 人餓死，該部分債務隨死者移除。
            var deaths = Math.Min(people, debt / config.HungerDebtPerStarvationDeath);
            var removedByDeath = deaths * config.HungerDebtPerStarvationDeath;
            var populationClosing = people - deaths;
            if (populationClosing == 0)
                removedByDeath = debt; // 聚落無人倖存：剩餘飢餓債沒有承擔者。
            debt -= removedByDeath;

            // ⑨ 人口帳。
            nextPopulation[settlement.Id] = populationClosing;
            nextDebt[settlement.Id] = debt;

            if (shortage > 0)
            {
                events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.Hunger, shortage,
                    $"需求 {demand}，可取得 {available}，飢餓債 {debtOpening}→{checked(debtOpening + shortage)}"));
            }

            if (deaths > 0)
            {
                events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.StarvationDeath, deaths,
                    $"飢餓：飢餓債累積達 TEMP 門檻（每人 {config.HungerDebtPerStarvationDeath}），連續缺糧"));
            }

            if (debtOpening > 0 && debt == 0 && populationClosing > 0 && shortage == 0)
            {
                events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.HungerRecovered, 0,
                    "來源：本週庫存與採收足以滿足全部需求"));
            }

            ledgers.Add(new DemoSettlementWeekLedger(
                week, settlement.Id, settlement.Name,
                reserveOpening, regenerated, harvested, reserveClosing,
                foodOpening, harvested, consumed, foodClosing, shortage,
                people, Births: 0, Deaths: deaths, PopulationClosing: populationClosing,
                FoodFromFarming: fromFarming, FoodFromGathering: fromGathering,
                HungerDebtOpening: debtOpening, HungerDebtRecovered: recovered,
                HungerDebtRemovedByDeath: removedByDeath, HungerDebtClosing: debt));
        }

        var report = new DemoWeekReport(week, worldTime, Array.AsReadOnly(ledgers.ToArray()),
            Array.AsReadOnly(events.ToArray()));
        Verify(report);
        return new DemoWeeklyOutcome(report, nextReserves.ToImmutable(), nextFood.ToImmutable(),
            nextPopulation.ToImmutable(), nextDebt.ToImmutable());
    }

    /// <summary>任何一個聚落帳目不平衡、或死亡沒有對應的帶原因事件，就丟出例外；呼叫端必須在發布任何狀態之前呼叫。</summary>
    public static void Verify(DemoWeekReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var ledger in report.Settlements)
        {
            if (!ledger.IsBalanced)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 帳目不平衡（ledger imbalance）。");

            // SURV_DEATH：死亡必須有原因。帳上的死亡數必須等於帶原因的餓死事件人數。
            var deathEvents = report.Events
                .Where(e => e.SettlementId == ledger.SettlementId && e.Kind == DemoSettlementEventKind.StarvationDeath)
                .Sum(e => e.Count);
            if (deathEvents != ledger.Deaths
                || report.Events.Any(e => e.SettlementId == ledger.SettlementId
                    && e.Kind == DemoSettlementEventKind.StarvationDeath && string.IsNullOrWhiteSpace(e.Cause)))
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 死亡沒有對應的帶原因事件（death without cause）。");
        }
    }
}
