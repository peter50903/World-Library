using System.Collections.Immutable;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-8 章 B3：信仰的創立（抽信條與教義）、改信、隨世界推進與存檔。</summary>
public sealed class DemoFaithTests
{
    [Fact]
    public void FoundingDrawsTwoToFourTenetsAndOneDoctrinePerMandatoryPool()
    {
        var mandatory = new[] { "POOL_REL_MARRIAGE", "POOL_REL_CONVERSION", "POOL_REL_FUNERAL", "POOL_REL_WAR" };
        for (ulong seed = 0; seed < 40; seed++)
        {
            var (tenets, doctrines) = DemoFaiths.Draw(new[] { "CT_MARTIAL" }, i => (seed + 1) * 6364136223846793005UL + i * 1442695040888963407UL);
            Assert.InRange(tenets.Count, 2, DemoFaiths.MaxTenets);
            Assert.Equal(tenets.Count, tenets.Distinct().Count());
            Assert.False(tenets.Contains("DOC_WAR") && tenets.Contains("DOC_PEACE"));
            if (tenets.Contains("DOC_SACRED_GROVE"))
                Assert.Contains("DOC_NATURE", tenets);
            if (tenets.Contains("DOC_CANNIBALISM"))
                Assert.DoesNotContain("DOC_MERCY", tenets);
            foreach (var pool in mandatory)
                Assert.Single(doctrines, d => DemoFaiths.Pool.First(e => e.EntryId == d).PoolId == pool);
        }
    }

    [Fact]
    public void FarFromTheHolySiteASettlementSplitsIntoAHeresy()
    {
        // 第 08-22d 章：聖地遙遠的聚落，信仰創立滿二十年後每年 6% 另立異端；本聚落的信徒全部改信。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (holy, far) = alive.SelectMany(a => alive.Select(b => (a, b)))
            .First(p => p.a.Id != p.b.Id && DemoKnowledge.Distance(world.Map, p.a, p.b) >= DemoFaiths.TempSchismDistance);
        var faith = new DemoFaith(1, "測試祖靈信仰", holy.Id, 0, 1, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty);
        var believers = ImmutableList.Create(new DemoCultureShare(1, 1000));
        var state = DemoFaithState.Empty with
        {
            Faiths = ImmutableList.Create(faith),
            Settlements = ImmutableList.Create(new DemoSettlementCulture(Math.Min(holy.Id, far.Id), believers), new DemoSettlementCulture(Math.Max(holy.Id, far.Id), believers))
        };
        DemoSettlementWeekLedger Ledger(int week, DemoSettlement s) => new(week, s.Id, s.Name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 50, 0, 0, 50);
        DemoSettlementEvent? schism = null;
        for (var year = 0; year < 200 && schism is null; year++)
        {
            var week = DemoFaiths.TempSchismMinWeeks + 1 + year * DemoFaiths.TempSchismCheckWeeks;
            week += (far.Id % DemoFaiths.TempSchismCheckWeeks - week % DemoFaiths.TempSchismCheckWeeks + DemoFaiths.TempSchismCheckWeeks) % DemoFaiths.TempSchismCheckWeeks;
            var report = new DemoWeekReport(week, TimeSpan.FromDays(week * 7), new[] { Ledger(week, holy), Ledger(week, far) });
            var (next, events) = DemoFaiths.Advance(state, report, Array.Empty<DemoSettlementEvent>(), world.Sites, world.Map, RngRegistry.Create(3),
                Array.Empty<DemoRepresentative>(), _ => Array.Empty<string>(), 1000);
            Assert.DoesNotContain(events, e => e.Kind == DemoSettlementEventKind.FaithSchism && e.SettlementId == holy.Id); // 聖地自己不分裂
            schism = events.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.FaithSchism);
            if (schism is not null)
                state = next;
        }
        Assert.NotNull(schism);
        Assert.Equal(far.Id, schism!.SettlementId);
        Assert.Contains("另立異端", schism.Cause, StringComparison.Ordinal);
        var heresy = Assert.Single(state.Faiths, f => f.Id == schism.Count);
        Assert.Equal((far.Id, $"{far.Name}教派"), (heresy.FounderSettlementId, heresy.Name));
        Assert.Contains(state.Settlements.Single(s => s.SettlementId == far.Id).Shares, x => x.CultureId == heresy.Id && x.Permille == 1000);
        Assert.Contains(state.Settlements.Single(s => s.SettlementId == holy.Id).Shares, x => x.CultureId == 1);
    }

    // 第 08-22g 章：狂熱度＝基本 40＋聖地在手 30（失去 −20）＋每個聚落 2－每個異端 15；同源誤入歧途、不同源敵對。
    [Fact]
    public void FervorCountsTheHolySiteBelieversAndHeresiesAndRelatedFaithsAreAstray()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (a, b, c) = (alive[0], alive[1], alive[2]);
        var root = new DemoFaith(1, "甲信仰", a.Id, 0, 1, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty);
        var heresy = new DemoFaith(2, "乙教派", b.Id, 0, 2, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty, ParentId: 1);
        var other = new DemoFaith(3, "丙信仰", c.Id, 0, 3, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty);
        var faiths = ImmutableList.Create(root, heresy, other);
        var mains = new Dictionary<int, int?> { [a.Id] = 1, [b.Id] = 2, [c.Id] = 1 }; // 丙信仰的聖地改信了甲信仰
        var living = new[] { a, b, c };
        var (value, factors) = DemoFaiths.Fervor(faiths, 1, living, id => mains.GetValueOrDefault(id));
        Assert.Equal(DemoFaiths.TempFervorBase + DemoFaiths.TempFervorHolySite + 2 * DemoFaiths.TempFervorPerSettlement + DemoFaiths.TempFervorPerHeresy, value);
        Assert.Contains(factors, f => f.Text == "聖地在手" && f.Value == DemoFaiths.TempFervorHolySite);
        Assert.Equal(DemoFaiths.TempFervorBase + DemoFaiths.TempFervorHolySiteLost, DemoFaiths.Fervor(faiths, 3, living, id => mains.GetValueOrDefault(id)).Value);
        var state = DemoFaithState.Empty with { Faiths = faiths };
        Assert.Equal(0, DemoFaiths.Hostility(state, 1, 1));
        Assert.Equal(1, DemoFaiths.Hostility(state, 1, 2)); // 異端與母信仰同源
        Assert.Equal(2, DemoFaiths.Hostility(state, 2, 3));
        Assert.Equal("誤入歧途", DemoFaiths.HostilityName(1));
    }

    // 第 08-22g 章：上級的主要信仰和自己不同時，下級慢慢改信（其他信仰的信徒也會）；主要信仰換掉時記「改宗」。
    [Fact]
    public void VassalsSlowlyConvertToTheirLiegesFaith()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (lord, vassal) = (alive[0], alive[1]);
        var state = DemoFaithState.Empty with
        {
            Faiths = ImmutableList.Create(
                new DemoFaith(1, "上級信仰", lord.Id, 0, 1, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty),
                new DemoFaith(2, "下級信仰", vassal.Id, 0, 1, ImmutableList.Create("DOC_MERCY"), ImmutableList<string>.Empty)),
            Settlements = ImmutableList.Create(
                new DemoSettlementCulture(lord.Id, ImmutableList.Create(new DemoCultureShare(1, 1000))),
                new DemoSettlementCulture(vassal.Id, ImmutableList.Create(new DemoCultureShare(2, 1000))))
        };
        DemoSettlementWeekLedger Ledger(int week, DemoSettlement s) => new(week, s.Id, s.Name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 50, 0, 0, 50);
        DemoSettlementEvent? converted = null;
        for (var week = 1; week <= 600 && converted is null; week++)
        {
            var report = new DemoWeekReport(week, TimeSpan.FromDays(week * 7), new[] { Ledger(week, lord), Ledger(week, vassal) });
            var (next, events) = DemoFaiths.Advance(state, report, Array.Empty<DemoSettlementEvent>(), world.Sites, world.Map, RngRegistry.Create(3),
                Array.Empty<DemoRepresentative>(), _ => Array.Empty<string>(), 1000, liegeOf: id => id == vassal.Id ? (int?)lord.Id : null);
            Assert.All(next.Settlements, x => Assert.Equal(1000, x.Shares.Sum(y => y.Permille)));
            Assert.DoesNotContain(events, e => e.Kind == DemoSettlementEventKind.FaithConverted && e.SettlementId == lord.Id); // 上級不改信
            converted = events.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.FaithConverted);
            state = next;
        }
        Assert.NotNull(converted);
        Assert.Equal((vassal.Id, 1L), (converted!.SettlementId, converted.Count));
        Assert.Contains("改信上級", converted.Cause, StringComparison.Ordinal);
        Assert.Equal(1, state.MainFaithOf(vassal.Id));
        Assert.Equal(1, state.MainFaithOf(lord.Id));
    }

    [Fact]
    public void ConvertAllMovesEveryOtherFaithAndKeepsTheTotal()
    {
        // 第 08-22g 章：其他信仰（含無信仰）各改信一定比例，合計不變；太少時至少 1‰（從人數最多的一群扣）。
        var shares = ImmutableList.Create(new DemoCultureShare(0, 200), new DemoCultureShare(1, 300), new DemoCultureShare(2, 500));
        var after = DemoFaiths.ConvertAll(shares, 1, 1000);
        Assert.Equal(new[] { (0, 180), (1, 370), (2, 450) }, after.Select(s => (s.CultureId, s.Permille)));
        var tiny = DemoFaiths.ConvertAll(shares, 1, 1);
        Assert.Equal(new[] { (0, 200), (1, 301), (2, 499) }, tiny.Select(s => (s.CultureId, s.Permille)));
        IReadOnlyList<DemoCultureShare> all = ImmutableList.Create(new DemoCultureShare(1, 1000));
        Assert.Same(all, DemoFaiths.ConvertAll(all, 1, 1000)); // 沒有其他信仰的人
    }

    [Fact]
    public void ConversionMovesNonBelieversAtLeastOnePermillePerWeek()
    {
        var after = DemoFaiths.Convert(DemoFaiths.NoFaith, 1, DemoFaiths.TempFounderRate);
        Assert.Equal(new[] { (0, 980), (1, 20) }, after.Select(s => (s.CultureId, s.Permille)));
        var tiny = DemoFaiths.Convert(new[] { new DemoCultureShare(0, 3), new DemoCultureShare(1, 997) }, 1, DemoFaiths.TempNeighbourRate);
        Assert.Equal(new[] { (0, 2), (1, 998) }, tiny.Select(s => (s.CultureId, s.Permille)));
        var full = new[] { new DemoCultureShare(2, 1000) };
        Assert.Same(full, DemoFaiths.Convert(full, 1, 200));
    }

    [Fact]
    public void AncestorRitesFoundFaithsThatSpreadAndSurviveSaving()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var rites = DemoTechnology.Of(DemoFaiths.AncestorRites);
        for (var d = 0; d < 100 * TestCalendar.DaysPerYear && world.Culture.Faith.Faiths.IsEmpty; d++)
            world.AdvanceOneDay();
        var faith = world.Culture.Faith;
        var learners = world.Knowledge.Records.Count(r => r.Learned && r.Technology == rites);
        Assert.True(!faith.Faiths.IsEmpty, $"百年內沒有信仰；習得祖靈祭祀的聚落 {learners} 個");
        for (var d = 0; d < 10 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay(); // 創立後再走 10 年，讓信仰傳播
        faith = world.Culture.Faith;
        foreach (var f in faith.Faiths)
        {
            Assert.InRange(f.Tenets.Count, 2, DemoFaiths.MaxTenets);
            Assert.NotEmpty(f.Doctrines);
        }
        Assert.Contains(faith.Settlements, s => s.Shares.Any(x => x.CultureId != 0));
        Assert.All(faith.Settlements, s => Assert.Equal(1000, s.Shares.Sum(x => x.Permille)));
        Assert.Contains(DemoWorldOverview.Of(world, TestCalendar.DaysPerYear).Lines(), l => l.StartsWith("信仰 ", StringComparison.Ordinal));
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Culture.Fingerprint(), reloaded.Culture.Fingerprint());
    }
}
