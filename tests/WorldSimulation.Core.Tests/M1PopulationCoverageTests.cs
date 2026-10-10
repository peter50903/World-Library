using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #98：M1_POP（人口守恆，Hard）的覆蓋補強。出生、死亡、外移、抵達要與週帳、事件、隊伍帳逐筆對得上，
/// 逐聚落與逐隊伍的期初要等於上週期末；漏記、改人數或把人在聚落間搬來搬去都必須 FAIL。
/// 具體化尚未實作：只能列為覆蓋限制，不能被當成已驗證。
/// </summary>
public sealed class M1PopulationCoverageTests
{
    private const int Years = 3;

    private static readonly Lazy<IReadOnlyList<M1CheckDefinition>> Checks = new(() =>
        M1CheckCatalog.Load(M1CheckCatalog.FindDefaultPath()!));

    /// <summary>局部饑荒：挑第一個（種子 42，再 1～30）在 3 年內有隊伍抵達的種子。</summary>
    private static readonly Lazy<M1RunData> MigrationRun = new(() =>
    {
        foreach (var seed in new ulong[] { 42 }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i)))
        {
            var run = M1RunCollector.Collect(seed, M1Preset.LocalFamine, Years, TestCalendar.DaysPerYear);
            if (run.Weeks.Any(w => w.Events.Any(e => e.Kind == DemoSettlementEventKind.Arrival)))
                return run;
        }

        throw new InvalidOperationException("找不到 3 年內有遷徙抵達的局部饑荒種子。");
    });

    private static M1CheckResult Pop(M1RunData run)
        => M1Evaluator.Evaluate(run, Checks.Value).Results.Single(r => r.Check.Id == "M1_POP");

    private static M1RunData WithWeek(M1RunData run, int index, Func<DemoWeekReport, DemoWeekReport> change)
    {
        var weeks = run.Weeks.ToList();
        weeks[index] = change(weeks[index]);
        return run with { Weeks = weeks };
    }

    private static int WeekIndexWith(M1RunData run, Func<DemoWeekReport, bool> predicate)
    {
        var index = run.Weeks.ToList().FindIndex(w => predicate(w));
        Assert.True(index >= 0, "測試資料應該包含要竄改的週");
        return index;
    }

    [Fact]
    public void RealRunPassesAndStatesCoverageLimits()
    {
        var pop = Pop(MigrationRun.Value);
        Assert.Equal(M1Verdict.Pass, pop.Verdict);
        Assert.Contains("逐聚落、逐隊伍期初＝上週期末", pop.Diagnosis);
        Assert.Contains("具體化尚未實作（未實作，不是通過）", pop.Diagnosis);
    }

    [Fact]
    public void SameSeedGivesTheSameM1PopDiagnosis()
    {
        var run = MigrationRun.Value;
        var again = M1RunCollector.Collect(run.Seed, M1Preset.LocalFamine, Years, TestCalendar.DaysPerYear);
        Assert.Equal(Pop(run).Diagnosis, Pop(again).Diagnosis);
    }

    [Fact]
    public void MissingEmigrationEventFails()
    {
        var run = MigrationRun.Value;
        var i = WeekIndexWith(run, w => w.Events.Any(e => e.Kind == DemoSettlementEventKind.Emigration));
        var tampered = WithWeek(run, i, w => w with
        {
            Events = w.Events.Where(e => e.Kind != DemoSettlementEventKind.Emigration).ToArray()
        });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("外移事件 0 人", pop.Diagnosis);
    }

    [Fact]
    public void ArrivalEventWithAForgedHeadcountFails()
    {
        var run = MigrationRun.Value;
        var i = WeekIndexWith(run, w => w.Events.Any(e => e.Kind == DemoSettlementEventKind.Arrival));
        var tampered = WithWeek(run, i, w => w with
        {
            Events = w.Events.Select(e => e.Kind == DemoSettlementEventKind.Arrival ? e with { Count = e.Count + 1 } : e).ToArray()
        });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("抵達／建立事件", pop.Diagnosis);
    }

    [Fact]
    public void DroppedDepartureLedgerFails()
    {
        var run = MigrationRun.Value;
        var i = WeekIndexWith(run, w => w.Migrations.Any(m => m.Departed));
        var tampered = WithWeek(run, i, w => w with { Migrations = w.Migrations.Where(m => !m.Departed).ToArray() });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("隊伍出發帳 0 人", pop.Diagnosis);
    }

    [Fact]
    public void BirthWithoutABirthEventFails()
    {
        // 帳本身平衡（出生 1、期末也多 1），但沒有任何出生事件：人口沒有來源。
        var run = MigrationRun.Value;
        var tampered = WithWeek(run, 5, w => w with
        {
            Settlements = w.Settlements
                .Select((l, k) => k == 0 ? l with { Births = 1, PopulationClosing = l.PopulationClosing + 1 } : l)
                .ToArray()
        });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("沒有任何帶原因的出生事件", pop.Diagnosis);
    }

    [Fact]
    public void MovingAPersonBetweenSettlementsAcrossWeeksFails()
    {
        // 甲聚落期初／期末都多 1 人、乙聚落都少 1 人：每本帳平衡、世界總量不變，只有逐聚落銜接抓得到。
        var run = MigrationRun.Value;
        var i = WeekIndexWith(run, w => w.Week > 1 && w.Settlements.Count(l => l.PopulationOpening > 0 && l.PopulationClosing > 0) >= 2);
        var tampered = WithWeek(run, i, w =>
        {
            var donor = w.Settlements.First(l => l.PopulationOpening > 0 && l.PopulationClosing > 0);
            var receiver = w.Settlements.First(l => l.SettlementId != donor.SettlementId && l.PopulationOpening > 0 && l.PopulationClosing > 0);
            return w with
            {
                Settlements = w.Settlements.Select(l =>
                    l.SettlementId == donor.SettlementId
                        ? l with { PopulationOpening = l.PopulationOpening - 1, PopulationClosing = l.PopulationClosing - 1 }
                        : l.SettlementId == receiver.SettlementId
                        ? l with { PopulationOpening = l.PopulationOpening + 1, PopulationClosing = l.PopulationClosing + 1 }
                        : l).ToArray()
            };
        });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("期初人口", pop.Diagnosis);
    }

    [Fact]
    public void BatchGainingPeopleOnTheRoadFails()
    {
        // 仍在路上（本週未抵達）的隊伍期初、期末都多 1 人（自身帳平衡）→ 與上週期末接不上。
        var run = MigrationRun.Value;
        var i = WeekIndexWith(run, w => w.Migrations.Any(m => !m.Departed && !m.Arrived));
        var tampered = WithWeek(run, i, w => w with
        {
            Migrations = w.Migrations.Select(m => m.Departed || m.Arrived
                ? m
                : m with { PopulationOpening = m.PopulationOpening + 1, PopulationClosing = m.PopulationClosing + 1 }).ToArray()
        });
        var pop = Pop(tampered);
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("上週期末", pop.Diagnosis);
    }
}
