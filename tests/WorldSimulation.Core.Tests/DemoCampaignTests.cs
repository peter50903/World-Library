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
    public void StatusFollowsTheMarchAndTheSiege()
    {
        // 第 08-46 章：戰況——行軍中（軍隊在路線上、還要幾週）、到了城下圍城（圍了幾週／要幾週）。
        var world = World.Value;
        var (map, sites) = (world.Map, world.Sites);
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        DemoCellRef At(DemoSettlement s) => new(s.Column, s.Row);
        var (a, c) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id).Select(y => (x, y)))
            .First(p => DemoRealms.WarRoute(map, At(p.x), At(p.y)).Count - 1 >= 3 * DemoRealms.TempMarchCellsPerWeek);
        var fold = new DemoRealms.Fold();
        fold.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        var war = fold.Wars[a.Id];
        var march = DemoRealms.Campaign(war, map, sites).MarchWeeks;
        DemoWarStatus At2(int week) => DemoRealms.Status(war, week, map, sites, fold.Liege, _ => true, _ => DemoSettlementTier.Village);
        var going = At2(6);
        Assert.Equal(DemoWarStage.Marching, going.Stage);
        Assert.Equal(going.Campaign.ArmyAt(5, 6), going.ArmyAt);
        Assert.Equal(5 + march - 6, going.MarchLeft);
        var arrived = At2(5 + march);
        Assert.Equal((DemoWarStage.Besieging, c.Id, 0, DemoRealms.TempSiegeWeeks(DemoSettlementTier.Village)),
            (arrived.Stage, arrived.Besieged, arrived.SiegeDone, arrived.SiegeNeed));
        Assert.Equal(At(c), arrived.ArmyAt);
        Assert.Equal(3, At2(5 + march + 3).SiegeDone);
    }

    [Fact]
    public void ALosingBesiegerStillTakesTheTownWhenItStarves()
    {
        // 第 08-47 章：會戰落下風時圍滿週數攻不下，圍到兩倍週數城內糧盡、照樣攻陷。
        var sites = World.Value.Sites;
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var (a, c) = (alive[0], alive[1]);
        var fold = new DemoRealms.Fold();
        fold.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        fold.Apply(Ev(6, a.Id, DemoSettlementEventKind.ConquestBattle, 20)); // 攻方死得多：會戰分數為負
        fold.Apply(Ev(6, c.Id, DemoSettlementEventKind.ConquestBattle, 5));
        Assert.True(DemoRealms.BattleScore(fold.Wars[a.Id]) < 0);
        var ctx = new DemoRealmContext(sites, id => id == a.Id ? 200 : id == c.Id ? 70 : 0, _ => 1, _ => null,
            id => id == a.Id ? DemoSettlementTier.City : DemoSettlementTier.Village, _ => false);
        var siege = DemoRealms.TempSiegeWeeks(DemoSettlementTier.Village);
        IReadOnlyList<DemoSettlementEvent> At(int week)
            => DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, ctx, new Dictionary<int, int>(), RngRegistry.Create(3), false, week * 100 + 50, conquestEra: false, wars: false);
        Assert.DoesNotContain(At(5 + siege), e => e.Kind == DemoSettlementEventKind.Occupied);
        Assert.DoesNotContain(At(5 + siege * DemoRealms.TempStarveTimes - 1), e => e.Kind == DemoSettlementEventKind.Occupied);
        var taken = Assert.Single(At(5 + siege * DemoRealms.TempStarveTimes), e => e.Kind == DemoSettlementEventKind.Occupied);
        Assert.Equal((c.Id, (long)a.Id), (taken.SettlementId, taken.Count));
        Assert.Contains("城內糧盡", taken.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void ARoutedOrOutnumberedArmyGoesHome()
    {
        // 第 08-49 章：野戰攻方死傷達守方 150% 以上＝潰敗撤兵；兵力掉到守方（含本土加成）70% 以下也撤兵。
        var world = World.Value;
        var (map, sites) = (world.Map, world.Sites);
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        DemoCellRef At(DemoSettlement s) => new(s.Column, s.Row);
        var (a, c) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id).Select(y => (x, y)))
            .First(p => DemoRealms.WarRoute(map, At(p.x), At(p.y)).Count - 1 >= 3 * DemoRealms.TempMarchCellsPerWeek);
        IReadOnlyList<DemoSettlementEvent> Advance(DemoRealms.Fold fold, int week, Func<int, long> residents, DemoHexWorld? withMap)
            => DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold,
                new DemoRealmContext(sites, residents, _ => 1, _ => null, id => id == a.Id ? DemoSettlementTier.City : DemoSettlementTier.Village, _ => false) { Map = withMap },
                new Dictionary<int, int>(), RngRegistry.Create(3), false, week * 100 + 50, conquestEra: false, wars: false);
        long Even(int id) => id == a.Id || id == c.Id ? 300 : 0;

        var routed = new DemoRealms.Fold();
        routed.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        var field = DemoRealms.Campaign(routed.Wars[a.Id], map, sites).FieldWeek;
        routed.Apply(Ev(field, a.Id, DemoSettlementEventKind.ConquestBattle, 30));
        routed.Apply(Ev(field, c.Id, DemoSettlementEventKind.ConquestBattle, 10));
        var end = Assert.Single(Advance(routed, field + 1, Even, map), e => e.Kind == DemoSettlementEventKind.WarEnded);
        Assert.Contains("野戰中被", end.Cause, StringComparison.Ordinal);
        // 野戰打贏（或打平）就照常行軍。
        var held = new DemoRealms.Fold();
        held.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        held.Apply(Ev(field, a.Id, DemoSettlementEventKind.ConquestBattle, 10));
        held.Apply(Ev(field, c.Id, DemoSettlementEventKind.ConquestBattle, 10));
        Assert.DoesNotContain(Advance(held, field + 1, Even, map), e => e.Kind == DemoSettlementEventKind.WarEnded);

        var weak = new DemoRealms.Fold();
        weak.Apply(Ev(5, a.Id, DemoSettlementEventKind.WarDeclared, c.Id));
        var retreat = Assert.Single(Advance(weak, 6, id => id == a.Id ? 100 : id == c.Id ? 200 : 0, null), e => e.Kind == DemoSettlementEventKind.WarEnded);
        Assert.Contains("兵力不支", retreat.Cause, StringComparison.Ordinal);
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
