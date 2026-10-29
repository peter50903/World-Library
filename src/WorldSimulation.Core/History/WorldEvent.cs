using System.Collections.Immutable;

namespace WorldSimulation.Core.History;

/// <summary>第 03 章 b 節（43F）：因果邊只有兩種。「相關」只是查詢層標記，不得存入史庫。</summary>
public enum CauseEdgeKind
{
    /// <summary>直接原因。</summary>
    Direct = 1,

    /// <summary>促成原因（本章尚無來源；由提交該事件的領域系統寫入）。</summary>
    Contributing = 2
}

/// <summary>一條原因邊：父事件 ID＋邊的種類。只存直接邊；後果由父項反向索引，不另存。</summary>
public readonly record struct CauseEdge(long ParentEventId, CauseEdgeKind Kind);

/// <summary>根因分類（不取代實際原因事件）。</summary>
public enum CauseRootCategory
{
    /// <summary>資源：飢餓類、遷徙類、建村。</summary>
    Resource = 1,

    /// <summary>【TEMP】生命週期：出生、自然死亡。</summary>
    Lifecycle = 2,

    /// <summary>【TEMP】第 08-6 章 知識：習得技術、時代開始。</summary>
    Knowledge = 3,

    /// <summary>【TEMP】第 08-8 章 B4 文化：文化分化。</summary>
    Culture = 4
}

/// <summary>
/// 第 03 章 b 節（43F）：正式事件。事件 ID 另有序列（不是實體 ID）、不重用；時間＝已提交世界時間；
/// 相關實體用永久實體 ID；原因只存直接邊（1～N 個，根因事件為空）；摘要不帶推測。
/// 陣列欄位以值比較。
/// </summary>
public sealed record WorldEvent(
    long EventId,
    TimeSpan WorldTime,
    string EventTypeId,
    int LocationColumn,
    int LocationRow,
    ImmutableArray<long> RelatedEntityIds,
    ImmutableArray<CauseEdge> Causes,
    CauseRootCategory Root,
    string Summary)
{
    public bool Equals(WorldEvent? other)
        => other is not null
        && EventId == other.EventId
        && WorldTime == other.WorldTime
        && EventTypeId == other.EventTypeId
        && LocationColumn == other.LocationColumn
        && LocationRow == other.LocationRow
        && RelatedEntityIds.SequenceEqual(other.RelatedEntityIds)
        && Causes.SequenceEqual(other.Causes)
        && Root == other.Root
        && Summary == other.Summary;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EventId);
        hash.Add(WorldTime);
        hash.Add(EventTypeId);
        hash.Add(LocationColumn);
        hash.Add(LocationRow);
        foreach (var id in RelatedEntityIds) hash.Add(id);
        foreach (var edge in Causes) hash.Add(edge);
        hash.Add(Root);
        hash.Add(Summary);
        return hash.ToHashCode();
    }

    /// <summary>指紋用的一行文字（不含摘要以外的推算值）。</summary>
    internal string Fingerprint()
        => FormattableString.Invariant(
            $"ev{EventId}@{WorldTime.Ticks}:{EventTypeId}:{LocationColumn},{LocationRow}:{string.Join(",", RelatedEntityIds)}:{string.Join(",", Causes.Select(c => $"{c.ParentEventId}/{(int)c.Kind}"))}:{(int)Root}:{Summary}");
}
