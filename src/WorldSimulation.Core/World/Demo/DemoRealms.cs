using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19b 章：一場脫離行動（派系對上級）：首領、上級、武力（獨立戰爭）或非武力（請願）、第幾週開始、成員；戰爭另記雙方累計死亡。
/// </summary>
public sealed record DemoSecession(int VassalId, int LiegeId, bool Armed, int StartWeek, IReadOnlyList<int> Members)
{
    /// <summary>請願的結束週；戰爭最晚的結束週（打滿一年維持原狀）。</summary>
    public int EndWeek => StartWeek + (Armed ? DemoRealms.TempWarMaxWeeks : DemoRealms.TempPeacefulWeeks);

    /// <summary>戰爭中叛軍（首領）、上級的累計死亡（由交戰事件推得）。</summary>
    public long RebelLosses { get; init; }
    public long LoyalLosses { get; init; }
}

/// <summary>忠誠度的一項因素（給詳情頁列明細）。</summary>
public sealed record DemoLoyaltyFactor(string Text, int Value);

/// <summary>算忠誠度、兵力要用到的世界狀態（都是查詢，不改世界）。</summary>
public sealed record DemoRealmContext(
    DemoWorldSites Sites,
    Func<int, long> Residents,
    Func<int, int?> Culture,
    Func<int, int?> Faith,
    Func<int, DemoSettlementTier> Tier,
    Func<int, bool> StrongRule);

/// <summary>
/// 第 08-19b 章【TEMP 規則】分裂鬥爭（使用者 2026-11-08：勢力分裂前應該要有分裂鬥爭，武力或非武力；參考 WorldBox 與十字軍之王）。
/// <list type="number">
/// <item><b>歸附</b>：沒有上級的聚落落在更高層級聚落的影響範圍內（第 08-19 章的自然上級），而且不會繞成圈，就歸附它。</item>
/// <item><b>忠誠度</b>（WorldBox 的城市忠誠）：下級對上級的忠誠由距離、文化與信仰、人口與層級、剛歸附、剛被鎮壓、上級轄下太多、強勢統治加減（<see cref="Loyalty"/>）。</item>
/// <item><b>獨立派系</b>（十字軍之王）：忠誠低於 0 加入、回到 <see cref="TempLeaveLoyalty"/> 以上退出（記事件）。同一個上級底下的成員是同一個派系，
///   首領是人口最多的成員；首領加入滿 <see cref="TempDiscontentWeeks"/> 週（不滿累積滿了）就行動。</item>
/// <item><b>請願</b>（非武力）：派系兵力不到上級方的 <see cref="TempUltimatumPercent"/>%，只能請願抗議 <see cref="TempPeacefulWeeks"/> 週，
///   期滿 <see cref="TempPeacefulWinPercent"/>%（文化不同再加 <see cref="TempCultureBonusPercent"/>）上級讓步。</item>
/// <item><b>最後通牒</b>：派系兵力達上級方的 <see cref="TempUltimatumPercent"/>% 以上就下通牒；達 <see cref="TempAcceptPercent"/>% 時上級接受、和平獨立，否則拒絕而開戰。</item>
/// <item><b>獨立戰爭</b>：每週首領與上級交戰，死亡依對方兵力（與天災同一欄週帳，天災、怪物優先）。打滿 <see cref="TempWarMinWeeks"/> 週後，
///   一方累計死亡達另一方的 <see cref="TempWarScorePercent"/>% 就分出勝負；打滿 <see cref="TempWarMaxWeeks"/> 週維持原狀（叛軍失敗）。</item>
/// <item><b>結果</b>：成功時派系成員一起獨立，其他成員歸附首領（像 WorldBox 叛亂後成立新王國）；失敗時成員被鎮壓，<see cref="TempTruceWeeks"/> 週內不再加入派系，忠誠加分。
///   上級成為廢墟或沒人時，下級直接獨立。</item>
/// </list>
/// 隸屬、派系與鬥爭都由事件推得（<see cref="Fold"/>），不另存；亂數用事件流的實體局部亂數（索引＝週次與聚落），不推進計數器，重跑相同。
/// </summary>
public static class DemoRealms
{
    /// <summary>【TEMP】不滿累積幾週後行動；請願幾週；戰爭至少、最多幾週；失敗後幾週內不再加入派系；剛歸附算幾週。</summary>
    public const int TempDiscontentWeeks = 26, TempPeacefulWeeks = 26, TempWarMinWeeks = 8, TempWarMaxWeeks = 52, TempTruceWeeks = 260, TempNewVassalWeeks = 52;

    /// <summary>【TEMP】派系兵力達上級方幾 % 下通牒、幾 % 上級接受；請願的讓步機率與文化不同的加成；戰爭分出勝負的死亡比。</summary>
    public const int TempUltimatumPercent = 80, TempAcceptPercent = 150, TempPeacefulWinPercent = 35, TempCultureBonusPercent = 20, TempWarScorePercent = 150;

    /// <summary>【TEMP】兵力＝居民的 1/10（至少 1）；每週交戰對方每 20 名兵多殺 1 人（再乘 0.5～1.5 的運氣）。</summary>
    public const int TempLevyDivisor = 10, TempArmyPerDeath = 20;

    /// <summary>【TEMP】忠誠回到這個值以上才退出派系。</summary>
    public const int TempLeaveLoyalty = 10;

    private const string Namespace = "TEMP-REALM-v2";

    public static bool IsBattle(DemoSettlementEventKind kind) => kind == DemoSettlementEventKind.SecessionBattle;

    /// <summary>由事件推得的隸屬、派系、進行中的鬥爭與停戰（依序餵入已提交的事件）。</summary>
    public sealed class Fold
    {
        private readonly Dictionary<int, int> _liege = new();
        private readonly Dictionary<int, int> _joinedWeek = new();
        private readonly Dictionary<int, (int Liege, int Week)> _faction = new();
        private readonly Dictionary<int, DemoSecession> _struggles = new();
        private readonly Dictionary<int, int> _truceUntil = new();

        public IReadOnlyDictionary<int, int> Liege => _liege;

        /// <summary>派系成員：聚落 → （反對的上級、加入週）。</summary>
        public IReadOnlyDictionary<int, (int Liege, int Week)> Faction => _faction;

        /// <summary>進行中的鬥爭（依首領）。</summary>
        public IReadOnlyDictionary<int, DemoSecession> Struggles => _struggles;

        public int? JoinedWeek(int settlementId) => _joinedWeek.TryGetValue(settlementId, out var w) ? w : null;

        /// <summary>剛被鎮壓（停戰中）。</summary>
        public bool Calm(int settlementId, int week) => _truceUntil.TryGetValue(settlementId, out var until) && week < until;

        /// <summary>這個聚落正在參與的鬥爭（首領、成員或上級）。</summary>
        public DemoSecession? StruggleOf(int settlementId)
            => _struggles.Values.OrderBy(s => s.VassalId).FirstOrDefault(s => s.VassalId == settlementId || s.LiegeId == settlementId || s.Members.Contains(settlementId));

        public void Apply(DemoSettlementEvent e)
        {
            var id = e.SettlementId;
            switch (e.Kind)
            {
                case DemoSettlementEventKind.VassalJoined:
                    _liege[id] = (int)e.Count;
                    _joinedWeek[id] = e.Week;
                    _faction.Remove(id);
                    break;
                case DemoSettlementEventKind.FactionJoined:
                    _faction[id] = ((int)e.Count, e.Week);
                    break;
                case DemoSettlementEventKind.FactionLeft:
                    _faction.Remove(id);
                    break;
                case DemoSettlementEventKind.SecessionArmed or DemoSettlementEventKind.SecessionPeaceful:
                    var members = _faction.Where(kv => kv.Value.Liege == (int)e.Count).Select(kv => kv.Key).Append(id).Distinct().OrderBy(x => x).ToList();
                    _struggles[id] = new DemoSecession(id, (int)e.Count, e.Kind == DemoSettlementEventKind.SecessionArmed, e.Week, members);
                    break;
                case DemoSettlementEventKind.SecessionBattle:
                    foreach (var (leader, s) in _struggles.Where(kv => kv.Value.Armed).OrderBy(kv => kv.Key).ToList())
                    {
                        if (id == leader)
                            _struggles[leader] = s with { RebelLosses = s.RebelLosses + e.Count };
                        else if (id == s.LiegeId)
                            _struggles[leader] = s with { LoyalLosses = s.LoyalLosses + e.Count };
                    }
                    break;
                case DemoSettlementEventKind.Independence:
                    _liege.Remove(id);
                    _joinedWeek.Remove(id);
                    _faction.Remove(id);
                    _struggles.Remove(id);
                    break;
                case DemoSettlementEventKind.SecessionFailed:
                    if (_struggles.Remove(id, out var lost))
                        foreach (var m in lost.Members)
                        {
                            _faction.Remove(m);
                            _truceUntil[m] = e.Week + TempTruceWeeks;
                        }
                    break;
            }
        }
    }

    private static ulong Draw(RngRegistry rng, int week, int settlementId, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace)
            .GetUInt64(checked((ulong)week * 1_048_576UL + (ulong)settlementId * 8UL + (ulong)k));

    /// <summary>兵力：居民的 1/10（有人就至少 1）。</summary>
    public static long Army(long residents) => residents <= 0 ? 0 : Math.Max(1L, residents / TempLevyDivisor);

    /// <summary>
    /// 忠誠度明細（WorldBox 的城市忠誠）：基本 +10；離上級每超過 2 格 −3；文化不同 −30、信仰不同 −15；
    /// 人口達上級一半 −15、超過上級 −30；層級與上級同級以上 −20；剛歸附 +15；剛被鎮壓 +25；上級直屬超過 5 個每多一個 −3；上級的文化強勢統治 +10。【TEMP】
    /// </summary>
    public static IReadOnlyList<DemoLoyaltyFactor> Loyalty(int vassal, int liege, int week, Fold fold, DemoRealmContext ctx)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(ctx);
        var v = ctx.Sites.Settlements.FirstOrDefault(s => s.Id == vassal);
        var l = ctx.Sites.Settlements.FirstOrDefault(s => s.Id == liege);
        var factors = new List<DemoLoyaltyFactor> { new("基本", 10) };
        if (v is not null && l is not null)
        {
            var d = DemoHexWorld.Distance(new DemoCellRef(v.Column, v.Row), new DemoCellRef(l.Column, l.Row));
            if (d > 2)
                factors.Add(new($"離上級 {d} 格", -3 * (d - 2)));
        }
        if (ctx.Culture(vassal) is { } c1 && ctx.Culture(liege) is { } c2 && c1 != c2)
            factors.Add(new("文化不同", -30));
        if (ctx.Faith(vassal) is { } f1 && ctx.Faith(liege) is { } f2 && f1 != f2)
            factors.Add(new("信仰不同", -15));
        var mine = ctx.Residents(vassal);
        var theirs = ctx.Residents(liege);
        if (mine > theirs)
            factors.Add(new("人口比上級多", -30));
        else if (mine * 2 >= theirs)
            factors.Add(new("人口達上級一半", -15));
        if (ctx.Tier(vassal) >= ctx.Tier(liege))
            factors.Add(new("層級不輸上級", -20));
        if (fold.JoinedWeek(vassal) is { } joined && week - joined < TempNewVassalWeeks)
            factors.Add(new("剛歸附", 15));
        if (fold.Calm(vassal, week))
            factors.Add(new("剛被鎮壓", 25));
        var direct = fold.Liege.Count(kv => kv.Value == liege);
        if (direct > 5)
            factors.Add(new($"上級直屬 {direct} 個聚落", -3 * (direct - 5)));
        if (ctx.StrongRule(liege))
            factors.Add(new("上級強勢統治", 10));
        return factors;
    }

    public static int LoyaltyValue(int vassal, int liege, int week, Fold fold, DemoRealmContext ctx) => Loyalty(vassal, liege, week, fold, ctx).Sum(f => f.Value);

    /// <summary>上級方的兵力：上級加上不在反它的派系裡的直屬下級。</summary>
    public static long LoyalArmy(int liege, Fold fold, Func<int, long> residents)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(residents);
        return Army(residents(liege)) + fold.Liege.Where(kv => kv.Value == liege && !(fold.Faction.TryGetValue(kv.Key, out var f) && f.Liege == liege))
            .Sum(kv => Army(residents(kv.Key)));
    }

    /// <summary>本週獨立戰爭的交戰（首領與上級；開戰的下一週起）；<paramref name="taken"/>＝本週已有天災或怪物的聚落。</summary>
    public static IReadOnlyDictionary<int, DemoDisasterStrike> Battles(int week, Fold fold, DemoWorldSites sites, Func<int, long> residents, IReadOnlySet<int> taken, RngRegistry rng)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(taken);
        var strikes = new Dictionary<int, DemoDisasterStrike>();
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        foreach (var s in fold.Struggles.Values.Where(x => x.Armed && week > x.StartWeek && week <= x.EndWeek).OrderBy(x => x.VassalId))
        {
            var rebels = s.Members.Sum(m => Army(residents(m)));
            var loyal = LoyalArmy(s.LiegeId, fold, residents);
            long Deaths(long enemy, long here, int k)
                => Math.Clamp(1 + enemy * (50 + (long)(Draw(rng, week, s.VassalId, k) % 101UL)) / (100L * TempArmyPerDeath), 0L, Math.Max(0L, here - 1));
            if (!taken.Contains(s.VassalId) && !strikes.ContainsKey(s.VassalId) && residents(s.VassalId) > 0)
                strikes[s.VassalId] = new DemoDisasterStrike(s.VassalId, DemoSettlementEventKind.SecessionBattle,
                    Deaths(loyal, residents(s.VassalId), 2), $"獨立戰爭：與{Name(s.LiegeId)}的軍隊交戰");
            if (!taken.Contains(s.LiegeId) && !strikes.ContainsKey(s.LiegeId) && residents(s.LiegeId) > 0)
                strikes[s.LiegeId] = new DemoDisasterStrike(s.LiegeId, DemoSettlementEventKind.SecessionBattle,
                    Deaths(rebels, residents(s.LiegeId), 3), $"出兵鎮壓叛離的{Name(s.VassalId)}");
        }
        return strikes;
    }

    /// <summary>
    /// 本週隸屬的變化（週結算之後）：①鬥爭結束（獨立或失敗）；②每個聚落依編號：上級沒落而獨立、歸附、加入或退出派系；
    /// ③派系首領加入滿半年就請願、下通牒（上級接受或開戰）。<paramref name="natural"/>＝依層級影響範圍的自然上級（第 08-19 章）。
    /// 事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static IReadOnlyList<DemoSettlementEvent> Advance(int week, TimeSpan worldTime, Fold fold, DemoRealmContext ctx,
        IReadOnlyDictionary<int, int> natural, RngRegistry rng, bool rebellions, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(natural);
        var sites = ctx.Sites;
        var events = new List<DemoSettlementEvent>();
        var byId = sites.Settlements.ToDictionary(s => s.Id);
        bool Alive(int id) => byId.TryGetValue(id, out var s) && !s.IsRuin && ctx.Residents(id) > 0;
        string Name(int id) => byId.TryGetValue(id, out var s) ? s.Name : $"#{id}";
        void Add(int settlementId, DemoSettlementEventKind kind, long count, string cause)
            => events.Add(new DemoSettlementEvent(week, worldTime, settlementId, Name(settlementId), kind, count, cause + "【TEMP】",
                EventId: checked(nextEventId + events.Count)));
        var lieges = new Dictionary<int, int>(fold.Liege.Where(kv => Alive(kv.Key)));
        var faction = fold.Faction.Where(kv => Alive(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var busy = new HashSet<int>(); // 本週已經有隸屬變化的聚落
        bool Cycle(int vassal, int liege)
        {
            var at = liege;
            for (var guard = 0; guard < 64; guard++)
            {
                if (at == vassal)
                    return true;
                if (!lieges.TryGetValue(at, out at))
                    return false;
            }
            return true;
        }
        void Free(int id, int former, string cause)
        {
            Add(id, DemoSettlementEventKind.Independence, former, cause);
            lieges.Remove(id);
            faction.Remove(id);
            busy.Add(id);
        }
        void Independence(int leader, int liege, IEnumerable<int> members, string text)
        {
            Free(leader, liege, text);
            // 其他成員一起獨立，歸附首領成立新勢力（WorldBox 叛亂後成立新王國）。
            foreach (var m in members.Where(m => m != leader && Alive(m) && lieges.TryGetValue(m, out var up) && up == liege).ToList())
            {
                Free(m, liege, $"隨{Name(leader)}脫離{Name(liege)}");
                if (!Cycle(m, leader))
                {
                    Add(m, DemoSettlementEventKind.VassalJoined, leader, $"{Name(m)}加入新獨立的{Name(leader)}");
                    lieges[m] = leader;
                }
            }
        }
        void Fail(DemoSecession s, string text)
        {
            Add(s.VassalId, DemoSettlementEventKind.SecessionFailed, s.LiegeId, text);
            foreach (var m in s.Members)
            {
                faction.Remove(m);
                busy.Add(m);
            }
        }

        // ① 鬥爭結束：請願期滿、戰爭分出勝負或打滿一年；首領沒了就結束（上級沒落交給 ②）。
        foreach (var s in fold.Struggles.Values.OrderBy(x => x.VassalId).ToList())
        {
            var leader = s.VassalId;
            if (!Alive(s.LiegeId))
                continue;
            if (!Alive(leader))
            {
                Fail(s, $"{Name(leader)}沒落，{Name(s.LiegeId)}的獨立派系瓦解");
                continue;
            }
            if (!s.Armed)
            {
                if (week < s.EndWeek)
                    continue;
                var differs = ctx.Culture(leader) is { } a && ctx.Culture(s.LiegeId) is { } b && a != b;
                if ((int)(Draw(rng, week, leader, 0) % 100UL) < TempPeacefulWinPercent + (differs ? TempCultureBonusPercent : 0))
                    Independence(leader, s.LiegeId, s.Members, $"{Name(s.LiegeId)}接受了{Name(leader)}的請願，讓它們獨立");
                else
                    Fail(s, $"{Name(s.LiegeId)}駁回了{Name(leader)}的請願");
                continue;
            }
            var fought = week - s.StartWeek;
            var score = $"（叛軍死 {s.RebelLosses}、上級死 {s.LoyalLosses}）";
            if (fought >= TempWarMinWeeks && s.LoyalLosses > 0 && s.LoyalLosses * 100 >= s.RebelLosses * TempWarScorePercent)
                Independence(leader, s.LiegeId, s.Members, $"{Name(leader)}在獨立戰爭中擊敗{Name(s.LiegeId)}{score}");
            else if (fought >= TempWarMinWeeks && s.RebelLosses > 0 && s.RebelLosses * 100 >= s.LoyalLosses * TempWarScorePercent)
                Fail(s, $"{Name(leader)}的叛軍被{Name(s.LiegeId)}擊潰{score}");
            else if (week >= s.EndWeek)
                Fail(s, $"獨立戰爭打了一年不分勝負，{Name(leader)}只好維持原狀{score}");
        }

        // ② 每個聚落：上級沒落、歸附、派系的加入與退出。
        foreach (var s in sites.Settlements.Where(x => Alive(x.Id) && !busy.Contains(x.Id)).OrderBy(x => x.Id))
        {
            var id = s.Id;
            if (!lieges.TryGetValue(id, out var liege))
            {
                if (natural.TryGetValue(id, out var lord) && Alive(lord) && !Cycle(id, lord))
                {
                    Add(id, DemoSettlementEventKind.VassalJoined, lord, $"{Name(id)}歸附{Name(lord)}");
                    lieges[id] = lord;
                }
                continue;
            }
            if (!Alive(liege))
            {
                Free(id, liege, $"上級{Name(liege)}已經沒落，{Name(id)}自然獨立");
                continue;
            }
            if (fold.Struggles.Values.Any(x => x.VassalId == id || x.Members.Contains(id)))
                continue;
            var loyalty = LoyaltyValue(id, liege, week, fold, ctx);
            var inFaction = faction.TryGetValue(id, out var member) && member.Liege == liege;
            if (!inFaction && loyalty < 0 && !fold.Calm(id, week) && rebellions) // 第 08-19c 章：世界法則關掉叛亂時不再加入派系
            {
                Add(id, DemoSettlementEventKind.FactionJoined, liege, $"{Name(id)}對{Name(liege)}不滿（忠誠 {loyalty}），加入獨立派系");
                faction[id] = (liege, week);
            }
            else if (inFaction && loyalty >= TempLeaveLoyalty)
            {
                Add(id, DemoSettlementEventKind.FactionLeft, liege, $"{Name(id)}對{Name(liege)}回心轉意（忠誠 {loyalty}），退出獨立派系");
                faction.Remove(id);
            }
        }

        // ③ 派系行動：首領（人口最多的成員）加入滿半年就請願或下通牒（只看上週以前就加入的成員）。
        foreach (var group in faction.Where(kv => rebellions && fold.Faction.ContainsKey(kv.Key)).GroupBy(kv => kv.Value.Liege).OrderBy(g => g.Key))
        {
            var liege = group.Key;
            if (!Alive(liege) || busy.Contains(liege) || fold.Struggles.Values.Any(x => x.LiegeId == liege))
                continue;
            var members = group.Select(kv => kv.Key).Where(m => !busy.Contains(m) && lieges.TryGetValue(m, out var up) && up == liege).OrderBy(m => m).ToList();
            if (members.Count == 0)
                continue;
            var leader = members.OrderByDescending(ctx.Residents).ThenBy(m => m).First();
            if (week - faction[leader].Week < TempDiscontentWeeks)
                continue;
            var rebels = members.Sum(m => Army(ctx.Residents(m)));
            var loyal = Math.Max(1L, LoyalArmy(liege, fold, ctx.Residents));
            var power = rebels * 100 / loyal;
            var who = members.Count == 1 ? Name(leader) : $"{Name(leader)}等 {members.Count} 個聚落";
            if (power >= TempAcceptPercent)
                Independence(leader, liege, members, $"{who}向{Name(liege)}下最後通牒（兵力 {rebels} 對 {loyal}），{Name(liege)}自知不敵，同意它們獨立");
            else if (power >= TempUltimatumPercent)
                Add(leader, DemoSettlementEventKind.SecessionArmed, liege, $"{who}向{Name(liege)}下最後通牒被拒，掀起獨立戰爭（兵力 {rebels} 對 {loyal}）");
            else
                Add(leader, DemoSettlementEventKind.SecessionPeaceful, liege, $"{who}兵力不足（{rebels} 對 {loyal}），向{Name(liege)}請願抗議、要求獨立");
        }
        return events;
    }

    /// <summary>勢力頁與聚落詳情的鬥爭說明。</summary>
    public static string Describe(DemoSecession struggle, DemoWorldSites sites, int week)
    {
        ArgumentNullException.ThrowIfNull(struggle);
        ArgumentNullException.ThrowIfNull(sites);
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var who = struggle.Members.Count <= 1 ? Name(struggle.VassalId) : $"{Name(struggle.VassalId)}等 {struggle.Members.Count} 個聚落";
        return struggle.Armed
            ? $"⚔ {who}正與{Name(struggle.LiegeId)}打獨立戰爭（第 {Math.Max(0, week - struggle.StartWeek)} 週；叛軍死 {struggle.RebelLosses}、上級死 {struggle.LoyalLosses}）"
            : $"✊ {who}正向{Name(struggle.LiegeId)}請願抗議（約 {Math.Max(0, struggle.EndWeek - week)} 週後有結果）";
    }

    /// <summary>聚落詳情的忠誠度明細與派系狀態（沒有上級回傳空）。</summary>
    public static IReadOnlyList<string> LoyaltyLines(int vassal, int week, Fold fold, DemoRealmContext ctx)
    {
        ArgumentNullException.ThrowIfNull(fold);
        if (!fold.Liege.TryGetValue(vassal, out var liege))
            return Array.Empty<string>();
        var factors = Loyalty(vassal, liege, week, fold, ctx);
        var lines = new List<string> { $"對上級的忠誠：{factors.Sum(f => f.Value)}" };
        lines.AddRange(factors.Select(f => $"　{f.Text} {(f.Value >= 0 ? "+" : "")}{f.Value}"));
        if (fold.Faction.TryGetValue(vassal, out var member))
            lines.Add($"已加入獨立派系 {week - member.Week} 週（首領加入滿 {TempDiscontentWeeks} 週就行動）");
        if (fold.Calm(vassal, week))
            lines.Add("剛被鎮壓，暫時不會再叛離");
        return lines;
    }
}
