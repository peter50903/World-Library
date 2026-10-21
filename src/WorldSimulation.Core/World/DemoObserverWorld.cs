using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.History;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.Scheduling;
using WorldSimulation.Core.Time;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// Explicitly NON-CANONICAL demonstration fixture: only the TEMP world seed.
/// C05（P1.0.04-a，使用者 2026-10-14 裁決）：舊 V0-01「單一虛擬村莊每日糧食」整條退役，
/// 不再有每日人數、初始糧與每日收穫上限；世界只剩多聚落每週結算。
/// </summary>
public sealed record DemoScenario(ulong Seed)
{
    public static DemoScenario Sample(ulong seed) => new(seed);
}

/// <summary>C05：推進一日的結果；有週結算時附上該週週報（摘要見 <see cref="DemoWeekReport.Summary"/>）。</summary>
public sealed record DemoDayAdvance(int Day, TimeSpan WorldTime, DemoWeekReport? SettledWeek);

/// <summary>C05：世界時鐘狀態；<see cref="DaysElapsed"/> 由世界時間換算（不再是每日帳筆數）。</summary>
public sealed record DemoWorldSnapshot(TimeSpan WorldTime, int DaysElapsed);

/// <summary>
/// Runnable, headless Observer vertical slice driven by the existing WorldClock, Scheduler and
/// deterministic RNG (C05: the daily tick only advances the clock; no daily food line),
/// with (V0-04A) a TEMP weekly L2 settlement for every demo settlement:
/// resource regeneration → labour and production → consumption, with a resource
/// ledger and a population ledger checked before anything is published, and (V0-04B)
/// hunger debt → starvation death with append-only, caused settlement events, and (V0-06A)
/// starving settlements sending migrant batches cell by cell to a known fed settlement, and (V0-06B)
/// founding batches that turn their survivors into a new settlement on an empty cell.
/// 第 03 章 a 節：每一天經 <see cref="DemoWorldCommit"/> 以固定順序提交（排程 → 亂數 → 帳本 → 生死 → 歷史 → 發布），
/// 五個故障檢查點見 <see cref="DemoCommitStage"/>；正式狀態只在發布步驟改變。
/// It does NOT implement official population rules, EntityStore, canonical History or durability.
/// </summary>
public sealed partial class DemoObserverWorld
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
    private const string DailyWorkKey = "DEMO_DAY_TICK"; // C05：只推進時鐘，不做帳。
    private const string WeeklyWorkKey = "DEMO_WEEKLY_SETTLEMENT";

    private readonly WorldRecipe _recipe;
    private readonly DemoWeeklyTempConfig _weekly;
    private DemoTempDemography? _demography;
    /// <summary>第 08 章 a 節：土地開發狀態（與地形分離）。目前沒有開墾行動，永遠是空的。</summary>
    private DemoLandDevelopmentMap _land = DemoLandDevelopmentMap.Empty;

    // 下列欄位只在 DemoWorldCommit.PublishTo（AdvanceOneDay 的發布步驟）或建立／讀檔安裝時改變。
    private WorldClock _clock = new();
    private Scheduler _scheduler;
    private RngRegistry _rng;
    private ImmutableList<DemoWeekReport> _weeks = ImmutableList<DemoWeekReport>.Empty;
    private ImmutableSortedDictionary<int, long> _reserves;
    private ImmutableSortedDictionary<int, long> _settlementFood;
    private ImmutableSortedDictionary<int, long> _population;
    private ImmutableSortedDictionary<int, long> _hungerDebt;
    private ImmutableList<DemoMigrationBatch> _migrations = ImmutableList<DemoMigrationBatch>.Empty;
    // C04（P1.0.03-a；44H SAVE_ID）：世界唯一實體 ID 分配器的已提交快照。所有實體（聚落、資源點、人口群體、
    // 遷徙隊伍、TEMP 人類個體）共用一個 ID 空間；週內在私有副本上配號，只在發布時寫回。
    private EntityIdAllocatorSnapshot _entityIds = DemoEntityIds.Fresh;
    private DemoWorldSites _sites;
    // 第 03 章 b 節：正式追加式史庫，只經 DemoWorldCommit.StageHistory→PublishTo 增加。
    private EventHistory _history = EventHistory.Empty;
    // 第 06 章 c 節：共享採集各來源的期末存量（只列＞0）。
    private ImmutableSortedDictionary<int, long> _sourceStocks = ImmutableSortedDictionary<int, long>.Empty;

    public DemoObserverWorld(DemoScenario scenario)
        : this(scenario, DemoWeeklyTempConfig.Sample)
    {
    }

    /// <summary>以世界種子建立（歷史種子由世界種子衍生）；等同 <see cref="WorldRecipe.ForSeeds"/>(scenario.Seed)。</summary>
    public DemoObserverWorld(DemoScenario scenario, DemoWeeklyTempConfig weekly)
        : this(WorldRecipe.ForSeeds((scenario ?? throw new ArgumentNullException(nameof(scenario))).Seed), weekly)
    {
    }

    /// <summary>
    /// 第 02 章 a 節：依世界配方建立。世界種子決定地圖、聚落與初始人口；歷史種子（輸入或衍生）
    /// 只影響之後的歷史亂數流（41A SEED_WORLD、SEED_HISTORY）。
    /// </summary>
    public DemoObserverWorld(WorldRecipe recipe, DemoWeeklyTempConfig weekly)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(weekly);
        weekly.Validate();

        _recipe = recipe;
        _weekly = weekly;
        // V0-03B: ONE world RngRegistry. The TEMP map (GEOGRAPHY) and TEMP sites (ECOLOGY_INIT only, C05)
        // are generated once here and cached. C05: nothing draws RNG at run time (RESOURCE is never
        // drawn after the opening; POP is reserved for birth/death, 41A SEED_POP).
        _rng = RngRegistry.Create(recipe.WorldSeed, recipe.HistorySeedInput);
        Map = DemoHexWorld.Generate(_rng);
        var openingIds = new EntityIdAllocator();
        _sites = DemoWorldSites.Generate(Map, _rng,
            weekly.CrowdedForaging
                ? DemoSitesTempConfig.Sample with { MinPopGroupSize = 40, MaxPopGroupSize = 48 }
                // 第 08-1 章【TEMP】：人類採集世界每個開局聚落 10～14 人（含三對育齡伴侶，見 DemoTempDemography.OpeningPerson）。
                // 診斷：6～10 人時小世界常因性別比例的隨機波動斷代；12～18 人時開局就超過附近來源，第 1 年大饑荒。
                : weekly.HumanLifecycleEnabled
                ? DemoSitesTempConfig.Sample with { MinPopGroupSize = 10, MaxPopGroupSize = 14 }
                : DemoSitesTempConfig.Sample,
            weekly.SharedCatchmentEnabled).WithOpeningEntityIds(openingIds);

        // V0-04A TEMP opening balances: every resource point starts with the same reserve,
        // every settlement with InitialFoodPerPerson per member of its population groups.
        // V0-06A TEST scenario: the TEMP local-famine settlement starts with nothing.
        _reserves = Sites.ResourcePoints.ToImmutableSortedDictionary(
            p => p.Id, p => weekly.IsFamineSettlement(p.SettlementId) || p.Kind == DemoResourceKind.Uncultivated
                ? 0L : weekly.InitialReservePerPoint); // 第 08 章 a 節：未開發草原沒有糧食存量
        _population = Sites.Settlements.ToImmutableSortedDictionary(
            s => s.Id, s => Sites.PopGroupsOf(s.Id).Sum(p => (long)p.Size));
        _settlementFood = _population.ToImmutableSortedDictionary(
            kv => kv.Key,
            kv => weekly.IsFamineSettlement(kv.Key) ? 0L : checked(kv.Value * weekly.InitialFoodPerPerson));
        _hungerDebt = _population.ToImmutableSortedDictionary(kv => kv.Key, _ => 0L);
        if (weekly.HumanLifecycleEnabled)
            _demography = DemoTempDemography.Open(
                _population.ToDictionary(p => p.Key, p => checked((int)p.Value)), DemoHumanLifecycle.Rules, openingIds,
                new DemoOpeningDraws(_rng)); // 第 07 章 a 節：混齡開局（世界種子 ECOLOGY_INIT 流，不推進計數器）
        _entityIds = openingIds.Capture();

        _scheduler = new Scheduler(_clock);
        _scheduler.Schedule(OneDay, DailyWorkKey);
        _scheduler.Schedule(OneWeek, WeeklyWorkKey);
    }

    /// <summary>第 02 章 a 節：本世界的配方（種子、規則版本、設定雜湊、程式版本）。</summary>
    public WorldRecipe Recipe => _recipe;

    /// <summary>本世界使用的 TEMP 生存情境（以新種子重建同情境世界時沿用）。</summary>
    public DemoWeeklyTempConfig WeeklyConfig => _weekly;

    /// <summary>The cached TEMP hex map generated from this world's registry (never regenerated).</summary>
    public DemoHexWorld Map { get; }

    /// <summary>第 08 章 a 節：土地開發狀態（未開發的格不列出）。</summary>
    public DemoLandDevelopmentMap Land => _land;

    /// <summary>
    /// The TEMP settlements / resource points / population groups. Generated once from the seed;
    /// V0-06B: settlements founded by migrant batches are appended in the weekly commit (never removed,
    /// IDs never reused). Each call returns the committed, immutable copy.
    /// </summary>
    public DemoWorldSites Sites => _sites;

    /// <summary>C04：世界唯一實體 ID 分配器的下一號（已提交狀態）；任何現存或曾存在的實體 ID 都小於它。</summary>
    public ulong EntityIdNextValue => _entityIds.NextValue;

    /// <summary>TEST ONLY: invoked at each commit checkpoint so tests can inject a mid-commit failure.</summary>
    internal Action<DemoCommitStage>? FaultInjector { get; set; }

    public DemoWorldSnapshot Snapshot() => new(_clock.Now, DaysOf(_clock.Now));

    /// <summary>C05：十條亂數流的已提交狀態（唯讀副本，依 <see cref="RngStreams.All"/> 順序；給 Godot／CLI 比對）。</summary>
    public IReadOnlyList<RngStreamState> RngState() => _rng.Capture().Streams.ToArray();

    private static int DaysOf(TimeSpan worldTime) => checked((int)(worldTime.Ticks / OneDay.Ticks));

    /// <summary>第 03 章 b 節：正式追加式史庫（唯讀、不可變；只有提交能追加）。</summary>
    public EventHistory EventHistory => _history;

    /// <summary>All committed weekly settlement reports, oldest first (detached copy).</summary>
    public IReadOnlyList<DemoWeekReport> WeekReports()
        => Array.AsReadOnly(_weeks.ToArray());

    /// <summary>The most recent weekly settlement, or null before the first one (day 7).</summary>
    public DemoWeekReport? LatestWeek => _weeks.IsEmpty ? null : _weeks[_weeks.Count - 1];

    /// <summary>Whether this playable TEMP world uses 20/5 shared forest and river food sources.</summary>
    public bool UsesSharedForaging => _weekly.SharedCatchmentEnabled;

    public IReadOnlyList<DemoCatchmentFoodSource> SharedFoodSources()
        => UsesSharedForaging ? DemoForagingWorld.Sources(Map, _sites, _land) : Array.Empty<DemoCatchmentFoodSource>();

    public bool IsMainRiverCell(int column, int row)
        => UsesSharedForaging && DemoForagingWorld.MainRiverTiles(Map).Contains(new DemoCellRef(column, row));

    /// <summary>
    /// 第 04 章 c 節：最近一週的共享採集分配，讀已提交的來源帳（不重算）。
    /// 未滿足需求＝需求（期初人口×每人消耗）－實採。
    /// </summary>
    public DemoCatchmentFoodResult? LatestSharedHarvest()
    {
        if (!UsesSharedForaging || LatestWeek is not { } week) return null;
        var harvests = week.Sources.SelectMany(x => x.Shares.Select(share => new DemoCatchmentHarvest(x.SourceId, share.SettlementId, share.Food)))
            .ToArray();
        var unmet = week.Settlements.ToDictionary(l => l.SettlementId,
            l => Math.Max(0, checked(l.FoodConsumed + l.FoodShortage) - l.Harvested));
        var unharvested = week.Sources.ToDictionary(x => x.SourceId, x => x.Unharvested);
        return new DemoCatchmentFoodResult(Array.AsReadOnly(harvests), unmet, unharvested);
    }

    /// <summary>第 04 章 c 節：依目前地圖與最近一週期初人口重算共享分配（只供冒煙與測試比對來源帳）。</summary>
    public DemoCatchmentFoodResult? RecomputeLatestSharedHarvest()
    {
        if (!UsesSharedForaging || LatestWeek is not { } week) return null;
        var opening = week.Settlements.ToDictionary(l => l.SettlementId, l => l.PopulationOpening);
        // 第 06 章 c 節：可採量用帳上的期初存量＋再生（存量本身由逐週帳鏈決定）。
        var available = week.Sources.Select(x => new DemoCatchmentFoodSource(x.SourceId, x.Column, x.Row, x.Kind, x.FoodAvailable)).ToArray();
        // 第 06 章 d 節：採集勞力＝成年人－取水者；需求＝本週需求（兒童半份）＝消耗＋缺糧。
        return DemoForagingWorld.Allocate(Map, _sites, opening, _weekly,
            week.Settlements.ToDictionary(l => l.SettlementId,
                l => (l.AdultWorkersOpening < 0 ? l.PopulationOpening : l.AdultWorkersOpening) - l.WaterCarriers), available,
            // 第 08-1 章：人類採集世界的要求量含補存糧（DemoCrowding.Claim，期初存糧＝帳上期初）。
            foodNeeded: week.Settlements.ToDictionary(l => l.SettlementId,
                l => DemoCrowding.Claim(_weekly.HumanLifecycleEnabled, l.FoodConsumed + l.FoodShortage, l.FoodOpening)));
    }

    /// <summary>Current TEMP food stock of a demo settlement.</summary>
    public long SettlementFood(int settlementId) => _settlementFood[settlementId];

    /// <summary>Current TEMP reserve of a demo resource point.</summary>
    public long ResourceReserve(int resourcePointId) => _reserves[resourcePointId];

    /// <summary>
    /// Current TEMP population of a demo settlement. Starts as the sum of its population
    /// groups; V0-04B starvation deaths are subtracted through the weekly population ledger.
    /// </summary>
    public long SettlementPopulation(int settlementId) => _population[settlementId];

    /// <summary>
    /// #103：各聚落「長期資源再生 vs 目前需求」的 TEMP 描述性估計（依聚落編號排序）。純讀取，不改世界、不產生糧食。
    /// </summary>
    private DemoMapCapacity? _mapCapacity;

    /// <summary>第 08-1 章：地圖理論承載上限（TEMP 估計；只由地圖與設定決定，不是世界狀態）。</summary>
    public DemoMapCapacity MapCapacity => _mapCapacity ??= DemoMapCapacity.For(Map, _weekly);

    public IReadOnlyList<DemoSettlementCapacity> SettlementCapacities()
        => DemoCarryingCapacity.Of(_sites, _weekly, SettlementPopulation, ResourceReserve, SettlementFood, SettlementHungerDebt);

    /// <summary>V0-04B: current TEMP hunger debt (unmet food demand not yet recovered).</summary>
    public long SettlementHungerDebt(int settlementId) => _hungerDebt[settlementId];

    /// <summary>
    /// V0-04B (#59): a settlement with people is starving while it carries hunger debt OR its latest
    /// committed week had unmet food demand (45C FOOD_UNMET). Starvation deaths can remove all the
    /// debt in the same week the shortage happened; that week must still read as starving.
    /// </summary>
    public bool IsStarving(int settlementId)
        => _population[settlementId] > 0
        && (_hungerDebt[settlementId] > 0 || LatestWeekShortage(settlementId) > 0);

    /// <summary>V0-04B (#59): unmet food demand of the settlement in the latest committed week (0 before day 7).</summary>
    public long LatestWeekShortage(int settlementId)
        => LatestWeek?.Settlements.FirstOrDefault(l => l.SettlementId == settlementId)?.FoodShortage ?? 0;

    /// <summary>
    /// C07（P1.0.01-b）：觀察者點格查詢。唯讀投影，每次重算；不改世界、不消耗亂數。
    /// 越界拋 <see cref="ArgumentOutOfRangeException"/>（地圖 12×8）。Godot 與 CLI `cell` 共用。
    /// </summary>
    public DemoCellView QueryCell(int column, int row) => DemoCellQuery.Build(this, column, row);

    /// <summary>V0-06A: migrant batches currently travelling between settlements (detached copy).</summary>
    public IReadOnlyList<DemoMigrationBatch> MigrationsInTransit()
        => Array.AsReadOnly(_migrations.ToArray());

    /// <summary>V0-06A: people currently travelling (counted once: not in any settlement).</summary>
    public long PopulationInTransit => _migrations.Sum(b => b.People);

    /// <summary>
    /// #61 TEST hook: run the NEXT week's settlement on the current committed state with a chosen set of
    /// published reports (to prove MIG_PULL uses only news that has arrived). Pure: publishes nothing.
    /// </summary>
    internal DemoWeeklyOutcome SettleNextWeekForTest(IReadOnlyList<DemoWeekReport> publishedWeeks)
    {
        var week = checked(_weeks.Count + 1);
        return DemoWeeklySettlement.Settle(
            week, TimeSpan.FromDays(7L * week), Map, _sites,
            _reserves, _settlementFood, _population, _hungerDebt,
            _migrations, _entityIds, publishedWeeks, _weekly, sourceStocks: _sourceStocks);
    }

    /// <summary>第 06 章 c 節：共享採集各來源目前存量（未採累積；沒有＝0）。</summary>
    public long SourceStock(int sourceId) => _sourceStocks.GetValueOrDefault(sourceId);

    /// <summary>#61: people living in settlements (sum of every settlement's current population).</summary>
    public long SettledPopulation => _population.Values.Sum();

    /// <summary>#61: world population = settled + in transit (each person counted once).</summary>
    public long WorldPopulation => checked(SettledPopulation + PopulationInTransit);

    /// <summary>The actual born/aged human cohort in this TEMP world; null in legacy scenarios.</summary>
    public DemoTempDemographicSnapshot? HumanPopulationSnapshot => _demography?.Snapshot();

    /// <summary>V0-06A: food carried by travelling migrant batches.</summary>
    public long FoodInTransit => _migrations.Sum(b => b.Food);

    /// <summary>
    /// All committed settlement events (V0-04B hunger, starvation death, recovery; V0-06A emigration,
    /// arrival, death on the road), oldest first.
    /// </summary>
    public IReadOnlyList<DemoSettlementEvent> SettlementEvents()
        => Array.AsReadOnly(_weeks.SelectMany(w => w.Events).ToArray());

    /// <summary>
    /// 推進到下一個排程日，整天是一次原子提交（第 03 章 a 節，44F Stage 4～6）：
    /// <see cref="DemoWorldCommit"/> 依固定順序暫存 ①排程與時鐘 → ②亂數 → ③帳本（週結算）→ ④生死 → ⑤歷史（週報），
    /// 最後 ⑥發布（只做欄位指派、不可失敗）。五個故障檢查點：排程後、亂數後、帳本後（生死前）、生死後、發布前；
    /// 非結算日也依序觸發。發布前任何例外都不改實體、亂數、排程、帳本與歷史。
    /// </summary>
    public DemoDayAdvance AdvanceOneDay() => AdvanceOneDayCore(settleWeekly: true);

    /// <summary>測試用：開一個新的提交暫存物件（不推進世界）。</summary>
    internal DemoWorldCommit BeginCommit(bool settleWeekly = true) => new(this, settleWeekly);

    /// <summary>
    /// V0-07B：<paramref name="settleWeekly"/>＝false 只重算一天中由種子決定的部分（時鐘、排程、亂數），
    /// 走同一個提交物件、只略過帳本／生死／歷史三步；週狀態之後由已驗證的存檔安裝。
    /// 目前不抽亂數（C05），兩條路徑得到相同的時鐘、排程與亂數狀態。
    /// </summary>
    private DemoDayAdvance AdvanceOneDayCore(bool settleWeekly)
    {
        var commit = BeginCommit(settleWeekly);
        commit.StageSchedule();
        Checkpoint(DemoCommitStage.AfterScheduleStaged);
        commit.StageRng();
        Checkpoint(DemoCommitStage.AfterRngStaged);

        if (commit.SettlesWeek)
        {
            var outcome = DemoWeeklySettlement.Settle(
                checked(_weeks.Count + 1), commit.DueTime, Map, _sites,
                _reserves, _settlementFood, _population, _hungerDebt,
                _migrations, _entityIds, _weeks, _weekly,
                _demography is null ? null : _sites.Settlements.ToDictionary(s => s.Id,
                    s => (long)_demography.AdultsIn(s.Id, DemoHumanLifecycle.Rules)),
                _sourceStocks,
                // 第 07 章 b 節：人類繁衍世界的外移人數依家庭決定（不拆散家庭）。
                _demography is null ? null : PlanFamilies(_demography, checked(_weeks.Count + 1), new DemoPopulationDraws(_rng)),
                _land,
                _demography is null ? null : MateMarkets(_demography, _sites, checked(_weeks.Count + 1)));
            commit.StageLedger(outcome);
            Checkpoint(DemoCommitStage.AfterLedgerStaged);

            if (_demography is not null)
            {
                // 第 04 章 a 節：生死抽樣只走歷史種子的 POP 流（實體局部亂數，不推進全域流計數器）。
                var lifecycle = DemoHumanLifecycle.Apply(outcome, _demography, new DemoPopulationDraws(_rng),
                    DemoCausalIndex.NextEventId(_weeks));
                commit.StagePopulation(lifecycle.Outcome, lifecycle.Population, lifecycle.Participants);
            }
            else
            {
                commit.StagePopulation(null, null);
            }
            Checkpoint(DemoCommitStage.AfterPopulationStaged);
            commit.StageHistory(commit.Outcome.Report);
        }
        else
        {
            Checkpoint(DemoCommitStage.AfterLedgerStaged);
            Checkpoint(DemoCommitStage.AfterPopulationStaged);
        }

        Checkpoint(DemoCommitStage.BeforePublish);
        commit.PublishTo(this);
        return new DemoDayAdvance(DaysOf(commit.DueTime), commit.DueTime, commit.PublishedWeek);
    }

    private static Func<int, long, long, DemoEmigrationMode, long> PlanFamilies(DemoTempDemography demography, int week, DemoPopulationDraws draws)
        => (settlementId, deaths, limit, mode) => DemoHumanLifecycle.PlanEmigrants(demography, week, settlementId, deaths, limit, draws, mode);

    /// <summary>第 08-1 章：各聚落婚嫁市場（找不到伴侶的單身者性別、未配對成年男女數；以本週開始時的名冊計）。</summary>
    private static IReadOnlyDictionary<int, DemoMateMarket> MateMarkets(DemoTempDemography demography, DemoWorldSites sites, int week)
    {
        var day = week * 7; // 與出發規劃（PlanEmigrants）同一天，選出的人一致
        var rules = DemoHumanLifecycle.Rules;
        return sites.Settlements.ToDictionary(s => s.Id, s =>
        {
            var residents = demography.People.Where(p => p.IsSettled && p.SettlementId == s.Id).ToList();
            return new DemoMateMarket(DemoFamilyMigration.MateSeeker(residents, day, rules)?.Sex,
                DemoFamilyMigration.Unpaired(residents, DemoTempSex.Female, day, rules),
                DemoFamilyMigration.Unpaired(residents, DemoTempSex.Male, day, rules));
        });
    }

    /// <summary>
    /// Deterministic text fingerprint of the WHOLE mutable world state — Entity (stocks and
    /// population), RNG, Scheduler (clock and queue), History and Ledger.
    /// 第 03 章 a 節確認涵蓋：時鐘、排程、亂數與計數器、實體分配器、各帳（存量／糧食／人口／飢餓債）、
    /// 聚落、資源點、遷徙隊伍、人口身分（示範人類個體）、週報（帳、事件、遷徙、亂數計數器、ID 區間）。
    /// 人口群體只在開局由世界種子產生、建村不新增，人類名冊種子＝世界種子，皆由配方決定，不需另列
    /// （指紋寫在存檔裡，多列會讓既有存檔失效）。 Used by tests to
    /// prove that a failed commit changed nothing and that the same seed gives the same world.
    /// </summary>
    internal string StateFingerprint()
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"clock={_clock.Now.Ticks};seq={_scheduler.NextSequence};");
        foreach (var work in _scheduler.PendingInOrder())
            sb.Append(inv, $"work={work.Handle.Value}@{work.DueTime.Ticks}:{work.WorkKey};");
        var rng = _rng.Capture();
        sb.Append(inv, $"rng={rng.WorldSeed}/{rng.HistorySeed};");
        foreach (var stream in rng.Streams)
            sb.Append(inv, $"{stream.StreamId}={stream.State}#{stream.DrawCounter};");
        foreach (var kv in _reserves) sb.Append(inv, $"reserve{kv.Key}={kv.Value};");
        foreach (var kv in _settlementFood) sb.Append(inv, $"sfood{kv.Key}={kv.Value};");
        foreach (var kv in _population) sb.Append(inv, $"pop{kv.Key}={kv.Value};");
        foreach (var kv in _hungerDebt) sb.Append(inv, $"debt{kv.Key}={kv.Value};");
        foreach (var kv in _sourceStocks) sb.Append(inv, $"sstock{kv.Key}={kv.Value};"); // 第 06 章 c 節
        sb.Append(inv, $"land={_land.Fingerprint()};"); // 第 08 章 a 節
        if (_demography is not null)
        {
            sb.Append(inv, $"humanWeek={_demography.LastWeek};nextPerson={_demography.NextId};");
            foreach (var p in _demography.People)
                sb.Append(inv, $"person{p.Id}:{p.SettlementId}:{p.Sex}:{p.BornDay}:{p.PregnancyDueWeek}:{p.FatherId}:{p.MotherId}:{p.BirthFatherId}:{p.PartnerId}:{p.BatchId};");
        }
        sb.Append(inv, $"nextEntity={_entityIds.NextValue}/{_entityIds.Exhausted};");
        foreach (var site in _sites.Settlements) sb.Append(inv, $"{site};");
        foreach (var point in _sites.ResourcePoints) sb.Append(inv, $"{point};");
        foreach (var batch in _migrations) sb.Append(inv, $"{batch.Fingerprint()};");
        foreach (var week in _weeks)
        {
            sb.Append(inv, $"week{week.Week}@{week.WorldTime.Ticks}:ids{week.EntityIdNextOpening}-{week.EntityIdNextClosing};");
            foreach (var c in week.RngCounters) sb.Append(inv, $"rngc{c.StreamId}={c.Before}-{c.After};");
            foreach (var ledger in week.Settlements) sb.Append(inv, $"{ledger};");
            foreach (var e in week.Events) sb.Append(inv, $"{e};");
            foreach (var m in week.Migrations) sb.Append(inv, $"{m};");
            foreach (var source in week.Sources) sb.Append(inv, $"{source};"); // 第 04 章 c 節：來源帳
        }
        foreach (var e in _history.Events) sb.Append(inv, $"{e.Fingerprint()};"); // 第 03 章 b 節：史庫

        return sb.ToString();
    }

    private void Checkpoint(DemoCommitStage stage) => FaultInjector?.Invoke(stage);
}
