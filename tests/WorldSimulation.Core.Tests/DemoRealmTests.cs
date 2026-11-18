using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19b 章：歸附、忠誠度、獨立派系、請願、最後通牒、獨立戰爭與鎮壓（參考 WorldBox 與十字軍之王）。</summary>
public sealed class DemoRealmTests
{
    private static (DemoObserverWorld World, int A, int B, int C) Setup()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        Assert.True(alive.Count >= 3);
        return (world, alive[0].Id, alive[1].Id, alive[2].Id);
    }

    private static DemoRealmContext Ctx(DemoWorldSites sites, Func<int, long> residents, Func<int, int?>? culture = null)
        => new(sites, residents, culture ?? (_ => 1), _ => null, _ => DemoSettlementTier.Village, _ => false);

    /// <summary>第 08-19h 章：<paramref name="town"/> 是 100 人的城鎮，其餘是 10 人的村莊、同文化——小村服氣，會自己歸附。</summary>
    private static DemoRealmContext Feudal(DemoWorldSites sites, int town)
        => new(sites, id => id == town ? 100 : 10, _ => 1, _ => null, id => id == town ? DemoSettlementTier.Town : DemoSettlementTier.Village, _ => true);

    /// <summary>跑一週：交戰事件（照 Battles）＋隸屬變化，全部餵進摺疊。</summary>
    private static IReadOnlyList<DemoSettlementEvent> Step(DemoRealms.Fold fold, int week, DemoRealmContext ctx, IReadOnlyDictionary<int, int> natural, RngRegistry rng)
    {
        var battles = DemoRealms.Battles(week, fold, ctx.Sites, ctx.Residents, new HashSet<int>(), rng).Values
            .Select((b, k) => new DemoSettlementEvent(week, TimeSpan.FromDays(week * 7), b.SettlementId, "", b.Kind, b.Deaths, b.Cause, EventId: week * 100 + k))
            .ToList();
        foreach (var e in battles)
            fold.Apply(e);
        var events = DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, ctx, natural, rng, true, week * 100 + 50);
        foreach (var e in events)
            fold.Apply(e);
        return battles.Concat(events).ToList();
    }

    [Fact]
    public void LoyaltyAddsUpTheFactors()
    {
        var (world, a, b, _) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        var ctx = new DemoRealmContext(world.Sites, id => id == a ? 100 : 60, id => id == a ? 1 : 2, _ => 3,
            id => id == a ? DemoSettlementTier.Town : DemoSettlementTier.Village, _ => true);
        var factors = DemoRealms.Loyalty(b, a, 10, fold, ctx);
        Assert.Contains(factors, f => f.Text == "文化不同" && f.Value == -30);
        Assert.Contains(factors, f => f.Text == "人口達上級一半" && f.Value == -15);
        Assert.Contains(factors, f => f.Text == "剛歸附" && f.Value == 15);
        Assert.Contains(factors, f => f.Text == "上級強勢統治" && f.Value == 10);
        Assert.DoesNotContain(factors, f => f.Text == "信仰不同");
        Assert.Equal(factors.Sum(f => f.Value), DemoRealms.LoyaltyValue(b, a, 10, fold, ctx));
        // 一年後「剛歸附」消失。
        Assert.DoesNotContain(DemoRealms.Loyalty(b, a, 1 + DemoRealms.TempNewVassalWeeks, fold, ctx), f => f.Text == "剛歸附");
        Assert.NotEmpty(DemoRealms.LoyaltyLines(b, 10, fold, ctx));
        Assert.Empty(DemoRealms.LoyaltyLines(a, 10, fold, ctx));
    }

    [Fact]
    public void WeakFactionPetitionsAndFailureBringsATruce()
    {
        var (world, a, b, c) = Setup();
        var rng = RngRegistry.Create(9);
        var fold = new DemoRealms.Fold();
        // 上級很大、兩個下級文化不同（忠誠為負）但兵力不足：加入派系、半年後請願（非武力）。
        var ctx = Ctx(world.Sites, id => id == a ? 1000 : 100, id => id == a ? 1 : 2);
        var natural = new Dictionary<int, int> { [b] = a, [c] = a };
        // 第 08-19h 章：一開始就不服的不會自己歸附，所以隸屬直接給定（例：被征服而來）。
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, c, "", DemoSettlementEventKind.VassalJoined, a, ""));
        // 文化不同、同層級：即使剛歸附 +15，忠誠仍是負的，第 2 週加入派系。
        var factions = Step(fold, 2, ctx, natural, rng);
        Assert.Equal(new[] { b, c }, factions.Where(e => e.Kind == DemoSettlementEventKind.FactionJoined).Select(e => e.SettlementId).OrderBy(x => x));
        for (var week = 3; week < 2 + DemoRealms.TempDiscontentWeeks; week++)
            Assert.Empty(Step(fold, week, ctx, natural, rng));
        var petition = Assert.Single(Step(fold, 2 + DemoRealms.TempDiscontentWeeks, ctx, natural, rng));
        Assert.Equal((DemoSettlementEventKind.SecessionPeaceful, (long)a), (petition.Kind, petition.Count));
        var struggle = Assert.Single(fold.Struggles.Values);
        Assert.Equal(new[] { b, c }.OrderBy(x => x), struggle.Members);
        Assert.Contains("請願", DemoRealms.Describe(struggle, world.Sites, struggle.StartWeek + 1));
        for (var week = struggle.StartWeek + 1; week < struggle.EndWeek; week++)
            Assert.Empty(Step(fold, week, ctx, natural, rng));
        var end = Step(fold, struggle.EndWeek, ctx, natural, rng);
        Assert.Empty(fold.Struggles);
        if (end[0].Kind == DemoSettlementEventKind.SecessionFailed)
        {
            // 失敗：成員被鎮壓，停戰期內不再加入派系。
            Assert.True(fold.Calm(b, struggle.EndWeek + 1) && fold.Calm(c, struggle.EndWeek + 1));
            Assert.Empty(fold.Faction);
            Assert.Empty(Step(fold, struggle.EndWeek + 1, ctx, natural, rng));
        }
        else
        {
            // 讓步：首領獨立、另一個成員歸附首領（新勢力）。
            Assert.Equal(DemoSettlementEventKind.Independence, end[0].Kind);
            var leader = end[0].SettlementId;
            Assert.False(fold.Liege.ContainsKey(leader));
            Assert.Equal(leader, fold.Liege[leader == b ? c : b]);
        }
    }

    [Fact]
    public void StrongFactionWinsByUltimatumOrWar()
    {
        var (world, a, b, c) = Setup();
        var natural = new Dictionary<int, int> { [b] = a, [c] = a };
        // 兵力是上級的 2 倍：上級接受通牒、和平獨立。
        var fold = new DemoRealms.Fold();
        var rng = RngRegistry.Create(4);
        var strong = Ctx(world.Sites, id => id == a ? 100 : 100, id => id == a ? 1 : 2);
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, c, "", DemoSettlementEventKind.VassalJoined, a, ""));
        for (var week = 2; week <= 2 + DemoRealms.TempDiscontentWeeks; week++)
            Step(fold, week, strong, natural, rng);
        Assert.False(fold.Liege.ContainsKey(b));
        Assert.Empty(fold.Struggles);
        // 兵力約上級的 1 倍：通牒被拒而開戰，每週雙方交戰，最後分出勝負或維持原狀。
        fold = new DemoRealms.Fold();
        var even = Ctx(world.Sites, id => id == a ? 200 : 100, id => id == a ? 1 : 2);
        var all = new List<DemoSettlementEvent>();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, c, "", DemoSettlementEventKind.VassalJoined, a, ""));
        for (var week = 2; week <= 2 + DemoRealms.TempDiscontentWeeks; week++)
            all.AddRange(Step(fold, week, even, natural, rng));
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.SecessionArmed);
        var war = Assert.Single(fold.Struggles.Values);
        Assert.True(war.Armed);
        var battles = DemoRealms.Battles(war.StartWeek + 1, fold, world.Sites, even.Residents, new HashSet<int>(), rng);
        Assert.Equal(new[] { a, war.VassalId }.OrderBy(x => x), battles.Keys.OrderBy(x => x));
        Assert.All(battles.Values, s => Assert.Equal(DemoSettlementEventKind.SecessionBattle, s.Kind));
        Assert.Empty(DemoRealms.Battles(war.StartWeek, fold, world.Sites, even.Residents, new HashSet<int>(), rng)); // 開戰那週還沒開打
        Assert.Equal(new[] { a }, DemoRealms.Battles(war.StartWeek + 1, fold, world.Sites, even.Residents, new HashSet<int> { war.VassalId }, rng).Keys);
        for (var week = war.StartWeek + 1; week <= war.EndWeek && fold.Struggles.Count > 0; week++)
            all.AddRange(Step(fold, week, even, natural, rng));
        Assert.Empty(fold.Struggles);
        Assert.Contains(all, e => e.Kind is DemoSettlementEventKind.Independence or DemoSettlementEventKind.SecessionFailed);
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.SecessionBattle);
    }

    // 第 08-19h 章：交戰死亡跟著對方兵力走，零頭用亂數進位；小兵力不再每週固定死一人。
    [Fact]
    public void WarDeathsScaleWithTheEnemyArmyAndRoundByChance()
    {
        Assert.Equal(0, DemoRealms.WarDeaths(0, 100, 12345));
        // 第 08-22i 章：兵力 60、運氣 1.0（draw % 101 = 50）＝剛好 1 人；兵力 120＝2 人。
        Assert.Equal(1, DemoRealms.WarDeaths(60, 100, 50));
        Assert.Equal(2, DemoRealms.WarDeaths(120, 100, 50));
        // 兵力 6、運氣 0.5＝0.05 人：進位亂數（draw / 101 % 1000）小於 50 才死 1 人。
        Assert.Equal(1, DemoRealms.WarDeaths(6, 100, 0));
        Assert.Equal(0, DemoRealms.WarDeaths(6, 100, 101UL * 50));
        // 最多留 1 人。
        Assert.Equal(4, DemoRealms.WarDeaths(2000, 5, 50));
        Assert.Equal(0, DemoRealms.WarDeaths(2000, 1, 50));
    }

    [Fact]
    public void LiegeRuinFreesVassalsAndJoiningNeverCycles()
    {
        var (world, a, b, _) = Setup();
        var rng = RngRegistry.Create(3);
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        Assert.Equal(a, fold.Liege[b]);
        var free = Assert.Single(Step(fold, 2, Ctx(world.Sites, id => id == a ? 0 : 120), new Dictionary<int, int>(), rng));
        Assert.Equal((DemoSettlementEventKind.Independence, b, (long)a), (free.Kind, free.SettlementId, free.Count));
        Assert.False(fold.Liege.ContainsKey(b));
        // 互為自然上級時只歸附一邊；同樣的輸入、同樣的結果。
        var cyc = new Dictionary<int, int> { [a] = b, [b] = a };
        var feudal = Feudal(world.Sites, a);
        var willing = DemoRealms.LoyaltyValue(b, a, 1, new DemoRealms.Fold(), feudal) >= 0; // 離得太遠的小村也不服
        var events = DemoRealms.Advance(1, TimeSpan.FromDays(7), new DemoRealms.Fold(), feudal, cyc, RngRegistry.Create(1), true, 10);
        Assert.Equal(willing ? new[] { (DemoSettlementEventKind.VassalJoined, b, (long)a) } : Array.Empty<(DemoSettlementEventKind, int, long)>(),
            events.Select(e => (e.Kind, e.SettlementId, e.Count)));
        Assert.Equal(events, DemoRealms.Advance(1, TimeSpan.FromDays(7), new DemoRealms.Fold(), Feudal(world.Sites, a), cyc, RngRegistry.Create(1), true, 10));
        // 第 08-19h 章：規模相當的同級聚落互不相服，誰也不歸附誰。
        Assert.Empty(DemoRealms.Advance(1, TimeSpan.FromDays(7), new DemoRealms.Fold(), Ctx(world.Sites, _ => 50), cyc, RngRegistry.Create(1), true, 10));
    }

    // 第 08-19h 章：獨立之後不會自己歸附回舊主（期滿才會）；湊不出 3 個兵的派系打不起獨立戰爭，只能請願。
    [Fact]
    public void FreedSettlementDoesNotRejoinItsFormerLiegeUntilTheIndependencePeriodEnds()
    {
        var (world, a, b, _) = Setup();
        var rng = RngRegistry.Create(5);
        var natural = new Dictionary<int, int> { [b] = a };
        var ctx = Feudal(world.Sites, a);
        var fold = new DemoRealms.Fold();
        var willing = DemoRealms.LoyaltyValue(b, a, 1, fold, ctx) >= 0;
        Assert.Equal(willing, Step(fold, 1, ctx, natural, rng).Any(e => e.Kind == DemoSettlementEventKind.VassalJoined && e.SettlementId == b));
        Assert.Equal(willing, fold.Liege.ContainsKey(b));
        fold.Apply(new DemoSettlementEvent(2, TimeSpan.FromDays(14), b, "", DemoSettlementEventKind.Independence, a, ""));
        Assert.True(fold.FreedFrom(b, a, 3));
        Assert.DoesNotContain(Step(fold, 3, ctx, natural, rng), e => e.Kind == DemoSettlementEventKind.VassalJoined);
        Assert.False(fold.Liege.ContainsKey(b));
        var later = 2 + DemoRealms.TempIndependenceWeeks;
        Assert.False(fold.FreedFrom(b, a, later));
        Assert.Equal(willing, Step(fold, later, ctx, natural, rng).Any(e => e.Kind == DemoSettlementEventKind.VassalJoined && e.SettlementId == b));
    }

    [Fact]
    public void TinyFactionPetitionsInsteadOfWagingWar()
    {
        var (world, a, b, _) = Setup();
        var rng = RngRegistry.Create(6);
        var natural = new Dictionary<int, int> { [b] = a };
        // 雙方各 20 人（兵力 2 對 2，原本會開戰）：兵力不到 3，只能請願。
        var ctx = Ctx(world.Sites, _ => 20, id => id == a ? 1 : 2);
        var fold = new DemoRealms.Fold();
        var all = new List<DemoSettlementEvent>();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        for (var week = 2; week <= 2 + DemoRealms.TempDiscontentWeeks; week++)
            all.AddRange(Step(fold, week, ctx, natural, rng));
        Assert.DoesNotContain(all, e => e.Kind == DemoSettlementEventKind.SecessionArmed);
        var petition = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.SecessionPeaceful);
        Assert.Contains("人少勢弱（2 對 2）", petition.Cause, StringComparison.Ordinal);
        Assert.False(Assert.Single(fold.Struggles.Values).Armed);
    }

    [Fact]
    public void TerritoryUsesTheLiegesFromEvents()
    {
        var (world, a, b, c) = Setup();
        var map = DemoTerritory.Compute(world.Map, world.Sites, _ => DemoSettlementTier.Village, null, new Dictionary<int, int> { [b] = a, [c] = b });
        Assert.Equal(a, map.LiegeOf(b));
        Assert.Equal(a, map.RealmOf(c));
        Assert.Null(map.LiegeOf(a));
        Assert.Empty(DemoTerritory.Compute(world.Map, world.Sites, _ => DemoSettlementTier.Village).Liege);
    }

    [Fact]
    public void LivingWorldRecordsRealmEventsWithValidCauses()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(4UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 7 * 104; d++)
            world.AdvanceOneDay();
        Assert.Empty(world.BuildCausalIndex().Problems);
        var territory = world.Territory();
        Assert.Equal(world.Realms().Liege.Where(kv => territory.Realm.ContainsKey(kv.Key) && territory.Realm.ContainsKey(kv.Value)).OrderBy(kv => kv.Key),
            territory.Liege.OrderBy(kv => kv.Key));
    }

    // 第 14-1 章★4：估算別人的兵力用「所知」的人口（舊消息）；沒有消息模型時就是即時人口。
    [Fact]
    public void KnownResidentsFallBackToLivePopulationWithoutANewsModel()
    {
        var ctx = new DemoRealmContext(null!, id => id * 10L, _ => null, _ => null, _ => DemoSettlementTier.Camp, _ => false);
        Assert.Equal(30, ctx.KnownResidents(1, 3));
        var delayed = ctx with { Known = (observer, target) => observer == target ? target * 10L : target * 10L - 5 };
        Assert.Equal(25, delayed.KnownResidents(1, 3));
        Assert.Equal(30, delayed.KnownResidents(3, 3));
    }
}
