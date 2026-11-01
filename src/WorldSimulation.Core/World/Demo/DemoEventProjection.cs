using System.Collections.Immutable;
using WorldSimulation.Core.History;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 03 章 b 節：把一份週報的聚落事件轉成正式事件（同一 EventId）。提交與讀檔重建共用這一個轉換器。
/// 地點＝聚落格（途中餓死＝隊伍當週所在格）；相關實體＝聚落（＋遷徙隊伍；建村再加來源聚落）＋當事人；
/// 原因＝CauseEventId＞0 時一條直接邊，否則為根因事件（空）。
/// </summary>
public static class DemoEventProjection
{
    private static readonly IReadOnlyDictionary<long, IReadOnlyList<long>> NoParticipants
        = new Dictionary<long, IReadOnlyList<long>>();

    /// <param name="report">已通過週帳驗證的週報。</param>
    /// <param name="sites">本週提交後的聚落（含本週新建聚落）。</param>
    /// <param name="participants">事件 ID → 當事人（人類繁衍情境的新生兒＋母＋父、自然死亡者、本週餓死者）；其他情境為空。</param>
    public static IReadOnlyList<WorldEvent> ToWorldEvents(DemoWeekReport report, DemoWorldSites sites,
        IReadOnlyDictionary<long, IReadOnlyList<long>>? participants)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(sites);
        participants ??= NoParticipants;
        var result = new List<WorldEvent>(report.Events.Count);
        // 第 08-13 章第 5 節：聚落依編號查（重複編號時 SingleOrDefault 會丟例外，這裡同樣丟）。
        var siteOf = report.Events.Count == 0 ? new Dictionary<int, DemoSettlement>() : sites.Settlements.ToDictionary(s => s.Id);
        foreach (var e in report.Events)
        {
            var type = DemoEventTypes.Of(e.Kind);
            var settlement = siteOf.GetValueOrDefault(e.SettlementId)
                ?? throw new InvalidOperationException($"事件 #{e.EventId} 的聚落 #{e.SettlementId} 不存在。");
            var (column, row) = (settlement.Column, settlement.Row);
            if (e.Kind is DemoSettlementEventKind.MigrantStarvationDeath or DemoSettlementEventKind.BeastAttack
                && report.Migrations.FirstOrDefault(m => m.BatchId == e.MigrationBatchId) is { } leg)
                (column, row) = (leg.CellAfter.Column, leg.CellAfter.Row);

            var related = new List<long> { e.SettlementId };
            if (e.MigrationBatchId > 0)
                related.Add(e.MigrationBatchId);
            if (e.Kind == DemoSettlementEventKind.SettlementFounded && settlement.FoundedFromSettlementId > 0)
                related.Add(settlement.FoundedFromSettlementId);
            if (participants.TryGetValue(e.EventId, out var people))
                related.AddRange(people);

            result.Add(new WorldEvent(
                e.EventId,
                e.WorldTime,
                type.TypeId,
                column,
                row,
                related.Distinct().ToImmutableArray(),
                e.CauseEventId > 0
                    ? ImmutableArray.Create(new CauseEdge(e.CauseEventId, CauseEdgeKind.Direct))
                    : ImmutableArray<CauseEdge>.Empty,
                type.Root,
                e.Describe()));
        }

        return result;
    }

    /// <summary>Godot 事件詳情與 CLI `ev N` 共用的四行（類型、地點、相關實體、原因邊＋根因）。</summary>
    public static IReadOnlyList<string> DescribeLines(WorldEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var causes = e.Causes.IsEmpty
            ? "無（根因事件）"
            : string.Join("、", e.Causes.Select(c => $"#{c.ParentEventId}（{(c.Kind == CauseEdgeKind.Direct ? "直接" : "促成")}）"));
        return new[]
        {
            $"類型：{DemoEventTypes.Display(e.EventTypeId)}",
            $"地點：({e.LocationColumn},{e.LocationRow})",
            $"相關實體：{string.Join("、", e.RelatedEntityIds.Select(id => $"#{id}"))}",
            $"原因邊：{causes}｜根因：{DemoEventTypes.RootName(e.Root)}"
        };
    }
}
