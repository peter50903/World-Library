using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-9 章 B1：文化實體、開局文化、背景人口比例與代表人物的文化（08-8）。</summary>
public sealed class DemoCultureTests
{
    [Fact]
    public void TraitDrawsFollowPoolLimitsAndExclusiveGroups()
    {
        var pools = DemoCultures.Pool.GroupBy(e => e.PoolId).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Contains("POOL_CULTURE_LIVELIHOOD", pools.Keys);
        Assert.Contains("POOL_CULTURE_CUSTOM", pools.Keys);
        Assert.Equal("林地子民", DemoCultures.TraitName("CT_FOREST_FOLK"));
        var conditions = new HashSet<string> { "NEAR_FOREST", "NEAR_GRASSLAND", "NEAR_RIVER", "OPENING" };
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = seed;
            var traits = DemoCultures.DrawTraits(conditions, i => unchecked((s * 6364136223846793005UL) ^ (i * 1442695040888963407UL) ^ (s << 17)));
            Assert.Equal(traits.Count, traits.Distinct().Count());
            foreach (var (id, entries) in pools)
            {
                var drawn = traits.Count(t => entries.Any(e => e.TraitId == t));
                Assert.InRange(drawn, 0, entries.Max(e => e.DrawMax));
                if (entries.Min(e => e.DrawMin) > 0 && entries.Any(e => e.Required.All(conditions.Contains)))
                    Assert.True(drawn >= 1, $"{id} 至少要抽 1 個");
            }
            // 互斥組不重複；條件不成立的（遷徙之民要 FOUNDED）不會抽到。
            var groups = traits.Select(t => DemoCultures.Pool.First(e => e.TraitId == t).ExclusiveGroup).Where(g => g.Length > 0).ToList();
            Assert.Equal(groups.Count, groups.Distinct().Count());
            Assert.DoesNotContain("CT_WANDERERS", traits);
            Assert.DoesNotContain("CT_SEA_FOLK", traits);
        }
    }

    [Fact]
    public void BlendWeighsByPeopleAndAlwaysSumsToAThousand()
    {
        var a = ImmutableList.Create(new DemoCultureShare(1, 1000));
        var b = ImmutableList.Create(new DemoCultureShare(2, 1000));
        Assert.Equal(new[] { new DemoCultureShare(1, 500), new DemoCultureShare(2, 500) }, DemoCultures.Blend(a, 10, b, 10));
        Assert.Equal(new[] { new DemoCultureShare(1, 667), new DemoCultureShare(2, 333) }, DemoCultures.Blend(a, 2, b, 1));
        var mixed = DemoCultures.Blend(ImmutableList.Create(new DemoCultureShare(1, 333), new DemoCultureShare(2, 667)), 7, b, 3);
        Assert.Equal(1000, mixed.Sum(x => x.Permille));
    }

    [Fact]
    public void OpeningSettlementsGetTheirClusterCulture_AndPeopleCarryItWhenTheyMove()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        var culture = world.Culture;
        Assert.NotEmpty(culture.Cultures);
        Assert.Equal(Enumerable.Range(1, culture.Cultures.Count), culture.Cultures.Select(c => c.Id));
        Assert.All(culture.Cultures, c => Assert.Single(c.Traits, t => t is "CT_FAMILY_EXTENDED" or "CT_FAMILY_NUCLEAR"));
        foreach (var s in world.Sites.Settlements)
        {
            var share = Assert.Single(culture.SharesOf(s.Id));
            Assert.Equal(1000, share.Permille);
            // 婚嫁範圍（12 格）內的開局聚落同一個文化。
            foreach (var o in world.Sites.Settlements.Where(o => DemoKnowledge.HexDistance(world.Map, s, o) <= DemoCultures.TempClusterRange))
                Assert.Equal(share.CultureId, culture.SharesOf(o.Id)[0].CultureId);
        }
        Assert.Equal(culture, new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample).Culture); // 可重現

        for (var day = 0; day < 6 * 365; day++)
            world.AdvanceOneDay();
        culture = world.Culture;
        Assert.All(world.Sites.Settlements, s => Assert.Equal(1000, culture.SharesOf(s.Id).Sum(x => x.Permille)));
        Assert.All(world.MigrationsInTransit(), b => Assert.Equal(1000, culture.Batches[b.Id].Sum(x => x.Permille)));
        var reps = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).ToList();
        Assert.NotEmpty(reps);
        Assert.All(reps, r => Assert.NotNull(culture.CultureOfRepresentative(r.Id)));
        var place = world.Sites.Settlements.First(s => world.SettlementPopulation(s.Id) > 0);
        var lines = world.QueryCell(place.Column, place.Row).Lines;
        Assert.Contains(lines, l => l.StartsWith("文化：", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("的特性：", StringComparison.Ordinal));
        Assert.Contains(DemoWorldOverview.Of(world, 365).Lines(), l => l.Contains("文化", StringComparison.Ordinal));

        // 存讀檔後文化相同、續跑一致；竄改比例（合計不是 1000）會被拒讀。
        var save = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(save);
        Assert.Equal(world.Culture, loaded.Culture);
        for (var day = 0; day < 60; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
        var doc = System.Text.Json.Nodes.JsonNode.Parse(save)!;
        var first = doc["State"]!["Culture"]!["Settlements"]!.AsObject().First().Value!.AsArray()[0]!;
        first["Permille"] = 999;
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(System.Text.Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Fact]
    public void CultureTraitsChangeYieldResearchAndLeadership()
    {
        // 第 08-9 章 B2：生計傳統（地形成立）每項採集＋10%、最多＋20%；重學研究＋10%；尚武／草原子民狩獵＋25%；海之民造船＋25%；
        // 守舊鄰近傳播減半、開放加倍；強人統治改變村長人選。
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        var place = world.Sites.Settlements.First(s => DemoKnowledge.NearForest(world.Map, s));
        DemoCultureState With(params string[] traits) => DemoCultureState.Empty with
        {
            Cultures = ImmutableList.Create(new DemoCulture(1, "測試文化", 0, place.Id, traits.ToImmutableList())),
            Settlements = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(place.Id, ImmutableList.Create(new DemoCultureShare(1, 1000))) })
        };
        Assert.Equal(10, DemoCultures.LivelihoodYieldPercent(With("CT_FOREST_FOLK"), world.Map, place));
        Assert.Equal(0, DemoCultures.LivelihoodYieldPercent(With("CT_MARTIAL"), world.Map, place));
        Assert.True(DemoCultures.LivelihoodYieldPercent(With("CT_FOREST_FOLK", "CT_RIVER_FOLK", "CT_GRASSLAND_FOLK"), world.Map, place) <= DemoCultures.TempLivelihoodYieldMax);
        DemoTechNode Node(string id) => DemoTechTree.Of(new DemoTechnology(id));
        Assert.Equal(110, DemoCultures.ResearchPercent(new HashSet<string> { "CT_SCHOLARLY" }, Node("TECH_CROP_CULTIVATION")));
        Assert.Equal(125, DemoCultures.ResearchPercent(new HashSet<string> { "CT_MARTIAL" }, Node("TECH_HUNTING")));
        Assert.Equal(100, DemoCultures.ResearchPercent(new HashSet<string> { "CT_MARTIAL" }, Node("TECH_CROP_CULTIVATION")));
        Assert.Equal(125, DemoCultures.ResearchPercent(new HashSet<string> { "CT_SEA_FOLK" }, Node("TECH_BASIC_BOATBUILDING")));
        Assert.Equal(50, DemoCultures.DiffusionPercent(new HashSet<string> { "CT_TRADITIONAL" }));
        Assert.Equal(200, DemoCultures.DiffusionPercent(new HashSet<string> { "CT_OPEN" }));
        Assert.True(DemoCultures.StrongmanRule(With("CT_STRONGMAN"), place.Id));
        Assert.False(DemoCultures.StrongmanRule(With("CT_ELDER_COUNCIL"), place.Id));

        // 長跑：週帳的工具加成＝工具階梯＋生計傳統（讀檔驗證同一規則）。
        for (var day = 0; day < 2 * 365; day++)
            world.AdvanceOneDay();
        Assert.All(world.LatestWeek!.Settlements.Where(l => !world.Sites.Settlements.Single(s => s.Id == l.SettlementId).IsRuin), l =>
            Assert.Contains(l.ToolYieldPercent - DemoKnowledge.ToolYieldPercent(world.Knowledge, l.SettlementId, l.Week), new long[] { 0, 10, 20 }));
        // 生計傳統在開拓時代才生效：希望時代的工具加成只有工具階梯（第 08-9 章 B2）。
        if (world.Era == DemoEra.Hope)
            Assert.All(world.WeekReports().SelectMany(w => w.Settlements), l => Assert.Equal(DemoKnowledge.ToolYieldPercent(world.Knowledge, l.SettlementId, l.Week), l.ToolYieldPercent));
        Assert.Equal(world.ExportTempSave(), DemoObserverWorld.ImportTempSave(world.ExportTempSave()).ExportTempSave());
    }

    [Fact]
    public void MinoritiesAssimilateYearly_AndSeparatedGroupsSplitAfterFiftyYears()
    {
        // 第 08-9 章 B4：每年少數文化 2%（至少千分之 1）轉為主要文化；與本群分開（婚嫁範圍外）滿 50 年分化出子文化。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        var settlements = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        var (home, far) = settlements.SelectMany(a => settlements.Where(b => DemoKnowledge.HexDistance(world.Map, a, b) > DemoCultures.TempClusterRange)
            .Select(b => (a, b))).First();
        var shares = ImmutableSortedDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(home.Id, ImmutableList.Create(new DemoCultureShare(1, 900), new DemoCultureShare(2, 100))),
            KeyValuePair.Create(far.Id, ImmutableList.Create(new DemoCultureShare(1, 1000)))
        });
        var state = DemoCultureState.Empty with
        {
            Cultures = ImmutableList.Create(
                new DemoCulture(1, "甲文化", 0, home.Id, ImmutableList.Create("CT_FAMILY_NUCLEAR", "CT_MARTIAL", "CT_HONOR")),
                new DemoCulture(2, "乙文化", 0, far.Id, ImmutableList.Create("CT_FAMILY_EXTENDED"))),
            Settlements = shares
        };
        var assimilated = DemoCultures.Assimilate(state, world.Sites);
        Assert.Equal(new[] { new DemoCultureShare(1, 902), new DemoCultureShare(2, 98) }, assimilated.SharesOf(home.Id));

        var week = 52 * 60;
        var report = world.LatestWeek ?? new DemoWeekReport(week, TimeSpan.FromDays(week * 7), Array.Empty<DemoSettlementWeekLedger>(), Array.Empty<DemoSettlementEvent>(),
            Array.Empty<DemoMigrationWeekLedger>());
        report = report with { Week = week };
        // 第一年記分開起始週，還不分化；滿 50 年才分化。
        var (first, none) = DemoCultures.Advance(state, report, new HashSet<int>(), world.RngRegistryForTests(), world.Sites, world.Map, 500);
        Assert.Empty(none);
        Assert.Equal(week, first.SeparatedSince[far.Id]);
        var (later, split) = DemoCultures.Advance(first, report with { Week = week + DemoCultures.TempSplitWeeks }, new HashSet<int>(),
            world.RngRegistryForTests(), world.Sites, world.Map, 500);
        var e = Assert.Single(split);
        Assert.Equal(DemoSettlementEventKind.CultureSplit, e.Kind);
        var child = later.Of((int)e.Count)!;
        Assert.Equal(1, child.ParentId);
        Assert.Equal(3, child.Traits.Count);
        Assert.Single(child.Traits.Except(state.Cultures[0].Traits));
        Assert.Equal(child.Id, later.MainCultureOf(far.Id)!.Id);
        Assert.Equal(1, later.MainCultureOf(home.Id)!.Id);
        Assert.False(later.SeparatedSince.ContainsKey(far.Id));
    }

    [Fact]
    public void WorldsWithoutHumanLifecycleHaveNoCultures()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1));
        Assert.Empty(world.Culture.Cultures);
        for (var day = 0; day < 21; day++)
            world.AdvanceOneDay();
        Assert.Empty(DemoObserverWorld.ImportTempSave(world.ExportTempSave()).Culture.Cultures);
    }
}
