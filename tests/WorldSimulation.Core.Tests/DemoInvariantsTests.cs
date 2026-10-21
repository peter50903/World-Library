using System.Collections.Immutable;
using System.Reflection;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08 章 c 節：不變量註冊表——七條不變量只登錄一次；真實世界（一般、局部饑荒、開拓、人類採集）
/// 全部成立（一次全檢與逐年增量檢查都一樣）；長測報告每年檢查一次；每條不變量都抓得到對應的竄改。
/// 竄改以反射替換世界內部的週報清單（只在測試中），證明檢查不是空殼。
/// </summary>
public sealed class DemoInvariantsTests
{
    private static readonly string[] ExpectedIds =
    {
        "TIME_MONOTONIC", "ID_NO_REUSE", "HISTORY_APPEND_ONLY", "POP_LEDGER", "RESOURCE_LEDGER", "SOURCE_LEDGER",
        "MIGRATION_CONSERVATION"
    };

    private static DemoObserverWorld Run(DemoWeeklyTempConfig config, ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        return world;
    }

    private static void AssertAllOk(IReadOnlyList<DemoInvariantResult> results)
    {
        Assert.Equal(ExpectedIds, results.Select(r => r.Id));
        foreach (var r in results)
            Assert.True(r.Ok, $"{r.Id}：{string.Join("；", r.Problems)}");
    }

    private static readonly FieldInfo WeeksField = typeof(DemoObserverWorld)
        .GetField("_weeks", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void ReplaceWeek(DemoObserverWorld world, int index, Func<DemoWeekReport, DemoWeekReport> change)
    {
        var weeks = (ImmutableList<DemoWeekReport>)WeeksField.GetValue(world)!;
        WeeksField.SetValue(world, weeks.SetItem(index, change(weeks[index])));
    }

    private static DemoInvariantResult Result(DemoObserverWorld world, string id, DemoInvariantCheckpoint? previous = null)
        => DemoInvariants.Check(world, previous).Single(r => r.Id == id);

    [Fact]
    public void RegistryListsEachInvariantOnce()
    {
        Assert.Equal(ExpectedIds, DemoInvariants.All.Select(i => i.Id));
        Assert.All(DemoInvariants.All, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
    }

    public static IEnumerable<object[]> Worlds()
    {
        yield return new object[] { "normal", 42UL };
        yield return new object[] { "local-famine", 42UL };
        yield return new object[] { "frontier", 7UL };
        yield return new object[] { "living", 5UL };
    }

    private static DemoWeeklyTempConfig Config(string key) => key switch
    {
        "normal" => DemoWeeklyTempConfig.Sample,
        "local-famine" => DemoWeeklyTempConfig.LocalFamineSample,
        "frontier" => DemoWeeklyTempConfig.FrontierSample,
        _ => DemoWeeklyTempConfig.LivingForagingSample
    };

    [Theory]
    [MemberData(nameof(Worlds))]
    public void RealWorldsSatisfyAllInvariantsInFullAndIncrementally(string preset, ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), Config(preset));
        AssertAllOk(DemoInvariants.Check(world));
        DemoInvariantCheckpoint? checkpoint = null;
        for (var year = 1; year <= 3; year++)
        {
            for (var d = 0; d < TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
            AssertAllOk(DemoInvariants.Check(world, checkpoint));
            checkpoint = DemoInvariants.Capture(world);
        }
        AssertAllOk(DemoInvariants.Check(world));
        // 只檢查不改世界。
        var before = world.ExportTempSave();
        DemoInvariants.Check(world);
        Assert.Equal(before, world.ExportTempSave());
    }

    [Fact]
    public void LongRunChecksEveryInvariantEveryYear()
    {
        var report = DemoCenturyRun.Run(42, 4, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LivingForagingSample);
        Assert.Empty(report.ConservationErrors);
        Assert.NotNull(report.Invariants);
        Assert.Equal(ExpectedIds, report.Invariants!.Select(t => t.Id));
        Assert.All(report.Invariants!, t =>
        {
            Assert.Equal(4, t.Checks);
            Assert.Equal(0, t.Failures);
            Assert.Contains("通過 4／4 次", t.Describe());
        });
    }

    [Fact]
    public void TimeRelabelledWeekIsCaught()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 70);
        ReplaceWeek(world, 9, w => w with { Week = 11 });
        Assert.False(Result(world, "TIME_MONOTONIC").Ok);
    }

    [Fact]
    public void IdRangeGapIsCaught()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 70);
        ReplaceWeek(world, 9, w => w with { EntityIdNextClosing = w.EntityIdNextClosing + 1 });
        Assert.False(Result(world, "ID_NO_REUSE").Ok);
    }

    [Fact]
    public void ReplacedPublishedWeekIsCaughtAgainstCheckpoint()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 70);
        var checkpoint = DemoInvariants.Capture(world);
        for (var d = 0; d < 14; d++)
            world.AdvanceOneDay();
        Assert.True(Result(world, "HISTORY_APPEND_ONLY", checkpoint).Ok);
        // 同樣內容的新物件也算改寫（已發布的週報只能追加）。
        ReplaceWeek(world, 0, w => w with { });
        Assert.False(Result(world, "HISTORY_APPEND_ONLY", checkpoint).Ok);
    }

    [Fact]
    public void PopulationLedgerTamperIsCaught()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 70);
        ReplaceWeek(world, 9, w => w with
        {
            Settlements = w.Settlements.Select((l, i) => i == 0 ? l with { PopulationClosing = l.PopulationClosing + 1 } : l).ToArray()
        });
        Assert.False(Result(world, "POP_LEDGER").Ok);
    }

    [Fact]
    public void MagicFoodIsCaught()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 70);
        ReplaceWeek(world, 9, w => w with
        {
            Settlements = w.Settlements.Select((l, i) => i == 0
                ? l with { FoodProduced = l.FoodProduced + 5, FoodClosing = l.FoodClosing + 5 } : l).ToArray()
        });
        Assert.False(Result(world, "RESOURCE_LEDGER").Ok);
    }

    [Fact]
    public void SourceLedgerTamperIsCaught()
    {
        var world = Run(DemoWeeklyTempConfig.LivingForagingSample, 5, 70);
        var weeks = world.WeekReports();
        var index = weeks.ToList().FindLastIndex(w => w.Sources.Count > 0);
        Assert.True(index >= 0, "人類採集情境應有來源帳");
        ReplaceWeek(world, index, w => w with
        {
            Sources = w.Sources.Select((s, i) => i == 0 ? s with { Unharvested = s.Unharvested + 3 } : s).ToArray()
        });
        Assert.False(Result(world, "SOURCE_LEDGER").Ok);
    }

    [Fact]
    public void EmigrantsWithoutDepartingBatchAreCaught()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, TestSeeds.NormalWithWoodlandAndGrassland, 70);
        var target = world.WeekReports()[9].Settlements.First(l => l.PopulationClosing > 0).SettlementId;
        ReplaceWeek(world, 9, w => w with
        {
            Settlements = w.Settlements.Select(l => l.SettlementId == target
                ? l with { Emigrants = l.Emigrants + 1, PopulationClosing = l.PopulationClosing - 1 } : l).ToArray()
        });
        Assert.False(Result(world, "MIGRATION_CONSERVATION").Ok);
    }
}
