using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08-2 章：希望時代（普通世界＝人類採集）。開局有 4 週存糧、開局聚落彼此不共用來源、開局不挨餓；
/// 飢餓不致死、只有老死；外移一定留下育齡女性與可配對男性；世界總覽與世界狀態一致。
/// </summary>
public sealed class DemoHopeEraTests
{
    private static readonly DemoWeeklyTempConfig Hope = DemoWeeklyTempConfig.LivingForagingSample;
    private static readonly DemoTempDemographyRules Rules = DemoTempDemographyRules.HumanFixture;

    [Fact]
    public void OnlyTheLivingWorldIsTheHopeEra()
    {
        Assert.True(Hope.HopeEra);
        Assert.Equal("希望時代", Hope.EraName);
        Assert.False(DemoWeeklyTempConfig.LocalFamineSample.HopeEra);
        Assert.Null(DemoWeeklyTempConfig.FrontierSample.EraName);
    }

    [Fact]
    public void OpeningVillagesHaveFourWeeksOfFoodAreSpreadOutAndDoNotStarveAtFirst()
    {
        var problems = new List<string>();
        var spaced = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), Hope);
            foreach (var s in world.Sites.Settlements)
                if (world.SettlementPopulation(s.Id) * Hope.FoodPerPersonPerWeek * 4 != world.SettlementFood(s.Id))
                    problems.Add($"種子 {seed} #{s.Id} 開局存糧 {world.SettlementFood(s.Id)}");

            // 開局聚落彼此相隔至少 TEMP 3 格；可住的海岸與河岸不夠時才退回（第 06-1 章起可住地只在水邊）。
            var cells = world.Sites.Settlements.Select(s => world.Map.GetCell(s.Column, s.Row)).ToList();
            if (cells.All(a => cells.All(b => ReferenceEquals(a, b) || DemoWorldSites.HexDistance(a, b) >= DemoWorldSites.TempOpeningSpacing)))
                spaced++;

            for (var d = 0; d < 4 * 7; d++)
                world.AdvanceOneDay();
            foreach (var w in world.WeekReports())
                foreach (var l in w.Settlements.Where(l => l.FoodShortage > 0 || l.WaterShortage > 0))
                    problems.Add($"種子 {seed} 第 {w.Week} 週 #{l.SettlementId} 缺糧 {l.FoodShortage}／缺水 {l.WaterShortage}");
        }
        Assert.True(problems.Count == 0, string.Join("；", problems.Take(10)));
        Assert.True(spaced >= 15, $"20 個種子只有 {spaced} 個開局聚落彼此相隔 ≥ 3 格");
    }

    [Fact]
    public void NobodyStarvesAndOnlyTheOldDieOfNaturalCauses()
    {
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), Hope);
            for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
            // 第 08-7c 章：希望時代是序章，30 年內可能已進入開拓時代（恢復餓死）；只驗收希望時代的那些週。
            var hopeEnds = world.SettlementEvents().Where(e => e.Kind == DemoSettlementEventKind.EraBegan && e.Count == (long)DemoEra.Pioneer)
                .Select(e => e.Week).DefaultIfEmpty(int.MaxValue).Min(); // 第 08-24 章：以開拓時代開始那一週為準（之後可能還有征服時代）
            var weeks = world.WeekReports().Where(w => w.Week <= hopeEnds).ToList();
            Assert.All(weeks, w =>
            {
                Assert.All(w.Settlements, l => Assert.Equal(l.NaturalDeaths, l.Deaths));
                Assert.All(w.Migrations, m => Assert.Equal(0, m.Deaths));
                Assert.DoesNotContain(w.Events, e => e.Kind is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath);
            });
            Assert.True(world.WorldPopulation > 0, $"種子 {seed}：希望時代 30 年內不應滅亡");
            Assert.True(weeks.Sum(w => w.Settlements.Sum(l => l.Births)) > 0);
        }
    }

    [Fact]
    public void YoungPeopleNeverDieOfNaturalCausesInTheHopeEra()
    {
        var hopeRules = DemoHumanLifecycle.Rules;
        Assert.Equal(0, hopeRules.AnnualNaturalDeathChance(30));
        Assert.Equal(0, hopeRules.AnnualNaturalDeathChance(DemoSurvivalTempSettings.Current.ElderAgeYears - 1));
        Assert.True(hopeRules.AnnualNaturalDeathChance(DemoSurvivalTempSettings.Current.ElderAgeYears) > 0);
        // 種族規則本身不變（其他時代仍照 39C 全年齡分段）。
        Assert.True(Rules.AnnualNaturalDeathChance(30) > 0);
    }

    private static DemoCohortDemography Village(params (int Age, DemoTempSex Sex)[] people)
        => DemoCohortDemography.Restore(new DemoCohortSnapshot(0,
            people.Select(p => new DemoCohortGroup(1, 0, -p.Age, p.Sex, 1)).ToImmutableArray(),
            ImmutableArray<DemoPregnancyGroup>.Empty));

    [Fact]
    public void EmigrationAlwaysLeavesAFertileWomanAndAPartnerBehind()
    {
        // 第 07-1 章：群體人口版的「留下一對育齡夫婦」。
        var two = Village((24, DemoTempSex.Female), (30, DemoTempSex.Female), (50, DemoTempSex.Female),
            (25, DemoTempSex.Male), (31, DemoTempSex.Male), (52, DemoTempSex.Male));
        // 只有兩名育齡女性：開拓要帶走兩名、還要留一名 → 不出發。
        Assert.Equal(0, DemoCohortMigration.Plan(two, 1, 10, DemoEmigrationMode.Founding, 0, Rules).People);
        // 缺糧外移依比例，但一定留下育齡女性與可配對男性。
        var all = DemoCohortMigration.Plan(two, 1, 6, DemoEmigrationMode.Shortage, 0, Rules);
        Assert.Equal(4, all.People);
        var left = two.Depart(1, all.Take, all.Pregnancies, 9).StatsOf(1, 0, 0, Rules);
        Assert.True(left.FertileWomen >= 1 && left.PartnerMen >= 1);

        var three = Village((20, DemoTempSex.Female), (24, DemoTempSex.Female), (30, DemoTempSex.Female),
            (25, DemoTempSex.Male), (31, DemoTempSex.Male), (52, DemoTempSex.Male));
        var founding = DemoCohortMigration.Plan(three, 1, 4, DemoEmigrationMode.Founding, 0, Rules);
        Assert.Equal(4, founding.People);
        // 最年輕的兩名育齡女性與兩名可配對男性出發。
        Assert.Equal(1, founding.Take[(-20, DemoTempSex.Female)]);
        Assert.Equal(1, founding.Take[(-24, DemoTempSex.Female)]);
        Assert.Equal(1, founding.Take[(-25, DemoTempSex.Male)]);
        Assert.Equal(1, founding.Take[(-31, DemoTempSex.Male)]);
        // 男女人數相當：沒有找不到伴侶的人，不會婚嫁遷移。
        Assert.Equal(0, DemoCohortMigration.Plan(three, 1, 1, DemoEmigrationMode.Mate, 0, Rules).People);
        // 只剩女性：一名最年輕的育齡女性去婚嫁。
        var women = Village((22, DemoTempSex.Female), (28, DemoTempSex.Female));
        var mate = DemoCohortMigration.Plan(women, 1, 1, DemoEmigrationMode.Mate, 0, Rules);
        Assert.Equal(1, mate.Take[(-22, DemoTempSex.Female)]);
    }

    [Fact]
    public void CohortPopulationConservesPeopleAndIsReproducible()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(2), Hope);
        for (var d = 0; d < 10 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var snapshot = world.HumanPopulationSnapshot!;
        Assert.Equal(world.WorldPopulation, snapshot.Groups.Sum(g => g.Count));
        Assert.All(world.Sites.Settlements, s => Assert.Equal(world.SettlementPopulation(s.Id),
            snapshot.Groups.Where(g => g.SettlementId == s.Id && g.BatchId == 0).Sum(g => g.Count)));
        var again = new DemoObserverWorld(DemoScenario.Sample(2), Hope);
        for (var d = 0; d < 10 * TestCalendar.DaysPerYear; d++)
            again.AdvanceOneDay();
        Assert.Equal(world.StateFingerprint(), again.StateFingerprint());
        // 二項分布抽樣：機率 0／1 與人數 0 的邊界。
        Assert.Equal(0, DemoBinomial.Draw(10, 0, 12345));
        Assert.Equal(10, DemoBinomial.Draw(10, 1, 12345));
        Assert.Equal(0, DemoBinomial.Draw(0, 0.5, 12345));
        Assert.InRange(DemoBinomial.Draw(100000, 0.5, ulong.MaxValue / 2), 49000, 51000);
    }

    [Fact]
    public void OverviewMatchesTheWorld()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(4), Hope);
        for (var d = 0; d < 3 * TestCalendar.DaysPerYear + 50; d++)
            world.AdvanceOneDay();
        var o = DemoWorldOverview.Of(world, TestCalendar.DaysPerYear);
        Assert.Equal("希望時代", o.EraName);
        Assert.Equal(4, o.Year);
        Assert.Equal(world.WorldPopulation, o.Population);
        Assert.Equal(world.PopulationInTransit, o.InTransit);
        Assert.Equal(world.Sites.Settlements.Count(s => !s.IsRuin), o.Villages);
        Assert.Equal(world.MapCapacity.People, o.Capacity);
        Assert.Equal(world.WorldPopulation, o.Children + o.Adults + o.Elders);
        var thisYear = DemoYearLedger.FromWeeks(world.WeekReports(), TestCalendar.DaysPerYear, world.Snapshot().DaysElapsed)[^1];
        Assert.Equal(thisYear.Births, o.BirthsThisYear);
        Assert.Equal(0, o.StarvationDeathsThisYear);
        var lines = o.Lines();
        Assert.StartsWith("希望時代｜第 4 年", lines[0]);
        Assert.Contains(lines, l => l.StartsWith($"世界人口 {world.WorldPopulation}／理論上限 {world.MapCapacity.People}", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("今年：出生", StringComparison.Ordinal));
    }

    // 第 14-1 章第 2 項：自製的次方、對數、指數與標準函式差在第 13 位以後，邊界值正確。
    [Fact]
    public void DeterministicMathMatchesTheStandardFunctions()
    {
        foreach (var x in new[] { 1e-300, 1e-9, 0.02425, 0.25, 0.5, 0.82, 0.999999, 1.0, 1.5, 2.0, 12345.678, 1e300 })
        {
            Assert.InRange(DemoDeterministicMath.Log(x) - Math.Log(x), -1e-12 * Math.Max(1, Math.Abs(Math.Log(x))), 1e-12 * Math.Max(1, Math.Abs(Math.Log(x))));
            if (x is <= 1 and >= 1e-9)
                foreach (var y in new[] { 7.0 / 365, 84.0 / 365, 1.0, 3.5 })
                    Assert.InRange(DemoDeterministicMath.Pow(x, y) / Math.Pow(x, y), 1 - 1e-12, 1 + 1e-12);
        }
        foreach (var x in new[] { -700.0, -1.0, -1e-9, 0.0, 1e-9, 0.3, 1.0, 20.0, 700.0 })
            Assert.InRange(DemoDeterministicMath.Exp(x) / Math.Exp(x), 1 - 1e-12, 1 + 1e-12);
        Assert.Equal(1.0, DemoDeterministicMath.PowInt(0.3, 0));
        Assert.InRange(DemoDeterministicMath.PowInt(0.97, 5000) / Math.Pow(0.97, 5000), 1 - 1e-11, 1 + 1e-11);
        Assert.Equal(0.0, DemoDeterministicMath.Pow(0, 0.5));
        Assert.Equal(1.0, DemoDeterministicMath.Pow(0.4, 0));
        Assert.Equal(0.0, DemoDeterministicMath.Log(1.0));
        Assert.True(double.IsNegativeInfinity(DemoDeterministicMath.Log(0)));
        Assert.True(double.IsNaN(DemoDeterministicMath.Log(-1)));
    }
}
