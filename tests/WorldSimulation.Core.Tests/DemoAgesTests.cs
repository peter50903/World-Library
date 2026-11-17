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
        // 霜雪時代減產、奇蹟時代研究加快與士兵 +1；其他時代不動這三項。
        Assert.Equal(DemoAges.TempFrostSourcePercent, DemoAges.SourcePercent(DemoAge.Frost));
        Assert.Equal(DemoAges.TempMiracleResearchPercent, DemoAges.ResearchPercent(DemoAge.Miracle));
        Assert.Equal(DemoAges.TempMiracleSoldierLevels, DemoAges.SoldierLevels(DemoAge.Miracle));
        Assert.All(Enum.GetValues<DemoAge>().Where(a => a != DemoAge.Frost), a => Assert.Equal(0, DemoAges.SourcePercent(a)));
        Assert.All(Enum.GetValues<DemoAge>().Where(a => a != DemoAge.Miracle), a => Assert.Equal(100, DemoAges.ResearchPercent(a)));
        Assert.All(Enum.GetValues<DemoAge>().Where(a => a != DemoAge.Miracle), a => Assert.Equal(0, DemoAges.SoldierLevels(a)));
        bool None(string _) => false;
        Assert.Equal(DemoCombat.Force(1000, DemoSettlementTier.Town, None).SoldierLevel + 1,
            DemoCombat.Force(1000, DemoSettlementTier.Town, None, extraLevels: DemoAges.SoldierLevels(DemoAge.Miracle)).SoldierLevel);
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

    // 第 08-6c 章：世界法則視窗指定時代——下一週起生效、一直持續到改回自動輪替、存檔帶著走、重播驗證相同；
    // 編年史寫「天命驟變」，史書一年歸在它最後一週的時代那一卷。
    [Fact]
    public void ManualAgeStartsNextWeekLastsUntilAutoAndIsSavedAndReplayed()
    {
        static DemoObserverWorld NewWorld() => new(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        static void Weeks(DemoObserverWorld w, int n)
        {
            for (var d = 0; d < 7 * n; d++)
                w.AdvanceOneDay();
        }
        var world = NewWorld();
        Weeks(world, 10);
        Assert.Null(world.AgeSetting);
        world.AgeSetting = DemoAge.Sun;
        world.AgeSetting = DemoAge.Frost;                    // 同一週再改：只留最後一次
        Assert.Equal(new[] { new DemoAgeChange(10, DemoAge.Frost) }, world.AgeChanges);
        Assert.Equal(DemoAge.Hope, world.Age);                // 下一個結算週才生效
        Assert.Equal(DemoAge.Frost, world.AgeAtWeek(11));
        Weeks(world, 1);
        Assert.Equal(DemoAge.Frost, world.Age);
        var twin = NewWorld();                                // 霜雪時代真的減產
        Weeks(twin, 11);
        static long Supply(DemoObserverWorld w) => w.WeekReports().Single(r => r.Week == 11).Sources.Sum(x => x.WeeklySupply + x.FoodAvailable);
        Assert.True(Supply(world) < Supply(twin), $"{Supply(world)} vs {Supply(twin)}");

        world.AgeSetting = null;
        world.AgeSetting = DemoAge.Frost;                    // 改回原樣：這一週等於沒改
        Assert.Single(world.AgeChanges);
        Weeks(world, 60);                                     // 跨過第 1 年底
        Assert.Equal(DemoAge.Frost, world.Age);
        var frost = world.Ages()[^1];
        Assert.Equal((DemoAge.Frost, 11, DemoAges.Forever, true), (frost.Age, frost.FromWeek, frost.ToWeek, frost.Manual));
        world.AgeSetting = null;                              // 改回自動輪替：接回輪替表（開局三十年內都是希望時代）
        Weeks(world, 1);
        Assert.Equal(DemoAge.Hope, world.Age);
        Assert.Equal(new[] { DemoAge.Hope, DemoAge.Frost, DemoAge.Hope }, world.Ages().Select(a => a.Age));
        Assert.Equal(new[] { 1, 11, 72 }, world.Ages().Select(a => a.FromWeek));
        Assert.Equal(new[] { new DemoAgeChange(10, DemoAge.Frost), new DemoAgeChange(71, null) }, world.AgeChanges);

        var era = DemoWorldChronicle.Build(world).SelectMany(y => y.Entries).Where(e => e.Category == DemoChronicleCategory.Era && e.Text.Contains("時代——", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { (11, "天命驟變，世界進入霜雪時代"), (72, "世界進入希望時代") },
            era.Select(e => (e.Week, e.Text[..e.Text.IndexOf("——", StringComparison.Ordinal)].Trim())));
        var volumes = DemoSaga.Write(world).Where(l => l.StartsWith("卷 ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, volumes.Count);
        Assert.StartsWith("卷 1　霜雪時代　第 1～1 年", volumes[0], StringComparison.Ordinal);
        Assert.StartsWith("卷 2　希望時代　第 2～2 年", volumes[1], StringComparison.Ordinal);

        var saved = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(saved);
        Assert.Equal(world.AgeChanges, loaded.AgeChanges);
        Assert.Equal(world.Ages(), loaded.Ages());
        Assert.Equal(saved, loaded.ExportTempSave());
        var verdict = DemoObserverWorld.VerifyTempSaveByReplay(saved);
        Assert.True(verdict.Ok, verdict.FirstDifference);
    }

    [Fact]
    public void AgeScheduleWithChangesMatchesTheWeeklyAge()
    {
        var rng = RngRegistry.Create(3UL);
        var changes = new[] { new DemoAgeChange(0, DemoAge.Dark), new DemoAgeChange(400, null), new DemoAgeChange(4000, DemoAge.Sun), new DemoAgeChange(4100, DemoAge.Hope) };
        var spans = DemoAges.Schedule(rng, changes, 5000);
        Assert.Equal(1, spans[0].FromWeek);
        Assert.Equal(DemoAges.Forever, spans[^1].ToWeek);
        for (var i = 1; i < spans.Count; i++)
        {
            Assert.Equal(spans[i - 1].ToWeek + 1, spans[i].FromWeek);
            Assert.NotEqual(spans[i - 1].Age, spans[i].Age);
        }
        foreach (var week in new[] { 1, 2, 400, 401, 2000, 3999, 4000, 4001, 4100, 4101, 5000 })
            Assert.Equal(DemoAges.AtWeek(rng, changes, week), spans.Single(s => s.FromWeek <= week && week <= s.ToWeek).Age);
        Assert.Equal(DemoAges.Schedule(rng, 77), DemoAges.Schedule(rng, Array.Empty<DemoAgeChange>(), DemoAges.LastWeek(77)));
        // 還沒生效的指定不列，目前這一段在本週結束。
        var pending = DemoAges.Schedule(rng, new[] { new DemoAgeChange(30, DemoAge.Chaos) }, 30);
        Assert.Equal((DemoAge.Hope, 30), (pending[^1].Age, pending[^1].ToWeek));
    }
}
