using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// C02：M1_POP 在人類繁衍情境（TEMP）下的對帳。合法出生要逐聚落對帶原因的出生事件、
/// 死亡按原因分類（餓死對餓死事件、自然死亡對自然死亡事件），不得再把合法出生／自然死亡誤報為錯誤；
/// 刪掉任一筆出生或自然死亡事件則必須 FAIL。第 08 章 d 節起此情境（<see cref="M1Preset.Living"/>）列入預設矩陣。
/// </summary>
/// <remarks>
/// 實測（CI，2026-10-15）：種子 42 人類繁衍 45 年＝出生 15、聚落餓死 27、自然死亡 0，45 年底人口 0
/// （TEMP 自然死亡風險 60 歲起，初始 18～22 歲的人口在那之前已餓死滅絕；種子 7／1／2／3 跑 70 年亦同）。
/// 真實長跑量測不到自然死亡，所以自然死亡的對帳以「把同一週一筆餓死改記為自然死亡」的週帳證明：
/// 帳與事件一致時 M1_POP 通過，刪掉那筆自然死亡事件則 FAIL。
/// </remarks>
public sealed class M1HumanLifecycleCoverageTests
{
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

    /// <summary>把某週一筆餓死事件（與週帳）改記為自然死亡：死亡總數與人口帳不變，只改原因分類。
    /// 目前 TEMP 人類情境真實輸出沒有自然死亡事件；依問答板 #25 使用者裁決 A（2026-10-15），
    /// 從真實自然死亡事件刪除的反例延到 P1.1.04（餓死改隨機抽取）後補驗，本卡先用此改記反例。</summary>
    private static M1RunData WithOneStarvationRecordedAsNaturalDeath(M1RunData run)
    {
        var weeks = run.Weeks.ToList();
        var index = weeks.FindIndex(w => w.Events.Any(e => e.Kind == DemoSettlementEventKind.StarvationDeath));
        Assert.True(index >= 0, "測試資料應該包含餓死事件");
        var week = weeks[index];
        var starvation = week.Events.First(e => e.Kind == DemoSettlementEventKind.StarvationDeath);
        weeks[index] = week with
        {
            Events = week.Events
                .Select(e => ReferenceEquals(e, starvation)
                    ? e with { Kind = DemoSettlementEventKind.NaturalDeath, Cause = "測試：改記為年齡自然死亡" }
                    : e)
                .ToArray(),
            Settlements = week.Settlements
                .Select(l => l.SettlementId == starvation.SettlementId
                    ? l with { NaturalDeaths = l.NaturalDeaths + starvation.Count }
                    : l)
                .ToArray()
        };
        return run with { Weeks = weeks };
    }

    [Fact]
    public void LegalBirthsAndDeathsByCauseAreNotReportedAsErrors()
    {
        var run = LivingRun.Value;
        Assert.True(run.Weeks.Sum(w => w.Settlements.Sum(l => l.Births)) > 0, "人類繁衍情境應有出生。");
        Assert.True(run.Weeks.Sum(w => w.Settlements.Sum(l => l.StarvationDeaths)) > 0, "人類繁衍情境（實測）應有餓死。");

        var pop = Pop(run);
        Assert.DoesNotContain("沒有任何帶原因的出生事件", pop.Diagnosis);
        Assert.DoesNotContain("帶原因的餓死事件", pop.Diagnosis);
        Assert.DoesNotContain("帶原因的自然死亡事件", pop.Diagnosis);
        Assert.Equal(M1Verdict.Pass, pop.Verdict);
        Assert.Contains("自然死亡", pop.Diagnosis);
        Assert.Contains("出生、孕期與自然死亡由人類採集情境的個人身分產生", pop.Diagnosis);
    }

    [Fact]
    public void NaturalDeathWithItsEventPasses()
    {
        var pop = Pop(WithOneStarvationRecordedAsNaturalDeath(LivingRun.Value));
        Assert.Equal(M1Verdict.Pass, pop.Verdict);
        Assert.DoesNotContain("帶原因的餓死事件", pop.Diagnosis);
        Assert.DoesNotContain("帶原因的自然死亡事件", pop.Diagnosis);
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
        var withNatural = WithOneStarvationRecordedAsNaturalDeath(LivingRun.Value);
        var pop = Pop(WithoutOneEvent(withNatural, DemoSettlementEventKind.NaturalDeath));
        Assert.Equal(M1Verdict.Fail, pop.Verdict);
        Assert.Contains("帶原因的自然死亡事件", pop.Diagnosis);
    }
}
