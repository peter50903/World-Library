using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 07-1 章 b 節：代表人物與命名。每個有人的聚落有村長（第 08-5 章：採集、漁撈代表取消）；
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
    public void EveryInhabitedSettlementHasALeaderCountedInItsCohorts()
    {
        var world = Run(3, 3 * TestCalendar.DaysPerYear);
        var day = world.Snapshot().DaysElapsed;
        var snapshot = world.HumanPopulationSnapshot!;
        foreach (var s in world.Sites.Settlements.Where(s => world.SettlementPopulation(s.Id) > 0))
        {
            var reps = world.RepresentativesOf(s.Id);
            Assert.Single(reps, r => r.Role == DemoRepresentativeRole.Leader);
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
        // ImmutableArray 的 Equals 比的是參考，改比內容。
        Assert.Equal(young.HumanPopulationSnapshot!.Representatives.ToList(), loaded.HumanPopulationSnapshot!.Representatives.ToList());
        // 第 04-1 章：讀檔後只保留近期週報（就任紀錄另存在聚落小史）。
        Assert.Equal(young.WeekReports().Where(w => w.Week >= loaded.FirstRetainedWeek).SelectMany(w => w.Appointments).ToList(),
            loaded.WeekReports().SelectMany(w => w.Appointments).ToList());
        Assert.All(young.Sites.Settlements, s => Assert.Equal(young.ChronicleOf(s.Id), loaded.ChronicleOf(s.Id)));
    }

    // 第 08-10l 節：領袖的稱號依聚落等級——營地族長、村莊村長、城鎮鎮長、城市以上城主；小史記上任時當時的稱號，
    // 之後依小史推得的等級顯示。
    [Fact]
    public void LeaderTitlesFollowTheSettlementTier()
    {
        Assert.Equal(new[] { "族長", "村長", "鎮長", "城主", "城主" }, Enum.GetValues<DemoSettlementTier>().Select(DemoRepresentative.LeaderTitle));
        Assert.Equal("鎮長", DemoRepresentative.RoleName(DemoRepresentativeRole.Leader, DemoSettlementTier.Town));
        Assert.Equal("木匠", DemoRepresentative.RoleName(DemoRepresentativeRole.Carpenter, DemoSettlementTier.City));
        var chronicle = DemoSettlementChronicle.Empty with
        {
            Milestones = System.Collections.Immutable.ImmutableList.Create(
                new DemoChronicleLine(10, "人口 30 人、有木工坊、磨坊，由營地發展為村莊"),
                new DemoChronicleLine(30, "人口首次達到 50 人"),
                new DemoChronicleLine(50, "人口 70 人、有市集、陶窯，由村莊發展為城鎮"),
                new DemoChronicleLine(80, "人口降到 40 人，降為村莊"))
        };
        Assert.Equal(new[] { DemoSettlementTier.Camp, DemoSettlementTier.Village, DemoSettlementTier.Village, DemoSettlementTier.Town, DemoSettlementTier.Village },
            new[] { 9, 10, 49, 60, 80 }.Select(chronicle.TierAt));

        var world = Run(3, 30 * 365);
        var appointments = 0;
        foreach (var site in world.Sites.Settlements)
        {
            var history = world.ChronicleOf(site.Id);
            Assert.Equal(history.Tier, history.TierAt(world.CommittedWeeks));
            foreach (var line in history.Representatives)
            {
                var title = DemoRepresentative.LeaderTitles.FirstOrDefault(t => line.Text.EndsWith("成為" + t, StringComparison.Ordinal));
                if (title is null)
                    continue;
                appointments++;
                Assert.Equal(DemoRepresentative.LeaderTitle(history.TierAt(line.Week - 1)), title);
            }
        }
        Assert.True(appointments > 0);
    }
}
