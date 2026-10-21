using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08-2 章：希望時代（普通世界＝人類採集）。開局有 4 週存糧、開局聚落彼此不共用來源、開局不挨餓；
/// 飢餓不致死、只有老死；外移一定留下一對育齡夫婦；世界總覽與世界狀態一致。
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
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), Hope);
            foreach (var s in world.Sites.Settlements)
                Assert.Equal(world.SettlementPopulation(s.Id) * Hope.FoodPerPersonPerWeek * 4, world.SettlementFood(s.Id));

            // 開局聚落彼此相隔至少 TEMP 3 格（沒有足夠空間時才退回，這張地圖 12×8 的調查種子都放得下）。
            var cells = world.Sites.Settlements.Select(s => world.Map.GetCell(s.Column, s.Row)).ToList();
            foreach (var a in cells)
                foreach (var b in cells.Where(b => !ReferenceEquals(a, b)))
                    Assert.True(DemoWorldSites.HexDistance(a, b) >= DemoWorldSites.TempOpeningSpacing,
                        $"種子 {seed}：開局聚落 ({a.Column},{a.Row}) 與 ({b.Column},{b.Row}) 太近");

            for (var d = 0; d < 4 * 7; d++)
                world.AdvanceOneDay();
            Assert.All(world.WeekReports(), w => Assert.All(w.Settlements, l =>
                Assert.True(l.FoodShortage == 0 && l.WaterShortage == 0, $"種子 {seed} 第 {w.Week} 週 #{l.SettlementId} 開局就缺糧／缺水")));
        }
    }

    [Fact]
    public void NobodyStarvesAndOnlyTheOldDieOfNaturalCauses()
    {
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), Hope);
            for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
            var weeks = world.WeekReports();
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

    private static DemoTempPerson Adult(long id, DemoTempSex sex, int age, long? partner = null)
        => new(id, 1, sex, -age * 365, PartnerId: partner);

    [Fact]
    public void EmigrationAlwaysLeavesTheYoungestFertileCoupleBehind()
    {
        var residents = new[]
        {
            Adult(1, DemoTempSex.Female, 24, 2), Adult(2, DemoTempSex.Male, 25, 1),
            Adult(3, DemoTempSex.Female, 30, 4), Adult(4, DemoTempSex.Male, 31, 3),
            Adult(5, DemoTempSex.Female, 50, 6), Adult(6, DemoTempSex.Male, 52, 5)
        };
        // 沒有保留時最年輕的夫婦先走；保留時他們留下。
        Assert.Equal(new long[] { 1, 2 }, DemoFamilyMigration.Choose(residents, 2, 0, Rules));
        Assert.Equal(new long[] { 3, 4 }, DemoFamilyMigration.Choose(residents, 2, 0, Rules, keepCouple: true));
        Assert.Equal(new long[] { 3, 4, 5, 6 }, DemoFamilyMigration.Choose(residents, 10, 0, Rules, keepCouple: true));
        // 開拓需要兩對伴侶：保留一對後只剩兩對（其中一對過了育齡也算伴侶）→ 仍可出發。
        Assert.Equal(new long[] { 3, 4, 5, 6 }, DemoFamilyMigration.Choose(residents, 10, 0, Rules, requireCouple: true, keepCouple: true));
        // 只有兩對伴侶時開拓不出發（否則原聚落沒有育齡夫婦）。
        Assert.Empty(DemoFamilyMigration.Choose(residents.Take(4), 10, 0, Rules, requireCouple: true, keepCouple: true));
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
}
