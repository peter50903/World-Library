using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// TEMP（暫定）每週結算示範設定：資源點初始存量、存量上限、每週再生量、每人每週產量／消耗、
/// 聚落初始糧食；V0-06A 起另含遷徙門檻（最少幾人才會外移、外移比例的除數、已知路線的最遠格數）
/// 與 TEST 用局部饑荒聚落編號。這些值沒有定案，只供 V0-04A 示範畫面與測試；不得寫回正式設定檔或 Notion 規則。
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
    long HungerRecoveryPerPersonPerWeek = 2,
    bool MigrationEnabled = true,
    long MinPopulationToEmigrate = 2,
    long EmigrationShareDivisor = 2,
    int KnownRouteMaxSteps = 6,
    int TempFamineSettlementId = 0)
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

    /// <summary>
    /// TEST／CI 用 TEMP 局部饑荒情境（V0-06A）：只有示範聚落 #1 的資源點沒有存量也不再生、
    /// 聚落沒有初始糧食；其他聚落照 <see cref="Sample"/>。用來重現「缺糧聚落人口外移到有糧聚落」。
    /// 不是正式數值。
    /// </summary>
    public static DemoWeeklyTempConfig LocalFamineSample { get; } = Sample with { TempFamineSettlementId = 1 };

    internal void Validate()
    {
        if (InitialReservePerPoint < 0 || ReserveCapPerPoint < InitialReservePerPoint
            || FarmlandRegenPerWeek < 0 || WoodlandRegenPerWeek < 0 || FisheryRegenPerWeek < 0
            || YieldPerWorkerPerWeek < 0 || FoodPerPersonPerWeek < 0 || InitialFoodPerPerson < 0
            || HungerDebtPerStarvationDeath <= 0 || HungerRecoveryPerPersonPerWeek < 0
            || MinPopulationToEmigrate < 2 || EmigrationShareDivisor < 2 || KnownRouteMaxSteps < 1
            || TempFamineSettlementId < 0)
            throw new ArgumentOutOfRangeException(nameof(DemoWeeklyTempConfig), "Invalid TEMP weekly settlement config.");
    }

    /// <summary>TEMP 局部饑荒情境下，這個聚落是否為沒有存量、再生與初始糧食的饑荒聚落。</summary>
    internal bool IsFamineSettlement(int settlementId)
        => TempFamineSettlementId != 0 && settlementId == TempFamineSettlementId;

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
    HungerRecovered = 2,

    /// <summary>V0-06A 外移：缺糧聚落的部分人口組成遷徙隊伍出發（45E MIG_PUSH／MIG_LEDGER）。Count＝人數。</summary>
    Emigration = 3,

    /// <summary>V0-06A 抵達：遷徙隊伍沿相鄰格走到目的聚落並併入（45E MIG_LEDGER）。Count＝人數。</summary>
    Arrival = 4,

    /// <summary>V0-06A 途中餓死：遷徙隊伍攜帶的糧食不足，飢餓債越過門檻（45E MIG_COST／MIG_FAIL）。Count＝人數。</summary>
    MigrantStarvationDeath = 5
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
    string Cause,
    int MigrationBatchId = 0)
{
    public string Describe()
    {
        var day = (int)WorldTime.TotalDays;
        return Kind switch
        {
            DemoSettlementEventKind.Hunger => $"第 {day} 天  |  {SettlementName} 飢餓：缺糧 {Count}（{Cause}）",
            DemoSettlementEventKind.StarvationDeath => $"第 {day} 天  |  {SettlementName} {Count} 人餓死（原因：{Cause}）",
            DemoSettlementEventKind.HungerRecovered => $"第 {day} 天  |  {SettlementName} 飢餓解除（{Cause}）",
            DemoSettlementEventKind.Emigration => $"第 {day} 天  |  {SettlementName} {Count} 人因缺糧外移（{Cause}）",
            DemoSettlementEventKind.Arrival => $"第 {day} 天  |  {SettlementName} {Count} 名遷徙者抵達（{Cause}）",
            DemoSettlementEventKind.MigrantStarvationDeath => $"第 {day} 天  |  自{SettlementName}出發的遷徙隊伍 {Count} 人途中餓死（原因：{Cause}）",
            _ => $"第 {day} 天  |  {SettlementName} 未知事件"
        };
    }
}

/// <summary>
/// 一個示範聚落一週的帳目（全部是純量，可直接比較相等）。
/// 資源點存量帳：期初＋再生－採收＝期末。
/// 糧食帳：期初＋生產－消耗＝期末；生產必須等於採收（無魔法糧食）。
/// 人口帳：期初＋出生－死亡＋遷入－遷出＝期末（出生仍固定為 0；死亡只來自餓死；遷入遷出 V0-06A）。
/// 飢餓債帳（V0-04B）：期初＋本週缺糧－恢復－隨死者移除＋隨遷入帶來－隨遷出帶走＝期末。
/// 遷徙隊伍攜帶的糧食（V0-06A）也記在糧食帳：期初＋生產＋帶入－消耗－帶出＝期末。
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
    long HungerDebtClosing = 0,
    long Immigrants = 0,
    long Emigrants = 0,
    long FoodCarriedIn = 0,
    long FoodCarriedOut = 0,
    long HungerDebtCarriedIn = 0,
    long HungerDebtCarriedOut = 0)
{
    public bool ReserveBalanced => ReserveOpening + Regenerated - Harvested == ReserveClosing;

    public bool FoodBalanced
        => FoodOpening + FoodProduced + FoodCarriedIn - FoodConsumed - FoodCarriedOut == FoodClosing;

    /// <summary>
    /// M1_NO_MAGIC_FOOD：糧食只能來自資源點的實際採收，且每一單位都歸屬合法來源（農牧／採集）。
    /// </summary>
    public bool NoMagicFood => FoodProduced == Harvested && FoodFromFarming + FoodFromGathering == FoodProduced;

    /// <summary>飢餓債帳：期初＋缺糧－恢復－隨死者移除＝期末；有死亡必有移除的飢餓債（死亡有原因）。</summary>
    public bool HungerDebtBalanced
        => HungerDebtOpening + FoodShortage - HungerDebtRecovered - HungerDebtRemovedByDeath
            + HungerDebtCarriedIn - HungerDebtCarriedOut == HungerDebtClosing
        && (Deaths == 0 || HungerDebtRemovedByDeath > 0)
        && (PopulationClosing > 0 || HungerDebtClosing == 0);

    /// <summary>
    /// M1_RES：聚落總資源（資源點存量＋糧食）只因再生與遷徙隊伍帶入增加、只因消耗與帶出減少。
    /// </summary>
    public bool TotalResourceBalanced
        => ReserveOpening + FoodOpening + Regenerated + FoodCarriedIn - FoodConsumed - FoodCarriedOut
            == ReserveClosing + FoodClosing;

    /// <summary>M1_POP 的人口帳部分。</summary>
    public bool PopulationBalanced
        => PopulationOpening + Births - Deaths + Immigrants - Emigrants == PopulationClosing;

    public bool NonNegative
        => ReserveOpening >= 0 && Regenerated >= 0 && Harvested >= 0 && ReserveClosing >= 0
        && FoodOpening >= 0 && FoodProduced >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && PopulationOpening >= 0 && Births >= 0 && Deaths >= 0 && PopulationClosing >= 0
        && FoodFromFarming >= 0 && FoodFromGathering >= 0 && HungerDebtOpening >= 0 && HungerDebtRecovered >= 0
        && HungerDebtRemovedByDeath >= 0 && HungerDebtClosing >= 0
        && Immigrants >= 0 && Emigrants >= 0 && FoodCarriedIn >= 0 && FoodCarriedOut >= 0
        && HungerDebtCarriedIn >= 0 && HungerDebtCarriedOut >= 0;

    public bool IsBalanced
        => ReserveBalanced && FoodBalanced && NoMagicFood && TotalResourceBalanced && PopulationBalanced
        && HungerDebtBalanced && NonNegative;
}

/// <summary>
/// 一次週結算的結果：第幾週、結算時的世界時間、各聚落帳目（依聚落編號排序），
/// 本週產生的帶原因事件（V0-04B，依聚落編號、事件順序排列），
/// 以及本週每支遷徙隊伍的帳目（V0-06A，依隊伍編號排列）。
/// </summary>
public sealed record DemoWeekReport(
    int Week,
    TimeSpan WorldTime,
    IReadOnlyList<DemoSettlementWeekLedger> Settlements,
    IReadOnlyList<DemoSettlementEvent> Events,
    IReadOnlyList<DemoMigrationWeekLedger> Migrations)
{
    /// <summary>沒有事件的週報（V0-04A 相容）。</summary>
    public DemoWeekReport(int week, TimeSpan worldTime, IReadOnlyList<DemoSettlementWeekLedger> settlements)
        : this(week, worldTime, settlements, Array.Empty<DemoSettlementEvent>(), Array.Empty<DemoMigrationWeekLedger>())
    {
    }

    /// <summary>沒有遷徙隊伍的週報（V0-04B 相容）。</summary>
    public DemoWeekReport(
        int week, TimeSpan worldTime, IReadOnlyList<DemoSettlementWeekLedger> settlements,
        IReadOnlyList<DemoSettlementEvent> events)
        : this(week, worldTime, settlements, events, Array.Empty<DemoMigrationWeekLedger>())
    {
    }
}

/// <summary>週結算的候選結果：報告與結算後的新狀態。在發布前不會寫入任何世界狀態。</summary>
internal sealed record DemoWeeklyOutcome(
    DemoWeekReport Report,
    ImmutableSortedDictionary<int, long> Reserves,
    ImmutableSortedDictionary<int, long> SettlementFood,
    ImmutableSortedDictionary<int, long> Population,
    ImmutableSortedDictionary<int, long> HungerDebt,
    ImmutableList<DemoMigrationBatch> Migrations,
    int NextMigrationBatchId);

/// <summary>
/// V0-04A／V0-04B／V0-06A 每週 L2 聚落結算（TEMP 示範），順序依 44B：
/// （途中隊伍先行：消耗攜帶糧食 → 健康後果 → 沿路線移動一格／抵達）→
/// ① 資源再生 → ② 勞動與生產 → ⑥ 消耗（形成飢餓債）→ ⑦ 健康後果（餓死）→
/// ⑧ 遷徙（缺糧聚落外移、併入本週抵達的隊伍）→ ⑨ 人口帳。
/// 純函式：只讀入目前狀態、回傳候選結果，不改動任何輸入。
/// 不使用亂數，因此不影響 RESOURCE 流（V0-01 每日收穫）的抽樣次數；餓死人數與遷徙都是確定性的。
/// 不做隱藏救濟（45D SURV_NO_RESCUE）：人口即將滅亡也不生成糧食或人口。
/// 遷徙只看上一週已發布的週報（45E MIG_PULL：只用已知資訊，不讀本週的世界真相）。
/// </summary>
internal static class DemoWeeklySettlement
{
    /// <summary>沒有地圖與遷徙狀態的結算（V0-04B 相容；不會發生遷徙）。</summary>
    public static DemoWeeklyOutcome Settle(
        int week,
        TimeSpan worldTime,
        DemoWorldSites sites,
        ImmutableSortedDictionary<int, long> reserves,
        ImmutableSortedDictionary<int, long> settlementFood,
        ImmutableSortedDictionary<int, long> population,
        ImmutableSortedDictionary<int, long> hungerDebt,
        DemoWeeklyTempConfig config)
        => Settle(week, worldTime, map: null, sites, reserves, settlementFood, population, hungerDebt,
            ImmutableList<DemoMigrationBatch>.Empty, nextMigrationBatchId: 1, previousWeek: null, config);

    public static DemoWeeklyOutcome Settle(
        int week,
        TimeSpan worldTime,
        DemoHexWorld? map,
        DemoWorldSites sites,
        ImmutableSortedDictionary<int, long> reserves,
        ImmutableSortedDictionary<int, long> settlementFood,
        ImmutableSortedDictionary<int, long> population,
        ImmutableSortedDictionary<int, long> hungerDebt,
        ImmutableList<DemoMigrationBatch> migrations,
        int nextMigrationBatchId,
        DemoWeekReport? previousWeek,
        DemoWeeklyTempConfig config)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        if (nextMigrationBatchId < 1 || migrations.Any(b => b.Id >= nextMigrationBatchId))
            throw new InvalidOperationException("Migration batch IDs must stay below the next ID (IDs are never reused).");

        var nextReserves = reserves.ToBuilder();
        var nextFood = settlementFood.ToBuilder();
        var nextPopulation = population.ToBuilder();
        var nextDebt = hungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(sites.Settlements.Count);
        var events = new List<DemoSettlementEvent>();
        var migrationLedgers = new List<DemoMigrationWeekLedger>();
        var nextMigrations = ImmutableList.CreateBuilder<DemoMigrationBatch>();
        var arrivals = new Dictionary<int, (long People, long Food, long Debt)>();

        // 途中隊伍（V0-06A）：只靠攜帶的糧食，飢餓與死亡規則與聚落相同；活著的人沿路線前進一格。
        foreach (var batch in migrations.OrderBy(b => b.Id))
        {
            var people = batch.People;
            var demand = checked(people * config.FoodPerPersonPerWeek);
            var consumed = Math.Min(batch.Food, demand);
            var foodClosing = batch.Food - consumed;
            var shortage = demand - consumed;
            var debt = checked(batch.HungerDebt + shortage);
            long recovered = 0;
            if (shortage == 0 && debt > 0)
            {
                recovered = Math.Min(debt, checked(people * config.HungerRecoveryPerPersonPerWeek));
                debt -= recovered;
            }

            var deaths = Math.Min(people, debt / config.HungerDebtPerStarvationDeath);
            var removedByDeath = deaths * config.HungerDebtPerStarvationDeath;
            var survivors = people - deaths;
            if (survivors == 0)
                removedByDeath = debt;
            debt -= removedByDeath;

            // 不瞬移：每週最多前進一格（TEMP 速度），全數死亡的隊伍停在原地。
            var step = survivors > 0 ? batch.StepIndex + 1 : batch.StepIndex;
            var arrived = survivors > 0 && step == batch.Path.Length - 1;

            if (deaths > 0)
            {
                events.Add(new DemoSettlementEvent(week, worldTime, batch.FromSettlementId, batch.FromName,
                    DemoSettlementEventKind.MigrantStarvationDeath, deaths,
                    survivors == 0
                        ? $"前往{batch.ToName}途中攜帶的糧食耗盡，隊伍全數死亡"
                        : $"前往{batch.ToName}途中攜帶的糧食不足，飢餓債達 TEMP 門檻（每人 {config.HungerDebtPerStarvationDeath}）",
                    batch.Id));
            }

            if (arrived)
            {
                arrivals.TryGetValue(batch.ToSettlementId, out var inbound);
                arrivals[batch.ToSettlementId] = (checked(inbound.People + survivors),
                    checked(inbound.Food + foodClosing), checked(inbound.Debt + debt));
                events.Add(new DemoSettlementEvent(week, worldTime, batch.ToSettlementId, batch.ToName,
                    DemoSettlementEventKind.Arrival, survivors,
                    $"來自{batch.FromName}，第 {batch.DepartedWeek} 週出發，沿相鄰格走了 {batch.Path.Length - 1} 格",
                    batch.Id));
            }
            else if (survivors > 0)
            {
                nextMigrations.Add(batch with { StepIndex = step, People = survivors, Food = foodClosing, HungerDebt = debt });
            }

            migrationLedgers.Add(new DemoMigrationWeekLedger(
                week, batch.Id, batch.FromSettlementId, batch.ToSettlementId,
                batch.Cell, batch.Path[step], Departed: false, Arrived: arrived,
                people, deaths, survivors,
                batch.Food, consumed, foodClosing, shortage,
                batch.HungerDebt, recovered, removedByDeath, debt));
        }

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
                var regenPerWeek = config.IsFamineSettlement(settlement.Id) ? 0 : config.RegenFor(point.Kind);
                var regen = Math.Max(0, Math.Min(regenPerWeek, config.ReserveCapPerPoint - opening));
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
            var survivors = people - deaths;
            if (survivors == 0)
                removedByDeath = debt; // 聚落無人倖存：剩餘飢餓債沒有承擔者。
            debt -= removedByDeath;

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

            if (debtOpening > 0 && debt == 0 && survivors > 0 && shortage == 0)
            {
                events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.HungerRecovered, 0,
                    "來源：本週庫存與採收足以滿足全部需求"));
            }

            // ⑧ 遷徙（V0-06A）：本週缺糧、倖存人數達 TEMP 門檻、且已知一個有糧且可走到的聚落時，
            // 一部分人帶著等比例的糧食與飢餓債組成隊伍出發（原子出發；本週停在來源格）。
            // 沒有已知可達的目的地就留下（45E MIG_NO_TARGET）；這不是人口自動平衡器。
            long emigrants = 0, foodOut = 0, debtOut = 0;
            if (map is not null && config.MigrationEnabled && shortage > 0
                && survivors >= config.MinPopulationToEmigrate
                && ChooseDestination(map, sites, settlement, previousWeek, config) is { } target)
            {
                emigrants = survivors / config.EmigrationShareDivisor;
                if (emigrants > 0)
                {
                    foodOut = checked(foodClosing * emigrants) / survivors;
                    debtOut = checked(debt * emigrants) / survivors;
                    var batch = new DemoMigrationBatch(
                        nextMigrationBatchId, settlement.Id, settlement.Name,
                        target.Settlement.Id, target.Settlement.Name, target.Route,
                        StepIndex: 0, People: emigrants, Food: foodOut, HungerDebt: debtOut, DepartedWeek: week);
                    nextMigrationBatchId = checked(nextMigrationBatchId + 1);
                    nextMigrations.Add(batch);

                    events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.Emigration, emigrants,
                        $"本週缺糧 {shortage}；前往上週消息中有糧的{target.Settlement.Name}，"
                        + $"路線 {target.Route.Length - 1} 格（TEMP 每週 1 格），攜帶糧食 {foodOut}",
                        batch.Id));
                    migrationLedgers.Add(new DemoMigrationWeekLedger(
                        week, batch.Id, settlement.Id, target.Settlement.Id,
                        batch.Cell, batch.Cell, Departed: true, Arrived: false,
                        emigrants, 0, emigrants,
                        foodOut, 0, foodOut, 0,
                        debtOut, 0, 0, debtOut));
                }
            }

            arrivals.TryGetValue(settlement.Id, out var arrived);
            var populationClosing = checked(survivors - emigrants + arrived.People);
            var foodFinal = checked(foodClosing - foodOut + arrived.Food);
            var debtFinal = checked(debt - debtOut + arrived.Debt);

            // ⑨ 人口帳。
            nextFood[settlement.Id] = foodFinal;
            nextPopulation[settlement.Id] = populationClosing;
            nextDebt[settlement.Id] = debtFinal;

            ledgers.Add(new DemoSettlementWeekLedger(
                week, settlement.Id, settlement.Name,
                reserveOpening, regenerated, harvested, reserveClosing,
                foodOpening, harvested, consumed, foodFinal, shortage,
                people, Births: 0, Deaths: deaths, PopulationClosing: populationClosing,
                FoodFromFarming: fromFarming, FoodFromGathering: fromGathering,
                HungerDebtOpening: debtOpening, HungerDebtRecovered: recovered,
                HungerDebtRemovedByDeath: removedByDeath, HungerDebtClosing: debtFinal,
                Immigrants: arrived.People, Emigrants: emigrants,
                FoodCarriedIn: arrived.Food, FoodCarriedOut: foodOut,
                HungerDebtCarriedIn: arrived.Debt, HungerDebtCarriedOut: debtOut));
        }

        if (arrivals.Keys.Any(id => sites.Settlements.All(s => s.Id != id)))
            throw new InvalidOperationException("A migration batch arrived at an unknown settlement.");

        var report = new DemoWeekReport(week, worldTime, Array.AsReadOnly(ledgers.ToArray()),
            Array.AsReadOnly(events.ToArray()),
            Array.AsReadOnly(migrationLedgers.OrderBy(m => m.BatchId).ToArray()));
        Verify(report);
        return new DemoWeeklyOutcome(report, nextReserves.ToImmutable(), nextFood.ToImmutable(),
            nextPopulation.ToImmutable(), nextDebt.ToImmutable(), nextMigrations.ToImmutable(), nextMigrationBatchId);
    }

    /// <summary>
    /// 45E MIG_PULL：只考慮上一週已發布週報裡「沒有缺糧、沒有飢餓債、仍有人」的聚落，
    /// 且 TEMP 路線長度不超過已知範圍；取路線最短者，同長取編號小者。第一週沒有任何消息，所以不會遷徙。
    /// </summary>
    private static (DemoSettlement Settlement, ImmutableArray<DemoCellRef> Route)? ChooseDestination(
        DemoHexWorld map, DemoWorldSites sites, DemoSettlement source, DemoWeekReport? previousWeek,
        DemoWeeklyTempConfig config)
    {
        if (previousWeek is null)
            return null;

        (DemoSettlement Settlement, ImmutableArray<DemoCellRef> Route)? best = null;
        foreach (var candidate in sites.Settlements.OrderBy(s => s.Id))
        {
            if (candidate.Id == source.Id)
                continue;
            var news = previousWeek.Settlements.FirstOrDefault(l => l.SettlementId == candidate.Id);
            if (news is null || news.FoodShortage > 0 || news.HungerDebtClosing > 0 || news.PopulationClosing == 0)
                continue;

            var route = DemoMigrationRoutes.FindRoute(map,
                new DemoCellRef(source.Column, source.Row), new DemoCellRef(candidate.Column, candidate.Row));
            if (route.Length < 2 || route.Length - 1 > config.KnownRouteMaxSteps)
                continue;
            if (best is null || route.Length < best.Value.Route.Length)
                best = (candidate, route);
        }

        return best;
    }

    /// <summary>
    /// 任何一個聚落或遷徙隊伍帳目不平衡、遷徙瞬移、兩端對不上、世界人口不守恆，
    /// 或死亡沒有對應的帶原因事件，就丟出例外；呼叫端必須在發布任何狀態之前呼叫。
    /// </summary>
    public static void Verify(DemoWeekReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var migrations = report.Migrations ?? Array.Empty<DemoMigrationWeekLedger>();
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

            // MIG_LEDGER：來源流出＝出發隊伍；目的地流入＝抵達隊伍（人口、糧食、飢餓債三本帳）。
            var departed = migrations.Where(m => m.Departed && m.FromSettlementId == ledger.SettlementId).ToList();
            var arrived = migrations.Where(m => m.Arrived && m.ToSettlementId == ledger.SettlementId).ToList();
            if (departed.Sum(m => m.PopulationOpening) != ledger.Emigrants
                || departed.Sum(m => m.FoodOpening) != ledger.FoodCarriedOut
                || departed.Sum(m => m.HungerDebtOpening) != ledger.HungerDebtCarriedOut
                || arrived.Sum(m => m.PopulationClosing) != ledger.Immigrants
                || arrived.Sum(m => m.FoodClosing) != ledger.FoodCarriedIn
                || arrived.Sum(m => m.HungerDebtClosing) != ledger.HungerDebtCarriedIn)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 遷徙兩端對帳不一致（migration ledger mismatch）。");
        }

        var settlementIds = report.Settlements.Select(l => l.SettlementId).ToHashSet();
        foreach (var m in migrations)
        {
            if (!m.IsBalanced || !settlementIds.Contains(m.FromSettlementId) || !settlementIds.Contains(m.ToSettlementId)
                || m.FromSettlementId == m.ToSettlementId)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週遷徙隊伍 #{m.BatchId} 帳目不平衡或瞬移（M1_MIG）。");

            var migrantDeaths = report.Events
                .Where(e => e.MigrationBatchId == m.BatchId && e.Kind == DemoSettlementEventKind.MigrantStarvationDeath)
                .ToList();
            if (migrantDeaths.Sum(e => e.Count) != m.Deaths || migrantDeaths.Any(e => string.IsNullOrWhiteSpace(e.Cause)))
                throw new InvalidOperationException(
                    $"第 {report.Week} 週遷徙隊伍 #{m.BatchId} 途中死亡沒有對應的帶原因事件（death without cause）。");
        }

        if (migrations.Select(m => m.BatchId).Distinct().Count() != migrations.Count)
            throw new InvalidOperationException($"第 {report.Week} 週遷徙隊伍編號重複。");

        // M1_POP（世界）：聚落人口＋途中人口在一週內只因出生與死亡改變；途中人口只計一次。
        var opening = report.Settlements.Sum(l => l.PopulationOpening)
            + migrations.Where(m => !m.Departed).Sum(m => m.PopulationOpening);
        var closing = report.Settlements.Sum(l => l.PopulationClosing)
            + migrations.Where(m => !m.Arrived).Sum(m => m.PopulationClosing);
        var births = report.Settlements.Sum(l => l.Births);
        var deaths = report.Settlements.Sum(l => l.Deaths) + migrations.Sum(m => m.Deaths);
        if (opening + births - deaths != closing)
            throw new InvalidOperationException(
                $"第 {report.Week} 週世界人口不守恆：{opening}＋{births}－{deaths}≠{closing}（含途中人口）。");
    }
}
