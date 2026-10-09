using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-03B: one world RngRegistry drives the TEMP map and TEMP settlements /
/// resource points / population groups, with isolated streams.
/// </summary>
public sealed class DemoWorldSitesTests
{
    [Fact]
    public void SameSeedProducesIdenticalMapAndSitesItemByItem()
    {
        var a = new DemoObserverWorld(DemoScenario.Sample(42));
        var b = new DemoObserverWorld(DemoScenario.Sample(42));

        Assert.Equal(a.Map.Cells.ToArray(), b.Map.Cells.ToArray());
        Assert.Equal(a.Sites.Settlements.ToArray(), b.Sites.Settlements.ToArray());
        Assert.Equal(a.Sites.ResourcePoints.ToArray(), b.Sites.ResourcePoints.ToArray());
        Assert.Equal(a.Sites.PopGroups.ToArray(), b.Sites.PopGroups.ToArray());
    }

    [Fact]
    public void DifferentSeedsDifferInAtLeastOneSiteItem()
    {
        var a = new DemoObserverWorld(DemoScenario.Sample(42)).Sites;
        var b = new DemoObserverWorld(DemoScenario.Sample(8128)).Sites;

        Assert.False(
            a.Settlements.SequenceEqual(b.Settlements) &&
            a.ResourcePoints.SequenceEqual(b.ResourcePoints) &&
            a.PopGroups.SequenceEqual(b.PopGroups));
    }

    [Fact]
    public void ObserverWorldMapEqualsStandaloneGenerationForSameSeed()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(99));
        Assert.Equal(DemoHexWorld.Generate(99).Cells.ToArray(), world.Map.Cells.ToArray());
        Assert.Equal((ulong)99, world.Map.Seed);
    }

    [Fact]
    public void MapAndSitesAreCachedAndNotRegeneratedByAdvancing()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(7));
        var map = world.Map;
        var sites = world.Sites;
        var cells = map.Cells.ToArray();
        var settlements = sites.Settlements.ToArray();

        for (var i = 0; i < 10; i++) world.AdvanceOneDay();

        Assert.Same(map, world.Map);
        Assert.Same(sites, world.Sites);
        Assert.Equal(cells, world.Map.Cells.ToArray());
        Assert.Equal(settlements, world.Sites.Settlements.ToArray());
    }

    [Fact]
    public void ExtraSiteDrawsDoNotChangeTerrain_StreamsAreIsolated()
    {
        const ulong seed = 31337;
        var plain = RngRegistry.Create(seed);
        var plainMap = DemoHexWorld.Generate(plain);

        // Consume extra ECOLOGY_INIT / POP / RESOURCE draws BEFORE generating terrain.
        var noisy = RngRegistry.Create(seed);
        for (var i = 0; i < 500; i++)
        {
            noisy.NextUInt64(RngStreams.EcologyInit);
            noisy.NextUInt64(RngStreams.Pop);
            noisy.NextUInt64(RngStreams.Resource);
        }
        var noisyMap = DemoHexWorld.Generate(noisy);

        Assert.Equal(plainMap.Cells.ToArray(), noisyMap.Cells.ToArray());
    }

    [Fact]
    public void SiteGenerationUsesOnlyEcologyInitAndPopStreams()
    {
        var rng = RngRegistry.Create(5);
        var map = DemoHexWorld.Generate(rng);
        var before = rng.Capture();
        var sites = DemoWorldSites.Generate(map, rng);

        var n = (ulong)sites.Settlements.Count;
        Assert.Equal(96UL, rng.GetDrawCounter(RngStreams.Geography));
        Assert.Equal(1UL + n, rng.GetDrawCounter(RngStreams.EcologyInit));
        Assert.Equal(n, rng.GetDrawCounter(RngStreams.Pop));
        Assert.Equal(0UL, rng.GetDrawCounter(RngStreams.Resource));

        var beforeById = before.Streams.ToDictionary(s => s.StreamId);
        foreach (var id in RngStreams.All.Except(new[] { RngStreams.EcologyInit, RngStreams.Pop }))
            Assert.Equal(beforeById[id].DrawCounter, rng.GetDrawCounter(id));
    }

    [Fact]
    public void FreshObserverWorldStillReportsZeroResourceDraws()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        Assert.Equal(0UL, world.Snapshot().ResourceRngDraws);
        world.AdvanceOneDay();
        Assert.Equal(1UL, world.Snapshot().ResourceRngDraws);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(8128UL)]
    [InlineData(123456UL)]
    [InlineData(ulong.MaxValue)]
    public void SitesAreWellFormedTempData(ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed));
        var sites = world.Sites;
        var config = DemoSitesTempConfig.Sample;

        Assert.InRange(sites.Settlements.Count, config.MinSettlements, config.MaxSettlements);
        Assert.Equal(sites.Settlements.Count, sites.ResourcePoints.Count);
        Assert.Equal(sites.Settlements.Count, sites.PopGroups.Count);

        // Distinct cells, valid coordinates, sequential demo IDs.
        Assert.Equal(sites.Settlements.Count,
            sites.Settlements.Select(s => (s.Column, s.Row)).Distinct().Count());
        for (var i = 0; i < sites.Settlements.Count; i++)
        {
            var s = sites.Settlements[i];
            Assert.Equal(i + 1, s.Id);
            Assert.Equal(config.SettlementNames[i], s.Name);
            var cell = world.Map.GetCell(s.Column, s.Row);
            Assert.Same(s, sites.SettlementAt(s.Column, s.Row));

            var resource = Assert.Single(sites.ResourcePointsOf(s.Id));
            Assert.Equal((s.Column, s.Row), (resource.Column, resource.Row));
            var expectedKind = cell.Terrain switch
            {
                DemoTerrain.Grassland => DemoResourceKind.Farmland,
                DemoTerrain.Forest => DemoResourceKind.Woodland,
                _ => DemoResourceKind.Fishery
            };
            Assert.Equal(expectedKind, resource.Kind);

            var pop = Assert.Single(sites.PopGroupsOf(s.Id));
            Assert.InRange(pop.Size, config.MinPopGroupSize, config.MaxPopGroupSize);
        }
    }

    [Fact]
    public void SettlementsPreferLandWhenLandIsAvailable()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed));
            var landCells = world.Map.Cells.Count(c => c.Terrain != DemoTerrain.Water);
            if (landCells < DemoSitesTempConfig.Sample.MaxSettlements)
                continue;

            Assert.All(world.Sites.Settlements,
                s => Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(s.Column, s.Row).Terrain));
        }
    }

    [Fact]
    public void GenerationRejectsReusedStreams()
    {
        var rng = RngRegistry.Create(3);
        var map = DemoHexWorld.Generate(rng);

        Assert.Throws<InvalidOperationException>(() => DemoHexWorld.Generate(rng));

        _ = DemoWorldSites.Generate(map, rng);
        Assert.Throws<InvalidOperationException>(() => DemoWorldSites.Generate(map, rng));
    }

    [Fact]
    public void SiteViewsAreReadOnly()
    {
        var sites = new DemoObserverWorld(DemoScenario.Sample(42)).Sites;
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DemoSettlement>)sites.Settlements).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DemoResourcePoint>)sites.ResourcePoints).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DemoPopGroup>)sites.PopGroups).Clear());
        Assert.Null(sites.SettlementAt(-1, -1));
    }
}
