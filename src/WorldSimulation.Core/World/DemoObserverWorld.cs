using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.Scheduling;
using WorldSimulation.Core.Time;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// Explicitly NON-CANONICAL demonstration fixture. Numbers here are NOT gameplay
/// parameters and must never be persisted to 01_世界參數. The real world bootstrap
/// and economy will later be driven by validated configuration.
/// </summary>
public sealed record DemoScenario(
    ulong Seed,
    int Population,
    long InitialFood,
    int MaxDailyHarvest,
    int FoodPerPersonPerDay)
{
    public static DemoScenario Sample(ulong seed)
        => new(seed, Population: 5, InitialFood: 8,
            MaxDailyHarvest: 8, FoodPerPersonPerDay: 1);
}

/// <summary>An append-only demonstration event, not the final History Kernel or Akashic format.</summary>
public sealed record DemoDayEvent(
    int Day,
    TimeSpan WorldTime,
    long FoodBefore,
    long Harvested,
    long Consumed,
    long FoodAfter,
    long Shortage);

public sealed record DemoWorldSnapshot(
    TimeSpan WorldTime,
    int Population,
    long FoodStock,
    long TotalShortage,
    int DaysElapsed,
    ulong ResourceRngDraws);

/// <summary>V0-04A：一次原子提交內可注入故障的檢查點（只供自動測試使用）。</summary>
internal enum DemoCommitStage
{
    AfterScheduleStaged,
    AfterRngStaged,
    AfterLedgerStaged,
    BeforePublish
}

/// <summary>
/// Runnable, headless Observer vertical slice. A one-settlement survival accounting
/// demonstration driven by the existing WorldClock, Scheduler and deterministic RNG,
/// plus (V0-04A) a TEMP weekly L2 settlement for every demo settlement:
/// resource regeneration → labour and production → consumption, with a resource
/// ledger and a population ledger checked before anything is published, and (V0-04B)
/// hunger debt → starvation death with append-only, caused settlement events, and (V0-06A)
/// starving settlements sending migrant batches cell by cell to a known fed settlement, and (V0-06B)
/// founding batches that turn their survivors into a new settlement on an empty cell.
/// It does NOT implement official population rules, EntityStore, canonical History,
/// durability or Save/Load.
/// </summary>
public sealed partial class DemoObserverWorld
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
    private const string DailyWorkKey = "DEMO_DAILY_FOOD_ACCOUNTING";
    private const string WeeklyWorkKey = "DEMO_WEEKLY_SETTLEMENT";

    private readonly DemoScenario _scenario;
    private readonly DemoWeeklyTempConfig _weekly;
    private DemoTempDemography? _demography;

    // Every field below is replaced only in the publish step of AdvanceOneDay.
    private WorldClock _clock = new();
    private Scheduler _scheduler;
    private RngRegistry _rng;
    private ImmutableList<DemoDayEvent> _history = ImmutableList<DemoDayEvent>.Empty;
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
    private long _food;
    private long _totalShortage;

    public DemoObserverWorld(DemoScenario scenario)
        : this(scenario, DemoWeeklyTempConfig.Sample)
    {
    }

    public DemoObserverWorld(DemoScenario scenario, DemoWeeklyTempConfig weekly)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(weekly);
        if (scenario.Population <= 0)
            throw new ArgumentOutOfRangeException(nameof(scenario), "Demo population must be positive.");
        if (scenario.InitialFood < 0 || scenario.MaxDailyHarvest < 0 || scenario.FoodPerPersonPerDay < 0)
            throw new ArgumentOutOfRangeException(nameof(scenario), "Demo inputs cannot be negative.");
        weekly.Validate();

        // Check that even the first-day demand fits in the signed accounting model.
        _ = checked((long)scenario.Population * scenario.FoodPerPersonPerDay);

        _scenario = scenario;
        _weekly = weekly;
        _food = scenario.InitialFood;
        // V0-03B: ONE world RngRegistry. The TEMP map (GEOGRAPHY) and TEMP sites
        // (ECOLOGY_INIT, POP) are generated once here and cached; daily harvest keeps
        // using the RESOURCE stream of this same registry.
        _rng = RngRegistry.Create(scenario.Seed);
        Map = DemoHexWorld.Generate(_rng);
        var openingIds = new EntityIdAllocator();
        _sites = DemoWorldSites.Generate(Map, _rng,
            weekly.CrowdedForaging
                ? DemoSitesTempConfig.Sample with { MinPopGroupSize = 40, MaxPopGroupSize = 48 }
                : DemoSitesTempConfig.Sample,
            weekly.SharedCatchmentEnabled).WithOpeningEntityIds(openingIds);

        // V0-04A TEMP opening balances: every resource point starts with the same reserve,
        // every settlement with InitialFoodPerPerson per member of its population groups.
        // V0-06A TEST scenario: the TEMP local-famine settlement starts with nothing.
        _reserves = Sites.ResourcePoints.ToImmutableSortedDictionary(
            p => p.Id, p => weekly.IsFamineSettlement(p.SettlementId) ? 0L : weekly.InitialReservePerPoint);
        _population = Sites.Settlements.ToImmutableSortedDictionary(
            s => s.Id, s => Sites.PopGroupsOf(s.Id).Sum(p => (long)p.Size));
        _settlementFood = _population.ToImmutableSortedDictionary(
            kv => kv.Key,
            kv => weekly.IsFamineSettlement(kv.Key) ? 0L : checked(kv.Value * weekly.InitialFoodPerPerson));
        _hungerDebt = _population.ToImmutableSortedDictionary(kv => kv.Key, _ => 0L);
        if (weekly.HumanLifecycleEnabled)
            _demography = DemoTempDemography.Open(scenario.Seed,
                _population.ToDictionary(p => p.Key, p => checked((int)p.Value)), DemoHumanLifecycle.Rules, openingIds);
        _entityIds = openingIds.Capture();

        _scheduler = new Scheduler(_clock);
        _scheduler.Schedule(OneDay, DailyWorkKey);
        _scheduler.Schedule(OneWeek, WeeklyWorkKey);
    }

    /// <summary>The cached TEMP hex map generated from this world's registry (never regenerated).</summary>
    public DemoHexWorld Map { get; }

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

    public DemoWorldSnapshot Snapshot()
        => new(_clock.Now, _scenario.Population, _food, _totalShortage,
            _history.Count, _rng.GetDrawCounter(RngStreams.Resource));

    /// <summary>Detached read-only copy; querying history never changes the world.</summary>
    public IReadOnlyList<DemoDayEvent> History()
        => Array.AsReadOnly(_history.ToArray());

    /// <summary>All committed weekly settlement reports, oldest first (detached copy).</summary>
    public IReadOnlyList<DemoWeekReport> WeekReports()
        => Array.AsReadOnly(_weeks.ToArray());

    /// <summary>The most recent weekly settlement, or null before the first one (day 7).</summary>
    public DemoWeekReport? LatestWeek => _weeks.IsEmpty ? null : _weeks[_weeks.Count - 1];

    /// <summary>Whether this playable TEMP world uses 20/5 shared forest and river food sources.</summary>
    public bool UsesSharedForaging => _weekly.SharedCatchmentEnabled;

    public IReadOnlyList<DemoCatchmentFoodSource> SharedFoodSources()
        => UsesSharedForaging ? DemoForagingWorld.Sources(Map, _sites) : Array.Empty<DemoCatchmentFoodSource>();

    public bool IsMainRiverCell(int column, int row)
        => UsesSharedForaging && DemoForagingWorld.MainRiverTiles(Map).Contains(new DemoCellRef(column, row));

    public DemoCatchmentFoodResult? LatestSharedHarvest()
    {
        if (!UsesSharedForaging || LatestWeek is not { } week) return null;
        var opening = week.Settlements.ToDictionary(l => l.SettlementId, l => l.PopulationOpening);
        return DemoForagingWorld.Allocate(Map, _sites, opening, _weekly,
            week.Settlements.ToDictionary(l => l.SettlementId,
                l => l.AdultWorkersOpening < 0 ? l.PopulationOpening : l.AdultWorkersOpening));
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
            _migrations, _entityIds, publishedWeeks, _weekly);
    }

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
    /// Advances to the next scheduled day as ONE atomic commit (03 §4):
    /// stage → validate → publish. Everything (clock, scheduler, RNG, daily event,
    /// weekly ledger and settlement stocks) is first computed on private copies; the
    /// live world is replaced only by the final field assignments, which cannot fail.
    /// Any exception before that leaves Entity, RNG, Scheduler, History and Ledger unchanged.
    /// </summary>
    public DemoDayEvent AdvanceOneDay() => AdvanceOneDayCore(settleWeekly: true);

    /// <summary>
    /// V0-07B: <paramref name="settleWeekly"/>＝false re-derives only the seed-determined part of a day
    /// (clock, scheduler, RNG, daily history) for direct snapshot restore; the weekly state is then
    /// installed from the verified save. Weekly settlement draws no RNG, so both paths reach the
    /// same clock, scheduler and RNG state.
    /// </summary>
    private DemoDayEvent AdvanceOneDayCore(bool settleWeekly)
    {
        // ① Scheduler and clock: private copies.
        var clock = _clock.Clone();
        var scheduler = _scheduler.CloneOnto(clock);
        var head = scheduler.PeekNext()
            ?? throw new InvalidOperationException("No future work was scheduled.");
        if (head.DueTime > TimeSpan.MaxValue - OneWeek)
            throw new InvalidOperationException("Demo timeline is exhausted.");

        var batch = scheduler.TakeNextDueBatch();
        var daily = batch.Where(w => w.WorkKey == DailyWorkKey).ToList();
        var weekly = batch.Where(w => w.WorkKey == WeeklyWorkKey).ToList();
        if (daily.Count != 1 || weekly.Count > 1 || daily.Count + weekly.Count != batch.Count)
            throw new InvalidOperationException("Unexpected demo schedule batch.");

        var dueTime = daily[0].DueTime;
        scheduler.Schedule(dueTime + OneDay, DailyWorkKey);
        if (weekly.Count == 1)
            scheduler.Schedule(dueTime + OneWeek, WeeklyWorkKey);
        Checkpoint(DemoCommitStage.AfterScheduleStaged);

        // ② V0-01 daily accounting on a private RNG draft (unchanged behaviour).
        var trial = RngResolutionDraft.Begin(_rng);
        var proposedFood = (long)(trial.NextUInt64(RngStreams.Resource)
            % ((ulong)_scenario.MaxDailyHarvest + 1UL));
        var available = checked(_food + proposedFood);
        var demand = checked((long)_scenario.Population * _scenario.FoodPerPersonPerDay);
        var consumed = Math.Min(available, demand);
        var shortage = demand - consumed;
        var newFood = available - consumed;
        var accumulatedShortage = checked(_totalShortage + shortage);
        var nextHistoryCount = checked(_history.Count + 1);
        var proposedRng = trial.CaptureProposed();

        if (!RngCandidatePreflight.IsCompatible(_rng.Capture(), trial.BaseSnapshot, proposedRng))
            throw new InvalidOperationException("Demo RNG candidate failed preflight.");
        var nextRng = RngRegistry.Restore(proposedRng);
        Checkpoint(DemoCommitStage.AfterRngStaged);

        var evt = new DemoDayEvent(
            nextHistoryCount, dueTime, _food, proposedFood, consumed, newFood, shortage);
        var nextHistory = _history.Add(evt);

        // ③ V0-04A weekly settlement + ledger check (throws before publish if unbalanced).
        var nextWeeks = _weeks;
        var nextReserves = _reserves;
        var nextSettlementFood = _settlementFood;
        var nextPopulation = _population;
        var nextHungerDebt = _hungerDebt;
        var nextMigrations = _migrations;
        var nextEntityIds = _entityIds;
        var nextSites = _sites;
        var nextDemography = _demography;
        if (weekly.Count == 1 && settleWeekly)
        {
            var outcome = DemoWeeklySettlement.Settle(
                checked(_weeks.Count + 1), dueTime, Map, _sites,
                _reserves, _settlementFood, _population, _hungerDebt,
                _migrations, _entityIds, _weeks, _weekly,
                _demography is null ? null : _sites.Settlements.ToDictionary(s => s.Id,
                    s => (long)_demography.AdultsIn(s.Id, DemoHumanLifecycle.Rules)));
            if (_demography is not null)
            {
                var demographicTrial = DemoHumanLifecycle.Apply(outcome, _demography,
                    DemoCausalIndex.NextEventId(_weeks));
                outcome = demographicTrial.Outcome;
                nextDemography = demographicTrial.Population;
            }
            nextWeeks = _weeks.Add(outcome.Report);
            nextReserves = outcome.Reserves;
            nextSettlementFood = outcome.SettlementFood;
            nextPopulation = outcome.Population;
            nextHungerDebt = outcome.HungerDebt;
            nextMigrations = outcome.Migrations;
            nextEntityIds = outcome.EntityIds;
            nextSites = outcome.Sites;
        }

        Checkpoint(DemoCommitStage.AfterLedgerStaged);
        Checkpoint(DemoCommitStage.BeforePublish);

        // ④ Publish: plain reference/value assignments only.
        _clock = clock;
        _scheduler = scheduler;
        _rng = nextRng;
        _food = newFood;
        _totalShortage = accumulatedShortage;
        _history = nextHistory;
        _weeks = nextWeeks;
        _reserves = nextReserves;
        _settlementFood = nextSettlementFood;
        _population = nextPopulation;
        _hungerDebt = nextHungerDebt;
        _migrations = nextMigrations;
        _entityIds = nextEntityIds;
        _sites = nextSites;
        _demography = nextDemography;
        return evt;
    }

    /// <summary>
    /// Deterministic text fingerprint of the WHOLE mutable world state — Entity (stocks and
    /// population), RNG, Scheduler (clock and queue), History and Ledger. Used by tests to
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
        sb.Append(inv, $"food={_food};shortage={_totalShortage};");
        foreach (var kv in _reserves) sb.Append(inv, $"reserve{kv.Key}={kv.Value};");
        foreach (var kv in _settlementFood) sb.Append(inv, $"sfood{kv.Key}={kv.Value};");
        foreach (var kv in _population) sb.Append(inv, $"pop{kv.Key}={kv.Value};");
        foreach (var kv in _hungerDebt) sb.Append(inv, $"debt{kv.Key}={kv.Value};");
        if (_demography is not null)
        {
            sb.Append(inv, $"humanWeek={_demography.LastWeek};nextPerson={_demography.NextId};");
            foreach (var p in _demography.People)
                sb.Append(inv, $"person{p.Id}:{p.SettlementId}:{p.Sex}:{p.BornDay}:{p.PregnancyDueWeek}:{p.FatherId};");
        }
        sb.Append(inv, $"nextEntity={_entityIds.NextValue}/{_entityIds.Exhausted};");
        foreach (var site in _sites.Settlements) sb.Append(inv, $"{site};");
        foreach (var point in _sites.ResourcePoints) sb.Append(inv, $"{point};");
        foreach (var batch in _migrations) sb.Append(inv, $"{batch.Fingerprint()};");
        foreach (var evt in _history) sb.Append(inv, $"{evt};");
        foreach (var week in _weeks)
        {
            sb.Append(inv, $"week{week.Week}@{week.WorldTime.Ticks}:ids{week.EntityIdNextOpening}-{week.EntityIdNextClosing};");
            foreach (var ledger in week.Settlements) sb.Append(inv, $"{ledger};");
            foreach (var e in week.Events) sb.Append(inv, $"{e};");
            foreach (var m in week.Migrations) sb.Append(inv, $"{m};");
        }

        return sb.ToString();
    }

    private void Checkpoint(DemoCommitStage stage) => FaultInjector?.Invoke(stage);
}
