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
