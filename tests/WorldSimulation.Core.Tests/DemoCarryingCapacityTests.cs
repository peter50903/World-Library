using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #103：一般 TEMP 世界的食物承載與長期生存實證。不改任何 TEMP 數值、不補糧，只用真實週帳證明：
/// 人口不超過「每週再生÷每人消耗」的聚落永遠不缺糧（可長期存活）；缺糧只發生在人口超過承載的聚落；
/// TEST 饑荒聚落（不再生）另外標出，不與一般聚落混用。
/// </summary>
public sealed class DemoCarryingCapacityTests
{
    private const int ScanYears = 30;
    private static readonly ulong[] ScanSeeds = Enumerable.Range(1, 20).Select(i => (ulong)i).Append(42UL).ToArray();

    private static DemoObserverWorld RunYears(ulong seed, DemoWeeklyTempConfig config, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var d = 0; d < years * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void CapacityFollowsTheTempRegenAndFoodRulesWithoutChangingTheWorld()
    {
        var config = DemoWeeklyTempConfig.Sample;
        var world = new DemoObserverWorld(DemoScenario.Sample(42), config);
        var before = world.ExportTempSave();
        var capacities = world.SettlementCapacities();
        Assert.Equal(before, world.ExportTempSave()); // 純讀取。

        Assert.Equal(world.Sites.Settlements.Select(s => s.Id).OrderBy(i => i), capacities.Select(c => c.SettlementId));
        foreach (var c in capacities)
        {
            var points = world.Sites.ResourcePointsOf(c.SettlementId);
            var regen = points.Sum(p => config.RegenFor(p.Kind));
            Assert.Equal(regen, c.RegenPerWeek);
            Assert.Equal(regen / config.FoodPerPersonPerWeek, c.SustainablePopulation);
            Assert.Equal(world.SettlementPopulation(c.SettlementId), c.Population);
            Assert.Equal(c.Population * config.FoodPerPersonPerWeek, c.DemandPerWeek);
            Assert.Equal(points.Sum(p => world.ResourceReserve(p.Id)), c.ReserveNow);
            Assert.Equal(c.Population <= c.SustainablePopulation, c.LongTermSustainable);
            Assert.False(c.TempFamineSettlement);
            Assert.Contains("TEMP", c.Describe());
        }

        // 森林 20、漁場 25、農地 30（TEMP）÷每人 4 → 5、6、7 人；8 人的林地聚落長期每週缺 12。
        var woodland8 = new DemoSettlementCapacity(9, "測試", "林地", false, 8, 20, 5, 4, 0, 0, 0);
        Assert.Equal(5, woodland8.SustainablePopulation);
        Assert.Equal(12, woodland8.LongTermGap);
        Assert.False(woodland8.LongTermSustainable);
        // 每人產量低於消耗：勞力本身養不活任何人。
        Assert.Equal(0, new DemoSettlementCapacity(9, "測試", "農地", false, 1, 30, 3, 4, 0, 0, 0).SustainablePopulation);
    }

    [Fact]
    public void TestFamineSettlementIsMarkedAndHasNoCapacity()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.LocalFamineSample);
        var famine = world.SettlementCapacities().Single(c => c.SettlementId == 1);
        Assert.True(famine.TempFamineSettlement);
        Assert.Equal(0, famine.RegenPerWeek);
        Assert.Equal(0, famine.SustainablePopulation);
        Assert.Contains("TEST 饑荒聚落", famine.Describe());
        Assert.All(world.SettlementCapacities().Where(c => c.SettlementId != 1), c => Assert.False(c.TempFamineSettlement));
    }

    [Fact]
    public void NormalWorldsShowBothLongTermSurvivalAndResourceShortageAndHungerOnlyAboveCapacity()
    {
        var seedsWithShortage = new List<ulong>();
        var neverShortSettlements = 0;
        foreach (var seed in ScanSeeds)
        {
            var world = RunYears(seed, DemoWeeklyTempConfig.Sample, ScanYears);
            var capacity = world.SettlementCapacities().ToDictionary(c => c.SettlementId);
            var weeks = world.WeekReports();

            foreach (var ledger in weeks.SelectMany(w => w.Settlements))
            {
                Assert.True(ledger.IsBalanced);
                Assert.Equal(0, ledger.Births); // 本題不加出生。
                // 人口不超過承載的聚落，本週採收一定 ≥ 需求：缺糧只會發生在人口超過承載時。
                if (ledger.FoodShortage > 0)
                    Assert.True(ledger.PopulationOpening > capacity[ledger.SettlementId].SustainablePopulation,
                        $"種子 {seed} 第 {ledger.Week} 週 {ledger.SettlementName} 人口 {ledger.PopulationOpening} 未超過承載卻缺糧");
            }

            // 長期存活：有林地／漁場再生的聚落不會被餓到 0。
            // 第 08 章 a 節：舊「草原自動農田 30」退場後，只靠未開墾草原的聚落沒有糧食來源，如實記錄其衰退（不調數值補回）。
            foreach (var c in capacity.Values)
            {
                var grassOnly = world.Sites.ResourcePointsOf(c.SettlementId).All(p => p.Kind == DemoResourceKind.Uncultivated);
                if (grassOnly)
                    Assert.Equal(0, c.SustainablePopulation);
                else
                    Assert.True(c.Population > 0, $"種子 {seed} {c.SettlementName} 在 {ScanYears} 年內滅絕");
            }

            if (weeks.Any(w => w.Settlements.Any(l => l.FoodShortage > 0)))
                seedsWithShortage.Add(seed);
            neverShortSettlements += capacity.Keys.Count(id =>
                weeks.All(w => w.Settlements.Where(l => l.SettlementId == id).All(l => l.FoodShortage == 0)));
        }

        // 兩邊都可重現：有聚落從頭到尾不缺糧（長期存活），也有種子因資源再生不足而缺糧。
        Assert.True(neverShortSettlements > 0, "沒有任何聚落長期不缺糧");
        Assert.NotEmpty(seedsWithShortage);
    }

    [Fact]
    public void FabricatedFoodWithoutHarvestIsStillRejected()
    {
        // 故障注入：帳上多出一份沒有採收來源的糧食（糧食帳本身仍平衡），週帳檢查必須拒絕。
        var world = RunYears(42, DemoWeeklyTempConfig.Sample, 1);
        var report = world.LatestWeek!;
        var first = report.Settlements[0];
        var forged = first with { FoodProduced = first.FoodProduced + 1, FoodClosing = first.FoodClosing + 1 };
        Assert.True(forged.FoodBalanced);
        Assert.False(forged.NoMagicFood);
        var tampered = report with { Settlements = report.Settlements.Skip(1).Prepend(forged).ToList() };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(tampered));
    }

    [Fact]
    public void StabilityDiagnosisListsPerSettlementCapacityEvidence()
    {
        var run = M1RunCollector.Collect(42, M1Preset.Normal, 10, TestCalendar.DaysPerYear);
        Assert.NotNull(run.FinalCapacities);
        var path = M1CheckCatalog.FindDefaultPath();
        Assert.NotNull(path);
        var report = M1Evaluator.Evaluate(run, M1CheckCatalog.Load(path!));
        var stability = report.Results.Single(r => r.Check.Id == "M1_STABILITY");
        // 第 08 章 d 節 TEMP 門檻 D1：10 年後仍有人、未爆量＝通過；承載證據照列。
        Assert.Equal(M1Verdict.Pass, stability.Verdict);
        Assert.Contains("長期可養活", stability.Diagnosis);
        foreach (var c in run.FinalCapacities!)
            Assert.Contains(c.SettlementName, stability.Diagnosis);

        var famine = M1Evaluator.Evaluate(M1RunCollector.Collect(42, M1Preset.LocalFamine, 10, TestCalendar.DaysPerYear), M1CheckCatalog.Load(path!));
        Assert.Contains("TEST 饑荒", famine.Results.Single(r => r.Check.Id == "M1_STABILITY").Diagnosis);
    }

    [Fact]
    public void CenturyRunReportsYearEndCapacitiesWithoutChangingSummaries()
    {
        var seen = new List<int>();
        var observed = DemoCenturyRun.Run(7, 3, TestCalendar.DaysPerYear,
            onYearSettlements: (y, caps) =>
            {
                seen.Add(y.Year);
                Assert.NotEmpty(caps);
            });
        var without = DemoCenturyRun.Run(7, 3, TestCalendar.DaysPerYear);
        Assert.Equal(new[] { 1, 2, 3 }, seen);
        Assert.Equal(without.Years, observed.Years);
    }
}
