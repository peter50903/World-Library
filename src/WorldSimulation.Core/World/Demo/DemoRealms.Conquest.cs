using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19e 章：一場征服戰爭（攻方王國、守方勢力的頂層、戰爭目標聚落、第幾週開戰）；雙方累計死亡由交戰事件推得。
/// 目標就是守方頂層時，打贏就征服整個勢力。
/// </summary>
public sealed record DemoWar(int AttackerId, int DefenderId, int TargetId, int StartWeek)
{
    /// <summary>最晚的結束週（第 08-22a 章：打滿兩年依戰爭分數議和）。</summary>
    public int EndWeek => StartWeek + DemoRealms.TempConquestMaxWeeks;

    /// <summary>第 08-22a 章：攻陷戰爭目標的那一週（0＝還在圍城）。</summary>
    public int OccupiedWeek { get; init; }

    public bool Occupied => OccupiedWeek > 0;

    public long AttackerLosses { get; init; }
    public long DefenderLosses { get; init; }

    /// <summary>宣戰事件的原因文字（宣戰理由）。</summary>
    public string Cause { get; init; } = "";

    public bool WholeRealm => TargetId == DefenderId;

    /// <summary>第 08-45 章：攻下戰爭目標之後又佔領的守方聚落（依佔領先後）；戰爭結束就歸還（只割讓戰爭目標）。</summary>
    public IReadOnlyList<int> Holdings { get; init; } = Array.Empty<int>();

    /// <summary>第 08-45 章：最近一次攻陷（戰爭目標或其他聚落）的週次；下一座從這週開始圍。</summary>
    public int LastOccupiedWeek { get; init; }
}

/// <summary>
/// 第 08-19e 章【TEMP 規則】征服時代的王國戰爭（使用者 2026-11-11：征服時代的規則——王國之間開戰、擴張與征服；參考十字軍之王與 WorldBox）。
/// <list type="number">
/// <item><b>王國</b>：不隸屬他人、層級至少 <see cref="TempKingdomTier"/>、勢力裡至少一個臣屬的聚落（整個勢力一起打仗）。</item>
/// <item><b>宣戰</b>（十字軍之王的宣戰理由＋WorldBox 的王國擴張）：每 <see cref="TempConquestCheckWeeks"/> 週檢查一次；
///   目標是離本國任一聚落 <see cref="TempConquestReach"/> 格內、別的勢力裡最近的聚落（守方頂層也可以，打贏就整個勢力臣服）；
///   本國兵力至少是守方的 <see cref="TempConquestStrengthPercent"/>% 才考慮，機率 <see cref="TempConquestChancePercent"/>%，信仰不同、文化不同再加。</item>
/// <item><b>交戰</b>：開戰的下一週起，每週攻方首都與戰爭目標交戰（與天災同一欄週帳）；守方在本土，戰力 ×<see cref="TempHomePercent"/>%。</item>
/// <item><b>結果</b>（第 08-22a 章修正，參考 CK3）：攻方先圍城，打滿圍城週數（<see cref="TempSiegeWeeks"/>）而會戰沒有落下風就攻陷目標；
///   戰爭分數（<see cref="WarScore"/>：會戰＋攻陷＋攻陷後每週加分）滿 100 攻方征服，到 −60 以下攻方敗退，打滿兩年依分數議和或白和平。
///   攻方贏了目標改隸屬攻方首都（征服）；不論勝負雙方停戰 <see cref="TempConquestTruceWeeks"/> 週。
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
    public const int TempConquestStrengthPercent = 120, TempConquestChancePercent = 25, TempHolyWarBonusPercent = 15, TempCultureWarBonusPercent = 10, TempHomePercent = 125, TempSurrenderPercent = 500;

    /// <summary>
    /// 第 08-22a 章【TEMP】戰爭分數（參考 CK3，−100～100）：會戰最多 ±<see cref="TempBattleScore"/>（雙方累計死亡差÷（合計＋<see cref="TempBattleDamp"/>））、
    /// 攻陷目標 +<see cref="TempOccupyScore"/>、攻陷後每週再 +<see cref="TempTickScore"/>（最多 +<see cref="TempTickMax"/>）。
    /// 滿 <see cref="TempWinScore"/> 攻方勝；打滿 <see cref="TempWarMinWeeks"/> 週後到 <see cref="TempLoseScore"/> 以下攻方敗退；
    /// 打滿 <see cref="TempConquestMaxWeeks"/> 週分數 <see cref="TempTermsScore"/> 以上攻方勝（議和割讓），否則白和平。
    /// </summary>
    public const int TempBattleScore = 40, TempBattleDamp = 10, TempOccupyScore = 30, TempTickScore = 3, TempTickMax = 60,
        TempWinScore = 100, TempLoseScore = -60, TempTermsScore = 50, TempConquestMaxWeeks = 104;

    /// <summary>第 08-22a 章【TEMP】圍城幾週才攻陷（目標層級越高、城牆越堅固）：營地 3、村莊 4、城鎮 6、城市 8、首都 10。</summary>
    public static int TempSiegeWeeks(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => 3,
        DemoSettlementTier.Village => 4,
        DemoSettlementTier.Town => 6,
        DemoSettlementTier.City => 8,
        _ => 10
    };

    /// <summary>會戰的分數（雙方累計死亡差換算，±<see cref="TempBattleScore"/>）。</summary>
    public static int BattleScore(DemoWar war)
    {
        ArgumentNullException.ThrowIfNull(war);
        var swing = TempBattleScore * (war.DefenderLosses - war.AttackerLosses) / Math.Max(1L, war.DefenderLosses + war.AttackerLosses + TempBattleDamp);
        return (int)Math.Clamp(swing, -TempBattleScore, TempBattleScore);
    }

    /// <summary>第 08-47 章：戰爭分數的各項（戰爭視窗的分項）：會戰、攻陷目標、佔領時間、又佔的聚落（加總前，未封頂）。</summary>
    public static (int Battle, int Occupy, int Time, int Holdings) WarScoreParts(DemoWar war, int week)
    {
        ArgumentNullException.ThrowIfNull(war);
        return (BattleScore(war), war.Occupied ? TempOccupyScore : 0,
            war.Occupied ? Math.Min(TempTickMax, Math.Max(0, week - war.OccupiedWeek) * TempTickScore) : 0,
            war.Holdings.Count * TempHoldingScore);
    }

    /// <summary>第 08-22a 章：這一週的戰爭分數（正＝攻方佔上風）。</summary>
    public static int WarScore(DemoWar war, int week)
    {
        ArgumentNullException.ThrowIfNull(war);
        var occupation = war.Occupied ? TempOccupyScore + Math.Min(TempTickMax, Math.Max(0, week - war.OccupiedWeek) * TempTickScore) : 0;
        return Math.Clamp(BattleScore(war) + occupation + war.Holdings.Count * TempHoldingScore, -100, 100); // 第 08-45 章：每多佔一座
    }

    /// <summary>
    /// 第 08-22e 章【TEMP】宣戰理由「收復失地」（參考 CK3 的宣稱）：被別的勢力征服走的聚落，原本的勢力 <see cref="TempReconquestWeeks"/> 週內可以宣戰收回——
    /// 兵力只要守方的 <see cref="TempReconquestStrengthPercent"/>%（一般要 120%），宣戰機率再加 <see cref="TempReconquestBonusPercent"/>%，先於其他目標。
    /// </summary>
    public const int TempReconquestWeeks = 1040, TempReconquestStrengthPercent = 80, TempReconquestBonusPercent = 40;

    public sealed partial class Fold
    {
        /// <summary>第 08-22e 章：被征服走的聚落 → 原本的勢力頂層與第幾週失去。</summary>
        private readonly Dictionary<int, (int Former, int Week)> _lostTo = new();

        /// <summary>第 08-22e 章：這個聚落在 <see cref="TempReconquestWeeks"/> 週內從哪個勢力被征服走（沒有＝null）。</summary>
        public int? LostBy(int settlementId, int week)
            => _lostTo.TryGetValue(settlementId, out var lost) && week - lost.Week < TempReconquestWeeks ? lost.Former : null;

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
                        else if (id == w.TargetId || w.Holdings.Contains(id) || Top(id, _liege) == w.DefenderId) // 第 08-45 章：圍攻守方的其他聚落
                            _wars[attacker] = w with { DefenderLosses = w.DefenderLosses + e.Count };
                    }
                    break;
                case DemoSettlementEventKind.Occupied: // 第 08-22a 章：攻方攻陷戰爭目標，之後每週加分
                    if (_wars.TryGetValue((int)e.Count, out var sieged) && sieged.TargetId == id && !sieged.Occupied)
                        _wars[(int)e.Count] = sieged with { OccupiedWeek = e.Week, LastOccupiedWeek = e.Week };
                    else if (sieged is not null && sieged.TargetId != id && !sieged.Holdings.Contains(id)) // 第 08-45 章：又佔領一座
                        _wars[(int)e.Count] = sieged with { Holdings = sieged.Holdings.Append(id).ToArray(), LastOccupiedWeek = e.Week };
                    break;
                case DemoSettlementEventKind.Conquered:
                    // 第 08-22e 章：原本屬於別的勢力（不是自己獨立）就記下來，原本的勢力之後可以收復。
                    if (Top(id, _liege) is var former && former != id && former != Top((int)e.Count, _liege))
                        _lostTo[id] = (former, e.Week);
                    else
                        _lostTo.Remove(id);
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

    /// <summary>
    /// 第 08-22b 章【TEMP】會戰的地形（參考 CK3）：守方的聚落在丘陵上，攻方造成的死傷 ×<see cref="TempHillsAttackPercent"/>%、
    /// 在森林裡 ×<see cref="TempForestAttackPercent"/>%；攻方首都到戰爭目標的直線要過河，再 ×<see cref="TempRiverAttackPercent"/>%。
    /// 守方造成的死傷不變（守方本來就有本土加成）。
    /// </summary>
    public const int TempHillsAttackPercent = 75, TempForestAttackPercent = 85, TempRiverAttackPercent = 80;

    /// <summary>第 08-22b 章：攻方打這個目標時的地形（傷害百分比與說明；沒有地圖＝100%、沒有說明）。</summary>
    public static (int Percent, string Note) BattleTerrain(DemoHexWorld? map, DemoSettlement? attacker, DemoSettlement? target)
    {
        if (map is null || attacker is null || target is null || !map.Contains(target.Column, target.Row))
            return (100, "");
        var percent = 100;
        var notes = new List<string>();
        switch (map.GetCell(target.Column, target.Row).Terrain)
        {
            case DemoTerrain.Hills:
                percent = percent * TempHillsAttackPercent / 100;
                notes.Add("據守丘陵");
                break;
            case DemoTerrain.Forest:
                percent = percent * TempForestAttackPercent / 100;
                notes.Add("據守森林");
                break;
        }
        if (CrossesRiver(map, attacker.Column, attacker.Row, target.Column, target.Row))
        {
            percent = percent * TempRiverAttackPercent / 100;
            notes.Add("攻方渡河");
        }
        return (percent, notes.Count == 0 ? "" : "（" + string.Join("、", notes) + "）");
    }

    /// <summary>兩格之間的直線（不含兩端）經過河流格。</summary>
    public static bool CrossesRiver(DemoHexWorld map, int c0, int r0, int c1, int r1)
    {
        ArgumentNullException.ThrowIfNull(map);
        var steps = Math.Max(Math.Abs(c1 - c0), Math.Abs(r1 - r0));
        for (var i = 1; i < steps; i++)
        {
            var c = (int)Math.Round(c0 + (c1 - c0) * (double)i / steps, MidpointRounding.AwayFromZero);
            var r = (int)Math.Round(r0 + (r1 - r0) * (double)i / steps, MidpointRounding.AwayFromZero);
            if (map.Contains(c, r) && map.GetCell(c, r) is { Terrain: DemoTerrain.Water, MainWaterway: true, Sea: false })
                return true;
        }
        return false;
    }

    /// <summary>
    /// 第 08-22f 章【TEMP】英雄帶兵（參考 CK3 的統帥）：攻方首都、戰爭目標有在世的英雄時，那一方造成的死傷 ×<see cref="TempHeroBattlePercent"/>%，
    /// 交戰的原因寫出英雄的名字（英雄的事蹟由此流傳）。
    /// </summary>
    public const int TempHeroBattlePercent = 130;

    /// <summary>本週征服戰爭的交戰（攻方首都與戰爭目標；開戰的下一週起）。</summary>
    private static void ConquestBattles(int week, Fold fold, DemoWorldSites sites, Func<int, long> residents, IReadOnlySet<int> taken, RngRegistry rng,
        Dictionary<int, DemoDisasterStrike> strikes, Func<int, DemoTrait>? temper = null, DemoHexWorld? map = null, Func<int, string?>? heroOf = null,
        Func<int, DemoDefense>? defense = null)
    {
        if (fold.Wars.Count == 0)
            return;
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var alive = sites.Settlements.Where(s => !s.IsRuin && residents(s.Id) > 0).Select(s => s.Id).ToList();
        var living = alive.ToHashSet();
        foreach (var w in fold.Wars.Values.Where(x => week > x.StartWeek && week <= x.EndWeek).OrderBy(x => x.AttackerId))
        {
            var attack = RealmArmy(w.AttackerId, fold.Liege, alive, residents);
            var defend = RealmArmy(Top(w.TargetId, fold.Liege), fold.Liege, alive, residents) * TempHomePercent / 100;
            long Deaths(long enemy, long here, int k) => WarDeaths(enemy, here, Draw(rng, week, w.AttackerId, k)); // 第 08-19h 章
            // 第 08-45 章：行軍——還沒到城下就不交戰；守方兵力夠時出城，在半路打一場野戰。
            var campaign = Campaign(w, map, sites);
            if (week <= w.StartWeek + campaign.MarchWeeks)
            {
                if (week == campaign.FieldWeek && defend * 100 >= attack * TempSallyPercent
                    && !taken.Contains(w.TargetId) && !strikes.ContainsKey(w.TargetId) && residents(w.TargetId) > 0
                    && !taken.Contains(w.AttackerId) && !strikes.ContainsKey(w.AttackerId) && residents(w.AttackerId) > 0)
                {
                    var at = campaign.FieldAt;
                    var (field, fieldNote) = BattleTerrain(map, sites.Settlements.FirstOrDefault(s => s.Id == w.AttackerId), new DemoSettlement(0, "", at.Column, at.Row));
                    strikes[w.TargetId] = new DemoDisasterStrike(w.TargetId, DemoSettlementEventKind.ConquestBattle,
                        Deaths(attack * field / 100 * TempFieldBattlePercent / 100, residents(w.TargetId), 8),
                        $"野戰：{Name(w.TargetId)}的守軍出城，在 ({at.Column},{at.Row}) 迎擊{Name(w.AttackerId)}的大軍{fieldNote}");
                    strikes[w.AttackerId] = new DemoDisasterStrike(w.AttackerId, DemoSettlementEventKind.ConquestBattle,
                        Deaths(defend * TempFieldBattlePercent / 100, residents(w.AttackerId), 9),
                        $"野戰：征討{Name(w.TargetId)}的大軍行至 ({at.Column},{at.Row})，遭守軍迎擊");
                }
                continue;
            }
            // 第 08-45 章：圍攻戰爭目標；攻下後轉攻守方附近的其他聚落。
            var besieged = NextHolding(w, sites, fold.Liege, living.Contains) is var next && next != 0 ? next : w.TargetId;
            var (terrain, note) = BattleTerrain(map, sites.Settlements.FirstOrDefault(s => s.Id == w.AttackerId), sites.Settlements.FirstOrDefault(s => s.Id == besieged)); // 第 08-22b 章
            // 第 08-22f 章：英雄帶兵。
            var attackHero = heroOf?.Invoke(w.AttackerId);
            var defendHero = heroOf?.Invoke(besieged);
            var attackBoost = attackHero is null ? 100 : TempHeroBattlePercent;
            var defendBoost = defendHero is null ? 100 : TempHeroBattlePercent;
            // 第 08-34 章：戰爭目標的城牆讓攻方造成的死傷變少。
            var walls = defense?.Invoke(besieged) ?? new DemoDefense(0, false, 100, 0, 0, null);
            if (walls.Wall is not null)
            {
                terrain = terrain * walls.WarDamagePercent / 100;
                note = note.Length == 0 ? $"（據{walls.Wall}而守）" : note.Insert(note.Length - 1, $"、據{walls.Wall}而守");
            }
            if (!taken.Contains(besieged) && !strikes.ContainsKey(besieged) && residents(besieged) > 0)
                strikes[besieged] = new DemoDisasterStrike(besieged, DemoSettlementEventKind.ConquestBattle,
                    Deaths(attack * DemoTraits.BattlePercent(temper?.Invoke(fold.Seat(w.AttackerId)) ?? DemoTrait.None) / 100 * terrain / 100 * attackBoost / 100, residents(besieged), 6),
                    $"征服戰爭：抵抗{Name(w.AttackerId)}的大軍" + (attackHero is null ? "" : $"，敵方英雄{attackHero}率軍") + (defendHero is null ? "" : $"，英雄{defendHero}領軍守城") + note);
            if (!taken.Contains(w.AttackerId) && !strikes.ContainsKey(w.AttackerId) && residents(w.AttackerId) > 0)
                strikes[w.AttackerId] = new DemoDisasterStrike(w.AttackerId, DemoSettlementEventKind.ConquestBattle,
                    Deaths(defend * DemoTraits.BattlePercent(temper?.Invoke(fold.Seat(w.DefenderId)) ?? DemoTrait.None) / 100 * defendBoost / 100, residents(w.AttackerId), 7),
                    $"出兵征討{Name(besieged)}" + (attackHero is null ? "" : $"，英雄{attackHero}率軍") + (defendHero is null ? "" : $"，遭遇敵方英雄{defendHero}守城"));
        }
    }

    /// <summary>④ 征服戰爭：先結束（征服、敗退、白和平），再讓王國宣戰（<paramref name="declare"/>＝征服時代而且世界法則開著）。</summary>
    private static void AdvanceConquest(int week, Fold fold, DemoRealmContext ctx, Dictionary<int, int> lieges, HashSet<int> busy,
        Func<int, bool> alive, Func<int, string> name, Action<int, DemoSettlementEventKind, long, string> add, RngRegistry rng, bool declare)
    {
        var fighting = new HashSet<int>();
        foreach (var w in fold.Wars.Values.OrderBy(x => x.AttackerId))
        {
            // 第 08-22a 章（參考 CK3）：圍城、攻陷與戰爭分數決定勝負（原本只看雙方死亡比例）。
            var warScore = WarScore(w, week);
            var score = $"（戰爭分數 {warScore}；攻方死 {w.AttackerLosses}、守方死 {w.DefenderLosses}）";
            var fought = week - w.StartWeek;
            void End(string text) => add(w.AttackerId, DemoSettlementEventKind.WarEnded, w.TargetId, text);
            if (!alive(w.AttackerId) || lieges.ContainsKey(w.AttackerId))
                End($"{name(w.AttackerId)}已無力再戰，對{name(w.TargetId)}的征服戰爭結束");
            else if (!alive(w.TargetId) || Top(w.TargetId, lieges) != w.DefenderId)
                End($"{name(w.TargetId)}的局勢已變，{name(w.AttackerId)}收兵");
            else if ((warScore >= TempWinScore || week >= w.EndWeek && warScore >= TempTermsScore) && Top(w.AttackerId, lieges) != w.TargetId)
            {
                var what = w.WholeRealm ? $"{name(w.TargetId)}與它的整個勢力" : name(w.TargetId);
                add(w.TargetId, DemoSettlementEventKind.Conquered, w.AttackerId, warScore >= TempWinScore
                    ? $"{name(w.AttackerId)}征服了{what}{score}"
                    : $"{name(w.DefenderId)}戰事不利，與{name(w.AttackerId)}議和，割讓{what}{score}");
                lieges[w.TargetId] = w.AttackerId;
                busy.Add(w.TargetId);
            }
            else if (fought >= TempWarMinWeeks && warScore <= TempLoseScore)
                End($"{name(w.AttackerId)}的大軍在{name(w.TargetId)}城下敗退{score}");
            else if (week >= w.EndWeek)
                End($"征服{name(w.TargetId)}的戰爭打了兩年不分勝負，{name(w.AttackerId)}與{name(w.DefenderId)}白和平{score}");
            else
            {
                // 圍城：打滿圍城週數、會戰沒有落下風，就攻陷戰爭目標（之後每週加分，CK3 的佔領）。
                // 第 08-45 章：行軍到城下才開始圍；攻下目標後接著圍守方附近的其他聚落（每座從上一座攻陷的那週算起）。
                var arrive = w.StartWeek + Campaign(w, ctx.Map, ctx.Sites).MarchWeeks;
                var besieged = NextHolding(w, ctx.Sites, lieges, alive);
                if (besieged != 0 && week > arrive)
                {
                    var since = besieged == w.TargetId ? arrive : Math.Max(arrive, w.LastOccupiedWeek);
                    var siege = SiegeNeed(besieged, ctx.Tier, ctx.Defense); // 第 08-34 章：城牆
                    if (week - since >= siege && BattleScore(w) >= 0)
                        add(besieged, DemoSettlementEventKind.Occupied, w.AttackerId, besieged == w.TargetId
                            ? $"{name(w.AttackerId)}攻陷了{name(w.TargetId)}（圍城 {week - since} 週）"
                            : $"{name(w.AttackerId)}的軍隊佔領了{name(w.DefenderId)}的{name(besieged)}（圍城 {week - since} 週）");
                }
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
            var members = living.Where(id => tops[id] == king).ToList(); // 第 08-19i 章：沒有臣屬的城市也能出兵（原本至少要一個臣屬）
            var attack = RealmArmy(king, lieges, living, ctx.Residents);
            // 第 08-10g 章：領袖的性格——謹慎的要有更大的兵力優勢才出兵；好戰的更常出兵。
            var temper = ctx.Leader?.Invoke(fold.Seat(king))?.Traits ?? DemoTrait.None;
            var need = DemoTraits.Has(temper, DemoTrait.Cautious) ? DemoTraits.TempCautiousStrengthPercent : TempConquestStrengthPercent;
            // 每個鄰近勢力取離本國最近的聚落當戰爭目標；只挑打得贏的（兵力至少 120%），最近的先。
            var pick = living.Where(t => tops[t] != king && !fighting.Contains(tops[t]) && !busy.Contains(t) && !busy.Contains(tops[t])
                    && fold.StruggleOf(t) is null && !Struggling(tops[t])
                    && (DemoTraits.Has(temper, DemoTrait.Deceitful) || !fold.WarTruce(king, tops[t], week))) // 第 08-10h 章：狡詐的不理會停戰
                .Select(t => (Target: t, Defender: tops[t], Distance: members.Min(m => Distance(m, t))))
                .Where(x => x.Distance <= TempConquestReach)
                .GroupBy(x => x.Defender)
                .Select(g => g.OrderBy(x => x.Distance).ThenBy(x => x.Target).First())
                .Select(x => (x.Target, x.Defender, x.Distance, Army: RealmArmy(x.Defender, lieges, living, id => ctx.KnownResidents(king, id)))) // 第 14-1 章★4：國王所知的對方兵力
                .Where(x => attack * 100 >= x.Army * need)
                .OrderBy(x => x.Distance).ThenBy(x => x.Army).ThenBy(x => x.Target)
                .Select(x => ((int, int, long)?)(x.Target, x.Defender, x.Army)).FirstOrDefault();
            // 第 08-22e 章：收復失地優先（兵力要求較低）。
            var regain = living.Where(t => fold.LostBy(t, week) == king && tops[t] != king && !fighting.Contains(tops[t]) && !busy.Contains(t) && !busy.Contains(tops[t])
                    && fold.StruggleOf(t) is null && !Struggling(tops[t]) && (DemoTraits.Has(temper, DemoTrait.Deceitful) || !fold.WarTruce(king, tops[t], week)))
                .Select(t => (Target: t, Defender: tops[t], Distance: members.Min(m => Distance(m, t))))
                .Where(x => x.Distance <= TempConquestReach)
                .Select(x => (x.Target, x.Defender, x.Distance, Army: RealmArmy(x.Defender, lieges, living, id => ctx.KnownResidents(king, id))))
                .Where(x => attack * 100 >= x.Army * TempReconquestStrengthPercent)
                .OrderBy(x => x.Distance).ThenBy(x => x.Target)
                .Select(x => ((int, int, long)?)(x.Target, x.Defender, x.Army)).FirstOrDefault();
            var reconquest = regain is not null;
            if ((regain ?? pick) is not { } p)
                continue;
            var (target, defender, army) = p;
            var holy = ctx.Faith(king) is { } f1 && ctx.Faith(target) is { } f2 && f1 != f2 && !DemoTraits.Has(temper, DemoTrait.Cynical); // 第 08-10h 章：多疑的不為信仰開戰
            var foreign = ctx.Culture(king) is { } c1 && ctx.Culture(target) is { } c2 && c1 != c2;
            // 第 08-22g 章：聖戰的加成依兩個信仰的敵意——同源（誤入歧途）一半、不同源（敵對）全額。
            var hostility = holy ? ctx.HostilityOf(ctx.Faith(king)!.Value, ctx.Faith(target)!.Value) : 0;
            var chance = TempConquestChancePercent + (foreign ? TempCultureWarBonusPercent : 0) + DemoTraits.WarChance(temper) + (reconquest ? TempReconquestBonusPercent : 0)
                + (holy ? TempHolyWarBonusPercent * hostility / 2 * (DemoTraits.Has(temper, DemoTrait.Zealous) ? DemoTraits.TempZealousHolyWarTimes : 1) : 0);
            if ((int)(Draw(rng, week, king, 5) % 100UL) >= chance)
                continue;
            var reason = reconquest ? "要收復失地" : holy ? "以信仰之名發動聖戰" : foreign ? "要把異族納入版圖" : "為了擴張領土"; // 第 08-22e 章
            if (attack * 100 >= army * DemoTraits.SurrenderPercent(ctx.Leader?.Invoke(fold.Seat(defender))?.Traits ?? DemoTrait.None, TempSurrenderPercent)) // 第 08-10h 章：堅毅、軟弱
            {
                // 第 08-19i 章：兵力懸殊（五倍以上）就不戰而降，不打一場沒有懸念的仗。
                add(target, DemoSettlementEventKind.Conquered, king, target == defender
                    ? $"{name(king)}兵臨城下（兵力 {attack} 對 {army}），{name(target)}不戰而降，歸入{name(king)}"
                    : $"{name(king)}兵臨城下（兵力 {attack} 對 {army}），{name(target)}背離{name(defender)}，歸入{name(king)}");
                lieges[target] = king;
                busy.Add(target);
                busy.Add(king);
                continue;
            }
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
        var siege = war.Occupied ? $"已攻陷{Name(war.TargetId)}" : $"圍城中";
        return $"⚔ {Name(war.AttackerId)}對{Name(war.DefenderId)}：征服戰爭（目標：{goal}）{cause}｜第 {Math.Max(0, week - war.StartWeek)} 週｜{siege}｜戰爭分數 {WarScore(war, week)}"
            + $"｜攻方死 {war.AttackerLosses}、守方死 {war.DefenderLosses}";
    }
}
