namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19f 章【TEMP 規則】繼位與內戰（使用者 2026-11-11：內戰用十字軍之王的方式——國家不變，換統治者）。
/// <list type="number">
/// <item><b>國王</b>：王國（第 08-19e 章）王座所在聚落的村長。王座預設在首都；內戰或逼宮成功後換到首領的聚落（新王朝），
///   那個聚落離開勢力時王座回到首都。</item>
/// <item><b>繼位</b>：王座所在聚落本週新任村長就記「繼位」；新君 <see cref="TempNewReignWeeks"/> 週內直屬臣屬忠誠 <see cref="TempNewReignLoyalty"/>（新君威望不足）。</item>
/// <item><b>篡位派系</b>：征服時代裡，對王國不滿、而且與國王同文化的臣屬加入的是篡位派系（異文化照舊是獨立派系）。
///   派系兵力達上級方 <see cref="TempAcceptPercent"/>% 逼國王退位；<see cref="TempUltimatumPercent"/>% 以上掀起內戰；不夠就繼續等。</item>
/// <item><b>內戰</b>：交戰、比分、勝負同獨立戰爭。打贏就篡位：王座換到首領的聚落，國家、首都與隸屬都不變；
///   擁立者 <see cref="TempBackedWeeks"/> 週內忠誠 +<see cref="TempBackedLoyalty"/>。打輸照舊被鎮壓。</item>
/// </list>
/// 王座、即位週與擁立者由事件推得，不另存。
/// </summary>
public static partial class DemoRealms
{
    /// <summary>【TEMP】新君即位後幾週內忠誠扣分、扣多少；國王出身的聚落加分；擁立者幾週內加分、加多少。</summary>
    public const int TempNewReignWeeks = 156, TempNewReignLoyalty = -15, TempRoyalSeatLoyalty = 20, TempBackedWeeks = 260, TempBackedLoyalty = 25;

    public sealed partial class Fold
    {
        private readonly Dictionary<int, int> _crown = new();
        private readonly Dictionary<int, int> _reignWeek = new();
        private readonly Dictionary<int, int> _backedUntil = new();

        /// <summary>王座所在的聚落（預設是勢力頂層；新王朝的聚落離開勢力時回到頂層）。</summary>
        public int Seat(int top)
            => _crown.TryGetValue(top, out var seat) && seat != top && Top(seat, _liege) == top ? seat : top;

        /// <summary>現任國王即位的週（沒有紀錄回傳 null）。</summary>
        public int? ReignWeek(int top) => _reignWeek.TryGetValue(top, out var w) ? w : null;

        /// <summary>擁立了現任國王（<see cref="TempBackedWeeks"/> 週內）。</summary>
        public bool Backed(int settlementId, int week) => _backedUntil.TryGetValue(settlementId, out var until) && week < until;

        private void ApplyCivil(DemoSettlementEvent e)
        {
            var id = e.SettlementId;
            switch (e.Kind)
            {
                case DemoSettlementEventKind.Succession:
                    _reignWeek[id] = e.Week;
                    break;
                case DemoSettlementEventKind.Usurped:
                    var liege = (int)e.Count;
                    var backers = _faction.Where(kv => kv.Value.Liege == liege).Select(kv => kv.Key)
                        .Concat(_struggles.TryGetValue(id, out var s) ? s.Members : Array.Empty<int>()).Append(id).Distinct().ToList();
                    foreach (var m in backers)
                    {
                        _faction.Remove(m);
                        _backedUntil[m] = e.Week + TempBackedWeeks;
                    }
                    _struggles.Remove(id);
                    _crown[liege] = id;
                    _reignWeek[liege] = e.Week;
                    break;
            }
        }
    }

    /// <summary>不滿的臣屬組的是篡位派系（征服時代、上級是王國、與上級同文化）。</summary>
    private static bool CivilFaction(int vassal, int liege, IReadOnlyDictionary<int, int> lieges, DemoRealmContext ctx, bool conquestEra)
        => conquestEra && !lieges.ContainsKey(liege) && ctx.Tier(liege) >= TempKingdomTier
            && ctx.Culture(vassal) is { } a && ctx.Culture(liege) is { } b && a == b;

    /// <summary>忠誠度的第 08-19f 章因素。</summary>
    private static void CivilFactors(int vassal, int liege, int week, Fold fold, List<DemoLoyaltyFactor> factors)
    {
        if (fold.ReignWeek(liege) is { } reign && week - reign < TempNewReignWeeks)
            factors.Add(new("新君即位", TempNewReignLoyalty));
        if (vassal != liege && fold.Seat(liege) == vassal)
            factors.Add(new("國王出身於此", TempRoyalSeatLoyalty));
        if (fold.Backed(vassal, week))
            factors.Add(new("擁立了現任國王", TempBackedLoyalty));
    }

    /// <summary>⑤ 王國的王座所在聚落本週新任村長：記繼位。</summary>
    private static void AdvanceSuccession(int week, Fold fold, DemoRealmContext ctx, Dictionary<int, int> lieges, HashSet<int> busy,
        Func<int, bool> alive, Func<int, string> name, Action<int, DemoSettlementEventKind, long, string> add, bool conquestEra)
    {
        var leaderOf = ctx.Leader;
        if (!conquestEra || leaderOf is null)
            return;
        var vassals = lieges.Values.ToHashSet();
        foreach (var top in ctx.Sites.Settlements.Select(s => s.Id).Where(id => alive(id) && !lieges.ContainsKey(id) && vassals.Contains(id)
            && !busy.Contains(id) && ctx.Tier(id) >= TempKingdomTier).OrderBy(id => id))
        {
            var seat = fold.Seat(top);
            if (!alive(seat))
                seat = top;
            if (leaderOf(seat) is not { } king || king.AppointedWeek != week || fold.ReignWeek(top) == week)
                continue;
            add(top, DemoSettlementEventKind.Succession, king.Id,
                $"{king.Name}繼承了{name(top)}的王位" + (seat != top ? $"（{name(seat)}王朝）" : ""));
        }
    }

    /// <summary>勢力頁的國王一行（不是王國回傳 null）。</summary>
    public static string? RulerLine(int top, Fold fold, Func<int, DemoRepresentative?> leader, DemoWorldSites sites, Func<int, DemoSettlementTier> tier, int day)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(tier);
        if (fold.Liege.ContainsKey(top) || !fold.Liege.Values.Contains(top) || tier(top) < TempKingdomTier)
            return null;
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var seat = fold.Seat(top);
        if (leader(seat) is not { } king)
            return null;
        var dynasty = seat != top ? $"，{Name(seat)}王朝" : "";
        var reign = fold.ReignWeek(top) is { } w ? $"，第 {w * 7 / 365 + 1} 年即位" : "";
        return $"國王：{king.Name}（{DemoCohortDemography.AgeAt(king.BirthYear, day)} 歲{dynasty}{reign}）";
    }
}
