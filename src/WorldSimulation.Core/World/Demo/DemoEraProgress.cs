namespace WorldSimulation.Core.World.Demo;

/// <summary>時代進度的一個條件（名稱、0～100、說明）。</summary>
public sealed record DemoEraCondition(string Name, int Percent, string Detail);

/// <summary>
/// 第 08-19d 章：時代進度條。<see cref="Next"/>＝下一個時代（已是最後一個時代＝null）；<see cref="Percent"/>＝整體進度（最接近的聚落）；
/// <see cref="Leader"/>＝最接近的聚落編號（沒有＝0）。
/// </summary>
public sealed record DemoEraProgress(DemoEra Current, DemoEra? Next, int Percent, int Leader, IReadOnlyList<DemoEraCondition> Conditions)
{
    /// <summary>【TEMP】王國至少要有幾個臣屬聚落（勢力裡除了首都以外的聚落）。</summary>
    public const int TempKingdomVassals = 2;

    /// <summary>
    /// 進度怎麼算（只讀世界）：
    /// <list type="bullet">
    /// <item>希望→開拓：任一聚落習得任一技術；進度＝世界上研究最快的那一項的百分比。</item>
    /// <item>開拓→征服（第 08-6 章：第一個王國成立）：某個不隸屬他人的聚落同時「會王權」「是首都」「勢力裡有 <see cref="TempKingdomVassals"/> 個以上臣屬聚落」；
    ///   每個聚落三項各算百分比取平均，取最接近的聚落。</item>
    /// <item>征服時代是目前最後一個時代。</item>
    /// </list>
    /// </summary>
    public static DemoEraProgress Of(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var era = world.Era;
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        string Name(int id) => world.Sites.Settlements.FirstOrDefault(s => s.Id == id) is { } s ? DemoSettlementHistory.TitledName(s, world.ChronicleOf(id).Tier) : $"#{id}";
        switch (era)
        {
            case DemoEra.Hope:
            {
                var best = world.Knowledge.Records.Where(r => !r.Learned && r.Progress > 0)
                    .Select(r => (r.SettlementId, r.Technology, Percent: (int)Math.Min(99, r.Progress * 100 / DemoKnowledge.ThresholdOf(r.Technology))))
                    .OrderByDescending(x => x.Percent).ThenBy(x => x.SettlementId).FirstOrDefault();
                var percent = best.SettlementId == 0 ? 0 : best.Percent;
                return new DemoEraProgress(era, DemoEra.Pioneer, percent, best.SettlementId, new[]
                {
                    new DemoEraCondition("任一聚落研究出第一項技術", percent,
                        best.SettlementId == 0 ? "還沒有聚落開始研究" : $"{Name(best.SettlementId)}的{DemoKnowledge.Name(best.Technology)}研究到 {percent}%")
                });
            }
            case DemoEra.Pioneer:
            {
                var kingship = DemoTechnology.Of(DemoSettlementHistory.CapitalTechId);
                var territory = world.Territory();
                (int Id, int Percent, int Tier, int King, int Vassals) Score(DemoSettlement s)
                {
                    var tier = (int)Math.Round((int)world.ChronicleOf(s.Id).Tier * 100.0 / (int)DemoSettlementTier.Capital);
                    var record = world.Knowledge.Of(s.Id, kingship);
                    var king = record is { Learned: true } ? 100 : record is null ? 0 : (int)Math.Min(99, record.Progress * 100 / DemoKnowledge.ThresholdOf(kingship));
                    var members = territory.LiegeOf(s.Id) is null ? territory.Realm.Count(kv => kv.Value == s.Id && kv.Key != s.Id) : 0;
                    var vassals = Math.Min(100, members * 100 / TempKingdomVassals);
                    return (s.Id, (tier + king + vassals) / 3, tier, king, vassals);
                }
                var best = alive.Select(Score).OrderByDescending(x => x.Percent).ThenBy(x => x.Id).FirstOrDefault();
                var site = alive.FirstOrDefault(s => s.Id == best.Id);
                var tierName = site is null ? "" : DemoSettlementHistory.TierName(world.ChronicleOf(site.Id).Tier);
                return new DemoEraProgress(era, (DemoEra)2, Math.Min(99, best.Percent), best.Id, new[]
                {
                    new DemoEraCondition("學會王權", best.King, best.King >= 100 ? "已學會" : $"研究到 {best.King}%"),
                    new DemoEraCondition("成為首都", best.Tier, $"目前是{tierName}"),
                    new DemoEraCondition($"統領 {TempKingdomVassals} 個以上臣屬聚落", best.Vassals,
                        $"目前 {best.Vassals * TempKingdomVassals / 100} 個"),
                });
            }
            default:
                return new DemoEraProgress(era, null, 100, 0, Array.Empty<DemoEraCondition>());
        }
    }

    /// <summary>
    /// 第一個王國：不隸屬他人、是首都、會王權，而且勢力裡（直屬與間接）有 <see cref="TempKingdomVassals"/> 個以上臣屬聚落的聚落（編號最小的）；沒有＝null。
    /// </summary>
    public static int? Kingdom(IReadOnlyDictionary<int, int> lieges, IEnumerable<int> alive, Func<int, DemoSettlementTier> tierOf, Func<int, bool> knowsKingship)
    {
        ArgumentNullException.ThrowIfNull(lieges);
        ArgumentNullException.ThrowIfNull(alive);
        var living = alive.ToHashSet();
        int Top(int id)
        {
            for (var guard = 0; guard < 64 && lieges.TryGetValue(id, out var up) && living.Contains(up); guard++)
                id = up;
            return id;
        }
        var members = living.Where(lieges.ContainsKey).GroupBy(Top).ToDictionary(g => g.Key, g => g.Count(id => id != g.Key));
        return living.Where(id => !lieges.ContainsKey(id) && tierOf(id) == DemoSettlementTier.Capital && knowsKingship(id)
                && members.GetValueOrDefault(id) >= TempKingdomVassals)
            .Order().Cast<int?>().FirstOrDefault();
    }

    /// <summary>介面用的一行與提示（例：「開拓時代 → 征服時代 42%」）。</summary>
    public IReadOnlyList<string> Lines(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Next is not { } next)
            return new[] { $"{DemoKnowledge.EraName(Current)}：目前最後的時代" };
        var lines = new List<string> { $"{DemoKnowledge.EraName(Current)} → {DemoKnowledge.EraName(next)}：{Percent}%" };
        if (Leader != 0 && world.Sites.Settlements.FirstOrDefault(s => s.Id == Leader) is { } s)
            lines.Add($"最接近的聚落：{DemoSettlementHistory.TitledName(s, world.ChronicleOf(s.Id).Tier)}");
        lines.AddRange(Conditions.Select(c => $"{(c.Percent >= 100 ? "✔" : "・")} {c.Name}（{c.Detail}）"));
        return lines;
    }
}
