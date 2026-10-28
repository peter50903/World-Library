using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10a 章 V2：家系地圖（聚落佔本格與相鄰陸地格、本格優先、家系追到開局聚落）。</summary>
public sealed class DemoLineageMapTests
{
    private static DemoObserverWorld Living(ulong seed, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < years * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    }

    private static readonly Lazy<DemoObserverWorld> Shared = new(() => Living(3, 25));

    [Fact]
    public void YearChangesEndWithTheCurrentSettlementsVillagesAndKnowledge()
    {
        var world = Shared.Value;
        var changes = DemoYearChanges.Of(world);
        Assert.Equal(Enumerable.Range(1, 25), changes.Keys.OrderBy(y => y));
        var last = changes[25];
        Assert.Equal(world.Sites.Settlements.Count(s => !s.IsRuin), last.Settlements);
        Assert.Equal(world.Sites.Settlements.Count(s => !s.IsRuin && world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.Village), last.Villages);
        Assert.Equal(world.Knowledge.Records.Count(r => r.Learned), last.KnowledgeLearned);
        // 開局聚落第 1 年就在；技術累計不會減少。
        Assert.True(changes[1].Settlements >= world.Sites.Settlements.Count(s => !s.IsFounded && !s.IsRuin));
        Assert.All(Enumerable.Range(2, 24), y => Assert.True(changes[y].KnowledgeLearned >= changes[y - 1].KnowledgeLearned));
    }

    [Fact]
    public void SettlementsOwnTheirCellAndNearbyLandAndCarryTheirLineage()
    {
        var world = Shared.Value;
        var cells = DemoLineageMap.Compute(world.Map, world.Sites);
        var roots = DemoLineageMap.Roots(world.Sites);
        var byId = world.Sites.Settlements.ToDictionary(s => s.Id);

        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
            Assert.Equal(s.Id, cells[new DemoCellRef(s.Column, s.Row)].SettlementId);
        foreach (var (cell, owner) in cells)
        {
            var hex = world.Map.GetCell(cell.Column, cell.Row);
            var site = byId[owner.SettlementId];
            var home = world.Map.GetCell(site.Column, site.Row);
            Assert.False(site.IsRuin);
            Assert.True(cell == new DemoCellRef(site.Column, site.Row) || hex.Terrain != DemoTerrain.Water);
            var distance = (Math.Abs(hex.AxialQ - home.AxialQ) + Math.Abs(hex.AxialR - home.AxialR)
                + Math.Abs(hex.AxialQ + hex.AxialR - home.AxialQ - home.AxialR)) / 2;
            Assert.InRange(distance, 0, 1);
            Assert.Equal(DemoLineageMap.LineageRoot(world.Sites, owner.SettlementId), owner.LineageRootId);
            Assert.Contains(owner.LineageRootId, roots);
            Assert.False(byId[owner.LineageRootId].IsFounded);
        }
        Assert.Equal(roots.OrderBy(r => r), roots);
    }
}
