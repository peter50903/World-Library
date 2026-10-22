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

    /// <summary>值比較（清單逐項比）。</summary>
    public bool Equals(DemoSettlementChronicle? other)
        => other is not null && NextMilestone == other.NextMilestone && Peak == other.Peak && PeakWeek == other.PeakWeek
        && Milestones.SequenceEqual(other.Milestones) && Representatives.SequenceEqual(other.Representatives);

    public override int GetHashCode() => HashCode.Combine(NextMilestone, Peak, PeakWeek, Milestones.Count, Representatives.Count);

    /// <summary>指紋用的一行文字。</summary>
    public string Fingerprint()
        => $"{NextMilestone}/{Peak}@{PeakWeek}|{string.Join("|", Milestones.Select(l => $"{l.Week}:{l.Text}"))}|{string.Join("|", Representatives.Select(l => $"{l.Week}:{l.Text}"))}";

    /// <summary>
    /// 依本週週報更新全部聚落的小史。<paramref name="inOffice"/>＝本週開始時在任的代表人物（卸任的人從這裡找名字）。
    /// 純函式；與第 08-3 章「逐週掃描週報」的結果相同。
    /// </summary>
    public static ImmutableSortedDictionary<int, DemoSettlementChronicle> Advance(
        ImmutableSortedDictionary<int, DemoSettlementChronicle> chronicles, DemoWeekReport report,
        IReadOnlyList<DemoRepresentative> inOffice)
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
                var label = milestones[next] == DemoSettlementHistory.TempVillagePopulation ? "，由聚落發展為村莊" : "";
                lines = lines.Add(new DemoChronicleLine(report.Week, $"人口首次達到 {milestones[next]} 人{label}"));
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
            or DemoSettlementEventKind.FarmlandCleared))
        {
            var c = builder.GetValueOrDefault(e.SettlementId) ?? Empty;
            var text = e.Kind switch
            {
                DemoSettlementEventKind.KnowledgeLearned => $"學會{DemoKnowledge.Name((DemoTechnology)e.Count)}",
                DemoSettlementEventKind.FarmlandCleared => "開墾一塊農田",
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
        return builder.ToImmutable();
    }
}
