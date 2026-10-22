using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 07-1 章 b 節：代表人物與命名。每個有人的聚落有村長與採集代表（有漁獲才有漁撈代表）；
/// 代表人物算在年齡群人數裡、不外移；老死後由同規則補任；聚落與人物名字是西式奇幻音譯、不超過六個字、同種子同名。
/// </summary>
public sealed class DemoRepresentativeTests
{
    private static readonly DemoWeeklyTempConfig Hope = DemoWeeklyTempConfig.LivingForagingSample;

    private static DemoObserverWorld Run(ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), Hope);
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void SettlementNamesAreWesternTransliterationsWithinSixCharactersAndUnique()
    {
        var settings = DemoNameTempSettings.Current;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var world = Run(seed, 0);
            var names = world.Sites.Settlements.Select(s => s.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.All(names, n =>
            {
                Assert.InRange(DemoNames.Length(n), 2, settings.MaxLength);
                Assert.Contains(settings.PlacePrefix, p => n.StartsWith(p, StringComparison.Ordinal));
            });
            // 同種子同名。
            Assert.Equal(names, Run(seed, 0).Sites.Settlements.Select(s => s.Name));
        }
        // 一般示範情境的名字不變。
        Assert.Equal("示範聚落甲", new DemoObserverWorld(DemoScenario.Sample(42)).Sites.Settlements[0].Name);
    }

    [Fact]
    public void EveryInhabitedSettlementHasALeaderAndAGathererCountedInItsCohorts()
    {
        var world = Run(3, 3 * TestCalendar.DaysPerYear);
        var day = world.Snapshot().DaysElapsed;
        var snapshot = world.HumanPopulationSnapshot!;
        foreach (var s in world.Sites.Settlements.Where(s => world.SettlementPopulation(s.Id) > 0))
        {
            var reps = world.RepresentativesOf(s.Id);
            Assert.Contains(reps, r => r.Role == DemoRepresentativeRole.Leader);
            Assert.Contains(reps, r => r.Role == DemoRepresentativeRole.Gatherer);
            Assert.All(reps, r =>
            {
                Assert.InRange(DemoNames.Length(r.Name), 2, DemoNameTempSettings.Current.MaxLength);
                Assert.Contains("·", r.Name);
                Assert.True(DemoCohortDemography.AgeAt(r.BirthYear, day) >= DemoTempDemographyRules.HumanFixture.AdultAgeYears);
                // 代表人物算在所屬聚落的年齡群裡。
                Assert.True(snapshot.Groups.Where(g => g.SettlementId == s.Id && g.BatchId == 0 && g.BirthYear == r.BirthYear && g.Sex == r.Sex)
                    .Sum(g => g.Count) >= 1);
            });
            // 點格詳情列出代表人物。
            var view = world.QueryCell(s.Column, s.Row);
            Assert.All(reps, r => Assert.Contains(view.Lines, l => l.Contains(r.Name, StringComparison.Ordinal)));
        }
        // 漁撈代表只在有河流或海洋漁獲的聚落。
        var fishers = world.WeekReports()[^1].Sources
            .Where(x => x.Kind is DemoCatchmentSourceKind.MainRiver or DemoCatchmentSourceKind.OpenWater)
            .SelectMany(x => x.Shares).Where(x => x.Food > 0).Select(x => x.SettlementId).ToHashSet();
        Assert.All(snapshot.Representatives.Where(r => r.Role == DemoRepresentativeRole.Fisher),
            r => Assert.Contains(r.SettlementId, fishers.Concat(world.WeekReports().SelectMany(w => w.Sources)
                .Where(x => x.Kind is DemoCatchmentSourceKind.MainRiver or DemoCatchmentSourceKind.OpenWater)
                .SelectMany(x => x.Shares).Where(x => x.Food > 0).Select(x => x.SettlementId))));
    }

    [Fact]
    public void LeadersDieOfOldAgeAndAreSucceededAndTheHistoryRecordsIt()
    {
        var world = Run(1, 40 * TestCalendar.DaysPerYear);
        var weeks = world.WeekReports();
        var exits = weeks.SelectMany(w => w.RepresentativeExits).ToList();
        Assert.Contains(exits, x => x.Reason.StartsWith("老死", StringComparison.Ordinal));
        // 同一聚落同一職位在任最多一人；卸任後有人補上（有人的聚落）。
        var appointed = weeks.SelectMany(w => w.Appointments).ToList();
        Assert.True(appointed.Count > world.Sites.Settlements.Count(s => !s.IsRuin), "老死後應有補任");
        var leaderExit = exits.First(x => x.Reason.StartsWith("老死", StringComparison.Ordinal));
        var history = DemoSettlementHistory.Lines(world, leaderExit.SettlementId);
        Assert.Contains(history, l => l.Contains("卸任：老死", StringComparison.Ordinal));
        Assert.Contains(history, l => l.Contains("成為", StringComparison.Ordinal));
        // 存讀檔後代表人物相同（十年存檔；四十年的週帳存檔超過 TEMP 大小上限，另見第 07-1 章 c 節）。
        var young = Run(1, 10 * TestCalendar.DaysPerYear);
        var loaded = DemoObserverWorld.ImportTempSave(young.ExportTempSave());
        Assert.Equal(young.HumanPopulationSnapshot!.Representatives, loaded.HumanPopulationSnapshot!.Representatives);
        Assert.Equal(young.WeekReports().SelectMany(w => w.Appointments), loaded.WeekReports().SelectMany(w => w.Appointments));
    }
}
