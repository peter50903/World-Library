using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19e 章：一場征服戰爭（攻方王國、守方勢力的頂層、戰爭目標聚落、第幾週開戰）；雙方累計死亡由交戰事件推得。
/// 目標就是守方頂層時，打贏就征服整個勢力。
/// </summary>
public sealed record DemoWar(int AttackerId, int DefenderId, int TargetId, int StartWeek)
{
    /// <summary>最晚的結束週（打滿一年不分勝負就白和平）。</summary>
    public int EndWeek => StartWeek + DemoRealms.TempWarMaxWeeks;

    public long AttackerLosses { get; init; }
    public long DefenderLosses { get; init; }

    /// <summary>宣戰事件的原因文字（宣戰理由）。</summary>
    public string Cause { get; init; } = "";

    public bool WholeRealm => TargetId == DefenderId;
}

/// <summary>
/// 第 08-19e 章【TEMP 規則】征服時代的王國戰爭（使用者 2026-11-11：征服時代的規則——王國之間開戰、擴張與征服；參考十字軍之王與 WorldBox）。
/// <list type="number">
/// <item><b>王國</b>：不隸屬他人、層級至少 <see cref="TempKingdomTier"/>、勢力裡至少一個臣屬的聚落（整個勢力一起打仗）。</item>
/// <item><b>宣戰</b>（十字軍之王的宣戰理由＋WorldBox 的王國擴張）：每 <see cref="TempConquestCheckWeeks"/> 週檢查一次；
///   目標是離本國任一聚落 <see cref="TempConquestReach"/> 格內、別的勢力裡最近的聚落（守方頂層也可以，打贏就整個勢力臣服）；
///   本國兵力至少是守方的 <see cref="TempConquestStrengthPercent"/>% 才考慮，機率 <see cref="TempConquestChancePercent"/>%，信仰不同、文化不同再加。</item>
/// <item><b>交戰</b>：開戰的下一週起，每週攻方首都與戰爭目標交戰（與天災同一欄週帳）；守方在本土，戰力 ×<see cref="TempHomePercent"/>%。</item>
/// <item><b>結果</b>：打滿 <see cref="TempWarMinWeeks"/> 週後一方累計死亡達另一方的 <see cref="TempWarScorePercent"/>% 分出勝負；
///   打滿一年白和平。攻方贏了目標改隸屬攻方首都（征服）；不論勝負雙方停戰 <see cref="TempConquestTruceWeeks"/> 週。
///   被征服的聚落 <see cref="TempConqueredWeeks"/> 週內對新上級忠誠 <see cref="TempConqueredLoyalty"/>（容易再叛離，第 08-19b 章）。</item>
/// </list>
/// 戰爭、停戰由事件推得（<see cref="Fold"/>），不另存；亂數同第 08-19b 章（實體局部亂數，不推進計數器）。
/// </summary>
public static partial class DemoRealms
{
    /// <summary>【TEMP】王國的最低層級。</summary>
    public const DemoSettlementTier TempKingdomTier = DemoSettlementTier.City;

    /// <summary>【TEMP】幾週檢查一次宣戰；目標要在幾格內；停戰幾週；被征服後幾週內忠誠扣分、扣多少。</summary>
    public const int TempConquestCheckWeeks = 13, TempConquestReach = 10, TempConquestTruceWeeks = 260, TempConqueredWeeks = 260, TempConqueredLoyalty = -10;

    /// <summary>【TEMP】兵力至少是守方幾 % 才宣戰；宣戰機率；信仰不同、文化不同各加幾 %；守方本土戰力加成。</summary>
    public const int TempConquestStrengthPercent = 120, TempConquestChancePercent = 25, TempHolyWarBonusPercent = 15, TempCultureWarBonusPercent = 10, TempHomePercent = 125;

    public sealed partial class Fold
    {
        private readonly Dictionary<int, DemoWar> _wars = new();
        private readonly Dictionary<(int, int), int> _warTruce = new();
        private readonly Dictionary<int, int> _conqueredWeek = new();

        /// <summary>進行中的征服戰爭（依攻方）。</summary>
        public IReadOnlyDictionary<int, DemoWar> Wars => _wars;

        /// <summary>這個聚落參與的戰爭（攻方、守方頂層或戰爭目標）。</summary>
        public DemoWar? WarOf(int settlementId)
            => _wars.Values.OrderBy(w => w.AttackerId).FirstOrDefault(w => w.AttackerId == settlementId || w.DefenderId == settlementId || w.TargetId == settlementId);

        /// <summary>兩個勢力之間還在停戰。</summary>
        public bool WarTruce(int a, int b, int week)
            => _warTruce.TryGetValue(a < b ? (a, b) : (b, a), out var until) && week < until;

        /// <summary>剛被征服（<see cref="TempConqueredWeeks"/> 週內）。</summary>
        public bool Conquered(int settlementId, int week)
            => _conqueredWeek.TryGetValue(settlementId, out var w) && week - w < TempConqueredWeeks;

        private void Truce(int a, int b, int week) => _warTruce[a < b ? (a, b) : (b, a)] = week + TempConquestTruceWeeks;

        private void ApplyConquest(DemoSettlementEvent e)
        {
            var id = e.SettlementId;
            switch (e.Kind)
            {
                case DemoSettlementEventKind.WarDeclared:
                    var target = (int)e.Count;
                    _wars[id] = new DemoWar(id, Top(target, _liege), target, e.Week) { Cause = DemoPlayerDetail.Clean(e.Cause) };
                    break;
                case DemoSettlementEventKind.ConquestBattle:
                    foreach (var (attacker, w) in _wars.OrderBy(kv => kv.Key).ToList())
                    {
                        if (id == attacker)
                            _wars[attacker] = w with { AttackerLosses = w.AttackerLosses + e.Count };
                        else if (id == w.TargetId)
                            _wars[attacker] = w with { DefenderLosses = w.DefenderLosses + e.Count };
                    }
                    break;
                case DemoSettlementEventKind.Conquered:
                    if (_wars.Remove((int)e.Count, out var won))
                        Truce(won.AttackerId, won.DefenderId, e.Week);
                    _liege[id] = (int)e.Count;
                    _joinedWeek.Remove(id);
                    _faction.Remove(id);
                    _struggles.Remove(id);
                    _conqueredWeek[id] = e.Week;
                    break;
                case DemoSettlementEventKind.WarEnded:
                    if (_wars.Remove(id, out var ended))
                        Truce(ended.AttackerId, ended.DefenderId, e.Week);
                    break;
            }
        }
    }

    /// <summary>沿隸屬往上找到勢力的頂層（不隸屬他人的聚落）。</summary>
    public static int Top(int id, IReadOnlyDictionary<int, int> lieges)
    {
        ArgumentNullException.ThrowIfNull(lieges);
        var at = id;
        for (var guard = 0; guard < 64 && lieges.TryGetValue(at, out var up); guard++)
            at = up;
        return at;
    }

    /// <summary>一個勢力的兵力：勢力裡每個有人的聚落的兵力總和。</summary>
    public static long RealmArmy(int top, IReadOnlyDictionary<int, int> lieges, IEnumerable<int> alive, Func<int, long> residents)
        => alive.Where(id => Top(id, lieges) == top).Sum(id => Army(residents(id)));

    /// <summary>本週征服戰爭的交戰（攻方首都與戰爭目標；開戰的下一週起）。</summary>
    private static void ConquestBattles(int week, Fold fold, DemoWorldSites sites, Func<int, long> residents, IReadOnlySet<int> taken, RngRegistry rng,
        Dictionary<int, DemoDisasterStrike> strikes)
    {
        if (fold.Wars.Count == 0)
            return;
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var alive = sites.Settlements.Where(s => !s.IsRuin && residents(s.Id) > 0).Select(s => s.Id).ToList();
        foreach (var w in fold.Wars.Values.Where(x => week > x.StartWeek && week <= x.EndWeek).OrderBy(x => x.AttackerId))
        {
            var attack = RealmArmy(w.AttackerId, fold.Liege, alive, residents);
            var defend = RealmArmy(Top(w.TargetId, fold.Liege), fold.Liege, alive, residents) * TempHomePercent / 100;
            long Deaths(long enemy, long here, int k)
                => Math.Clamp(1 + enemy * (50 + (long)(Draw(rng, week, w.AttackerId, k) % 101UL)) / (100L * TempArmyPerDeath), 0L, Math.Max(0L, here - 1));
            if (!taken.Contains(w.TargetId) && !strikes.ContainsKey(w.TargetId) && residents(w.TargetId) > 0)
                strikes[w.TargetId] = new DemoDisasterStrike(w.TargetId, DemoSettlementEventKind.ConquestBattle,
                    Deaths(attack, residents(w.TargetId), 6), $"征服戰爭：抵抗{Name(w.AttackerId)}的大軍");
            if (!taken.Contains(w.AttackerId) && !strikes.ContainsKey(w.AttackerId) && residents(w.AttackerId) > 0)
                strikes[w.AttackerId] = new DemoDisasterStrike(w.AttackerId, DemoSettlementEventKind.ConquestBattle,
                    Deaths(defend, residents(w.AttackerId), 7), $"出兵征討{Name(w.TargetId)}");
        }
    }

    /// <summary>④ 征服戰爭：先結束（征服、敗退、白和平），再讓王國宣戰（<paramref name="declare"/>＝征服時代而且世界法則開著）。</summary>
    private static void AdvanceConquest(int week, Fold fold, DemoRealmContext ctx, Dictionary<int, int> lieges, HashSet<int> busy,
        Func<int, bool> alive, Func<int, string> name, Action<int, DemoSettlementEventKind, long, string> add, RngRegistry rng, bool declare)
    {
        var fighting = new HashSet<int>();
        foreach (var w in fold.Wars.Values.OrderBy(x => x.AttackerId))
        {
            var score = $"（攻方死 {w.AttackerLosses}、守方死 {w.DefenderLosses}）";
            var fought = week - w.StartWeek;
            void End(string text) => add(w.AttackerId, DemoSettlementEventKind.WarEnded, w.TargetId, text);
            if (!alive(w.AttackerId) || lieges.ContainsKey(w.AttackerId))
                End($"{name(w.AttackerId)}已無力再戰，對{name(w.TargetId)}的征服戰爭結束");
            else if (!alive(w.TargetId) || Top(w.TargetId, lieges) != w.DefenderId)
                End($"{name(w.TargetId)}的局勢已變，{name(w.AttackerId)}收兵");
            else if (fought >= TempWarMinWeeks && w.DefenderLosses > 0 && w.DefenderLosses * 100 >= w.AttackerLosses * TempWarScorePercent
                && Top(w.AttackerId, lieges) != w.TargetId)
            {
                var what = w.WholeRealm ? $"{name(w.TargetId)}與它的整個勢力" : name(w.TargetId);
                add(w.TargetId, DemoSettlementEventKind.Conquered, w.AttackerId, $"{name(w.AttackerId)}征服了{what}{score}");
                lieges[w.TargetId] = w.AttackerId;
                busy.Add(w.TargetId);
            }
            else if (fought >= TempWarMinWeeks && w.AttackerLosses > 0 && w.AttackerLosses * 100 >= w.DefenderLosses * TempWarScorePercent)
                End($"{name(w.AttackerId)}的大軍在{name(w.TargetId)}城下敗退{score}");
            else if (week >= w.EndWeek)
                End($"征服{name(w.TargetId)}的戰爭打了一年不分勝負，{name(w.AttackerId)}與{name(w.DefenderId)}白和平{score}");
            else
            {
                fighting.Add(w.AttackerId);
                fighting.Add(w.DefenderId);
                continue;
            }
            busy.Add(w.AttackerId);
        }
        if (!declare)
            return;

        var living = ctx.Sites.Settlements.Where(s => alive(s.Id)).Select(s => s.Id).OrderBy(x => x).ToList();
        var byId = ctx.Sites.Settlements.ToDictionary(s => s.Id);
        var tops = living.ToDictionary(id => id, id => Top(id, lieges));
        int Distance(int a, int b) => DemoHexWorld.Distance(new DemoCellRef(byId[a].Column, byId[a].Row), new DemoCellRef(byId[b].Column, byId[b].Row));
        bool Struggling(int top) => fold.Struggles.Values.Any(s => s.LiegeId == top || tops.GetValueOrDefault(s.LiegeId) == top);
        foreach (var king in living.Where(id => !lieges.ContainsKey(id) && week % TempConquestCheckWeeks == id % TempConquestCheckWeeks))
        {
            if (fighting.Contains(king) || busy.Contains(king) || ctx.Tier(king) < TempKingdomTier || Struggling(king))
                continue;
            var members = living.Where(id => tops[id] == king).ToList();
            if (members.Count < 2)
                continue;
            var attack = RealmArmy(king, lieges, living, ctx.Residents);
            // 每個鄰近勢力取離本國最近的聚落當戰爭目標；只挑打得贏的（兵力至少 120%），最近的先。
            var pick = living.Where(t => tops[t] != king && !fighting.Contains(tops[t]) && !busy.Contains(t) && !busy.Contains(tops[t])
                    && fold.StruggleOf(t) is null && !Struggling(tops[t]) && !fold.WarTruce(king, tops[t], week))
                .Select(t => (Target: t, Defender: tops[t], Distance: members.Min(m => Distance(m, t))))
                .Where(x => x.Distance <= TempConquestReach)
                .GroupBy(x => x.Defender)
                .Select(g => g.OrderBy(x => x.Distance).ThenBy(x => x.Target).First())
                .Select(x => (x.Target, x.Defender, x.Distance, Army: RealmArmy(x.Defender, lieges, living, ctx.Residents)))
                .Where(x => attack * 100 >= x.Army * TempConquestStrengthPercent)
                .OrderBy(x => x.Distance).ThenBy(x => x.Army).ThenBy(x => x.Target)
                .Select(x => ((int, int, long)?)(x.Target, x.Defender, x.Army)).FirstOrDefault();
            if (pick is not { } p)
                continue;
            var (target, defender, army) = p;
            var holy = ctx.Faith(king) is { } f1 && ctx.Faith(target) is { } f2 && f1 != f2;
            var foreign = ctx.Culture(king) is { } c1 && ctx.Culture(target) is { } c2 && c1 != c2;
            var chance = TempConquestChancePercent + (holy ? TempHolyWarBonusPercent : 0) + (foreign ? TempCultureWarBonusPercent : 0);
            if ((int)(Draw(rng, week, king, 5) % 100UL) >= chance)
                continue;
            var reason = holy ? "以信仰之名發動聖戰" : foreign ? "要把異族納入版圖" : "為了擴張領土";
            var goal = target == defender ? $"要征服整個{name(defender)}" : $"要奪取{name(defender)}的{name(target)}";
            add(king, DemoSettlementEventKind.WarDeclared, target, $"{name(king)}向{name(defender)}宣戰，{reason}，{goal}（兵力 {attack} 對 {army}）");
            fighting.Add(king);
            fighting.Add(defender);
        }
    }

    /// <summary>勢力頁與聚落詳情的戰爭說明（雙方與目標 → 起因 → 進展 → 損失）。</summary>
    public static string DescribeWar(DemoWar war, DemoWorldSites sites, int week)
    {
        ArgumentNullException.ThrowIfNull(war);
        ArgumentNullException.ThrowIfNull(sites);
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var goal = war.WholeRealm ? "整個勢力" : Name(war.TargetId);
        var cause = war.Cause.Length > 0 ? $"｜起因：{war.Cause}" : "";
        return $"⚔ {Name(war.AttackerId)}對{Name(war.DefenderId)}：征服戰爭（目標：{goal}）{cause}｜第 {Math.Max(0, week - war.StartWeek)} 週｜攻方死 {war.AttackerLosses}、守方死 {war.DefenderLosses}";
    }
}
