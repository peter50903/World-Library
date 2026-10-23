using WorldSimulation.Core.Rng;
using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// TEMP（暫定）每週結算示範設定：資源點初始存量、存量上限、每週再生量、每人每週產量／消耗、
/// 聚落初始糧食；V0-06A 起另含遷徙門檻（最少幾人才會外移、外移比例的除數、已知路線的最遠格數）
/// 與 TEST 用局部饑荒聚落編號；V0-06B 起另含開拓開關、開拓選址的最遠格數與 TEST 用「開局聚落全部饑荒」。這些值沒有定案，只供 V0-04A 示範畫面與測試；不得寫回正式設定檔或 Notion 規則。
/// </summary>
public sealed record DemoWeeklyTempConfig(
    long InitialReservePerPoint,
    long ReserveCapPerPoint,
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
    int TempFamineSettlementId = 0,
    int NewsDelayWeeksPerCell = 1,
    bool FoundingEnabled = false,
    int FoundingSearchMaxSteps = 3,
    bool TempFamineAllOriginal = false,
    bool SharedCatchmentEnabled = false,
    bool CrowdedForaging = false,
    bool HumanLifecycleEnabled = false)
{
    /// <summary>TEMP 範例值（未定案）。</summary>
    public static DemoWeeklyTempConfig Sample { get; } = new(
        InitialReservePerPoint: 60,
        ReserveCapPerPoint: 120,
        // 第 08 章 a 節：舊「草原自動農田 30」退場——草原不再自動產農糧（開墾與農田產出待第 11 章知識系統）。
        WoodlandRegenPerWeek: 20,
        FisheryRegenPerWeek: 25,
        YieldPerWorkerPerWeek: 5,
        FoodPerPersonPerWeek: 4,
        InitialFoodPerPerson: 7);

    /// <summary>2026-10-11 playable human-only TEMP foraging, forest 20 / explicit river 5 per source,
    /// shared once across neighbouring settlements; no automatically developed farmland.</summary>
    public static DemoWeeklyTempConfig ForagingSample { get; } = Sample with
    {
        SharedCatchmentEnabled = true,
        InitialReservePerPoint = 0,
        ReserveCapPerPoint = 0,
        WoodlandRegenPerWeek = 0,
        FisheryRegenPerWeek = 0
    };

    /// <summary>TEMP playable human births, pregnancy and age-related deaths.
    /// 第 07 章 b 節起遷徙隊伍以家庭為單位攜帶個人身分（不複製、不遺失）。</summary>
    public static DemoWeeklyTempConfig LivingForagingSample { get; } = ForagingSample with
    {
        HumanLifecycleEnabled = true,
        // 第 07 章 b 節：遷徙隊伍攜帶家庭身分，人類繁衍情境不再禁止遷徙；c 節：也可開拓新聚落。
        FoundingEnabled = true,
        // 第 08-1 章【TEMP】：開拓選址 3→6 格，聚落群附近的空地用完後仍能擴散到地圖其他地方。
        FoundingSearchMaxSteps = 6,
        // TEMP adult gather capacity: enough to support non-working dependents,
        // still limited by genuine natural-source budget (never mint food).
        // 第 08-1 章：8→16。診斷（--diagnose-living）顯示 2 名成人扣 1 名取水者後勞力上限＝需求，
        // 孩子一出生就缺糧餓死、人口無法成長；提高後限制人口的是可及自然來源（承載上限），不是勞力。
        YieldPerWorkerPerWeek = 16,
        // 第 08-2 章【TEMP】希望時代開局存糧：每人 4 週食量（成人每週 4 × 4），開局不會一開始就挨餓。
        InitialFoodPerPerson = 16
    };

    /// <summary>Player comparison mode: same forest20 / river5, but unusually crowded camps
    /// (40–48 people) start with no stored food. Demonstrates scarcity without disabling wild sources.</summary>
    public static DemoWeeklyTempConfig CrowdedForagingSample { get; } = ForagingSample with
    {
        CrowdedForaging = true,
        InitialFoodPerPerson = 0
    };

    /// <summary>
    /// TEST／CI 用 TEMP 饑荒情境：資源點沒有存量也不再生、聚落沒有初始糧食，
    /// 用來在數週內重現飢餓與餓死事件。不是正式數值。
    /// </summary>
    public static DemoWeeklyTempConfig FamineSample { get; } = Sample with
    {
        InitialReservePerPoint = 0,
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

    /// <summary>
    /// TEST／CI 用 TEMP 開拓情境（V0-06B）：開局的每個聚落都沒有存量、再生與初始糧食（舊土地耗竭），
    /// 遷徙隊伍建立的新聚落則照 <see cref="Sample"/> 的再生（新資源點存量從 0 開始，只靠再生）；開啟開拓。
    /// 用來重現「附近沒有已知有糧聚落 → 缺糧人口出發到空格建立新聚落」。不是正式數值。
    /// </summary>
    public static DemoWeeklyTempConfig FrontierSample { get; } = Sample with
    {
        TempFamineAllOriginal = true,
        FoundingEnabled = true
    };

    /// <summary>
    /// 第 08-2 章：希望時代（世界的第一個時代；目前＝人類採集普通世界）。沒有災難、沒有紛爭，
    /// 飢餓與缺水不致死（缺糧只會停止受孕、促成遷徙，飢餓債照記），只有老年才會自然死亡（見 <see cref="DemoHumanLifecycle.Rules"/>）。
    /// 其他 TEMP 情境（局部饑荒、開拓等驗收用情境）仍照舊會餓死。
    /// </summary>
    public bool HopeEra => HumanLifecycleEnabled;

    /// <summary>第 08-2 章：時代名稱（沒有時代標籤的 TEMP 情境＝null）。</summary>
    public string? EraName => HopeEra ? "希望時代" : null;

    internal void Validate()
    {
        if (InitialReservePerPoint < 0 || ReserveCapPerPoint < InitialReservePerPoint
            || WoodlandRegenPerWeek < 0 || FisheryRegenPerWeek < 0
            || YieldPerWorkerPerWeek < 0 || FoodPerPersonPerWeek < 0 || InitialFoodPerPerson < 0
            || HungerDebtPerStarvationDeath <= 0 || HungerRecoveryPerPersonPerWeek < 0
            || MinPopulationToEmigrate < 2 || EmigrationShareDivisor < 2 || KnownRouteMaxSteps < 1
            || TempFamineSettlementId < 0 || NewsDelayWeeksPerCell < 1 || FoundingSearchMaxSteps < 1
            || HumanLifecycleEnabled && !SharedCatchmentEnabled)
            throw new ArgumentOutOfRangeException(nameof(DemoWeeklyTempConfig), "Invalid TEMP weekly settlement config.");
    }

    /// <summary>
    /// TEMP 饑荒情境下，這個「開局」聚落是否為沒有存量、再生與初始糧食的饑荒聚落
    /// （局部饑荒的指定聚落，或 V0-06B 開拓情境的所有開局聚落）。
    /// </summary>
    internal bool IsFamineSettlement(int settlementId)
        => TempFamineAllOriginal || (TempFamineSettlementId != 0 && settlementId == TempFamineSettlementId);

    /// <summary>V0-06B：新建的聚落不是開局聚落，不套用 TEMP 饑荒情境（新土地照常再生）。</summary>
    internal bool IsFamineSettlement(DemoSettlement settlement)
        => !settlement.IsFounded && IsFamineSettlement(settlement.Id);

    internal long RegenFor(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Woodland => WoodlandRegenPerWeek,
        DemoResourceKind.Fishery => FisheryRegenPerWeek,
        DemoResourceKind.Uncultivated => 0,
        _ => throw new InvalidOperationException("Unknown TEMP resource kind.")
    };

    /// <summary>
    /// M1_NO_MAGIC_FOOD 的合法來源（TEMP 對應）：林地（採集狩獵）與漁場（漁撈）＝採集。
    /// 第 08 章 a 節：農牧只能來自真實開墾的農田（<see cref="DemoLandDevelopment"/>），本章尚不產糧。
    /// 沒有對應合法來源的資源種類不得產生糧食。
    /// </summary>
    internal static DemoFoodSource FoodSourceOf(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Woodland => DemoFoodSource.Gathering,
        DemoResourceKind.Fishery => DemoFoodSource.Gathering,
        DemoResourceKind.Uncultivated => throw new InvalidOperationException("Uncultivated land produces no farm food."),
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
    MigrantStarvationDeath = 5,

    /// <summary>
    /// V0-06B 建立新聚落：開拓隊走到空格，以倖存者建立新聚落（45F SETTLE_NEW；M1_SETTLE 人口全部來自隊伍）。
    /// SettlementId＝新聚落；Count＝人數。
    /// </summary>
    SettlementFounded = 6,
    /// <summary>Verified human pregnancy completed after 39 TEMP weeks.</summary>
    Birth = 7,
    /// <summary>Verified age/typical lifespan-based natural death, not hunger death.</summary>
    NaturalDeath = 8,
    /// <summary>
    /// 第 07 章 c 節：聚落人口連續為 0 滿 TEMP 週數，成為遺跡（45F SETTLE_FAIL 留下遺跡）；該格恢復自然來源。
    /// 根因事件，證據是週帳連續的期末人口 0。Count＝空置週數。
    /// </summary>
    SettlementRuined = 9,
    /// <summary>第 08-6 章：聚落習得一項技術（根因：長年經驗累積或遷入者帶來）。Count＝知識樹技術編號（<see cref="DemoTechNode.Index"/>，第 08-9 章 A1）。</summary>
    KnowledgeLearned = 10,
    /// <summary>第 08-6 章：世界進入新時代；直接原因＝同週同聚落的習得技術事件。Count＝時代（<see cref="DemoEra"/>）。</summary>
    EraBegan = 11,
    /// <summary>第 08-6 章 c 節：聚落開墾完成一塊農田（根因）。Count＝格編號（列×欄數＋欄＋1）。</summary>
    FarmlandCleared = 12,
    /// <summary>
    /// 第 08-9 章 A5：聚落成為遺跡、勢力範圍內又沒有其他持有者，這項知識在此地失傳。
    /// 直接原因＝同週同聚落的成為遺跡事件。Count＝知識樹技術編號。
    /// </summary>
    KnowledgeLost = 13,
    /// <summary>第 08-9 章 B3：聚落創立新信仰；直接原因＝同週同聚落習得祖靈祭祀。Count＝信仰編號。</summary>
    FaithFounded = 14,
    /// <summary>第 08-9 章 B3：信仰零信徒而滅絕（48A：不刪除，可同編號復興）。根因。Count＝信仰編號。</summary>
    FaithExtinct = 15,
    /// <summary>第 08-9 章 B4：一群聚落與同文化本群分開滿 50 年，分化出子文化（根因）。Count＝新文化編號。</summary>
    CultureSplit = 16,
    /// <summary>
    /// 第 08-9 章 C2：途中隊伍在生活圈外遭野獸襲擊而死（12a 第 3 節）；直接原因＝隊伍出發的外移事件。
    /// SettlementId＝出發聚落；Count＝死亡人數。
    /// </summary>
    MigrantKilledByBeasts = 17
}

/// <summary>
/// V0-04B 帶原因的聚落事件（只追加、不改寫）。Count 為缺糧量、死亡人數或 0。
/// Cause 是玩家可讀的繁體中文原因，Describe() 給歷史列表顯示。
/// #88：EventId＝世界內唯一、從 1 起連續遞增、不重用的事件編號；
/// CauseEventId＝43F CAUSE_PARENT 直接原因事件編號（0＝根因，例如本週缺糧本身）。
/// M1 示範每筆事件至多一個直接原因（正式 43F 允許 1～N 個，TEMP 簡化）；
/// 直接後果（43F EFFECT_CHILD）由 <see cref="DemoCausalIndex"/> 反向索引，不另存。
/// </summary>
public sealed record DemoSettlementEvent(
    int Week,
    TimeSpan WorldTime,
    int SettlementId,
    string SettlementName,
    DemoSettlementEventKind Kind,
    long Count,
    string Cause,
    int MigrationBatchId = 0,
    long EventId = 0,
    long CauseEventId = 0)
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
            DemoSettlementEventKind.SettlementFounded => $"第 {day} 天  |  建立新聚落「{SettlementName}」：{Count} 名遷徙者定居（{Cause}）",
            DemoSettlementEventKind.Birth => $"第 {day} 天  |  {SettlementName} 出生 {Count} 人（{Cause}）",
            DemoSettlementEventKind.NaturalDeath => $"第 {day} 天  |  {SettlementName} 自然死亡 {Count} 人（{Cause}）",
            DemoSettlementEventKind.SettlementRuined => $"第 {day} 天  |  {SettlementName} 成為遺跡（{Cause}）",
            DemoSettlementEventKind.KnowledgeLearned => $"第 {day} 天  |  {SettlementName} {Cause}",
            DemoSettlementEventKind.EraBegan => $"第 {day} 天  |  {Cause}",
            DemoSettlementEventKind.FarmlandCleared => $"第 {day} 天  |  {SettlementName} {Cause}",
            DemoSettlementEventKind.KnowledgeLost => $"第 {day} 天  |  {SettlementName} {Cause}",
            DemoSettlementEventKind.FaithFounded => $"第 {day} 天  |  {SettlementName} {Cause}",
            DemoSettlementEventKind.FaithExtinct => $"第 {day} 天  |  {Cause}",
            DemoSettlementEventKind.CultureSplit => $"第 {day} 天  |  {SettlementName} {Cause}",
            DemoSettlementEventKind.MigrantKilledByBeasts => $"第 {day} 天  |  自{SettlementName}出發的隊伍 {Count} 人遭野獸襲擊身亡（{Cause}）",
            _ => $"第 {day} 天  |  {SettlementName} 未知事件"
        };
    }
}

/// <summary>
/// 一個示範聚落一週的帳目（全部是純量，可直接比較相等）。
/// 資源點存量帳：期初＋再生－採收＝期末。
/// 糧食帳：期初＋生產－消耗＝期末；生產必須等於採收（無魔法糧食）。
/// 人口帳：期初＋出生－死亡＋遷入－遷出＝期末（出生與自然死亡只在人類繁衍情境；死亡＝餓死＋自然死亡，見 <see cref="StarvationDeaths"/>；遷入遷出 V0-06A）。
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
    long HungerDebtCarriedOut = 0,
    long NaturalDeaths = 0,
    long HungerDebtRemovedByNaturalDeath = 0,
    long AdultWorkersOpening = -1,
    long FoodSpoiled = 0,
    long WaterShortage = 0,
    long WaterCarriers = 0,
    long ChildStarvationDeaths = 0,
    long ElderStarvationDeaths = 0,
    long SourceSlack = 0,
    long ToolYieldPercent = 0,
    long FoodFromDead = 0,
    long FoodFromHunting = 0)
{
    /// <summary>第 08-6 章 e 節：本週每名勞力可採量（工具加成後）。</summary>
    public long YieldPerWorker(DemoWeeklyTempConfig config)
        => checked(config.YieldPerWorkerPerWeek * (100 + ToolYieldPercent) / 100);

    /// <summary>
    /// 本週聚落餓死人數＝全部死亡－自然死亡（Notion 02 §E：死亡按原因分類）。
    /// 下游（Godot 診斷、M1 驗收、百年長測）一律用此屬性，不各自相減。
    /// </summary>
    public long StarvationDeaths => Deaths - NaturalDeaths;

    public bool ReserveBalanced => ReserveOpening + Regenerated - Harvested == ReserveClosing;

    /// <summary>糧食帳：期初＋生產＋帶入－消耗－帶出－腐敗（第 06 章 d 節）＝期末。</summary>
    public bool FoodBalanced
        => FoodOpening + FoodProduced + FoodCarriedIn + FoodFromDead + FoodFromHunting - FoodConsumed - FoodCarriedOut - FoodSpoiled == FoodClosing; // 第 08-9 章 B3b 食人儀式、C1 狩獵

    /// <summary>
    /// M1_NO_MAGIC_FOOD：糧食只能來自資源點的實際採收，且每一單位都歸屬合法來源（農牧／採集）。
    /// </summary>
    public bool NoMagicFood => FoodProduced == Harvested && FoodFromFarming + FoodFromGathering == FoodProduced;

    /// <summary>飢餓債帳：期初＋缺糧－恢復－隨死者移除＝期末；有死亡必有移除的飢餓債（死亡有原因）。</summary>
    public bool HungerDebtBalanced
        => HungerDebtOpening + FoodShortage + WaterShortage - HungerDebtRecovered - HungerDebtRemovedByDeath - HungerDebtRemovedByNaturalDeath
            + HungerDebtCarriedIn - HungerDebtCarriedOut == HungerDebtClosing
        && (Deaths == NaturalDeaths || HungerDebtRemovedByDeath > 0)
        && (PopulationClosing > 0 || HungerDebtClosing == 0);

    /// <summary>
    /// M1_RES：聚落總資源（資源點存量＋糧食）只因再生與遷徙隊伍帶入增加、只因消耗與帶出減少。
    /// </summary>
    public bool TotalResourceBalanced
        => ReserveOpening + FoodOpening + Regenerated + FoodCarriedIn + FoodFromDead + FoodFromHunting - FoodConsumed - FoodCarriedOut - FoodSpoiled
            == ReserveClosing + FoodClosing;

    /// <summary>M1_POP 的人口帳部分。</summary>
    public bool PopulationBalanced
        => PopulationOpening + Births - Deaths + Immigrants - Emigrants == PopulationClosing;

    public bool NonNegative
        => ReserveOpening >= 0 && Regenerated >= 0 && Harvested >= 0 && ReserveClosing >= 0
        && FoodOpening >= 0 && FoodProduced >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && PopulationOpening >= 0 && Births >= 0 && Deaths >= 0 && NaturalDeaths >= 0
        && NaturalDeaths <= Deaths && HungerDebtRemovedByNaturalDeath >= 0 && PopulationClosing >= 0
        && FoodFromFarming >= 0 && FoodFromGathering >= 0 && HungerDebtOpening >= 0 && HungerDebtRecovered >= 0
        && HungerDebtRemovedByDeath >= 0 && HungerDebtClosing >= 0
        && Immigrants >= 0 && Emigrants >= 0 && FoodCarriedIn >= 0 && FoodCarriedOut >= 0
        && HungerDebtCarriedIn >= 0 && HungerDebtCarriedOut >= 0
        && (AdultWorkersOpening == -1 || AdultWorkersOpening >= 0 && AdultWorkersOpening <= PopulationOpening)
        && FoodSpoiled >= 0 && WaterShortage >= 0 && WaterCarriers >= 0
        && ChildStarvationDeaths >= 0 && ElderStarvationDeaths >= 0
        && ChildStarvationDeaths + ElderStarvationDeaths <= Deaths - NaturalDeaths
        // 第 08-9 章 B3b：死者的糧食只來自本週餓死的人。
        && FoodFromDead >= 0 && FoodFromDead <= checked((Deaths - NaturalDeaths) * DemoFaiths.TempFoodPerDead)
        // 第 08-9 章 C1：獵得的糧食是整頭獸的倍數。
        && FoodFromHunting >= 0 && FoodFromHunting % DemoWildlife.TempFoodPerGrazer == 0;

    public bool IsBalanced
        => ReserveBalanced && FoodBalanced && NoMagicFood && TotalResourceBalanced && PopulationBalanced
        && HungerDebtBalanced && NonNegative;
}

/// <summary>第 04 章 c 節：一個共享自然來源本週分給一個聚落的量。</summary>
public sealed record DemoSourceShare(int SettlementId, long Food);

/// <summary>
/// 第 04 章 c 節：共享採集來源層級週帳。守恆：Σ份額＋未採＝本週可採，皆非負，同一來源不重複聚落。
/// 第 06 章 c 節：加期初存量與再生——本週可採（FoodAvailable）＝期初存量＋再生；
/// 再生＝min(每週供應, 上限－期初)，上限＝每週供應 × TEMP 週數；未採（Unharvested）＝期末存量，留到下週。
/// 份額陣列以值比較。
/// </summary>
public sealed record DemoSourceWeekLedger(
    int Week,
    int SourceId,
    DemoCatchmentSourceKind Kind,
    int Column,
    int Row,
    long FoodAvailable,
    IReadOnlyList<DemoSourceShare> Shares,
    long Unharvested,
    long StockOpening = 0,
    long Regenerated = 0)
{
    /// <summary>本週採收合計（Σ份額）。</summary>
    public long Harvested => (Shares ?? Array.Empty<DemoSourceShare>()).Sum(x => x.Food);

    /// <summary>期末存量＝未採。</summary>
    public long StockClosing => Unharvested;

    public bool Equals(DemoSourceWeekLedger? other)
        => other is not null && Week == other.Week && SourceId == other.SourceId && Kind == other.Kind
        && Column == other.Column && Row == other.Row && FoodAvailable == other.FoodAvailable
        && Unharvested == other.Unharvested && StockOpening == other.StockOpening && Regenerated == other.Regenerated
        && (Shares ?? Array.Empty<DemoSourceShare>()).SequenceEqual(other.Shares ?? Array.Empty<DemoSourceShare>());

    public override int GetHashCode()
        => HashCode.Combine(Week, SourceId, Kind, Column, Row, FoodAvailable, Unharvested, HashCode.Combine(StockOpening, Regenerated, Shares?.Count ?? 0));

    public override string ToString()
        => FormattableString.Invariant(
            $"source{SourceId}@w{Week}:{Kind}({Column},{Row}) {StockOpening}+{Regenerated}={FoodAvailable}=[{string.Join(",", (Shares ?? Array.Empty<DemoSourceShare>()).Select(x => $"{x.SettlementId}:{x.Food}"))}]+{Unharvested}");

    /// <summary>
    /// 第 06 章 c 節：本週每個來源的期初存量、再生與可採（依編號；可採＞0 才列）。
    /// <paramref name="potential"/> 的 FoodAvailable 是每週供應；<paramref name="stocks"/> 是上週期末存量（沒有＝0）。
    /// </summary>
    internal static IReadOnlyList<(DemoCatchmentFoodSource Source, long Opening, long Regenerated)> Plan(
        IReadOnlyList<DemoCatchmentFoodSource> potential, IReadOnlyDictionary<int, long> stocks, int capWeeks)
    {
        var plan = new List<(DemoCatchmentFoodSource, long, long)>();
        foreach (var source in potential.OrderBy(x => x.Id))
        {
            var opening = stocks.GetValueOrDefault(source.Id);
            var cap = checked(source.FoodAvailable * capWeeks);
            var regenerated = Math.Max(0, Math.Min(source.FoodAvailable, cap - opening));
            var available = checked(opening + regenerated);
            if (available > 0)
                plan.Add((source with { FoodAvailable = available }, opening, regenerated));
        }
        return plan;
    }

    /// <summary>由一次共享分配組出全部來源帳。</summary>
    internal static IReadOnlyList<DemoSourceWeekLedger> From(int week,
        IReadOnlyList<(DemoCatchmentFoodSource Source, long Opening, long Regenerated)> plan, DemoCatchmentFoodResult allocation)
        => Array.AsReadOnly(plan.Select(p =>
            new DemoSourceWeekLedger(week, p.Source.Id, p.Source.Kind, p.Source.Column, p.Source.Row, p.Source.FoodAvailable,
                Array.AsReadOnly(allocation.Harvests.Where(h => h.SourceId == p.Source.Id).OrderBy(h => h.SettlementId)
                    .Select(h => new DemoSourceShare(h.SettlementId, h.FoodHarvested)).ToArray()),
                allocation.UnharvestedSourceFood.GetValueOrDefault(p.Source.Id), p.Opening, p.Regenerated)).ToArray());
}

/// <summary>
/// 一次週結算的結果：第幾週、結算時的世界時間、各聚落帳目（依聚落編號排序），
/// 本週產生的帶原因事件（V0-04B，依聚落編號、事件順序排列），
/// 以及本週每支遷徙隊伍的帳目（V0-06A，依隊伍編號排列）。
/// V0-06B：本週建立的新聚落也在 Settlements 中（期初全部為 0、人口只來自遷入）。
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

    /// <summary>
    /// 第 04 章 c 節：共享採集來源層級週帳（每來源本週預算、分給哪些聚落各多少、未採多少），
    /// 依 SourceId 遞增、份額依 SettlementId 遞增；只列預算＞0 的來源。非共享情境為空。
    /// </summary>
    public IReadOnlyList<DemoSourceWeekLedger> Sources { get; init; } = Array.Empty<DemoSourceWeekLedger>();

    /// <summary>C04：本週結算開始時世界實體 ID 分配器的下一號（0＝未記錄，舊格式）。</summary>
    public long EntityIdNextOpening { get; init; }

    /// <summary>C04：本週全部配號（新聚落、資源點、隊伍、新生兒）之後的下一號；本週新發 ID 恰為 [Opening, Closing)。</summary>
    public long EntityIdNextClosing { get; init; }

    /// <summary>
    /// C05（P1.0.04-a；44G SCHED_RNG_SCOPE、44J FAIL_REPLAY）：本次週提交十條亂數流「提交前→提交後」計數器，
    /// 依 <see cref="RngStreams.All"/> 順序；與週提交同一交易寫入，供重播分歧定位。
    /// </summary>
    public IReadOnlyList<DemoRngCounter> RngCounters { get; init; } = Array.Empty<DemoRngCounter>();

    /// <summary>第 07-1 章 b 節：本週新任的代表人物（實體 ID 在本週配號區間內）。</summary>
    public IReadOnlyList<DemoRepresentative> Appointments { get; init; } = Array.Empty<DemoRepresentative>();

    /// <summary>第 07-1 章 b 節：本週卸任的代表人物（老死、聚落無人）。</summary>
    public IReadOnlyList<DemoRepresentativeExit> RepresentativeExits { get; init; } = Array.Empty<DemoRepresentativeExit>();

    /// <summary>C05：一行繁體中文週結算摘要（Godot 通知／歷史欄與 CLI 共用；只加總本週既有週帳欄位）。</summary>
    public string Summary()
    {
        var day = (long)WorldTime.TotalDays;
        return $"第 {Week} 週結算（第 {day} 日）｜聚落人口 {Settlements.Sum(l => l.PopulationClosing)}"
            + $"｜產糧 {Settlements.Sum(l => l.FoodProduced)}｜消耗 {Settlements.Sum(l => l.FoodConsumed)}"
            + $"｜缺糧 {Settlements.Sum(l => l.FoodShortage)}｜出生 {Settlements.Sum(l => l.Births)}"
            + $"｜死亡 {Settlements.Sum(l => l.Deaths)}｜遷出 {Settlements.Sum(l => l.Emigrants)}";
    }
}

/// <summary>C05：一條亂數流在一次週提交前後的抽樣計數器。</summary>
public sealed record DemoRngCounter(string StreamId, ulong Before, ulong After);

/// <summary>第 08-1 章：婚嫁遷移的 TEMP 參數。</summary>
public static class DemoMateTempSettings
{
    /// <summary>【TEMP】婚嫁遷移最遠路程（格）。</summary>
    public const int MateRouteMaxSteps = 12;
}

/// <summary>第 08-1 章：外移的種類（決定名冊依什麼規則挑出發者）。</summary>
public enum DemoEmigrationMode
{
    /// <summary>缺糧外移到有糧聚落（整戶）。</summary>
    Shortage = 0,

    /// <summary>開拓新聚落（需兩對伴侶，整戶）。</summary>
    Founding = 1,

    /// <summary>婚嫁遷移（一位找不到伴侶的育齡單身者）。</summary>
    Mate = 2
}

/// <summary>第 08-1 章：一個聚落的婚嫁市場（本週開始時名冊）。SeekerSex＝找不到伴侶的單身者性別（null＝沒有）。</summary>
public sealed record DemoMateMarket(DemoTempSex? SeekerSex, int UnpairedWomen, int UnpairedMen);

/// <summary>週結算的候選結果：報告與結算後的新狀態。在發布前不會寫入任何世界狀態。</summary>
internal sealed record DemoWeeklyOutcome(
    DemoWeekReport Report,
    ImmutableSortedDictionary<int, long> Reserves,
    ImmutableSortedDictionary<int, long> SettlementFood,
    ImmutableSortedDictionary<int, long> Population,
    ImmutableSortedDictionary<int, long> HungerDebt,
    ImmutableList<DemoMigrationBatch> Migrations,
    EntityIdAllocatorSnapshot EntityIds,
    DemoWorldSites Sites)
{
    /// <summary>第 06 章 c 節：共享採集各來源的期末存量（只列＞0）；非共享情境為空。</summary>
    public ImmutableSortedDictionary<int, long> SourceStocks { get; init; } = ImmutableSortedDictionary<int, long>.Empty;
}

/// <summary>
/// V0-04A／V0-04B／V0-06A 每週 L2 聚落結算（TEMP 示範），順序依 44B：
/// （途中隊伍先行：消耗攜帶糧食 → 健康後果 → 沿路線移動一格／抵達）→
/// ① 資源再生 → ② 勞動與生產 → ⑥ 消耗（形成飢餓債）→ ⑦ 健康後果（餓死）→
/// ⑧ 遷徙（缺糧聚落外移、併入本週抵達的隊伍）→ ⑨ 人口帳。
/// 純函式：只讀入目前狀態、回傳候選結果，不改動任何輸入。
/// 不使用亂數（C05 起執行期零抽樣；每週提交另記十條流計數器）；餓死人數與遷徙都是確定性的。
/// 不做隱藏救濟（45D SURV_NO_RESCUE）：人口即將滅亡也不生成糧食或人口。
/// 遷徙只用已經傳到來源聚落的消息（45E MIG_PULL：只用已知資訊，不讀本週的世界真相，
/// 也不讀還沒傳到的較新週報；見 <see cref="DemoMigrationNews"/>）。
/// V0-06B（TEMP 開拓）：缺糧聚落在已知範圍內每個可達聚落的消息都已傳到、且沒有一個有糧時，
/// 改派開拓隊到附近 TEMP 範圍內有水源的空格；開拓隊抵達時以倖存者建立新聚落（新編號、期初全部為 0、
/// 人口／糧食／飢餓債全部來自隊伍；45F SETTLE_NEW／SETTLE_KNOW／SETTLE_SUPPLY／SETTLE_FAIL、45G M1_SETTLE）。
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
            ImmutableList<DemoMigrationBatch>.Empty, DemoEntityIds.After(sites),
            publishedWeeks: Array.Empty<DemoWeekReport>(), config);

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
        EntityIdAllocatorSnapshot entityIds,
        IReadOnlyList<DemoWeekReport> publishedWeeks,
        DemoWeeklyTempConfig config,
        IReadOnlyDictionary<int, long>? adultWorkers = null,
        IReadOnlyDictionary<int, long>? sourceStocks = null,
        Func<int, long, long, DemoEmigrationMode, long>? emigrantPlanner = null,
        DemoLandDevelopmentMap? land = null,
        IReadOnlyDictionary<int, DemoMateMarket>? mates = null,
        Func<DemoSettlement, ISet<string>, string>? namer = null,
        long? firstEventId = null,
        Func<int, long>? debtSourceOf = null,
        bool? hopeRules = null,
        Func<int, long>? toolYieldPercent = null,
        Func<int, int>? cannibalPermille = null,
        IReadOnlyDictionary<int, long>? huntedFood = null,
        IReadOnlyDictionary<int, long>? beastAttacks = null)
    {
        // 第 08-6 章 b 節：希望時代的「不餓死」是時代特性——世界進入開拓時代後恢復餓死（使用者 2026-10-23）。
        var hope = hopeRules ?? config.HopeEra;
        // 【TEMP 規則】希望時代飢餓債上限＝致死門檻－1：超過的部分計為恢復（不致死、不跨時代累積）。
        void CapHopeDebt(ref long debt, ref long recovered)
        {
            if (!hope || debt < config.HungerDebtPerStarvationDeath) return;
            var excess = debt - (config.HungerDebtPerStarvationDeath - 1);
            recovered = checked(recovered + excess);
            debt -= excess;
        }
        // 第 07 章 b 節：emigrantPlanner（聚落 ID, 本週餓死人數, 想外移人數上限）→ 實際整戶外移人數（≤上限）；
        // 人類繁衍世界由名冊依家庭決定，其他世界沒有名冊時直接用上限。
        long PlanEmigrants(int settlementId, long deaths, long desired, DemoEmigrationMode mode = DemoEmigrationMode.Shortage)
        {
            if (emigrantPlanner is null || desired <= 0) return desired;
            var planned = emigrantPlanner(settlementId, deaths, desired, mode);
            if (planned < 0 || planned > desired)
                throw new InvalidOperationException("The family emigrant plan exceeds the allowed emigrants.");
            return planned;
        }
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(publishedWeeks);
        if (publishedWeeks.Count > 0 && publishedWeeks[publishedWeeks.Count - 1].Week >= week)
            throw new InvalidOperationException("Only weeks published before this settlement can be known (MIG_PULL).");
        ArgumentNullException.ThrowIfNull(entityIds);
        // C04：本週所有新實體（新聚落、資源點、隊伍）都由這個私有分配器發號；只有發布時才寫回世界。
        var ids = EntityIdAllocator.Restore(entityIds);
        var entityIdOpening = DemoEntityIds.NextValue(entityIds);
        if (migrations.Any(b => b.Id >= entityIdOpening) || sites.EntityIds().Any(id => id >= entityIdOpening))
            throw new InvalidOperationException("Entity IDs must stay below the allocator's next ID (IDs are never reused).");

        // Allocate each forest/river source ONCE before per-settlement weekly accounting.
        // 第 06 章 c 節：每個來源有持久存量（上週未採＋本週再生，上限 TEMP 週數），分配的是本週可採量。
        DemoCatchmentFoodResult? sharedHarvest = null;
        IReadOnlyList<DemoSourceWeekLedger> sourceLedgers = Array.Empty<DemoSourceWeekLedger>();
        var nextSourceStocks = ImmutableSortedDictionary<int, long>.Empty;
        // 第 06 章 d 節（只用於自然採集世界）：取水——聚落格或鄰格有水域才可取水，每 TEMP 人數需 1 名成年取水者，
        // 取水者不採集；取不到水的人形成缺水生存債。兒童食量為成人 TEMP 百分比；存糧每週腐敗（29 P_FOOD_LOSS_WEEK）。
        var survival = DemoSurvivalTempSettings.Current;
        var water = new Dictionary<int, (long Adults, long Children, long Carriers, long WaterShortage)>();
        var foragingWorkers = new Dictionary<int, long>();
        var foodNeeded = new Dictionary<int, long>();
        var baseNeeds = new Dictionary<int, long>();
        if (config.SharedCatchmentEnabled)
        {
            foreach (var settlement in sites.Settlements)
            {
                var people = population[settlement.Id];
                var adults = Math.Min(people, adultWorkers?.GetValueOrDefault(settlement.Id) ?? people);
                var hasWater = DemoMigrationRoutes.HasWaterAccess(map!, new DemoCellRef(settlement.Column, settlement.Row));
                var carriers = hasWater ? Math.Min(adults, survival.CarriersNeeded(people)) : 0;
                var served = hasWater ? Math.Min(people, checked(carriers * survival.PeoplePerWaterCarrier)) : 0;
                water[settlement.Id] = (adults, people - adults, carriers,
                    DemoSurvivalTempSettings.DehydrationDebt(people - served, config.FoodPerPersonPerWeek));
                foragingWorkers[settlement.Id] = adults - carriers;
                // 第 08-1 章：人類採集世界向來源要求「需求＋補存糧」（DemoCrowding.Claim），消耗仍只算需求。
                baseNeeds[settlement.Id] = survival.Demand(adults, people - adults, config.FoodPerPersonPerWeek);
                foodNeeded[settlement.Id] = DemoCrowding.Claim(config.HumanLifecycleEnabled, baseNeeds[settlement.Id], settlementFood[settlement.Id]);
            }
        }

        if (config.SharedCatchmentEnabled)
        {
            var plan = DemoSourceWeekLedger.Plan(
                DemoForagingWorld.Sources(map ?? throw new InvalidOperationException("Shared foraging needs map."), sites, land),
                sourceStocks ?? ImmutableSortedDictionary<int, long>.Empty, DemoForagingWorld.Settings.SourceStockCapWeeks);
            sharedHarvest = DemoForagingWorld.Allocate(map, sites, population, config, foragingWorkers,
                plan.Select(p => p.Source).ToArray(), foodNeeded: foodNeeded,
                yieldPercent: toolYieldPercent, // 第 08-6 章 e 節：工具提高採集效率
                baseNeeds: config.HumanLifecycleEnabled ? baseNeeds : null); // 第 08-3 章兩階段分配
            sourceLedgers = DemoSourceWeekLedger.From(week, plan, sharedHarvest);
            nextSourceStocks = sourceLedgers.Where(x => x.StockClosing > 0)
                .ToImmutableSortedDictionary(x => x.SourceId, x => x.StockClosing);
        }
        // 第 08-1 章：每個聚落可及範圍內全部自然來源的「每週可持續供應－本週全部採收」（DemoCrowding）。
        var slackBySettlement = config.SharedCatchmentEnabled && map is not null
            ? DemoCrowding.SlackInReach(map, sites, sourceLedgers)
            : new Dictionary<int, long>();
        var spareMap = new Lazy<Dictionary<DemoCellRef, long>>(() => DemoCrowding.SpareMap(map!, sourceLedgers, sites.Settlements.Where(x => !x.IsRuin))); // 第 08-1 章開拓選址
        var harvestedBySettlement = sharedHarvest?.Harvests.GroupBy(x => x.SettlementId)
            .ToDictionary(g => g.Key, g => g.Sum(h => h.FoodHarvested));
        // 第 08-6 章 c 節：從農田來源採收的部分記為農糧。
        var farmlandSources = sourceLedgers.Where(x => x.Kind == DemoCatchmentSourceKind.Farmland).Select(x => x.SourceId).ToHashSet();
        var farmedBySettlement = sharedHarvest?.Harvests.Where(h => farmlandSources.Contains(h.SourceId)).GroupBy(x => x.SettlementId)
            .ToDictionary(g => g.Key, g => g.Sum(h => h.FoodHarvested));
        var nextReserves = reserves.ToBuilder();
        var nextFood = settlementFood.ToBuilder();
        var nextPopulation = population.ToBuilder();
        var nextDebt = hungerDebt.ToBuilder();
        var ledgers = new List<DemoSettlementWeekLedger>(sites.Settlements.Count);
        var events = new List<DemoSettlementEvent>();

        // #88：事件編號接續已發布的最後一筆（只遞增、不重用）；每筆同時記下 43F 直接原因編號。
        // 第 04-1 章：世界傳入正式史庫的下一號（讀檔後的週報不含早期事件）；未傳時由週報推得。
        var nextEventId = firstEventId ?? DemoCausalIndex.NextEventId(publishedWeeks);
        DemoSettlementEvent Stamp(DemoSettlementEvent e, long causeEventId)
        {
            var stamped = e with { EventId = nextEventId, CauseEventId = causeEventId };
            nextEventId = checked(nextEventId + 1);
            return stamped;
        }

        var migrationLedgers = new List<DemoMigrationWeekLedger>();
        var nextMigrations = ImmutableList.CreateBuilder<DemoMigrationBatch>();
        var arrivals = new Dictionary<int, (long People, long Food, long Debt)>();
        var weekSites = sites; // V0-06B：本週建立的新聚落會加進這份副本，輸入的 sites 不變。

        // 途中隊伍（V0-06A）：只靠攜帶的糧食，飢餓與死亡規則與聚落相同；活著的人沿路線前進一格。
        foreach (var batch in migrations.OrderBy(b => b.Id))
        {
            // #88：隊伍的途中餓死、抵達、建立新聚落，直接原因都是它出發那週的外移事件。
            var emigrationEventId = DemoCausalIndex.EmigrationEventId(publishedWeeks, batch);
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

            // 第 08-2 章：希望時代飢餓不致死。第 08-6 章 b 節：希望時代飢餓債不超過致死門檻（多出的計為恢復），
            // 進入開拓時代時不會因為累積多年的債一次餓死。
            CapHopeDebt(ref debt, ref recovered);
            var deaths = hope ? 0 : Math.Min(people, debt / config.HungerDebtPerStarvationDeath);
            var removedByDeath = deaths * config.HungerDebtPerStarvationDeath;
            var survivors = people - deaths;
            if (survivors == 0)
                removedByDeath = debt;
            debt -= removedByDeath;
            // 第 08-9 章 C2：生活圈外遭野獸襲擊（襲擊與人數在結算前由世界依掠食獸與局部亂數決定）。
            var killed = Math.Min(survivors, beastAttacks?.GetValueOrDefault(batch.Id) ?? 0);
            var starved = deaths;
            if (killed > 0)
            {
                survivors -= killed;
                deaths += killed;
                if (survivors == 0)
                {
                    removedByDeath += debt;
                    debt = 0;
                }
            }

            // 不瞬移：每週最多前進一格（TEMP 速度），全數死亡的隊伍停在原地。
            var step = survivors > 0 ? batch.StepIndex + 1 : batch.StepIndex;
            var arrived = survivors > 0 && step == batch.Path.Length - 1;

            if (killed > 0)
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, batch.FromSettlementId, batch.FromName,
                    DemoSettlementEventKind.MigrantKilledByBeasts, killed,
                    $"前往{batch.ToName}途中，在生活圈外遇上掠食獸群【TEMP】", batch.Id), emigrationEventId));
            if (starved > 0)
            {
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, batch.FromSettlementId, batch.FromName,
                    DemoSettlementEventKind.MigrantStarvationDeath, starved,
                    survivors == 0 && batch.Founding
                        ? $"開拓隊前往{batch.ToName}途中攜帶的糧食耗盡，全數死亡，開拓失敗、沒有建立聚落"
                        : survivors == 0
                        ? $"前往{batch.ToName}途中攜帶的糧食耗盡，隊伍全數死亡"
                        : $"前往{batch.ToName}途中攜帶的糧食不足，飢餓債達 TEMP 門檻（每人 {config.HungerDebtPerStarvationDeath}）",
                    batch.Id), emigrationEventId));
            }

            var toSettlementId = batch.ToSettlementId;
            if (arrived && batch.Founding)
            {
                // V0-06B SETTLE_NEW：開拓隊走到目的空格，以倖存者建立新聚落。新編號只會遞增；
                // 期初人口、糧食、飢餓債、資源點存量全部為 0，全部由隊伍帶入（M1_SETTLE 不憑空）。
                if (map is null)
                    throw new InvalidOperationException("A founding batch needs the TEMP map.");
                var cell = batch.Destination;
                var foundedId = DemoEntityIds.NextInt(ids);
                var founded = new DemoSettlement(foundedId, $"新聚落{checked(weekSites.FoundedSettlementCount + 1)}號",
                    cell.Column, cell.Row, FoundedWeek: week, FoundedFromSettlementId: batch.FromSettlementId,
                    FoundedByBatchId: batch.Id);
                // 第 07-1 章 b 節：希望時代的新聚落取西式奇幻音譯名（可能沿用母村名，例：新索倫）。
                if (namer is not null)
                    founded = founded with { Name = namer(founded, weekSites.Settlements.Select(s => s.Name).ToHashSet()) };
                weekSites = weekSites.WithFoundedSettlement(founded, DemoEntityIds.NextInt(ids), map);
                var point = weekSites.ResourcePointsOf(founded.Id).Single();
                reserves = reserves.Add(point.Id, 0);
                settlementFood = settlementFood.Add(founded.Id, 0);
                population = population.Add(founded.Id, 0);
                hungerDebt = hungerDebt.Add(founded.Id, 0);
                toSettlementId = founded.Id;
                arrivals[founded.Id] = (survivors, foodClosing, debt);
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, founded.Id, founded.Name,
                    DemoSettlementEventKind.SettlementFounded, survivors,
                    $"來自{batch.FromName}的開拓隊，第 {batch.DepartedWeek} 週出發，沿相鄰格走了 {batch.Path.Length - 1} 格到 {cell}；"
                    + $"人口、糧食 {foodClosing}、飢餓債 {debt} 全部由隊伍帶來，新資源點存量從 0 開始（TEMP）",
                    batch.Id), emigrationEventId));
            }
            else if (arrived)
            {
                arrivals.TryGetValue(batch.ToSettlementId, out var inbound);
                arrivals[batch.ToSettlementId] = (checked(inbound.People + survivors),
                    checked(inbound.Food + foodClosing), checked(inbound.Debt + debt));
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, batch.ToSettlementId, batch.ToName,
                    DemoSettlementEventKind.Arrival, survivors,
                    $"來自{batch.FromName}，第 {batch.DepartedWeek} 週出發，沿相鄰格走了 {batch.Path.Length - 1} 格",
                    batch.Id), emigrationEventId));
            }
            else if (survivors > 0)
            {
                nextMigrations.Add(batch with { StepIndex = step, People = survivors, Food = foodClosing, HungerDebt = debt });
            }

            migrationLedgers.Add(new DemoMigrationWeekLedger(
                week, batch.Id, batch.FromSettlementId, toSettlementId,
                batch.Cell, batch.Path[step], Departed: false, Arrived: arrived,
                people, deaths, survivors,
                batch.Food, consumed, foodClosing, shortage,
                batch.HungerDebt, recovered, removedByDeath, debt, Founding: batch.Founding, BeastDeaths: killed));
        }

        foreach (var settlement in weekSites.Settlements.OrderBy(s => s.Id))
        {
            var people = population[settlement.Id];
            long reserveOpening = 0, regenerated = 0, harvested = 0, reserveClosing = 0;
            long fromFarming = 0, fromGathering = 0;

            if (config.SharedCatchmentEnabled)
            {
                // Nature supplies one weekly pool per real forest/river source; allocation is
                // decided BEFORE this loop. A local resource point is a zero-stock accounting
                // slot, not a second source of food. Week ledger remains balanced.
                var share = harvestedBySettlement?.GetValueOrDefault(settlement.Id) ?? 0L;
                foreach (var point in weekSites.ResourcePointsOf(settlement.Id))
                {
                    if (reserves[point.Id] != 0)
                        throw new InvalidOperationException("Foraging settlement reserve must stay zero.");
                    nextReserves[point.Id] = 0;
                }
                regenerated = share; // 第 04 章 c 節：共享模式的 Regenerated＝由來源帳轉入（Σ份額），不是本地再生。
                harvested = share;
                fromFarming = farmedBySettlement?.GetValueOrDefault(settlement.Id) ?? 0L;
                fromGathering = share - fromFarming;
            }
            else
            {
            // ① 資源再生，② 勞動與生產：勞力依資源點編號順序採收，不能超過再生後的存量。
            // 勞動力＝目前人口，所以死亡會降低下週產量（45D SURV_CASCADE）。
            var laborLeft = checked(people * config.YieldPerWorkerPerWeek);
            foreach (var point in weekSites.ResourcePointsOf(settlement.Id).OrderBy(p => p.Id))
            {
                var opening = reserves[point.Id];
                if (point.Kind == DemoResourceKind.Uncultivated)
                {
                    // 第 08 章 a 節：未開發草原沒有存量、不再生、不產糧（農田需真實開墾，第 11 章才產糧）。
                    if (opening != 0)
                        throw new InvalidOperationException("Uncultivated grassland cannot hold a food reserve.");
                    nextReserves[point.Id] = 0;
                    continue;
                }
                var regenPerWeek = config.IsFamineSettlement(settlement) ? 0 : config.RegenFor(point.Kind);
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

            }

            // ⑥ 消耗：糧食只來自庫存與本週採收；取得量低於需求的部分形成飢餓債。
            // 第 06 章 d 節（自然採集世界）：先扣存糧腐敗；兒童半份；缺水另加生存債（併入飢餓債）。
            var foodOpening = settlementFood[settlement.Id];
            var waterPlan = water.TryGetValue(settlement.Id, out var plannedWater)
                ? plannedWater
                : (Adults: people, Children: 0L, Carriers: 0L, WaterShortage: 0L);
            var spoiled = config.SharedCatchmentEnabled ? DemoSurvivalTempSettings.Spoilage(foodOpening) : 0L;
            var available = checked(foodOpening - spoiled + harvested);
            var demand = config.SharedCatchmentEnabled
                ? survival.Demand(waterPlan.Adults, waterPlan.Children, config.FoodPerPersonPerWeek)
                : checked(people * config.FoodPerPersonPerWeek);
            var consumed = Math.Min(available, demand);
            var foodClosing = available - consumed;
            var shortage = demand - consumed;
            var waterShortage = waterPlan.WaterShortage;
            var survivalShortage = checked(shortage + waterShortage);

            var debtOpening = hungerDebt[settlement.Id];
            var debt = checked(debtOpening + survivalShortage);

            // 恢復也需來源：只有本週食物與水都被實際滿足時，飢餓債才下降（TEMP 速率）。
            long recovered = 0;
            if (survivalShortage == 0 && debt > 0)
            {
                recovered = Math.Min(debt, checked(people * config.HungerRecoveryPerPersonPerWeek));
                debt -= recovered;
            }

            // ⑦ 健康後果：每累積 TEMP 門檻的飢餓債造成 1 人餓死，該部分債務隨死者移除。
            // 第 08-2 章：希望時代飢餓不致死（飢餓債照記，只停止受孕、促成遷徙）；第 08-6 章 b 節：債不超過致死門檻。
            CapHopeDebt(ref debt, ref recovered);
            var deaths = hope ? 0 : Math.Min(people, debt / config.HungerDebtPerStarvationDeath);
            var removedByDeath = deaths * config.HungerDebtPerStarvationDeath;
            var survivors = people - deaths;
            // 第 08-9 章 B3b【TEMP 規則】食人儀式：信奉此信條的人口比例決定有多少死者被食用（每人 TempFoodPerDead 糧食，週末併入存糧）。
            var fromDead = deaths == 0 || survivors == 0 ? 0
                : checked(deaths * DemoFaiths.TempFoodPerDead * (cannibalPermille?.Invoke(settlement.Id) ?? 0) / 1000);
            if (survivors == 0)
                removedByDeath = debt; // 聚落無人倖存：剩餘飢餓債沒有承擔者。
            debt -= removedByDeath;

            // #88 因果：飢餓是根因（週帳缺糧為證據）；餓死與飢餓解除的直接原因是本聚落最近一筆
            // 帶來飢餓債的事件（本週飢餓，否則往回找飢餓或帶著飢餓債抵達／建立）；外移的直接原因是本週飢餓。
            long hungerEventId = 0;
            if (survivalShortage > 0)
            {
                var hunger = Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.Hunger, survivalShortage,
                    (shortage > 0 ? $"需求 {demand}，可取得 {available}" : "糧食足夠")
                    + (waterShortage > 0 ? $"，缺水（生存債 +{waterShortage}）" : "")
                    + $"，飢餓債 {debtOpening}→{checked(debtOpening + survivalShortage)}"), 0);
                hungerEventId = hunger.EventId;
                events.Add(hunger);
            }

            var debtSourceEventId = hungerEventId != 0
                ? hungerEventId
                : deaths > 0 || debtOpening > 0
                ? debtSourceOf?.Invoke(settlement.Id) ?? DemoCausalIndex.LatestDebtSourceId(publishedWeeks, settlement.Id) // 第 04-1 章：世界保存的債源
                : 0;

            if (deaths > 0)
            {
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.StarvationDeath, deaths,
                    $"飢餓：飢餓債累積達 TEMP 門檻（每人 {config.HungerDebtPerStarvationDeath}），連續缺糧"
                    + (fromDead > 0 ? $"；依食人儀式，死者化為糧食 {fromDead}" : "")),
                    debtSourceEventId));
            }

            if (debtOpening > 0 && debt == 0 && survivors > 0 && survivalShortage == 0)
            {
                events.Add(Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.HungerRecovered, 0,
                    "來源：本週庫存與採收足以滿足全部需求"), debtSourceEventId));
            }

            // ⑧ 遷徙（V0-06A）：本週缺糧、倖存人數達 TEMP 門檻、且已知一個有糧且可走到的聚落時，
            // 一部分人帶著等比例的糧食與飢餓債組成隊伍出發（原子出發；本週停在來源格）。
            // 沒有已知可達的目的地就留下（45E MIG_NO_TARGET）；這不是人口自動平衡器。
            long emigrants = 0, foodOut = 0, debtOut = 0;
            // 第 08-4 章【TEMP 規則】希望時代：已在路上前往某聚落的人數佔用它的來源餘量（同一週多個聚落不會同時湧入同一村）。
            long CommittedTo(int destinationId)
                => checked(nextMigrations.Where(b => !b.Founding && b.ToSettlementId == destinationId).Sum(b => b.People)
                    * config.FoodPerPersonPerWeek);
            // 第 08-1 章：人類採集世界在挨餓之前就分出一戶開拓（擁擠外移，只開拓新聚落、不搬進別的聚落）。
            var slack = slackBySettlement.GetValueOrDefault(settlement.Id);
            var crowded = map is not null && config.MigrationEnabled && config.FoundingEnabled
                && config.SharedCatchmentEnabled && config.HumanLifecycleEnabled
                && DemoCrowding.Crowded(shortage, demand, survivors, slack);
            var wantsToLeave = map is not null && config.MigrationEnabled && (shortage > 0 || crowded)
                && survivors >= config.MinPopulationToEmigrate;
            var target = wantsToLeave && !crowded
                // 第 08-3 章：希望時代只搬去「已知來源餘量養得起這批人」的聚落，避免缺糧者湧入別村、別村再缺糧外移的連鎖。
                ? ChooseDestination(map!, weekSites, settlement, week, publishedWeeks, config,
                    config.HopeEra ? checked(survivors / config.EmigrationShareDivisor * config.FoodPerPersonPerWeek) : 0,
                    config.HopeEra ? CommittedTo : null)
                : null;
            if (wantsToLeave && target is null && config.FoundingEnabled
                && !nextMigrations.Any(b => b.Founding && b.FromSettlementId == settlement.Id)
                && (crowded || AllReachableNewsArrived(map!, weekSites, settlement, week, publishedWeeks, config)))
            {
                // V0-06B：已知範圍內每個可達聚落的消息都已傳到、沒有一個有糧（或根本沒有可達聚落）→
                // 改派開拓隊到附近 TEMP 範圍內有水源的空格（不能是聚落，也不能是別支開拓隊的目的格）。
                // 每個聚落同時最多一支開拓隊在路上（TEMP）。
                // 第 07 章 c 節：選址另須可及至少一個未被佔用的自然來源（非水域、可取水已由 FindFoundingSite 檢查；
                // 主水道是水域，不能建村）。遺跡格不能直接重建【TEMP】。
                var occupiedNow = weekSites.OccupiedCells();
                // 第 08-1 章：人類採集世界的開拓隊只選可持續餘量養得起預計人數的空地，並挑餘量最大者。
                var partyLimit = crowded ? survivors / DemoCrowding.TempCrowdingShareDivisor : survivors / config.EmigrationShareDivisor;
                var scoreSites = config.SharedCatchmentEnabled && config.HumanLifecycleEnabled;
                bool SiteOk(DemoCellRef c) => weekSites.SettlementAt(c.Column, c.Row) is null
                        && (land is null || land.StateOf(c) == DemoLandDevelopment.Undeveloped) // 第 08-6 章 c 節：不在開墾地建村
                        && !nextMigrations.Any(b => b.Founding && b.Destination == c)
                        && (!config.SharedCatchmentEnabled || DemoForagingWorld.ReachesFreeSource(map!, occupiedNow, c))
                        && (!scoreSites || spareMap.Value.GetValueOrDefault(c) >= checked(partyLimit * config.FoodPerPersonPerWeek));
                var site = DemoMigrationRoutes.FindFoundingSite(map!, new DemoCellRef(settlement.Column, settlement.Row),
                    config.FoundingSearchMaxSteps, SiteOk, scoreSites ? c => spareMap.Value.GetValueOrDefault(c) : null);
                // 第 08-4 章【TEMP 規則】希望時代：附近找不到空地時，缺糧或擁擠的聚落願意走得更遠（婚嫁範圍）去開拓，
                // 不會一直困在養不起的地方。
                if (site.Length < 2 && config.HopeEra && config.FoundingSearchMaxSteps < DemoMateTempSettings.MateRouteMaxSteps)
                    site = DemoMigrationRoutes.FindFoundingSite(map!, new DemoCellRef(settlement.Column, settlement.Row),
                        DemoMateTempSettings.MateRouteMaxSteps, SiteOk, scoreSites ? c => spareMap.Value.GetValueOrDefault(c) : null);
                emigrants = site.Length >= 2 ? PlanEmigrants(settlement.Id, deaths, partyLimit, DemoEmigrationMode.Founding) : 0;
                if (emigrants > 0)
                {
                    foodOut = checked(foodClosing * emigrants) / survivors;
                    debtOut = checked(debt * emigrants) / survivors;
                    var destination = site[site.Length - 1];
                    var batch = new DemoMigrationBatch(
                        DemoEntityIds.NextInt(ids), settlement.Id, settlement.Name,
                        0, $"新聚落預定地{destination}", site,
                        StepIndex: 0, People: emigrants, Food: foodOut, HungerDebt: debtOut, DepartedWeek: week,
                        NewsWeek: 0, Founding: true);
                    nextMigrations.Add(batch);

                    events.Add(Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.Emigration, emigrants,
                        (crowded
                            ? $"人口接近附近來源上限：本週取用來源只剩 {slack}（＜需求 {demand} 的 TEMP {DemoCrowding.TempCrowdingSlackPercent}%），"
                              + "在挨餓之前分出一戶，"
                            : $"本週缺糧 {shortage}；TEMP {config.KnownRouteMaxSteps} 格內可達聚落的消息都已傳到、沒有一個有糧，")
                        + $"前往 {destination} 開拓新聚落（TEMP 選址：{config.FoundingSearchMaxSteps} 格內有水源的空地），"
                        + $"路線 {site.Length - 1} 格（TEMP 每週 1 格），攜帶糧食 {foodOut}",
                        batch.Id), crowded ? 0 : hungerEventId));
                    migrationLedgers.Add(new DemoMigrationWeekLedger(
                        week, batch.Id, settlement.Id, 0,
                        batch.Cell, batch.Cell, Departed: true, Arrived: false,
                        emigrants, 0, emigrants,
                        foodOut, 0, foodOut, 0,
                        debtOut, 0, 0, debtOut, Founding: true));
                }
            }
            else if (target is { } chosen)
            {
                emigrants = PlanEmigrants(settlement.Id, deaths, survivors / config.EmigrationShareDivisor);
                if (emigrants > 0)
                {
                    foodOut = checked(foodClosing * emigrants) / survivors;
                    debtOut = checked(debt * emigrants) / survivors;
                    var batch = new DemoMigrationBatch(
                        DemoEntityIds.NextInt(ids), settlement.Id, settlement.Name,
                        chosen.Settlement.Id, chosen.Settlement.Name, chosen.Route,
                        StepIndex: 0, People: emigrants, Food: foodOut, HungerDebt: debtOut, DepartedWeek: week,
                        NewsWeek: chosen.NewsWeek);
                    nextMigrations.Add(batch);

                    events.Add(Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.Emigration, emigrants,
                        $"本週缺糧 {shortage}；依第 {chosen.NewsWeek} 週傳來的消息前往有糧的{chosen.Settlement.Name}"
                        + $"（消息沿路傳 {chosen.Route.Length - 1} 格，TEMP 每格 {config.NewsDelayWeeksPerCell} 週），"
                        + $"路線 {chosen.Route.Length - 1} 格（TEMP 每週 1 格），攜帶糧食 {foodOut}",
                        batch.Id), hungerEventId));
                    migrationLedgers.Add(new DemoMigrationWeekLedger(
                        week, batch.Id, settlement.Id, chosen.Settlement.Id,
                        batch.Cell, batch.Cell, Departed: true, Arrived: false,
                        emigrants, 0, emigrants,
                        foodOut, 0, foodOut, 0,
                        debtOut, 0, 0, debtOut));
                }
            }
            // 第 08-1 章：本週沒有人因缺糧或擁擠實際出發，才考慮婚嫁遷移（擁擠但湊不到兩對伴侶開拓時，仍可婚嫁）。
            if (emigrants == 0 && map is not null && config.MigrationEnabled && week >= 2 && survivors >= 2
                     && mates is not null && mates.TryGetValue(settlement.Id, out var market) && market.SeekerSex is { } seekerSex
                     && !nextMigrations.Any(b => b.FromSettlementId == settlement.Id && b.DepartedWeek == week))
            {
                // 第 08-1 章【TEMP 規則】婚嫁遷移：找不到伴侶的育齡單身者，一個人走到 TEMP 12 格內、
                // 有未配對成年異性的最近聚落；目的地也有異性尋偶者時只由編號大的聚落一方搬（避免兩邊對調）。
                (DemoSettlement Settlement, ImmutableArray<DemoCellRef> Route)? mateTarget = null;
                foreach (var candidate in weekSites.Settlements.Where(c => c.Id != settlement.Id && !c.IsRuin).OrderBy(c => c.Id))
                {
                    if (!mates.TryGetValue(candidate.Id, out var other)
                        || (seekerSex == DemoTempSex.Female ? other.UnpairedMen : other.UnpairedWomen) == 0)
                        continue;
                    if (other.SeekerSex is { } theirs && theirs != seekerSex && candidate.Id > settlement.Id)
                        continue; // 對方會搬過來
                    var route = DemoMigrationRoutes.FindRoute(map,
                        new DemoCellRef(settlement.Column, settlement.Row), new DemoCellRef(candidate.Column, candidate.Row));
                    if (route.Length < 2 || route.Length - 1 > DemoMateTempSettings.MateRouteMaxSteps)
                        continue;
                    // 第 08-4 章【TEMP 規則】希望時代：只嫁娶到已知來源餘量還養得起一個人的聚落（扣掉已在路上的人）。
                    if (config.HopeEra)
                    {
                        var news = DemoMigrationNews.Received(publishedWeeks, week, candidate.Id, route.Length - 1, config.NewsDelayWeeksPerCell);
                        if (news is null || news.Value.Ledger.SourceSlack - CommittedTo(candidate.Id) < config.FoodPerPersonPerWeek)
                            continue;
                    }
                    if (mateTarget is null || route.Length < mateTarget.Value.Route.Length)
                        mateTarget = (candidate, route);
                }

                if (mateTarget is { } mate && PlanEmigrants(settlement.Id, deaths, 1, DemoEmigrationMode.Mate) == 1)
                {
                    emigrants = 1;
                    // 路糧：帶足全程（每格一週份，TEMP），存糧不夠就帶光。
                    foodOut = Math.Min(foodClosing, checked(config.FoodPerPersonPerWeek * (mate.Route.Length - 1)));
                    debtOut = debt / survivors;
                    var batch = new DemoMigrationBatch(
                        DemoEntityIds.NextInt(ids), settlement.Id, settlement.Name,
                        mate.Settlement.Id, mate.Settlement.Name, mate.Route,
                        StepIndex: 0, People: 1, Food: foodOut, HungerDebt: debtOut, DepartedWeek: week,
                        NewsWeek: week - 1);
                    nextMigrations.Add(batch);
                    events.Add(Stamp(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.Emigration, 1,
                        $"婚嫁遷移：聚落裡沒有可配對的成年{(seekerSex == DemoTempSex.Female ? "男性" : "女性")}（只剩手足或同性），"
                        + $"前往有未配對成年{(seekerSex == DemoTempSex.Female ? "男性" : "女性")}的{mate.Settlement.Name}"
                        + $"，路線 {mate.Route.Length - 1} 格，攜帶路糧 {foodOut}",
                        batch.Id), 0));
                    migrationLedgers.Add(new DemoMigrationWeekLedger(
                        week, batch.Id, settlement.Id, mate.Settlement.Id,
                        batch.Cell, batch.Cell, Departed: true, Arrived: false,
                        1, 0, 1,
                        foodOut, 0, foodOut, 0,
                        debtOut, 0, 0, debtOut, MateSeeking: true));
                }
            }

            arrivals.TryGetValue(settlement.Id, out var arrived);
            var populationClosing = checked(survivors - emigrants + arrived.People);
            var fromHunting = huntedFood?.GetValueOrDefault(settlement.Id) ?? 0; // 第 08-9 章 C1：本週獵得的草食獸
            var foodFinal = checked(foodClosing - foodOut + arrived.Food + fromDead + fromHunting);
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
                HungerDebtCarriedIn: arrived.Debt, HungerDebtCarriedOut: debtOut,
                AdultWorkersOpening: adultWorkers?.GetValueOrDefault(settlement.Id) ?? people,
                FoodSpoiled: spoiled, WaterShortage: waterShortage, WaterCarriers: waterPlan.Carriers,
                SourceSlack: slackBySettlement.GetValueOrDefault(settlement.Id),
                ToolYieldPercent: config.SharedCatchmentEnabled ? toolYieldPercent?.Invoke(settlement.Id) ?? 0 : 0,
                FoodFromDead: fromDead, FoodFromHunting: fromHunting));
        }

        if (arrivals.Keys.Any(id => weekSites.Settlements.All(s => s.Id != id)))
            throw new InvalidOperationException("A migration batch arrived at an unknown settlement.");

        // 第 07 章 c 節：人口連續為 0 滿 TEMP 週數的聚落成為遺跡（仍有隊伍正前往時延後）。
        var ruinRule = DemoSettlementTempSettings.Current;
        foreach (var ledger in ledgers)
        {
            var place = weekSites.Settlements.Single(s => s.Id == ledger.SettlementId);
            if (place.IsRuin || !ruinRule.EmptyLongEnough(place.Id, week, ledger.PopulationClosing, publishedWeeks)
                || nextMigrations.Any(b => b.ToSettlementId == place.Id))
                continue;
            weekSites = weekSites.WithRuin(place.Id, week);
            events.Add(Stamp(new DemoSettlementEvent(week, worldTime, place.Id, place.Name,
                DemoSettlementEventKind.SettlementRuined, ruinRule.RuinAfterEmptyWeeks,
                $"人口連續 {ruinRule.RuinAfterEmptyWeeks} 週為 0（TEMP），聚落成為遺跡；該格恢復自然產出"), 0));
        }

        var report = new DemoWeekReport(week, worldTime, Array.AsReadOnly(ledgers.ToArray()),
            Array.AsReadOnly(events.ToArray()),
            Array.AsReadOnly(migrationLedgers.OrderBy(m => m.BatchId).ToArray()))
        {
            EntityIdNextOpening = entityIdOpening,
            EntityIdNextClosing = DemoEntityIds.NextValue(ids.Capture()),
            Sources = sourceLedgers
        };
        Verify(report);
        return new DemoWeeklyOutcome(report, nextReserves.ToImmutable(), nextFood.ToImmutable(),
            nextPopulation.ToImmutable(), nextDebt.ToImmutable(), nextMigrations.ToImmutable(), ids.Capture(),
            weekSites) { SourceStocks = nextSourceStocks };
    }

    /// <summary>
    /// 第 04 章 c 節：來源帳內部一致——每源 Σ份額＋未採＝預算、皆非負、同源不重複聚落、依編號排序；
    /// 有來源帳時（共享模式）每個聚落的 Harvested＝Regenerated＝它從各來源分到的合計。
    /// </summary>
    private static void VerifySources(DemoWeekReport report)
    {
        var sources = report.Sources ?? Array.Empty<DemoSourceWeekLedger>();
        if (sources.Count == 0)
            return;
        var previousId = 0;
        foreach (var source in sources)
        {
            if (source is null || source.Shares is null)
                throw new InvalidOperationException($"第 {report.Week} 週來源帳有空白項目。");
            if (source.Week != report.Week || source.SourceId <= previousId)
                throw new InvalidOperationException($"第 {report.Week} 週來源 #{source.SourceId} 的週次或排序不符。");
            previousId = source.SourceId;
            if (source.FoodAvailable <= 0 || source.Unharvested < 0 || source.Shares.Any(x => x is null || x.Food <= 0))
                throw new InvalidOperationException($"第 {report.Week} 週來源 #{source.SourceId} 的預算、份額或未採為負或為 0。");
            if (source.Shares.Select(x => x.SettlementId).Distinct().Count() != source.Shares.Count
                || !source.Shares.Select(x => x.SettlementId).SequenceEqual(source.Shares.Select(x => x.SettlementId).Order()))
                throw new InvalidOperationException($"第 {report.Week} 週來源 #{source.SourceId} 的份額聚落重複或未排序。");
            if (checked(source.Shares.Sum(x => x.Food) + source.Unharvested) != source.FoodAvailable)
                throw new InvalidOperationException($"第 {report.Week} 週來源 #{source.SourceId} 不守恆：份額＋未採≠預算。");
            if (source.StockOpening < 0 || source.Regenerated < 0
                || checked(source.StockOpening + source.Regenerated) != source.FoodAvailable)
                throw new InvalidOperationException($"第 {report.Week} 週來源 #{source.SourceId} 不守恆：期初存量＋再生≠本週可採。");
        }
        var shares = sources.SelectMany(x => x.Shares).GroupBy(x => x.SettlementId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Food));
        if (shares.Keys.Any(id => report.Settlements.All(l => l.SettlementId != id)))
            throw new InvalidOperationException($"第 {report.Week} 週來源帳分給了不在週帳裡的聚落。");
        foreach (var ledger in report.Settlements)
        {
            var total = shares.GetValueOrDefault(ledger.SettlementId);
            if (ledger.Harvested != total || ledger.Regenerated != total)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 的採收 {ledger.Harvested}／轉入 {ledger.Regenerated} 與來源帳合計 {total} 不符。");
        }
    }

    /// <summary>
    /// V0-06B SETTLE_KNOW：開拓前，來源對 TEMP 已知範圍內每個陸路可達的聚落都已經收到消息
    /// （本週才建立、消息還沒傳到的聚落也算「還不知道」）。有任何一個還沒有消息就先等，不冒然開拓；
    /// 範圍內根本沒有可達聚落則視為已知。純函式、只讀已發布週報。
    /// </summary>
    private static bool AllReachableNewsArrived(
        DemoHexWorld map, DemoWorldSites sites, DemoSettlement source, int week,
        IReadOnlyList<DemoWeekReport> publishedWeeks, DemoWeeklyTempConfig config)
    {
        foreach (var candidate in sites.Settlements.OrderBy(s => s.Id))
        {
            if (candidate.Id == source.Id || candidate.IsRuin)
                continue;
            var route = DemoMigrationRoutes.FindRoute(map,
                new DemoCellRef(source.Column, source.Row), new DemoCellRef(candidate.Column, candidate.Row));
            if (route.Length < 2 || route.Length - 1 > config.KnownRouteMaxSteps)
                continue;
            if (DemoMigrationNews.Received(publishedWeeks, week, candidate.Id, route.Length - 1,
                    config.NewsDelayWeeksPerCell) is null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 45E MIG_PULL：只考慮「已經傳到來源聚落」的消息（<see cref="DemoMigrationNews"/>）：
    /// 路程 d 格的目的地，來源在第 W 週只知道它第 W－d×延遲 週的週報；該週報中目的地沒有缺糧、
    /// 沒有飢餓債、仍有人，且 TEMP 路線長度不超過已知範圍，才是候選。取路線最短者，同長取編號小者。
    /// 第一週沒有任何消息，所以不會遷徙；沒有陸路就沒有消息（MIG_NO_TARGET）。
    /// </summary>
    private static (DemoSettlement Settlement, ImmutableArray<DemoCellRef> Route, int NewsWeek)? ChooseDestination(
        DemoHexWorld map, DemoWorldSites sites, DemoSettlement source, int week,
        IReadOnlyList<DemoWeekReport> publishedWeeks, DemoWeeklyTempConfig config, long minSlack = 0,
        Func<int, long>? committed = null)
    {
        (DemoSettlement Settlement, ImmutableArray<DemoCellRef> Route, int NewsWeek)? best = null;
        foreach (var candidate in sites.Settlements.OrderBy(s => s.Id))
        {
            if (candidate.Id == source.Id || candidate.IsRuin) // 第 07 章 c 節：遺跡不是遷徙目的地
                continue;

            var route = DemoMigrationRoutes.FindRoute(map,
                new DemoCellRef(source.Column, source.Row), new DemoCellRef(candidate.Column, candidate.Row));
            if (route.Length < 2 || route.Length - 1 > config.KnownRouteMaxSteps)
                continue;

            var news = DemoMigrationNews.Received(publishedWeeks, week, candidate.Id, route.Length - 1,
                config.NewsDelayWeeksPerCell);
            if (news is null || !DemoMigrationNews.SaysFed(news.Value.Ledger))
                continue;
            if (minSlack > 0 && news.Value.Ledger.SourceSlack - (committed?.Invoke(candidate.Id) ?? 0) < minSlack) // 第 08-3 章【TEMP 規則】；第 08-4 章扣掉已在路上的人
                continue;
            if (best is null || route.Length < best.Value.Route.Length)
                best = (candidate, route, news.Value.Week);
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
        VerifySources(report);
        foreach (var ledger in report.Settlements)
        {
            if (!ledger.IsBalanced)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週 {ledger.SettlementName} 帳目不平衡（ledger imbalance）。");

            // SURV_DEATH：死亡必須有原因。帳上的死亡數必須等於帶原因的餓死事件人數。
            // 第 07 章 b 節：途中隊伍的出生／自然死亡事件帶隊伍編號，記在隊伍帳，不算聚落的。
            var deathEvents = report.Events
                .Where(e => e.SettlementId == ledger.SettlementId && e.MigrationBatchId == 0 && e.Kind is
                    DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.NaturalDeath)
                .Sum(e => e.Count);
            var naturalEvents = report.Events
                .Where(e => e.SettlementId == ledger.SettlementId && e.MigrationBatchId == 0
                    && e.Kind == DemoSettlementEventKind.NaturalDeath).Sum(e => e.Count);
            var birthEvents = report.Events
                .Where(e => e.SettlementId == ledger.SettlementId && e.MigrationBatchId == 0
                    && e.Kind == DemoSettlementEventKind.Birth).Sum(e => e.Count);
            if (deathEvents != ledger.Deaths || naturalEvents != ledger.NaturalDeaths
                || birthEvents != ledger.Births
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
            // V0-06B：開拓隊抵達前沒有目的聚落（ToSettlementId＝0）；抵達那一週必須指向它建立的聚落。
            var destinationOk = m.Founding && !m.Arrived
                ? m.ToSettlementId == 0
                : settlementIds.Contains(m.ToSettlementId);
            if (!m.IsBalanced || !settlementIds.Contains(m.FromSettlementId) || !destinationOk
                || m.FromSettlementId == m.ToSettlementId)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週遷徙隊伍 #{m.BatchId} 帳目不平衡或瞬移（M1_MIG）。");

            var migrantDeaths = report.Events
                .Where(e => e.MigrationBatchId == m.BatchId && e.Kind is DemoSettlementEventKind.MigrantStarvationDeath or DemoSettlementEventKind.MigrantKilledByBeasts)
                .ToList();
            if (migrantDeaths.Sum(e => e.Count) != m.Deaths || migrantDeaths.Any(e => string.IsNullOrWhiteSpace(e.Cause))
                || migrantDeaths.Where(e => e.Kind == DemoSettlementEventKind.MigrantKilledByBeasts).Sum(e => e.Count) != m.BeastDeaths)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週遷徙隊伍 #{m.BatchId} 途中死亡沒有對應的帶原因事件（death without cause）。");
            var travelBirths = report.Events.Where(e => e.MigrationBatchId == m.BatchId && e.Kind == DemoSettlementEventKind.Birth).Sum(e => e.Count);
            var travelNatural = report.Events.Where(e => e.MigrationBatchId == m.BatchId && e.Kind == DemoSettlementEventKind.NaturalDeath).Sum(e => e.Count);
            if (travelBirths != m.Births || travelNatural != m.NaturalDeaths)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週遷徙隊伍 #{m.BatchId} 途中出生／自然死亡沒有對應的事件。");
        }

        if (migrations.Select(m => m.BatchId).Distinct().Count() != migrations.Count)
            throw new InvalidOperationException($"第 {report.Week} 週遷徙隊伍編號重複。");

        // 45G M1_SETTLE：新聚落不憑空。每個「建立新聚落」事件都對應一支本週抵達的開拓隊，
        // 新聚落的期初人口／糧食／飢餓債／資源點存量全部為 0，遷入＝隊伍抵達人數；
        // 第 08-1 章：建村當週隊伍裡的孕婦可以分娩（出生另由帶原因的出生事件對帳），不再要求出生為 0；
        // 每支抵達的開拓隊都建立了一個聚落（一對一）。糧食與飢餓債由上面的兩端對帳檢查。
        var foundedEvents = report.Events.Where(e => e.Kind == DemoSettlementEventKind.SettlementFounded).ToList();
        var foundingArrivals = migrations.Where(m => m.Founding && m.Arrived).ToList();
        if (foundedEvents.Count != foundingArrivals.Count
            || foundedEvents.Select(e => e.SettlementId).Distinct().Count() != foundedEvents.Count)
            throw new InvalidOperationException($"第 {report.Week} 週新聚落與開拓隊對不上（M1_SETTLE）。");
        foreach (var founded in foundedEvents)
        {
            var batch = foundingArrivals.SingleOrDefault(m => m.BatchId == founded.MigrationBatchId);
            var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == founded.SettlementId);
            if (batch is null || ledger is null || batch.ToSettlementId != founded.SettlementId
                || string.IsNullOrWhiteSpace(founded.Cause)
                || founded.Count != batch.PopulationClosing || founded.Count <= 0
                || ledger.PopulationOpening != 0 || ledger.FoodOpening != 0 || ledger.HungerDebtOpening != 0
                || ledger.ReserveOpening != 0
                || ledger.Immigrants != batch.PopulationClosing)
                throw new InvalidOperationException(
                    $"第 {report.Week} 週新聚落 {founded.SettlementName} 不是完全由開拓隊建立（M1_SETTLE：新聚落不憑空）。");
        }

        // M1_POP（世界）：聚落人口＋途中人口在一週內只因出生與死亡改變；途中人口只計一次。
        var opening = report.Settlements.Sum(l => l.PopulationOpening)
            + migrations.Where(m => !m.Departed).Sum(m => m.PopulationOpening);
        var closing = report.Settlements.Sum(l => l.PopulationClosing)
            + migrations.Where(m => !m.Arrived).Sum(m => m.PopulationClosing);
        var births = report.Settlements.Sum(l => l.Births) + migrations.Sum(m => m.Births);
        var deaths = report.Settlements.Sum(l => l.Deaths) + migrations.Sum(m => m.Deaths + m.NaturalDeaths);
        if (opening + births - deaths != closing)
            throw new InvalidOperationException(
                $"第 {report.Week} 週世界人口不守恆：{opening}＋{births}－{deaths}≠{closing}（含途中人口）。");
    }
}
