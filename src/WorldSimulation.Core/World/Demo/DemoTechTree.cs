namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10d 章 D：知識樹上一項技術在某個聚落的狀態。</summary>
public enum DemoTechStatus
{
    /// <summary>已經會（開局知識或已習得）。</summary>
    Known = 0,

    /// <summary>研究中（已有進度）。</summary>
    Researching = 1,

    /// <summary>前置都已具備，還沒開始累積。</summary>
    Ready = 2,

    /// <summary>還缺前置技術。</summary>
    Blocked = 3
}

/// <summary>
/// 第 08-10d 章 D：知識樹的一個節點（介面依領域分區、依階級分欄，前置之間畫連線）。
/// <see cref="StatusText"/>＝這個聚落目前的狀態（哪一年習得、進度、還缺哪些前置）；
/// <see cref="Origin"/>＝這項技術在世界上的來歷（誰首創、哪一年、已有幾個聚落會）。
/// </summary>
public sealed record DemoTechTreeNode(
    string Id, string Name, string Domain, string DomainName, int Tier, DemoTechStatus Status, long Percent, int? LearnedYear,
    IReadOnlyList<string> ParentIds, string StatusText, string Origin);

/// <summary>
/// 第 08-10d 章 D：某個聚落眼中的整棵知識樹。只讀知識紀錄，不改世界、不抽亂數。
/// 「前置都已具備」只看知識樹的前置；地理、人口、等級等其他研究條件不在這裡判斷（見「研究」頁）。
/// </summary>
public static class DemoTechTree
{
    private static int YearOf(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));

    public static IReadOnlyList<DemoTechTreeNode> Build(DemoObserverWorld world, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var learned = world.Knowledge.Records.Where(r => r.Learned).ToList();
        // 每項技術：世界上第一個習得的聚落（同一週取編號小的）與已習得的聚落數。
        var firsts = learned.GroupBy(r => r.Technology).ToDictionary(g => g.Key,
            g => (First: g.OrderBy(r => r.LearnedWeek).ThenBy(r => r.SettlementId).First(), Count: g.Select(r => r.SettlementId).Distinct().Count()));
        var mine = world.Knowledge.Records.Where(r => r.SettlementId == settlementId).GroupBy(r => r.Technology).ToDictionary(g => g.Key, g => g.First());
        bool Has(string techId) => DemoTechCatalog.Contains(techId)
            && (DemoKnowledge.IsOpening(DemoTechnology.Of(techId)) || (mine.TryGetValue(DemoTechnology.Of(techId), out var r) && r.Learned));

        var nodes = new List<DemoTechTreeNode>();
        foreach (var tech in DemoTechnology.All)
        {
            var node = tech.Node;
            var parents = node.Parents.Where(DemoTechCatalog.Contains).ToList();
            var opening = DemoKnowledge.IsOpening(tech);
            mine.TryGetValue(tech, out var rec);
            var known = opening || rec is { Learned: true };
            var missing = parents.Where(p => !Has(p)).ToList();
            var percent = known ? 100 : rec is null ? 0 : Math.Clamp(rec.Progress * 100 / Math.Max(1, DemoKnowledge.ThresholdOf(tech)), 0, 99);
            var status = known ? DemoTechStatus.Known
                : percent > 0 || rec is { Progress: > 0 } ? DemoTechStatus.Researching
                : missing.Count == 0 ? DemoTechStatus.Ready
                : DemoTechStatus.Blocked;
            int? learnedYear = !opening && rec is { Learned: true } ? YearOf(rec.LearnedWeek) : null;
            var statusText = status switch
            {
                DemoTechStatus.Known => opening ? "開局時就會" : $"第 {learnedYear} 年習得",
                DemoTechStatus.Researching => $"研究中，進度 {percent}%",
                DemoTechStatus.Ready => "前置都已具備，居民實踐後就會開始研究",
                _ => "還缺前置：" + string.Join("、", missing.Select(p => DemoKnowledge.Name(DemoTechnology.Of(p))))
            };
            string origin;
            if (opening)
                origin = "開局知識：每個聚落一開始就會";
            else if (!firsts.TryGetValue(tech, out var first))
                origin = "世界上還沒有聚落會";
            else
            {
                var who = first.First.SettlementId == settlementId ? "本聚落" : names.GetValueOrDefault(first.First.SettlementId, $"#{first.First.SettlementId}");
                origin = $"世界首創：{who}（第 {YearOf(first.First.LearnedWeek)} 年）；世界上已有 {first.Count} 個聚落會";
            }
            nodes.Add(new DemoTechTreeNode(node.Id, DemoKnowledge.Name(tech), node.Domain, DemoPlayerDetail.DomainName(node.Domain), node.Tier,
                status, percent, learnedYear, parents, statusText, origin));
        }
        return nodes.AsReadOnly();
    }
}
