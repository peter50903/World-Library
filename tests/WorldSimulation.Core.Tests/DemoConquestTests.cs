using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19e 章：征服時代的王國戰爭（宣戰、交戰、征服、白和平與停戰）。</summary>
public sealed class DemoConquestTests
{
    /// <summary>王國 a（城市、有臣屬 b）與 10 格內獨立的 c。</summary>
    private static (DemoWorldSites Sites, int A, int B, int C) Setup()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        int D(DemoSettlement x, DemoSettlement y) => DemoHexWorld.Distance(new DemoCellRef(x.Column, x.Row), new DemoCellRef(y.Column, y.Row));
        var (a, c) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id && D(x, y) <= DemoRealms.TempConquestReach).Select(y => (x, y))).First();
        var b = alive.First(s => s.Id != a.Id && s.Id != c.Id);
        return (world.Sites, a.Id, b.Id, c.Id);
    }

    private static DemoRealmContext Ctx(DemoWorldSites sites, int king, Func<int, long> residents, Func<int, int?>? faith = null)
        => new(sites, residents, _ => 1, faith ?? (_ => null), id => id == king ? DemoSettlementTier.City : DemoSettlementTier.Village, _ => false);

    private static IReadOnlyList<DemoSettlementEvent> Step(DemoRealms.Fold fold, int week, DemoRealmContext ctx, RngRegistry rng, bool conquest = true, bool cityWars = false)
    {
        var battles = DemoRealms.Battles(week, fold, ctx.Sites, ctx.Residents, new HashSet<int>(), RngRegistry.Create(1)).Values
            .Select((b, k) => new DemoSettlementEvent(week, TimeSpan.FromDays(week * 7), b.SettlementId, "", b.Kind, b.Deaths, b.Cause, EventId: week * 100 + k))
            .ToList();
        foreach (var e in battles)
            fold.Apply(e);
        var events = DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, ctx, new Dictionary<int, int>(), rng, false, week * 100 + 50, conquest, cityWars: cityWars);
        foreach (var e in events)
            fold.Apply(e);
        return battles.Concat(events).ToList();
    }

    [Fact]
    public void StrongKingdomDeclaresFightsAndConquers()
    {
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        var ctx = Ctx(sites, a, id => id == a ? 200 : id == b ? 100 : id == c ? 70 : 0, id => id == c ? 2 : 1); // 兵力 30 對 7：不到五倍，要打
        var rng = RngRegistry.Create(7);
        var all = new List<DemoSettlementEvent>();
        for (var week = 2; week < 2 + 13 * 60 && !all.Any(e => e.Kind is DemoSettlementEventKind.Conquered or DemoSettlementEventKind.WarEnded); week++)
            all.AddRange(Step(fold, week, ctx, rng));
        var declared = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.WarDeclared);
        Assert.Equal((a, (long)c), (declared.SettlementId, declared.Count));
        Assert.Contains("聖戰", declared.Cause, StringComparison.Ordinal); // 信仰不同
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.ConquestBattle && e.SettlementId == c && e.Count > 0);
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.ConquestBattle && e.SettlementId == a);
        var won = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.Conquered);
        Assert.Equal((c, (long)a), (won.SettlementId, won.Count));
        Assert.Equal(a, fold.Liege[c]);
        Assert.Empty(fold.Wars);
        Assert.Contains(DemoRealms.Loyalty(c, a, won.Week + 1, fold, ctx), f => f.Text == "剛被征服" && f.Value == DemoRealms.TempConqueredLoyalty);
        Assert.True(won.Week - declared.Week >= DemoRealms.TempWarMinWeeks);
        // 第 08-22a 章：先圍城、攻陷，戰爭分數滿了才征服。
        var occupied = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.Occupied);
        Assert.Equal((c, (long)a), (occupied.SettlementId, occupied.Count));
        Assert.True(occupied.Week - declared.Week >= DemoRealms.TempSiegeWeeks(DemoSettlementTier.Village));
        Assert.True(occupied.Week < won.Week);
        Assert.Contains("戰爭分數", won.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void LostLandCanBeReconqueredWithLessStrength()
    {
        // 第 08-22e 章：c 原本是 a 的臣屬，被 b 征服走；a 的兵力只有 b 方的九成（一般宣戰要 120%），仍可宣戰收復。
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, c, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(10, TimeSpan.Zero, c, "", DemoSettlementEventKind.Conquered, b, ""));
        Assert.Equal(a, fold.LostBy(c, 11));
        Assert.Null(fold.LostBy(c, 10 + DemoRealms.TempReconquestWeeks));
        Assert.Null(fold.LostBy(a, 11));
        var ctx = Ctx(sites, a, id => id == a ? 180 : id == b ? 150 : id == c ? 50 : 0);
        var rng = RngRegistry.Create(7);
        var all = new List<DemoSettlementEvent>();
        for (var week = 11; week < 11 + 13 * 40 && !all.Any(e => e.Kind == DemoSettlementEventKind.WarDeclared); week++)
            all.AddRange(Step(fold, week, ctx, rng));
        var declared = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.WarDeclared);
        Assert.Equal((a, (long)c), (declared.SettlementId, declared.Count));
        Assert.Contains("收復失地", declared.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroesLeadArmiesAndAreNamedInTheBattle()
    {
        // 第 08-22f 章：攻方首都有在世的英雄，攻方造成的死傷 ×130%，交戰寫出英雄的名字。
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(5, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarDeclared, c, "甲向丙宣戰"));
        long Residents(int id) => id == a ? 2000 : id == b ? 1000 : id == c ? 1500 : 0;
        DemoDisasterStrike Hit(Func<int, string?>? heroOf)
            => DemoRealms.Battles(6, fold, sites, Residents, new HashSet<int>(), RngRegistry.Create(1), heroOf: heroOf)[c];
        var plain = Hit(null);
        var led = Hit(id => id == a ? "艾拉·洛恩" : null);
        Assert.True(led.Deaths >= plain.Deaths * DemoRealms.TempHeroBattlePercent / 100 - 1);
        Assert.True(led.Deaths > plain.Deaths);
        Assert.Contains("敵方英雄艾拉·洛恩率軍", led.Cause, StringComparison.Ordinal);
        var guarded = DemoRealms.Battles(6, fold, sites, Residents, new HashSet<int>(), RngRegistry.Create(1), heroOf: id => id == c ? "布蘭·洛威" : null);
        Assert.Contains("遭遇敵方英雄布蘭·洛威守城", guarded[a].Cause, StringComparison.Ordinal);
        Assert.Contains("英雄布蘭·洛威領軍守城", guarded[c].Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void HillsForestsAndRiversFavourTheDefender()
    {
        // 第 08-22b 章：守方據守丘陵、森林，攻方渡河，攻方造成的死傷變少。
        var map = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample).Map;
        var hills = map.Cells.First(c => c.Terrain == DemoTerrain.Hills);
        var forest = map.Cells.First(c => c.Terrain == DemoTerrain.Forest);
        var grass = map.Cells.First(c => c.Terrain == DemoTerrain.Grassland);
        var attacker = new DemoSettlement(1, "甲", grass.Column, grass.Row);
        var (hillPercent, hillNote) = DemoRealms.BattleTerrain(map, attacker, new DemoSettlement(2, "乙", hills.Column, hills.Row));
        Assert.Contains("據守丘陵", hillNote, StringComparison.Ordinal);
        Assert.True(hillPercent <= DemoRealms.TempHillsAttackPercent);
        var (forestPercent, forestNote) = DemoRealms.BattleTerrain(map, attacker, new DemoSettlement(3, "丙", forest.Column, forest.Row));
        Assert.Contains("據守森林", forestNote, StringComparison.Ordinal);
        Assert.True(forestPercent <= DemoRealms.TempForestAttackPercent);
        Assert.Equal((100, ""), DemoRealms.BattleTerrain(null, attacker, new DemoSettlement(2, "乙", hills.Column, hills.Row)));
        // 河的兩岸：直線經過河流格就算渡河；同一岸不算。
        bool Dry(int c, int r) => map.Contains(c, r) && DemoProvinceMap.IsLand(map.GetCell(c, r)) && !DemoProvinceMap.IsRiver(map.GetCell(c, r));
        var river = map.Cells.First(c => DemoProvinceMap.IsRiver(c) && (Dry(c.Column - 1, c.Row) && Dry(c.Column + 1, c.Row) || Dry(c.Column, c.Row - 1) && Dry(c.Column, c.Row + 1)));
        var (dc, dr) = Dry(river.Column - 1, river.Row) && Dry(river.Column + 1, river.Row) ? (1, 0) : (0, 1);
        Assert.True(DemoRealms.CrossesRiver(map, river.Column - dc, river.Row - dr, river.Column + dc, river.Row + dr));
        Assert.False(DemoRealms.CrossesRiver(map, river.Column - dc, river.Row - dr, river.Column - dc, river.Row - dr));
        var across = DemoRealms.BattleTerrain(map, new DemoSettlement(1, "甲", river.Column - dc, river.Row - dr), new DemoSettlement(2, "乙", river.Column + dc, river.Row + dr));
        Assert.Contains("攻方渡河", across.Note, StringComparison.Ordinal);
        Assert.True(across.Percent <= DemoRealms.TempRiverAttackPercent);
    }

    [Fact]
    public void WarScoreCountsBattlesTheSiegeAndHoldingTheGoal()
    {
        var war = new DemoWar(1, 2, 2, 10);
        Assert.Equal(0, DemoRealms.WarScore(war, 12));
        var winning = war with { DefenderLosses = 90 };
        Assert.Equal(36, DemoRealms.BattleScore(winning)); // 40×90÷(90＋10)
        Assert.Equal(-36, DemoRealms.BattleScore(war with { AttackerLosses = 100 })); // 40×(−100)÷110
        var held = winning with { OccupiedWeek = 20 };
        Assert.Equal(36 + DemoRealms.TempOccupyScore, DemoRealms.WarScore(held, 20));
        Assert.Equal(36 + DemoRealms.TempOccupyScore + 5 * DemoRealms.TempTickScore, DemoRealms.WarScore(held, 25));
        Assert.Equal(100, DemoRealms.WarScore(held, 60)); // 封頂
        // 攻陷只記第一次；勢力頁寫出攻陷與戰爭分數。
        var (sites, a, _, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(5, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarDeclared, c, ""));
        Assert.Contains("圍城中", DemoRealms.DescribeWar(fold.Wars[a], sites, 6), StringComparison.Ordinal);
        fold.Apply(new DemoSettlementEvent(9, TimeSpan.Zero, c, "", DemoSettlementEventKind.Occupied, a, ""));
        fold.Apply(new DemoSettlementEvent(11, TimeSpan.Zero, c, "", DemoSettlementEventKind.Occupied, a, ""));
        Assert.Equal(9, fold.Wars[a].OccupiedWeek);
        var text = DemoRealms.DescribeWar(fold.Wars[a], sites, 12);
        Assert.Contains("已攻陷", text, StringComparison.Ordinal);
        Assert.Contains($"戰爭分數 {DemoRealms.TempOccupyScore + 3 * DemoRealms.TempTickScore}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoWarsBeforeTheConquestEraOrWhenTooWeak()
    {
        var (sites, a, b, c) = Setup();
        foreach (var (residents, conquest) in new[] { (200L, false), (20L, true) })
        {
            var fold = new DemoRealms.Fold();
            fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
            var ctx = Ctx(sites, a, id => id == a ? residents : id == b ? 10 : id == c ? 300 : 0);
            var rng = RngRegistry.Create(7);
            for (var week = 2; week < 2 + 13 * 30; week++)
                Assert.DoesNotContain(Step(fold, week, ctx, rng, conquest), e => e.Kind == DemoSettlementEventKind.WarDeclared);
        }
    }

    // 第 08-19i 章：沒有臣屬、世上還沒有王國，城市也能出兵征服鄰居；勢力由征服而來。
    [Fact]
    public void LoneCityConquersItsNeighbourBeforeAnyKingdomExists()
    {
        var (sites, a, _, c) = Setup();
        var fold = new DemoRealms.Fold();
        var ctx = Ctx(sites, a, id => id == a ? 200 : id == c ? 50 : 0, id => id == c ? 2 : 1);
        var rng = RngRegistry.Create(7);
        var all = new List<DemoSettlementEvent>();
        for (var week = 2; week < 2 + 13 * 60 && !all.Any(e => e.Kind is DemoSettlementEventKind.Conquered or DemoSettlementEventKind.WarEnded); week++)
            all.AddRange(Step(fold, week, ctx, rng, conquest: false, cityWars: true));
        var declared = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.WarDeclared);
        Assert.Equal((a, (long)c), (declared.SettlementId, declared.Count));
        Assert.Contains(all, e => e.Kind is DemoSettlementEventKind.Conquered or DemoSettlementEventKind.WarEnded);
        // 兵力五倍以上：不戰而降（沒有宣戰與交戰），而且剛被征服的不會加入派系。
        var meek = new DemoRealms.Fold();
        var weak = Ctx(sites, a, id => id == a ? 200 : id == c ? 20 : 0, id => id == c ? 2 : 1);
        var quick = new List<DemoSettlementEvent>();
        for (var week = 2; week < 2 + 13 * 60 && !quick.Any(e => e.Kind == DemoSettlementEventKind.Conquered); week++)
            quick.AddRange(Step(meek, week, weak, RngRegistry.Create(7), conquest: false, cityWars: true));
        var surrender = Assert.Single(quick, e => e.Kind == DemoSettlementEventKind.Conquered);
        Assert.Contains("不戰而降", surrender.Cause, StringComparison.Ordinal);
        Assert.DoesNotContain(quick, e => e.Kind is DemoSettlementEventKind.WarDeclared or DemoSettlementEventKind.ConquestBattle);
        Assert.Equal(a, meek.Liege[c]);
        Assert.True(meek.Conquered(c, surrender.Week + 1));
        // 不開這條規則、也不是征服時代：不宣戰。
        var quiet = new DemoRealms.Fold();
        for (var week = 2; week < 2 + 13 * 30; week++)
            Assert.DoesNotContain(Step(quiet, week, ctx, RngRegistry.Create(7), conquest: false), e => e.Kind == DemoSettlementEventKind.WarDeclared);
    }

    [Fact]
    public void WhitePeaceBringsATruceAndTheWarIsDescribed()
    {
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(5, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarDeclared, c, "甲向丙宣戰"));
        var war = Assert.Single(fold.Wars.Values);
        Assert.Equal((a, c, c, true), (war.AttackerId, war.DefenderId, war.TargetId, war.WholeRealm));
        Assert.Same(war, fold.WarOf(c));
        Assert.Contains("征服戰爭", DemoRealms.DescribeWar(war, sites, 9), StringComparison.Ordinal);
        fold.Apply(new DemoSettlementEvent(30, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarEnded, c, ""));
        Assert.Empty(fold.Wars);
        Assert.True(fold.WarTruce(c, a, 31));
        Assert.False(fold.WarTruce(a, c, 30 + DemoRealms.TempConquestTruceWeeks));
    }

    [Fact]
    public void LawsCanStopWars()
    {
        Assert.False(DemoWorldLaws.Default.With("wars", false).Wars);
        Assert.Contains(DemoWorldLaws.Default.Entries, e => e.Key == "wars" && e.On);
    }
}
