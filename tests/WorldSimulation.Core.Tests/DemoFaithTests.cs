using System.Collections.Immutable;
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
