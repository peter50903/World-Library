using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #88：M1_HISTORY 的真實因果證據。每筆聚落事件有連續不重用的事件編號與 43F 直接原因
/// （CAUSE_PARENT），直接後果（EFFECT_CHILD）由反向索引得到；竄改或偽造的因果必須被抓到。
/// </summary>
public sealed class DemoCausalIndexTests
{
    private const int Years = 3;

    private static IReadOnlyList<DemoWeekReport> Run(ulong seed, DemoWeeklyTempConfig config, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var day = 0; day < days; day++)
            world.AdvanceOneDay();
        return world.WeekReports();
    }

    /// <summary>開拓情境：沿用 Godot 情境的種子搜尋（42，再 1～30），找第一個建立新聚落的種子。</summary>
    private static readonly Lazy<IReadOnlyList<DemoWeekReport>> FrontierWeeks = new(() =>
    {
        foreach (var seed in new ulong[] { 42 }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i)))
        {
            var weeks = Run(seed, DemoWeeklyTempConfig.FrontierSample, Years * TestCalendar.DaysPerYear);
            if (weeks.SelectMany(w => w.Events).Any(e => e.Kind == DemoSettlementEventKind.SettlementFounded))
                return weeks;
        }

        throw new InvalidOperationException("找不到會建立新聚落的開拓種子。");
    });

    private static readonly Lazy<IReadOnlyList<DemoWeekReport>> FamineWeeks = new(() =>
        Run(42, DemoWeeklyTempConfig.FamineSample, Years * TestCalendar.DaysPerYear));

    private static List<DemoSettlementEvent> Events(IReadOnlyList<DemoWeekReport> weeks)
        => weeks.SelectMany(w => w.Events).ToList();

    private static IReadOnlyList<DemoWeekReport> Replace(
        IReadOnlyList<DemoWeekReport> weeks, DemoSettlementEvent target, Func<DemoSettlementEvent, DemoSettlementEvent> change)
        => weeks.Select(w => w.Events.Contains(target)
                ? w with { Events = w.Events.Select(e => e == target ? change(e) : e).ToArray() }
                : w)
            .ToList();

    public static TheoryData<string> Presets => new() { "Sample", "LocalFamine", "Frontier", "Famine" };

    private static DemoWeeklyTempConfig Preset(string name) => name switch
    {
        "LocalFamine" => DemoWeeklyTempConfig.LocalFamineSample,
        "Frontier" => DemoWeeklyTempConfig.FrontierSample,
        "Famine" => DemoWeeklyTempConfig.FamineSample,
        _ => DemoWeeklyTempConfig.Sample
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void RealEventsHaveConsecutiveIdsAndLegalDirectCauses(string preset)
    {
        foreach (var seed in new ulong[] { 42, 7, 1 })
        {
            var weeks = Run(seed, Preset(preset), Years * TestCalendar.DaysPerYear);
            var index = DemoCausalIndex.Build(weeks);
            Assert.Empty(index.Problems);

            var events = Events(weeks);
            Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long)i), events.Select(e => e.EventId));
            foreach (var e in events)
            {
                if (e.Kind == DemoSettlementEventKind.Hunger)
                    Assert.Equal(0, e.CauseEventId); // 根因：週帳缺糧本身。
                else
                    Assert.InRange(e.CauseEventId, 1L, e.EventId - 1); // 其他事件都有更早的直接原因。
            }
        }
    }

    [Fact]
    public void SameSeedGivesTheSameIdsAndCauses()
    {
        var a = Events(Run(42, DemoWeeklyTempConfig.FrontierSample, 2 * TestCalendar.DaysPerYear));
        var b = Events(Run(42, DemoWeeklyTempConfig.FrontierSample, 2 * TestCalendar.DaysPerYear));
        Assert.Equal(a, b);
    }

    [Fact]
    public void FoundingChainIsHungerThenEmigrationThenFoundingAndChildrenAreTheReverseIndex()
    {
        var weeks = FrontierWeeks.Value;
        var index = DemoCausalIndex.Build(weeks);
        var founded = Events(weeks).First(e => e.Kind == DemoSettlementEventKind.SettlementFounded);

        var chain = index.ChainTo(founded);
        Assert.Equal(
            new[] { DemoSettlementEventKind.Hunger, DemoSettlementEventKind.Emigration, DemoSettlementEventKind.SettlementFounded },
            chain.Select(e => e.Kind));
        var (hunger, leave) = (chain[0], chain[1]);
        Assert.Equal(hunger.SettlementId, leave.SettlementId);
        Assert.Equal(hunger.Week, leave.Week);
        Assert.Equal(leave.MigrationBatchId, founded.MigrationBatchId);
        Assert.True(leave.Week < founded.Week);

        // EFFECT_CHILD：外移的直接後果包含建立新聚落；饑荒的直接後果包含外移。
        Assert.Contains(founded, index.ChildrenOf(leave));
        Assert.Contains(leave, index.ChildrenOf(hunger));
        Assert.All(index.ChildrenOf(hunger), child => Assert.Equal(hunger.EventId, child.CauseEventId));
    }

    [Fact]
    public void StarvationDeathPointsBackToTheSettlementsHunger()
    {
        var weeks = FamineWeeks.Value;
        var index = DemoCausalIndex.Build(weeks);
        var deaths = Events(weeks).Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath).ToList();
        Assert.NotEmpty(deaths);
        foreach (var death in deaths)
        {
            var root = index.ChainTo(death)[0];
            Assert.Equal(DemoSettlementEventKind.Hunger, root.Kind);
            Assert.True(root.EventId < death.EventId);
        }
    }

    // ── 反例：竄改或偽造的因果一律被抓到 ──

    [Fact]
    public void EmigrationWithoutCauseIsRejected()
    {
        var weeks = FrontierWeeks.Value;
        var leave = Events(weeks).First(e => e.Kind == DemoSettlementEventKind.Emigration);
        var problems = DemoCausalIndex.Build(Replace(weeks, leave, e => e with { CauseEventId = 0 })).Problems;
        Assert.Contains(problems, p => p.Contains("外移的直接原因必須是同一聚落同一週的飢餓事件"));
    }

    [Fact]
    public void CauseFromTheFutureIsRejected()
    {
        var weeks = FrontierWeeks.Value;
        var leave = Events(weeks).First(e => e.Kind == DemoSettlementEventKind.Emigration);
        var problems = DemoCausalIndex.Build(Replace(weeks, leave, e => e with { CauseEventId = e.EventId + 1 })).Problems;
        Assert.Contains(problems, p => p.Contains("原因不能來自未來"));
    }

    [Fact]
    public void EmigrationCausedByAnotherWeeksHungerIsRejected()
    {
        // 局部饑荒：消息要幾週才傳到，所以外移前同一聚落已經餓了好幾週（開拓情境可能第一週就出發）。
        var (weeks, leave, otherHunger) = new ulong[] { 42 }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i))
            .Select(seed => Run(seed, DemoWeeklyTempConfig.LocalFamineSample, Years * TestCalendar.DaysPerYear))
            .Select(w =>
            {
                var events = Events(w);
                bool EarlierHunger(DemoSettlementEvent hunger, DemoSettlementEvent emigration)
                    => hunger.Kind == DemoSettlementEventKind.Hunger && hunger.SettlementId == emigration.SettlementId
                        && hunger.EventId < emigration.EventId && hunger.Week < emigration.Week;
                var found = events.FirstOrDefault(x => x.Kind == DemoSettlementEventKind.Emigration
                    && events.Any(y => EarlierHunger(y, x)));
                var earlier = found is null ? null : events.First(y => EarlierHunger(y, found));
                return (Weeks: w, Leave: found, Hunger: earlier);
            })
            .First(x => x.Leave is not null);
        var problems = DemoCausalIndex.Build(Replace(weeks, leave!, e => e with { CauseEventId = otherHunger!.EventId })).Problems;
        Assert.Contains(problems, p => p.Contains("同一聚落同一週的飢餓事件"));
    }

    [Fact]
    public void DeathCausedByAnotherSettlementIsRejected()
    {
        var weeks = FamineWeeks.Value;
        var events = Events(weeks);
        var death = events.First(e => e.Kind == DemoSettlementEventKind.StarvationDeath
            && events.Any(h => h.Kind == DemoSettlementEventKind.Hunger && h.EventId < e.EventId && h.SettlementId != e.SettlementId));
        var foreign = events.First(h => h.Kind == DemoSettlementEventKind.Hunger && h.EventId < death.EventId && h.SettlementId != death.SettlementId);
        var problems = DemoCausalIndex.Build(Replace(weeks, death, e => e with { CauseEventId = foreign.EventId })).Problems;
        Assert.Contains(problems, p => p.Contains("同一聚落帶來飢餓債的事件"));
    }

    [Fact]
    public void ForgedEmigrationInAWeekWithoutShortageIsRejected()
    {
        // 偽造：把外移那週來源聚落的週帳缺糧改成 0（事件仍在）→ 飢餓根因沒有週帳證據，整條鏈不成立。
        var weeks = FrontierWeeks.Value;
        var leave = Events(weeks).First(e => e.Kind == DemoSettlementEventKind.Emigration);
        var forged = weeks.Select(w => w.Week != leave.Week
                ? w
                : w with
                {
                    Settlements = w.Settlements
                        .Select(l => l.SettlementId == leave.SettlementId ? l with { FoodShortage = 0 } : l)
                        .ToArray()
                })
            .ToList();
        var problems = DemoCausalIndex.Build(forged).Problems;
        Assert.Contains(problems, p => p.Contains("週帳沒有對應的缺糧"));
    }

    [Fact]
    public void ReusedEventIdIsRejected()
    {
        var weeks = FrontierWeeks.Value;
        var events = Events(weeks);
        var second = events[1];
        var problems = DemoCausalIndex.Build(Replace(weeks, second, e => e with { EventId = events[0].EventId })).Problems;
        Assert.Contains(problems, p => p.Contains("事件編號必須從 1 起連續遞增、不重用"));
    }

    [Fact]
    public void M1HistoryReportsTheChainByEventIdAndFlagsTamperedCauses()
    {
        var checks = M1CheckCatalog.Load(M1CheckCatalog.FindDefaultPath()!);
        var run = M1RunCollector.Collect(42, M1Preset.Famine, Years, TestCalendar.DaysPerYear);
        var history = M1Evaluator.Evaluate(run, checks).Results.Single(r => r.Check.Id == "M1_HISTORY");
        Assert.Equal(M1Verdict.Pass, history.Verdict);
        Assert.Contains("饑荒事件 #", history.Diagnosis);
        Assert.Contains("【覆蓋限制 TEMP】", history.Diagnosis);

        var death = run.Weeks.SelectMany(w => w.Events).First(e => e.Kind == DemoSettlementEventKind.StarvationDeath);
        var tampered = run with { Weeks = Replace(run.Weeks, death, e => e with { CauseEventId = 0 }) };
        var flagged = M1Evaluator.Evaluate(tampered, checks).Results.Single(r => r.Check.Id == "M1_HISTORY");
        Assert.NotEqual(M1Verdict.Pass, flagged.Verdict);
        Assert.Contains("因果證據不合法", flagged.Diagnosis);
    }
}
