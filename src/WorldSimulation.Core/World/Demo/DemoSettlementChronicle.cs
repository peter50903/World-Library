using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 04-1 章：聚落小史的一行（發生的週次＋文字）。</summary>
public sealed record DemoChronicleLine(int Week, string Text);

/// <summary>
/// 第 04-1 章：每個聚落自己記得的小史（使用者 2026-10-22：組織只需要知道自己勢力範圍內的事）。
/// 每週提交時由本週週報增量更新，存進存檔；讀檔後不需要舊週報也能列出聚落小史（<see cref="DemoSettlementHistory"/>）。
/// 規模里程碑（人口第一次達到門檻，見 DemoSettlementHistory）、人口高峰、代表人物就任與卸任；建立、分出新村與成為遺跡由聚落資料推得，不另記。
/// </summary>
public sealed record DemoSettlementChronicle(
    int NextMilestone,
    long Peak,
    int PeakWeek,
    ImmutableList<DemoChronicleLine> Milestones,
    ImmutableList<DemoChronicleLine> Representatives)
{
    public static DemoSettlementChronicle Empty { get; } = new(0, 0, 0,
        ImmutableList<DemoChronicleLine>.Empty, ImmutableList<DemoChronicleLine>.Empty);

    /// <summary>第 08-6 章 d 節：聚落目前的等級（每週結算後依人口與必要職位更新；升降都記一筆）。</summary>
    public DemoSettlementTier Tier { get; init; }

    /// <summary>值比較（清單逐項比）。</summary>
    public bool Equals(DemoSettlementChronicle? other)
        => other is not null && NextMilestone == other.NextMilestone && Peak == other.Peak && PeakWeek == other.PeakWeek
        && Tier == other.Tier
        && Milestones.SequenceEqual(other.Milestones) && Representatives.SequenceEqual(other.Representatives);

    public override int GetHashCode() => HashCode.Combine(NextMilestone, Peak, PeakWeek, Tier, Milestones.Count, Representatives.Count);

    /// <summary>指紋用的一行文字。</summary>
    public string Fingerprint()
        => $"{NextMilestone}/{Peak}@{PeakWeek}/t{(int)Tier}|{string.Join("|", Milestones.Select(l => $"{l.Week}:{l.Text}"))}|{string.Join("|", Representatives.Select(l => $"{l.Week}:{l.Text}"))}";

    /// <summary>
    /// 依本週週報更新全部聚落的小史。<paramref name="inOffice"/>＝本週開始時在任的代表人物（卸任的人從這裡找名字）。
    /// 純函式；與第 08-3 章「逐週掃描週報」的結果相同。
    /// </summary>
    /// <summary>第 08-10b 章：等級升級小史的關鍵字（與下方 <see cref="Advance"/> 的文字同一處維護）。</summary>
    public const string TierUpMarker = "發展為";

    /// <summary>第 08-10b 章：等級降級小史的關鍵字。</summary>
    public const string TierDownMarker = "，降為";

    /// <summary>第 08-10b 章：這一筆小史是不是等級升降（世界編年史列為重大）。</summary>
    public static bool IsTierChange(DemoChronicleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Text.Contains(TierUpMarker, StringComparison.Ordinal) || line.Text.Contains(TierDownMarker, StringComparison.Ordinal);
    }

    /// <summary>第 08-10b 章：這一筆小史是不是人口里程碑。</summary>
    public static bool IsPopulationMilestone(DemoChronicleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Text.StartsWith("人口首次達到", StringComparison.Ordinal);
    }

    public static ImmutableSortedDictionary<int, DemoSettlementChronicle> Advance(
        ImmutableSortedDictionary<int, DemoSettlementChronicle> chronicles, DemoWeekReport report,
        IReadOnlyList<DemoRepresentative> inOffice, DemoKnowledgeState? knowledge = null)
    {
        ArgumentNullException.ThrowIfNull(chronicles);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(inOffice);
        var builder = chronicles.ToBuilder();
        var milestones = DemoSettlementHistory.TempMilestones;
        foreach (var ledger in report.Settlements)
        {
            var c = builder.GetValueOrDefault(ledger.SettlementId) ?? Empty;
            var pop = ledger.PopulationClosing;
            var next = c.NextMilestone;
            var lines = c.Milestones;
            while (next < milestones.Count && pop >= milestones[next])
            {
                // 第 08-6 章 d 節：村莊改由木匠與人口共同決定（見下方等級），里程碑只記人數。
                lines = lines.Add(new DemoChronicleLine(report.Week, $"人口首次達到 {milestones[next]} 人"));
                next++;
            }
            var updated = c with { NextMilestone = next, Milestones = lines };
            if (pop > updated.Peak)
                updated = updated with { Peak = pop, PeakWeek = report.Week };
            if (!Equals(updated, c) || !builder.ContainsKey(ledger.SettlementId))
                builder[ledger.SettlementId] = updated;
        }

        // 第 08-6 章：習得技術、本聚落率先讓世界進入新時代。
        // 第 08-6 章 c 節：開墾完成農田。
        foreach (var e in report.Events.Where(e => e.Kind is DemoSettlementEventKind.KnowledgeLearned or DemoSettlementEventKind.EraBegan
            or DemoSettlementEventKind.FarmlandCleared or DemoSettlementEventKind.FarmlandAbandoned or DemoSettlementEventKind.CultureSplit or DemoSettlementEventKind.FaithFounded
            or DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Tornado or DemoSettlementEventKind.Meteorite or DemoSettlementEventKind.Flood
            or DemoSettlementEventKind.MeteoriteOre or DemoSettlementEventKind.Heatwave or DemoSettlementEventKind.Wildfire))
        {
            var c = builder.GetValueOrDefault(e.SettlementId) ?? Empty;
            var text = e.Kind switch
            {
                DemoSettlementEventKind.KnowledgeLearned => $"學會{DemoKnowledge.Name((DemoTechnology)e.Count)}",
                DemoSettlementEventKind.FarmlandCleared => "開墾一塊農田",
                DemoSettlementEventKind.FarmlandAbandoned => "一塊農田無人耕作，開始荒廢",
                DemoSettlementEventKind.CultureSplit or DemoSettlementEventKind.FaithFounded => DemoPlayerDetail.Clean(e.Cause),
                DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Tornado or DemoSettlementEventKind.Meteorite
                    or DemoSettlementEventKind.Flood => $"{DemoDisasters.Name(e.Kind)}：{DemoPlayerDetail.Clean(e.Cause)}", // 第 12-1 章 C3
                DemoSettlementEventKind.MeteoriteOre => DemoPlayerDetail.Clean(e.Cause),
                DemoSettlementEventKind.Heatwave or DemoSettlementEventKind.Wildfire => DemoPlayerDetail.Clean(e.Cause), // 第 12-1 章 C3b
                _ => $"率先讓世界進入{DemoKnowledge.EraName((DemoEra)e.Count)}"
            };
            builder[e.SettlementId] = c with { Milestones = c.Milestones.Add(new DemoChronicleLine(report.Week, text)) };
        }

        var known = inOffice.ToDictionary(r => r.Id);
        foreach (var r in report.Appointments)
        {
            known[r.Id] = r;
            var c = builder.GetValueOrDefault(r.SettlementId) ?? Empty;
            builder[r.SettlementId] = c with
            {
                Representatives = c.Representatives.Add(new DemoChronicleLine(report.Week, $"{r.Name} 成為{DemoRepresentative.RoleName(r.Role)}"))
            };
        }
        foreach (var x in report.RepresentativeExits)
        {
            if (!known.TryGetValue(x.Id, out var who)) continue;
            var c = builder.GetValueOrDefault(x.SettlementId) ?? Empty;
            builder[x.SettlementId] = c with
            {
                Representatives = c.Representatives.Add(new DemoChronicleLine(report.Week,
                    $"{DemoRepresentative.RoleName(who.Role)} {who.Name} 卸任：{x.Reason}"))
            };
        }
        // 第 08-11 章【TEMP 規則】居住地五級（世紀帝國式）：人口與本級設施（專家或技術）決定升降，每週最多一級（記入小史）。
        var exited = report.RepresentativeExits.Select(x => x.Id).ToHashSet();
        var office = inOffice.Where(r => !exited.Contains(r.Id)).Concat(report.Appointments).ToList();
        foreach (var ledger in report.Settlements)
        {
            var c = builder.GetValueOrDefault(ledger.SettlementId) ?? Empty;
            var id = ledger.SettlementId;
            bool Knows(string tech) => knowledge?.Knows(id, DemoTechnology.Of(tech)) == true;
            bool Has(DemoTierFacility f) => f.Role is { } role
                ? office.Any(r => r.SettlementId == id && r.Role == role)
                : f.TechId is { } tech && Knows(tech);
            var tier = DemoSettlementHistory.TierFor(ledger.PopulationClosing, Has, c.Tier, Knows);
            if (tier == c.Tier)
                continue;
            string Names(DemoSettlementTier t) => string.Join("、", DemoSettlementHistory.FacilitiesOf(t).Where(Has).Select(f => f.Name));
            var text = tier > c.Tier
                ? $"人口 {ledger.PopulationClosing} 人、有{Names(c.Tier)}，由{DemoSettlementHistory.TierName(c.Tier)}{TierUpMarker}{DemoSettlementHistory.TierName(tier)}"
                : $"{(ledger.PopulationClosing < DemoSettlementHistory.TempKeepPopulation(c.Tier) ? $"人口降到 {ledger.PopulationClosing} 人" : "設施不足兩種")}{TierDownMarker}{DemoSettlementHistory.TierName(tier)}";
            builder[ledger.SettlementId] = c with { Tier = tier, Milestones = c.Milestones.Add(new DemoChronicleLine(report.Week, text)) };
        }
        return builder.ToImmutable();
    }
}
