using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-6c 章：世界時代的輪替表（由種子決定、不另存）與各時代的效果。</summary>
public sealed class DemoAgesTests
{
    [Fact]
    public void ScheduleStartsWithHopeNeverRepeatsAndCoversEveryYear()
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 42 })
        {
            var rng = RngRegistry.Create(seed);
            var spans = DemoAges.Schedule(rng, 500);
            Assert.Equal(DemoAge.Hope, spans[0].Age);
            Assert.Equal(1, spans[0].FromYear);
            Assert.True(spans[^1].FromYear <= 500 && spans[^1].ToYear >= 500);
            for (var i = 0; i < spans.Count; i++)
            {
                var years = spans[i].ToYear - spans[i].FromYear + 1;
                Assert.InRange(years, DemoAges.TempMinYears, DemoAges.TempMaxYears);
                if (i > 0)
                {
                    Assert.Equal(spans[i - 1].ToYear + 1, spans[i].FromYear);
                    Assert.NotEqual(spans[i - 1].Age, spans[i].Age);
                }
            }
            // 同一個種子重算相同；較短的表是較長的表的開頭。
            Assert.Equal(spans, DemoAges.Schedule(RngRegistry.Create(seed), 500));
            var shorter = DemoAges.Schedule(rng, 100);
            Assert.Equal(spans.Take(shorter.Count), shorter);
            Assert.All(spans, s => Assert.Equal(s.Age, DemoAges.At(rng, s.FromYear)));
            Assert.Equal(spans[0].Age, DemoAges.AtWeek(rng, 1));
        }
    }

    [Fact]
    public void OnlyTheNamedAgeChangesEachLaw()
    {
        Assert.Equal(100, DemoAges.FirePercent(DemoAge.Hope));
        Assert.Equal(DemoAges.TempSunFirePercent, DemoAges.FirePercent(DemoAge.Sun));
        Assert.Equal(100, DemoAges.DungeonPercent(DemoAge.Sun));
        Assert.Equal(DemoAges.TempDarkDungeonPercent, DemoAges.DungeonPercent(DemoAge.Dark));
        Assert.Equal(DemoAges.TempDarkDungeonTargetPercent, DemoAges.DungeonTargetPercent(DemoAge.Dark));
        Assert.Equal(0, DemoAges.Loyalty(DemoAge.Dark));
        Assert.Equal(DemoAges.TempChaosLoyalty, DemoAges.Loyalty(DemoAge.Chaos));
        Assert.Equal(Enum.GetValues<DemoAge>().Length, Enum.GetValues<DemoAge>().Select(DemoAges.Name).Distinct().Count());
        Assert.Equal(Enum.GetValues<DemoAge>().Length, Enum.GetValues<DemoAge>().Select(DemoAges.Description).Distinct().Count());
        // 第 05-1 章：文明里程碑的說法不用「時代」。
        Assert.All(Enum.GetValues<DemoEra>(), e => Assert.DoesNotContain("時代", DemoKnowledge.MilestoneName(e), StringComparison.Ordinal));
        Assert.Equal(3, Enum.GetValues<DemoEra>().Select(DemoKnowledge.MilestoneName).Distinct().Count());
    }

    [Fact]
    public void ChaosAgeLowersLoyaltyAndTheWorldReportsItsAge()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        Assert.Equal(DemoAge.Hope, world.Age);
        Assert.Equal(DemoAge.Hope, Assert.Single(world.Ages()).Age);
        var sites = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (a, b) = (sites[0].Id, sites[1].Id);
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        var calm = new DemoRealmContext(world.Sites, _ => 50, _ => 1, _ => null, _ => DemoSettlementTier.Village, _ => false);
        var chaos = calm with { AgeOfWeek = _ => DemoAge.Chaos };
        Assert.Equal(DemoRealms.LoyaltyValue(b, a, 100, fold, calm) + DemoAges.TempChaosLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, chaos));
        Assert.Contains(DemoRealms.Loyalty(b, a, 100, fold, chaos), f => f.Text == "混沌時代" && f.Value == DemoAges.TempChaosLoyalty);
        Assert.Equal(DemoRealms.LoyaltyValue(b, a, 100, fold, calm), DemoRealms.LoyaltyValue(b, a, 100, fold, calm with { AgeOfWeek = _ => DemoAge.Sun }));
    }
}
