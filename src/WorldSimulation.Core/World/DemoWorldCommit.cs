using System.Collections.Immutable;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.Scheduling;
using WorldSimulation.Core.Time;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>V0-04A／第 03 章 a 節：一次原子提交內可注入故障的五個檢查點（只供自動測試使用）。</summary>
internal enum DemoCommitStage
{
    AfterScheduleStaged,
    AfterRngStaged,
    /// <summary>週結算（帳本）暫存後、生死前。</summary>
    AfterLedgerStaged,
    /// <summary>生死暫存後；未啟用生死的情境與非結算日照樣觸發。</summary>
    AfterPopulationStaged,
    BeforePublish
}

public sealed partial class DemoObserverWorld
{
    /// <summary>
    /// 第 03 章 a 節（44F Stage 4～6、44E MOD_COMMIT）：一日的提交暫存物件。建立時只取已發布欄位的參照
    /// （不可變集合，不複製）；依固定順序 <see cref="StageSchedule"/> → <see cref="StageRng"/> →
    /// <see cref="StageLedger"/> → <see cref="StagePopulation"/> → <see cref="StageHistory"/> →
    /// <see cref="PublishTo"/> 暫存，任何一步失敗都不碰世界。順序錯、重複或略過都丟
    /// <see cref="InvalidOperationException"/>。非結算日與讀檔重算（不結算週帳）只做排程、亂數兩步，
    /// 帳本／生死／歷史三步必須整組略過。<see cref="PublishTo"/> 只做欄位指派、不可失敗；
    /// <c>_weeks</c> 只能經 <see cref="StageHistory"/>→<see cref="PublishTo"/> 增加。
    /// </summary>
    internal sealed class DemoWorldCommit
    {
        private enum Step
        {
            Begun,
            Schedule,
            Rng,
            Ledger,
            Population,
            History,
            Published
        }

        private readonly DemoObserverWorld _world;
        private readonly bool _settleWeekly;
        private Step _step = Step.Begun;

        // 已發布狀態的參照（建立時取得）。
        private readonly RngRegistry _baseRng;
        private readonly ImmutableList<DemoWeekReport> _baseWeeks;

        // 暫存結果；未暫存的欄位沿用已發布參照。
        private WorldClock? _clock;
        private Scheduler? _scheduler;
        private RngRegistry? _rng;
        private RngSnapshot? _proposedRng;
        private ImmutableList<DemoWeekReport> _weeks;
        private ImmutableSortedDictionary<int, long> _reserves;
        private ImmutableSortedDictionary<int, long> _settlementFood;
        private ImmutableSortedDictionary<int, long> _population;
        private ImmutableSortedDictionary<int, long> _hungerDebt;
        private ImmutableList<DemoMigrationBatch> _migrations;
        private EntityIdAllocatorSnapshot _entityIds;
        private DemoWorldSites _sites;
        private DemoTempDemography? _demography;
        private DemoWeeklyOutcome? _outcome;

        internal DemoWorldCommit(DemoObserverWorld world, bool settleWeekly)
        {
            _world = world;
            _settleWeekly = settleWeekly;
            _baseRng = world._rng;
            _baseWeeks = world._weeks;
            _weeks = world._weeks;
            _reserves = world._reserves;
            _settlementFood = world._settlementFood;
            _population = world._population;
            _hungerDebt = world._hungerDebt;
            _migrations = world._migrations;
            _entityIds = world._entityIds;
            _sites = world._sites;
            _demography = world._demography;
        }

        /// <summary>本日到期時間（<see cref="StageSchedule"/> 後才有）。</summary>
        public TimeSpan DueTime { get; private set; }

        /// <summary>今天是否有週結算到期（不論是否實際結算）。</summary>
        public bool WeeklyDue { get; private set; }

        /// <summary>今天要結算週帳（帳本／生死／歷史三步不可略過）。</summary>
        public bool SettlesWeek => WeeklyDue && _settleWeekly;

        /// <summary>暫存完成、已發布的週報（只在結算日發布後有值）。</summary>
        public DemoWeekReport? PublishedWeek { get; private set; }

        private void Advance(Step expected, Step next, string name)
        {
            if (_step != expected)
                throw new InvalidOperationException($"提交順序錯誤：{name} 必須在 {expected} 之後呼叫（目前 {_step}）。");
            _step = next;
        }

        /// <summary>① 排程與時鐘：在私有副本上取出今天到期的工作並排下一次。</summary>
        public void StageSchedule()
        {
            Advance(Step.Begun, Step.Schedule, nameof(StageSchedule));
            var clock = _world._clock.Clone();
            var scheduler = _world._scheduler.CloneOnto(clock);
            var head = scheduler.PeekNext()
                ?? throw new InvalidOperationException("No future work was scheduled.");
            if (head.DueTime > TimeSpan.MaxValue - OneWeek)
                throw new InvalidOperationException("Demo timeline is exhausted.");

            var batch = scheduler.TakeNextDueBatch();
            var daily = batch.Where(w => w.WorkKey == DailyWorkKey).ToList();
            var weekly = batch.Where(w => w.WorkKey == WeeklyWorkKey).ToList();
            if (daily.Count != 1 || weekly.Count > 1 || daily.Count + weekly.Count != batch.Count)
                throw new InvalidOperationException("Unexpected demo schedule batch.");

            DueTime = daily[0].DueTime;
            WeeklyDue = weekly.Count == 1;
            scheduler.Schedule(DueTime + OneDay, DailyWorkKey);
            if (WeeklyDue)
                scheduler.Schedule(DueTime + OneWeek, WeeklyWorkKey);
            _clock = clock;
            _scheduler = scheduler;
        }

        /// <summary>
        /// ② 亂數：私有草稿＋預檢（C05：目前零抽樣；草稿與預檢保留給之後的規則，44G SCHED_RNG_SCOPE）。
        /// </summary>
        public void StageRng()
        {
            Advance(Step.Schedule, Step.Rng, nameof(StageRng));
            var trial = RngResolutionDraft.Begin(_baseRng);
            var proposed = trial.CaptureProposed();
            if (!RngCandidatePreflight.IsCompatible(_baseRng.Capture(), trial.BaseSnapshot, proposed))
                throw new InvalidOperationException("Demo RNG candidate failed preflight.");
            _proposedRng = proposed;
            _rng = RngRegistry.Restore(proposed);
        }

        /// <summary>③ 帳本：週結算結果（已在 Settle 內通過資源帳與人口帳檢查）。</summary>
        public void StageLedger(DemoWeeklyOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            RequireWeekly(nameof(StageLedger));
            Advance(Step.Rng, Step.Ledger, nameof(StageLedger));
            _outcome = outcome;
        }

        /// <summary>
        /// ④ 生死：有人類繁衍時以生死後的結果取代週結算結果並暫存新的人口名冊；
        /// 未啟用生死的情境傳 null（步驟仍要走，確保順序固定）。
        /// </summary>
        public void StagePopulation(DemoWeeklyOutcome? outcomeAfterLifecycle, DemoTempDemography? demography)
        {
            RequireWeekly(nameof(StagePopulation));
            Advance(Step.Ledger, Step.Population, nameof(StagePopulation));
            if (outcomeAfterLifecycle is not null)
            {
                _outcome = outcomeAfterLifecycle;
                _demography = demography ?? throw new ArgumentNullException(nameof(demography));
            }
            else if (demography is not null)
            {
                throw new ArgumentException("沒有生死結果時不可替換人口名冊。", nameof(demography));
            }

            var outcome = _outcome!;
            _reserves = outcome.Reserves;
            _settlementFood = outcome.SettlementFood;
            _population = outcome.Population;
            _hungerDebt = outcome.HungerDebt;
            _migrations = outcome.Migrations;
            _entityIds = outcome.EntityIds;
            _sites = outcome.Sites;
        }

        /// <summary>⑤ 歷史：週報（附十條亂數流「提交前→提交後」計數器）只追加，與帳本同一交易。</summary>
        public void StageHistory(DemoWeekReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            RequireWeekly(nameof(StageHistory));
            Advance(Step.Population, Step.History, nameof(StageHistory));
            if (report.Week != _baseWeeks.Count + 1)
                throw new InvalidOperationException($"週報第 {report.Week} 週不是下一週（應為第 {_baseWeeks.Count + 1} 週）。");
            var before = _baseRng.Capture().Streams.ToDictionary(x => x.StreamId, x => x.DrawCounter);
            var after = _proposedRng!.Streams.ToDictionary(x => x.StreamId, x => x.DrawCounter);
            var counters = RngStreams.All.Select(id => new DemoRngCounter(id, before[id], after[id])).ToArray();
            _weeks = _baseWeeks.Add(report with { RngCounters = Array.AsReadOnly(counters) });
        }

        /// <summary>週結算結果（<see cref="StagePopulation"/> 之後為生死後結果）；給世界組裝週報。</summary>
        public DemoWeeklyOutcome Outcome => _outcome ?? throw new InvalidOperationException("尚未暫存週結算。");

        /// <summary>⑥ 發布：只做欄位指派，不可失敗。只能呼叫一次。</summary>
        public void PublishTo(DemoObserverWorld world)
        {
            if (!ReferenceEquals(world, _world))
                throw new InvalidOperationException("提交物件只能發布到建立它的世界。");
            var ready = SettlesWeek ? Step.History : Step.Rng;
            if (_step != ready)
                throw new InvalidOperationException($"提交尚未完成或已發布：目前 {_step}，發布需要 {ready}。");
            if (!ReferenceEquals(world._weeks, _baseWeeks) || !ReferenceEquals(world._rng, _baseRng))
                throw new InvalidOperationException("世界在提交暫存期間已被另一次提交改變。");
            _step = Step.Published;

            world._clock = _clock!;
            world._scheduler = _scheduler!;
            world._rng = _rng!;
            world._weeks = _weeks;
            world._reserves = _reserves;
            world._settlementFood = _settlementFood;
            world._population = _population;
            world._hungerDebt = _hungerDebt;
            world._migrations = _migrations;
            world._entityIds = _entityIds;
            world._sites = _sites;
            world._demography = _demography;
            PublishedWeek = SettlesWeek ? _weeks[^1] : null;
        }

        private void RequireWeekly(string name)
        {
            if (!SettlesWeek)
                throw new InvalidOperationException($"{name}：今天不結算週帳（非結算日或讀檔重算），帳本／生死／歷史三步必須略過。");
        }
    }
}
