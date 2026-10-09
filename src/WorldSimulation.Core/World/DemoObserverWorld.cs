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

/// <summary>
/// Runnable, headless Observer vertical slice. A one-settlement survival accounting
/// demonstration driven by the existing WorldClock, Scheduler and deterministic RNG.
/// It does NOT implement official population rules, EntityStore, a durable transaction,
/// canonical History, Godot rendering or Save/Load.
/// </summary>
public sealed class DemoObserverWorld
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private const string DailyWorkKey = "DEMO_DAILY_FOOD_ACCOUNTING";

    private readonly WorldClock _clock = new();
    private readonly Scheduler _scheduler;
    private readonly DemoScenario _scenario;
    private readonly List<DemoDayEvent> _history = new();
    private RngRegistry _rng;
    private long _food;
    private long _totalShortage;

    public DemoObserverWorld(DemoScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.Population <= 0)
            throw new ArgumentOutOfRangeException(nameof(scenario), "Demo population must be positive.");
        if (scenario.InitialFood < 0 || scenario.MaxDailyHarvest < 0 || scenario.FoodPerPersonPerDay < 0)
            throw new ArgumentOutOfRangeException(nameof(scenario), "Demo inputs cannot be negative.");

        // Check that even the first-day demand fits in the signed accounting model.
        _ = checked((long)scenario.Population * scenario.FoodPerPersonPerDay);

        _scenario = scenario;
        _food = scenario.InitialFood;
        // V0-03B: ONE world RngRegistry. The TEMP map (GEOGRAPHY) and TEMP sites
        // (ECOLOGY_INIT, POP) are generated once here and cached; daily harvest keeps
        // using the RESOURCE stream of this same registry.
        _rng = RngRegistry.Create(scenario.Seed);
        Map = DemoHexWorld.Generate(_rng);
        Sites = DemoWorldSites.Generate(Map, _rng);
        _scheduler = new Scheduler(_clock);
        _scheduler.Schedule(OneDay, DailyWorkKey);
    }

    /// <summary>The cached TEMP hex map generated from this world's registry (never regenerated).</summary>
    public DemoHexWorld Map { get; }

    /// <summary>The cached TEMP settlements / resource points / population groups.</summary>
    public DemoWorldSites Sites { get; }

    public DemoWorldSnapshot Snapshot()
        => new(_clock.Now, _scenario.Population, _food, _totalShortage,
            _history.Count, _rng.GetDrawCounter(RngStreams.Resource));

    /// <summary>Detached read-only copy; querying history never changes the world.</summary>
    public IReadOnlyList<DemoDayEvent> History()
        => Array.AsReadOnly(_history.ToArray());

    /// <summary>
    /// Resolve the next scheduled demo day using a private RNG draft, validate all
    /// accounting BEFORE advancing the scheduler, then publish a new state and event.
    /// This simple in-memory demo path must not be mistaken for the future multi-domain
    /// Atomic Commit / durability / rollback engine.
    /// </summary>
    public DemoDayEvent AdvanceOneDay()
    {
        var work = _scheduler.PeekNext()
            ?? throw new InvalidOperationException("No future work was scheduled.");
        if (work.WorkKey != DailyWorkKey)
            throw new InvalidOperationException("Unsupported demo work.");
        if (work.DueTime > TimeSpan.MaxValue - OneDay)
            throw new InvalidOperationException("Demo timeline is exhausted.");

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

        var evt = new DemoDayEvent(
            nextHistoryCount, work.DueTime, _food, proposedFood, consumed, newFood, shortage);

        // All arithmetic, RNG validation and next-due validation occurred first.
        // The scheduler and state are private, not externally writable, in this demo.
        var due = _scheduler.TakeNextDueBatch();
        if (due.Count != 1 || due[0] != work)
            throw new InvalidOperationException("Unexpected demo schedule batch.");
        _scheduler.Schedule(work.DueTime + OneDay, DailyWorkKey);
        _rng = RngRegistry.Restore(proposedRng);
        _food = newFood;
        _totalShortage = accumulatedShortage;
        _history.Add(evt);
        return evt;
    }
}
