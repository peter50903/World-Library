using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-19b 章：一場脫離鬥爭（下級聚落想脫離上級）：誰、從誰、武力或非武力、第幾週開始。</summary>
public sealed record DemoSecession(int VassalId, int LiegeId, bool Armed, int StartWeek)
{
    public int EndWeek => StartWeek + (Armed ? DemoRealms.TempArmedWeeks : DemoRealms.TempPeacefulWeeks);
}

/// <summary>
/// 第 08-19b 章【TEMP 規則】隸屬要經過事件才改變（使用者 2026-11-08：勢力分裂前應該要有分裂鬥爭，武力或非武力）。
/// <list type="number">
/// <item><b>歸附</b>：沒有上級的聚落落在更高層級聚落的影響範圍內（第 08-19 章的「自然上級」），而且不會繞成圈，就歸附它（記「歸附」）。</item>
/// <item><b>脫離鬥爭</b>：已有上級的聚落，自然上級變了（長到同級以上、離開影響範圍、或另有更近的上級），不會立刻脫離，而是開始一場鬥爭——
///   主要文化與上級不同時是<b>武力抗爭</b>，相同時以 <see cref="TempArmedPercent"/>% 走武力、其餘是<b>非武力</b>的請願與抗議。</item>
/// <item><b>武力抗爭</b>：<see cref="TempArmedWeeks"/> 週，每週兩邊都有傷亡（與天災同一欄週帳，天災與怪物優先）；結束時以下級人口÷（下級＋上級人口）的比例
///   （<see cref="TempMinWinPercent"/>～<see cref="TempMaxWinPercent"/>%）決定成敗。</item>
/// <item><b>非武力</b>：<see cref="TempPeacefulWeeks"/> 週，沒有傷亡；結束時 <see cref="TempPeacefulWinPercent"/>% 成功（文化不同再加 <see cref="TempCultureBonusPercent"/>）。</item>
/// <item>成功記「獨立」、失敗記「脫離失敗」；失敗後 <see cref="TempCooldownWeeks"/> 週內不再鬥爭。上級成為廢墟或沒人時，下級直接獨立（不必鬥爭）。</item>
/// </list>
/// 隸屬與鬥爭由事件推得（<see cref="Fold"/>），不另存；亂數用事件流的實體局部亂數（索引＝週次與聚落），不推進計數器，重跑相同。
/// </summary>
public static class DemoRealms
{
    /// <summary>【TEMP】武力抗爭、非武力鬥爭各持續幾週；失敗後幾週內不再鬥爭。</summary>
    public const int TempArmedWeeks = 8, TempPeacefulWeeks = 26, TempCooldownWeeks = 104;

    /// <summary>【TEMP】文化相同時走武力的機率；武力的勝率上下限；非武力的基本勝率與文化不同的加成。</summary>
    public const int TempArmedPercent = 30, TempMinWinPercent = 15, TempMaxWinPercent = 85, TempPeacefulWinPercent = 35, TempCultureBonusPercent = 20;

    /// <summary>【TEMP】武力抗爭每週的傷亡：下級每 80 人、上級每 150 人多死 1 人（各至少 1 人）。</summary>
    public const int TempRebelsPerDeath = 80, TempLoyalistsPerDeath = 150;

    private const string Namespace = "TEMP-REALM-v1";

    public static bool IsBattle(DemoSettlementEventKind kind) => kind == DemoSettlementEventKind.SecessionBattle;

    /// <summary>由事件推得的隸屬、進行中的鬥爭與冷卻（依序餵入已提交的事件）。</summary>
    public sealed class Fold
    {
        private readonly Dictionary<int, int> _liege = new();
        private readonly Dictionary<int, DemoSecession> _struggles = new();
        private readonly Dictionary<int, int> _calmUntil = new();

        public IReadOnlyDictionary<int, int> Liege => _liege;

        public IReadOnlyDictionary<int, DemoSecession> Struggles => _struggles;

        public bool Calm(int settlementId, int week) => _calmUntil.TryGetValue(settlementId, out var until) && week < until;

        public void Apply(DemoSettlementEvent e)
        {
            switch (e.Kind)
            {
                case DemoSettlementEventKind.VassalJoined:
                    _liege[e.SettlementId] = (int)e.Count;
                    break;
                case DemoSettlementEventKind.SecessionArmed or DemoSettlementEventKind.SecessionPeaceful:
                    _struggles[e.SettlementId] = new DemoSecession(e.SettlementId, (int)e.Count, e.Kind == DemoSettlementEventKind.SecessionArmed, e.Week);
                    break;
                case DemoSettlementEventKind.Independence:
                    _liege.Remove(e.SettlementId);
                    _struggles.Remove(e.SettlementId);
                    break;
                case DemoSettlementEventKind.SecessionFailed:
                    _struggles.Remove(e.SettlementId);
                    _calmUntil[e.SettlementId] = e.Week + TempCooldownWeeks;
                    break;
            }
        }
    }

    private static ulong Draw(RngRegistry rng, int week, int settlementId, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace)
            .GetUInt64(checked((ulong)week * 1_048_576UL + (ulong)settlementId * 8UL + (ulong)k));

    /// <summary>本週武力抗爭的傷亡（打在聚落上，與天災同一欄週帳）；<paramref name="taken"/>＝本週已有天災或怪物的聚落。</summary>
    public static IReadOnlyDictionary<int, DemoDisasterStrike> Battles(int week, Fold fold, DemoWorldSites sites, Func<int, long> residents, IReadOnlySet<int> taken)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(taken);
        var strikes = new Dictionary<int, DemoDisasterStrike>();
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        foreach (var s in fold.Struggles.Values.Where(x => x.Armed && week > x.StartWeek && week < x.EndWeek).OrderBy(x => x.VassalId))
        {
            if (!taken.Contains(s.VassalId) && !strikes.ContainsKey(s.VassalId) && residents(s.VassalId) > 0)
                strikes[s.VassalId] = new DemoDisasterStrike(s.VassalId, DemoSettlementEventKind.SecessionBattle,
                    1 + residents(s.VassalId) / TempRebelsPerDeath, $"為脫離{Name(s.LiegeId)}與上級的軍隊交戰");
            if (!taken.Contains(s.LiegeId) && !strikes.ContainsKey(s.LiegeId) && residents(s.LiegeId) > 0)
                strikes[s.LiegeId] = new DemoDisasterStrike(s.LiegeId, DemoSettlementEventKind.SecessionBattle,
                    1 + residents(s.LiegeId) / TempLoyalistsPerDeath, $"出兵鎮壓叛離的{Name(s.VassalId)}");
        }
        return strikes;
    }

    /// <summary>
    /// 本週隸屬的變化（週結算之後；依聚落編號）：歸附、開始鬥爭、鬥爭結束（獨立或失敗）、上級消失而獨立。
    /// <paramref name="natural"/>＝依層級影響範圍的自然上級（第 08-19 章）。事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static IReadOnlyList<DemoSettlementEvent> Advance(int week, TimeSpan worldTime, Fold fold, DemoWorldSites sites,
        IReadOnlyDictionary<int, int> natural, Func<int, long> residents, Func<int, int?> cultureOf, RngRegistry rng, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(natural);
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(cultureOf);
        var events = new List<DemoSettlementEvent>();
        var byId = sites.Settlements.ToDictionary(s => s.Id);
        bool Alive(int id) => byId.TryGetValue(id, out var s) && !s.IsRuin && residents(id) > 0;
        string Name(int id) => byId.TryGetValue(id, out var s) ? s.Name : $"#{id}";
        void Add(int settlementId, DemoSettlementEventKind kind, long count, string cause)
            => events.Add(new DemoSettlementEvent(week, worldTime, settlementId, Name(settlementId), kind, count, cause + "【TEMP】",
                EventId: checked(nextEventId + events.Count)));
        // 這週新的上級（避免同一週內繞成圈）。
        var lieges = new Dictionary<int, int>(fold.Liege.Where(kv => Alive(kv.Key)));
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
        foreach (var s in sites.Settlements.Where(x => Alive(x.Id)).OrderBy(x => x.Id))
        {
            var id = s.Id;
            var want = natural.TryGetValue(id, out var n) && Alive(n) ? n : (int?)null;
            var has = lieges.TryGetValue(id, out var l) ? l : (int?)null;
            if (has is { } current && !Alive(current))
            {
                // 上級成了廢墟或沒人：直接獨立。
                Add(id, DemoSettlementEventKind.Independence, current, $"上級{Name(current)}已經沒落，{Name(id)}自然獨立");
                lieges.Remove(id);
                continue;
            }
            if (fold.Struggles.TryGetValue(id, out var struggle))
            {
                if (week < struggle.EndWeek)
                    continue;
                // 鬥爭結束：決定成敗。
                var rebels = residents(id);
                var loyal = Math.Max(1L, residents(struggle.LiegeId));
                var cultureDiffers = cultureOf(id) is { } c1 && cultureOf(struggle.LiegeId) is { } c2 && c1 != c2;
                var chance = struggle.Armed
                    ? (int)Math.Clamp(rebels * 100 / Math.Max(1L, rebels + loyal), TempMinWinPercent, TempMaxWinPercent)
                    : TempPeacefulWinPercent + (cultureDiffers ? TempCultureBonusPercent : 0);
                if ((int)(Draw(rng, week, id, 0) % 100UL) < chance)
                {
                    Add(id, DemoSettlementEventKind.Independence, struggle.LiegeId,
                        struggle.Armed ? $"{Name(id)}在武力抗爭中擊敗{Name(struggle.LiegeId)}，宣告獨立" : $"{Name(id)}長年請願抗議，{Name(struggle.LiegeId)}終於同意它獨立");
                    lieges.Remove(id);
                }
                else
                    Add(id, DemoSettlementEventKind.SecessionFailed, struggle.LiegeId,
                        struggle.Armed ? $"{Name(id)}的武力抗爭被{Name(struggle.LiegeId)}鎮壓，仍隸屬於它" : $"{Name(id)}的請願被{Name(struggle.LiegeId)}駁回，仍隸屬於它");
                continue;
            }
            if (has is null && want is { } lord && !Cycle(id, lord))
            {
                Add(id, DemoSettlementEventKind.VassalJoined, lord, $"{Name(id)}歸附{Name(lord)}");
                lieges[id] = lord;
            }
            else if (has is { } liege && want != liege && !fold.Calm(id, week))
            {
                var differs = cultureOf(id) is { } a && cultureOf(liege) is { } b && a != b;
                var armed = differs || (int)(Draw(rng, week, id, 1) % 100UL) < TempArmedPercent;
                var why = want is null ? "已經壯大到不必依附" : $"更想投靠{Name(want.Value)}";
                Add(id, armed ? DemoSettlementEventKind.SecessionArmed : DemoSettlementEventKind.SecessionPeaceful, liege,
                    armed ? $"{Name(id)}{why}，拿起武器要脫離{Name(liege)}" : $"{Name(id)}{why}，向{Name(liege)}請願脫離");
            }
        }
        return events;
    }

    /// <summary>勢力頁與聚落詳情的鬥爭說明（沒有回傳 null）。</summary>
    public static string? Describe(DemoSecession struggle, DemoWorldSites sites, int week)
    {
        ArgumentNullException.ThrowIfNull(struggle);
        ArgumentNullException.ThrowIfNull(sites);
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var left = Math.Max(0, struggle.EndWeek - week);
        return struggle.Armed
            ? $"⚔ {Name(struggle.VassalId)}正以武力抗爭，想脫離{Name(struggle.LiegeId)}（約 {left} 週後分出勝負）"
            : $"✊ {Name(struggle.VassalId)}正請願抗議，想脫離{Name(struggle.LiegeId)}（約 {left} 週後有結果）";
    }
}
