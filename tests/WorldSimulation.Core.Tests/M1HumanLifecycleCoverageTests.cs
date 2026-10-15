using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// C02：M1_POP 在人類繁衍情境（TEMP）下的對帳。合法出生要逐聚落對帶原因的出生事件、
/// 死亡按原因分類（餓死對餓死事件、自然死亡對自然死亡事件），不得再把合法出生／自然死亡誤報為錯誤；
/// 刪掉任一筆出生或自然死亡事件則必須 FAIL。此情境只用於測試，不加入 <see cref="M1Preset.Default"/>。
/// </summary>
public sealed class M1HumanLifecycleCoverageTests
{
    /// <summary>初始 18～22 歲、TEMP 自然死亡風險隨年齡上升，需約 40 年以上才會出現自然死亡。</summary>
    private const int Years = 45;

    private static readonly M1Preset Living = new("living", "人類繁衍", DemoWeeklyTempConfig.LivingForagingSample);

    private static readonly Lazy<IReadOnlyList<M1CheckDefinition>> Checks = new(() =>
        M1CheckCatalog.Load(M1CheckCatalog.FindDefaultPath()!));

    private static readonly Lazy<M1RunData> LivingRun = new(() =>
        M1RunCollector.Collect(42, Living, Years, TestCalendar.DaysPerYear));

    private static M1CheckResult Pop(M1RunData run)
        => M1Evaluator.Evaluate(run, Checks.Value).Results.Single(r => r.Check.Id == "M1_POP");

    private static M1RunData WithoutOneEvent(M1RunData run, DemoSettlementEventKind kind)
    {
        var weeks = run.Weeks.ToList();
        var index = weeks.FindIndex(w => w.Events.Any(e => e.Kind == kind));
        Assert.True(index >= 0, $"測試資料應該包含 {kind} 事件");
        var week = weeks[index];
        var victim = week.Events.First(e => e.Kind == kind);
        weeks[index] = week with { Events = week.Events.Where(e => !ReferenceEquals(e, victim)).ToArray() };
        return run with { Weeks = weeks };
    }

    [Fact]
    public void LegalBirthsAndNaturalDeathsAreNotReportedAsErrors()
    {
        var run = LivingRun.Value;
        Assert.True(run.Weeks.Sum(w => w.Settlements.Sum(l => l.Births)) > 0, "人類繁衍情境應有出生。");
        Assert.True(run.Weeks.Sum(w => w.Settlements.Sum(l => l.NaturalDeaths)) > 0, "人類繁衍情境應有自然死亡。");

        var pop = Pop(run);
        Assert.DoesNotContain("沒有任何帶原因的出生事件", pop.Diagnosis);
        Assert.DoesNotContain("帶原因的餓死事件", pop.Diagnosis);
        Assert.DoesNotContain("帶原因的自然死亡事件", pop.Diagnosis);
        Assert.Equal(M1Verdict.Pass, pop.Verdict);
        Assert.Contains("自然死亡", pop.Diagnosis);
        Assert.Contains("出生只在人類繁衍情境發生，預設矩陣不含該情境", pop.Diagnosis);
    }

    [Fact]
    public void MissingBirthEventFails()
    {
        var pop = Pop(WithoutOneEvent(LivingRun.Value, DemoSettlementEventKind.Birth));
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("帶原因的出生事件", pop.Diagnosis);
    }

    [Fact]
    public void MissingNaturalDeathEventFails()
    {
        var pop = Pop(WithoutOneEvent(LivingRun.Value, DemoSettlementEventKind.NaturalDeath));
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("帶原因的自然死亡事件", pop.Diagnosis);
    }
}
