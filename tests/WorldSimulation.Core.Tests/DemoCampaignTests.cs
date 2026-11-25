using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-45 章：行軍、野戰、圍城，攻下目標後接著佔領守方附近的聚落。</summary>
public sealed class DemoCampaignTests
{
    private static readonly Lazy<DemoObserverWorld> World = new(() =>
        new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample));

    private static DemoSettlementEvent Ev(int week, int id, DemoSettlementEventKind kind, long count)
        => new(week, TimeSpan.FromDays(week * 7), id, "", kind, count, "", EventId: week * 100 + id);

    [Fact]
    public void ArmiesMarchMeetInTheFieldAndThenBesiege()
    {
        var world = World.Value;
        var (map, sites) = (world.Map, world.Sites);
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        DemoCellRef At(DemoSettlement s) => new(s.Column, s.Row);
        var (a, c) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id).Select(y => (x, y)))
            .First(p => DemoRealms.WarRoute(map, At(p.x), At(p.y)).Count - 1 >= 3 * DemoRealms.TempMarchCellsPerWeek);
        var fold = new DemoRealms.Fold();
        fold.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        var war = fold.Wars[a.Id];
        var campaign = DemoRealms.Campaign(war, map, sites);
        var route = DemoRealms.WarRoute(map, At(a), At(c));
        Assert.Equal(route, campaign.Route);
        Assert.Equal((At(a), At(c)), (campaign.Route[0], campaign.Route[^1]));
        Assert.Equal((route.Count - 1 + DemoRealms.TempMarchCellsPerWeek - 1) / DemoRealms.TempMarchCellsPerWeek, campaign.MarchWeeks);
        Assert.InRange(campaign.FieldWeek, 6, 5 + campaign.MarchWeeks);
        Assert.Contains(campaign.FieldAt, campaign.Route);
        Assert.Equal(At(a), campaign.ArmyAt(5, 5));
        Assert.Equal(At(c), campaign.ArmyAt(5, 5 + campaign.MarchWeeks));
        Assert.Same(DemoCampaign.None, DemoRealms.Campaign(war, null, sites));

        // 兵力相當：守方出城，在半路打一場野戰；其他行軍週不交戰；到了城下才圍城交戰。
        long Residents(int id) => id == a.Id || id == c.Id ? 300 : 0;
        var none = new HashSet<int>();
        for (var week = 6; week <= 5 + campaign.MarchWeeks; week++)
        {
            var strikes = DemoRealms.Battles(week, fold, sites, Residents, none, RngRegistry.Create(1), map: map);
            if (week == campaign.FieldWeek)
            {
                Assert.Contains("野戰", strikes[c.Id].Cause, StringComparison.Ordinal);
                Assert.Contains("野戰", strikes[a.Id].Cause, StringComparison.Ordinal);
            }
            else
                Assert.Empty(strikes);
        }
        var siege = DemoRealms.Battles(6 + campaign.MarchWeeks, fold, sites, Residents, none, RngRegistry.Create(1), map: map);
        Assert.StartsWith("征服戰爭：抵抗", siege[c.Id].Cause, StringComparison.Ordinal);
        // 守方太弱不出城：行軍途中都不交戰。
        long Weak(int id) => id == a.Id ? 2000 : id == c.Id ? 100 : 0;
        Assert.Empty(DemoRealms.Battles(campaign.FieldWeek, fold, sites, Weak, none, RngRegistry.Create(1), map: map));
        // 沒有地圖（舊規則）：開戰下一週就在城下交戰。
        Assert.Contains(c.Id, DemoRealms.Battles(6, fold, sites, Residents, none, RngRegistry.Create(1)).Keys);
    }

    [Fact]
    public void AfterTheTargetFallsTheArmyOccupiesNearbyHoldingsForWarScore()
    {
        var world = World.Value;
        var sites = world.Sites;
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        int D(DemoSettlement x, DemoSettlement y) => DemoHexWorld.Distance(new DemoCellRef(x.Column, x.Row), new DemoCellRef(y.Column, y.Row));
        var (c, d) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id && D(x, y) <= DemoRealms.TempOccupyRange).Select(y => (x, y))).First();
        var a = alive.First(s => s.Id != c.Id && s.Id != d.Id);
        var fold = new DemoRealms.Fold();
        fold.Apply(Ev(1, d.Id, DemoSettlementEventKind.VassalJoined, c.Id));
        fold.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        bool Alive(int id) => id == a.Id || id == c.Id || id == d.Id;
        Assert.Equal(c.Id, DemoRealms.NextHolding(fold.Wars[a.Id], sites, fold.Liege, Alive));
        fold.Apply(Ev(20, c.Id, DemoSettlementEventKind.Occupied, a.Id));
        var held = fold.Wars[a.Id];
        Assert.Equal(d.Id, DemoRealms.NextHolding(held, sites, fold.Liege, Alive));
        // 圍攻 d 的交戰算在守方的損失。
        fold.Apply(Ev(22, d.Id, DemoSettlementEventKind.ConquestBattle, 7));
        Assert.Equal(7, fold.Wars[a.Id].DefenderLosses);
        var before = DemoRealms.WarScore(fold.Wars[a.Id], 26);
        fold.Apply(Ev(26, d.Id, DemoSettlementEventKind.Occupied, a.Id));
        var more = fold.Wars[a.Id];
        Assert.Equal(new[] { d.Id }, more.Holdings);
        Assert.Equal((20, 26), (more.OccupiedWeek, more.LastOccupiedWeek));
        Assert.Equal(Math.Min(100, before + DemoRealms.TempHoldingScore), DemoRealms.WarScore(more, 26));
        Assert.Equal(0, DemoRealms.NextHolding(more, sites, fold.Liege, Alive)); // 附近沒有別的了
        // 議和只割讓戰爭目標：征服後 d 仍屬 c。
        fold.Apply(Ev(40, c.Id, DemoSettlementEventKind.Conquered, a.Id));
        Assert.Equal(a.Id, fold.Liege[c.Id]);
        Assert.Equal(c.Id, fold.Liege[d.Id]);
        Assert.Empty(fold.Wars);
    }
}
