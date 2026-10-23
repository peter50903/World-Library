using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-9 章 B3：信仰的創立（祖靈祭祀）、信條與教義、擴散、無信仰者、滅絕（08-8）。</summary>
public sealed class DemoFaithTests
{
    [Fact]
    public void DrawnFaithsFollowTheGenerationRules()
    {
        var mandatory = DemoFaiths.DoctrinePool.GroupBy(e => e.PoolId).Where(g => g.Min(e => e.DrawMin) >= 1).Select(g => g.Key).ToList();
        Assert.NotEmpty(mandatory);
        Assert.Equal("武裝朝聖", DemoFaiths.Name("DOC_ARMED_PILGRIMAGE"));
        Assert.Equal("食人儀式", DemoFaiths.Name("DOC_CANNIBALISM"));
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = seed;
            var (tenets, doctrines) = DemoFaiths.Draw(new HashSet<string> { "CT_MARTIAL" },
                i => unchecked((s * 6364136223846793005UL) ^ ((i + 7) * 1442695040888963407UL) ^ (s << 21)));
            Assert.InRange(tenets.Count, 2, 4);
            Assert.All(tenets, t => Assert.True(DemoFaiths.IsKnownTenet(t)));
            Assert.All(doctrines, d => Assert.True(DemoFaiths.IsKnownDoctrine(d)));
            foreach (var pool in mandatory)
                Assert.Single(doctrines, d => DemoFaiths.DoctrinePool.Any(e => e.PoolId == pool && e.TraitId == d));
            // 條件：武裝朝聖要有朝聖傳統、不與和平並存；食人儀式不與慈悲、聖宴並存；聖樹崇拜要有自然。
            if (tenets.Contains("DOC_ARMED_PILGRIMAGE"))
                Assert.True(tenets.Contains("DOC_PILGRIMAGE") && !tenets.Contains("DOC_PEACE"));
            if (tenets.Contains("DOC_CANNIBALISM"))
                Assert.False(tenets.Contains("DOC_MERCY") || tenets.Contains("DOC_RITUAL_FEAST"));
            if (tenets.Contains("DOC_SACRED_GROVE"))
                Assert.Contains("DOC_NATURE", tenets);
            Assert.False(tenets.Contains("DOC_WAR") && tenets.Contains("DOC_PEACE")); // 互斥組
        }
    }

    [Fact]
    public void AncestorRitesFoundAFaith_OrJoinOneNearby_AndUnbelieversConvertSlowly()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 14; day++)
            world.AdvanceOneDay();
        var report = world.LatestWeek!;
        var reps = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).ToList();
        var (s, o) = world.Sites.Settlements.Where(x => reps.Any(r => r.SettlementId == x.Id && r.Role == DemoRepresentativeRole.Leader))
            .SelectMany(x => world.Sites.Settlements.Where(y => y.Id != x.Id && !y.IsRuin
                && DemoKnowledge.HexDistance(world.Map, x, y) <= DemoFaiths.TempContactRange).Select(y => (x, y)))
            .First();
        var rites = DemoTechTree.Of(DemoFaiths.AncestorRites).Index;
        var learnedHere = new DemoSettlementEvent(report.Week, report.WorldTime, s.Id, s.Name, DemoSettlementEventKind.KnowledgeLearned, rites, "測試", EventId: 800);
        var transit = world.MigrationsInTransit().Select(b => b.Id).ToHashSet();
        var (first, events) = DemoFaiths.Advance(DemoFaithState.Empty, report, new[] { learnedHere }, world.Sites, world.Map, world.Culture,
            reps, transit, world.RngRegistryForTests(), 801);
        var founded = Assert.Single(events, e => e.Kind == DemoSettlementEventKind.FaithFounded);
        Assert.Equal(800, founded.CauseEventId);
        var faith = Assert.Single(first.Faiths);
        Assert.Equal(reps.Single(r => r.SettlementId == s.Id && r.Role == DemoRepresentativeRole.Leader).Id, faith.FounderId);
        Assert.Equal(faith, first.FaithOfRepresentative(faith.FounderId));
        // 創立聚落第一週 2% 改信（倍率依教義）；其他人仍無信仰。
        Assert.Equal(DemoFaiths.TempFounderConversionPermille * DemoFaiths.SpreadPercent(faith) / 100, first.PermilleOf(s.Id, faith.Id));
        Assert.Equal(1000, first.SharesOf(s.Id).Sum(x => x.Permille));

        // 鄰近聚落習得祖靈祭祀：接觸既有信仰，不另創。
        var learnedThere = learnedHere with { SettlementId = o.Id, SettlementName = o.Name, EventId = 900 };
        var (second, more) = DemoFaiths.Advance(first, report, new[] { learnedThere }, world.Sites, world.Map, world.Culture, reps, transit,
            world.RngRegistryForTests(), 901);
        Assert.DoesNotContain(more, e => e.Kind == DemoSettlementEventKind.FaithFounded);
        Assert.Single(second.Faiths);
        Assert.True(second.PermilleOf(o.Id, faith.Id) > 0); // 鄰近擴散

        // 只有無信仰者的世界：信仰全部滅絕。
        var emptied = first with { Settlements = ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>>.Empty,
            Batches = ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>>.Empty,
            Representatives = ImmutableSortedDictionary<long, int>.Empty };
        var noReps = Array.Empty<DemoRepresentative>();
        // 之後的一週、沒有遷徙與就任：創立聚落已無信徒，不會自己復燃。
        var quietReport = report with { Week = report.Week + 1, Appointments = Array.Empty<DemoRepresentative>(),
            Migrations = Array.Empty<DemoMigrationWeekLedger>() };
        var (gone, extinct) = DemoFaiths.Advance(emptied, quietReport, Array.Empty<DemoSettlementEvent>(), world.Sites, world.Map, world.Culture,
            noReps, transit, world.RngRegistryForTests(), 1000);
        Assert.True(gone.Faiths[0].Extinct);
        Assert.Single(extinct, e => e.Kind == DemoSettlementEventKind.FaithExtinct);
    }

    [Fact]
    public void EveryoneStartsWithoutFaith_AndTheFirstFaithIsSavedAndShown()
    {
        DemoObserverWorld? world = null;
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            Assert.Empty(world.Faith.Faiths); // 開局人人無信仰
            for (var day = 0; day < 90 * 365 && world.Faith.Faiths.Count == 0; day++)
                world.AdvanceOneDay();
            if (world.Faith.Faiths.Count > 0) break;
        }
        var faith = Assert.Single(world!.Faith.Faiths);
        Assert.Contains(world.SettlementEvents(), e => e.Kind == DemoSettlementEventKind.FaithFounded && e.Count == faith.Id);
        Assert.Equal("TEMP_EV_FAITH_FOUNDED", DemoEventTypes.Of(DemoSettlementEventKind.FaithFounded).TypeId);
        var origin = world.Sites.Settlements.Single(s => s.Id == faith.OriginSettlementId);
        Assert.Contains(world.QueryCell(origin.Column, origin.Row).Lines, l => l.StartsWith("信仰：", StringComparison.Ordinal));
        Assert.Contains(DemoSettlementHistory.Lines(world, origin.Id), l => l.Contains("創立信仰", StringComparison.Ordinal));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Faith, loaded.Faith);
        for (var day = 0; day < 30; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }
}
