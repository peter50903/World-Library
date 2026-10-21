using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 03 章 a 節：提交故障注入矩陣。7 個 TEMP 情境 × 種子 {1,2,3,42} × 5 個檢查點：在第一個有事件的結算日
/// 注入例外，世界（指紋、週報數、人口快照、亂數、實體分配器）完全不變；移除注入重跑＝從未失敗的世界。
/// 另驗證提交物件的順序守衛（錯序、重複發布、略過步驟都丟例外且世界不變）。
/// </summary>
public sealed class DemoCommitFaultMatrixTests
{
    private sealed class InjectedFault : Exception
    {
        public InjectedFault(string message) : base(message) { }
    }

    private const int SearchWeeks = 104;

    private static readonly Dictionary<string, DemoWeeklyTempConfig> Configs = new()
    {
        ["Sample"] = DemoWeeklyTempConfig.Sample,
        ["ForagingSample"] = DemoWeeklyTempConfig.ForagingSample,
        ["LivingForagingSample"] = DemoWeeklyTempConfig.LivingForagingSample,
        ["CrowdedForagingSample"] = DemoWeeklyTempConfig.CrowdedForagingSample,
        ["FamineSample"] = DemoWeeklyTempConfig.FamineSample,
        ["LocalFamineSample"] = DemoWeeklyTempConfig.LocalFamineSample,
        ["FrontierSample"] = DemoWeeklyTempConfig.FrontierSample
    };

    public static TheoryData<string, ulong> Matrix()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var name in Configs.Keys)
            foreach (var seed in new ulong[] { 1, 2, 3, 42 })
                data.Add(name, seed);
        return data;
    }

    private static DemoObserverWorld NewWorld(string config, ulong seed)
        => new(DemoScenario.Sample(seed), Configs[config]);

    private static bool IsInteresting(string config, DemoWeekReport week)
        => config == "LivingForagingSample"
            ? week.Settlements.Any(l => l.Births > 0 || l.NaturalDeaths > 0)
            : week.Events.Count > 0;

    /// <summary>
    /// 一定會產生事件的情境（飢餓、餓死、外移、建村、出生／自然死亡）：104 週內找不到就讓測試失敗並印出情境與種子。
    /// 一般、採集、擁擠採集在穩定種子下可能整整兩年都沒有事件（實測一般情境種子 42 第一年事件 0），
    /// 這三種找不到時改在第一個結算日（第 7 日，含週帳與亂數計數器）注入（第 03 章 a 節，Claude 定案）。
    /// </summary>
    private static readonly HashSet<string> MustHaveEvents = new()
    {
        "LivingForagingSample", "FamineSample", "LocalFamineSample", "FrontierSample"
    };

    /// <summary>從未失敗的世界：找第一個「有事件」的結算日，回傳該日與當日指紋。</summary>
    private static (int Day, string Fingerprint) FirstInterestingDay(string config, ulong seed)
    {
        var world = NewWorld(config, seed);
        string? firstSettlement = null;
        for (var day = 1; day <= SearchWeeks * 7; day++)
        {
            var advance = world.AdvanceOneDay();
            if (advance.SettledWeek is not { } week)
                continue;
            if (IsInteresting(config, week))
                return (advance.Day, world.StateFingerprint());
            firstSettlement ??= world.StateFingerprint();
        }
        Assert.False(MustHaveEvents.Contains(config), $"{config} 種子 {seed}：{SearchWeeks} 週內找不到有事件的週。");
        return (7, firstSettlement!);
    }

    private sealed record Observed(string Fingerprint, int Weeks, string People, string Rng, ulong NextEntity, int Day);

    private static Observed Observe(DemoObserverWorld world) => new(
        world.StateFingerprint(),
        world.WeekReports().Count,
        world.HumanPopulationSnapshot is { } h ? $"{h.Seed}/{h.LastWeek}/{h.NextId}/{string.Join(",", h.People)}" : "none",
        string.Join(",", world.RngState()),
        world.EntityIdNextValue,
        world.Snapshot().DaysElapsed);

    [Theory]
    [MemberData(nameof(Matrix))]
    public void FaultAtEveryCheckpoint_OnTheFirstEventfulWeek_ChangesNothing_AndRetryMatchesTheNeverFailedWorld(string config, ulong seed)
    {
        var (day, control) = FirstInterestingDay(config, seed);
        var world = NewWorld(config, seed);
        for (var i = 1; i < day; i++)
            world.AdvanceOneDay();
        var published = world.WeekReports();
        var before = Observe(world);

        foreach (var stage in Enum.GetValues<DemoCommitStage>())
        {
            world.FaultInjector = s =>
            {
                if (s == stage) throw new InjectedFault($"注入故障：{s}");
            };
            Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());
            Assert.Equal(before, Observe(world));
        }

        world.FaultInjector = null;
        var advance = world.AdvanceOneDay();
        Assert.Equal(day, advance.Day);
        Assert.NotNull(advance.SettledWeek);
        Assert.Equal(control, world.StateFingerprint());
        // 失敗前已發布的週報原封不動（同一個物件，逐筆相等）。
        var after = world.WeekReports();
        Assert.Equal(published.Count + 1, after.Count);
        for (var i = 0; i < published.Count; i++)
            Assert.Same(published[i], after[i]);
    }

    [Fact]
    public void EveryCheckpointFiresInOrder_OnOrdinaryAndSettlementDays()
    {
        var world = NewWorld("LivingForagingSample", 42);
        var seen = new List<DemoCommitStage>();
        world.FaultInjector = seen.Add;
        world.AdvanceOneDay(); // 第 1 日：非結算日
        Assert.Equal(Enum.GetValues<DemoCommitStage>(), seen);
        seen.Clear();
        for (var i = 0; i < 6; i++)
            world.AdvanceOneDay(); // 第 7 日：結算日（含生死）
        Assert.Equal(Enum.GetValues<DemoCommitStage>(), seen.Skip(5 * 5));
    }

    [Fact]
    public void FaultOnAnOrdinaryDay_AtEveryCheckpoint_ChangesNothing()
    {
        var world = NewWorld("Sample", 5);
        for (var i = 0; i < 10; i++)
            world.AdvanceOneDay();
        var before = Observe(world);
        foreach (var stage in Enum.GetValues<DemoCommitStage>())
        {
            world.FaultInjector = s =>
            {
                if (s == stage) throw new InjectedFault("注入故障");
            };
            Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());
            Assert.Equal(before, Observe(world));
        }
    }

    private static DemoObserverWorld DayBeforeFirstSettlement()
    {
        var world = NewWorld("Sample", 42);
        for (var i = 0; i < 6; i++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void HistoryBeforeLedger_IsRejected_AndTheWorldIsUnchanged()
    {
        var world = DayBeforeFirstSettlement();
        var before = world.StateFingerprint();
        var commit = world.BeginCommit();
        commit.StageSchedule();
        commit.StageRng();
        Assert.True(commit.SettlesWeek);
        Assert.Throws<InvalidOperationException>(() =>
            commit.StageHistory(new DemoWeekReport(1, TimeSpan.FromDays(7), Array.Empty<DemoSettlementWeekLedger>())));
        Assert.Throws<InvalidOperationException>(() => commit.PublishTo(world));
        Assert.Equal(before, world.StateFingerprint());
    }

    [Fact]
    public void SkippingRng_IsRejected_AndTheWorldIsUnchanged()
    {
        var world = DayBeforeFirstSettlement();
        var before = world.StateFingerprint();
        var outcome = world.SettleNextWeekForTest(world.WeekReports());
        var commit = world.BeginCommit();
        commit.StageSchedule();
        Assert.Throws<InvalidOperationException>(() => commit.StageLedger(outcome));
        Assert.Throws<InvalidOperationException>(() => commit.PublishTo(world));
        Assert.Equal(before, world.StateFingerprint());

        // 結算日不可略過帳本／生死／歷史直接發布。
        var partial = world.BeginCommit();
        partial.StageSchedule();
        partial.StageRng();
        Assert.Throws<InvalidOperationException>(() => partial.PublishTo(world));
        Assert.Equal(before, world.StateFingerprint());
    }

    [Fact]
    public void PublishingTwice_IsRejected_AndTheSecondCallChangesNothing()
    {
        var world = NewWorld("Sample", 42);
        var control = NewWorld("Sample", 42);
        control.AdvanceOneDay();

        var commit = world.BeginCommit();
        commit.StageSchedule();
        Assert.False(commit.SettlesWeek);
        Assert.Throws<InvalidOperationException>(() => commit.StageLedger(world.SettleNextWeekForTest(world.WeekReports())));
        commit.StageRng();
        commit.PublishTo(world);
        Assert.Equal(control.StateFingerprint(), world.StateFingerprint());

        var published = world.StateFingerprint();
        Assert.Throws<InvalidOperationException>(() => commit.PublishTo(world));
        Assert.Throws<InvalidOperationException>(() => commit.StageSchedule());
        Assert.Equal(published, world.StateFingerprint());
    }

    [Fact]
    public void StaleCommit_CannotOverwriteANewerPublishedDay()
    {
        var world = NewWorld("Sample", 42);
        var stale = world.BeginCommit();
        stale.StageSchedule();
        stale.StageRng();
        world.AdvanceOneDay(); // 另一次提交先發布了第 1 日
        var published = world.StateFingerprint();
        Assert.Throws<InvalidOperationException>(() => stale.PublishTo(world));
        Assert.Equal(published, world.StateFingerprint());
    }

    [Fact]
    public void SameSeed_GivesTheSameDailyFingerprints()
    {
        var a = NewWorld("FrontierSample", 3);
        var b = NewWorld("FrontierSample", 3);
        for (var i = 0; i < 120; i++)
        {
            a.AdvanceOneDay();
            b.AdvanceOneDay();
            Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        }
    }
}
