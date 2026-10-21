using System.Collections.Immutable;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.History;

/// <summary>
/// 第 03 章 b 節：追加式史庫（不可變）。只接受已提交事件；唯一新增方法是 internal 的
/// <see cref="AppendCommitted"/>（只由提交物件呼叫），回傳新物件、原物件不變；沒有任何 public 修改方法。
/// 內含反向索引（父 → 直接後果），後果不另存。
/// </summary>
public sealed class EventHistory
{
    private readonly ImmutableList<WorldEvent> _events;
    private readonly ImmutableDictionary<long, WorldEvent> _byId;
    private readonly ImmutableDictionary<long, ImmutableList<long>> _children;

    public static EventHistory Empty { get; } = new(
        ImmutableList<WorldEvent>.Empty, ImmutableDictionary<long, WorldEvent>.Empty,
        ImmutableDictionary<long, ImmutableList<long>>.Empty);

    private EventHistory(ImmutableList<WorldEvent> events, ImmutableDictionary<long, WorldEvent> byId,
        ImmutableDictionary<long, ImmutableList<long>> children)
    {
        _events = events;
        _byId = byId;
        _children = children;
    }

    public int Count => _events.Count;

    /// <summary>最後一筆事件 ID；空史庫為 0。</summary>
    public long LastEventId => _events.IsEmpty ? 0 : _events[^1].EventId;

    /// <summary>全部事件，舊的在前。</summary>
    public IReadOnlyList<WorldEvent> Events => _events;

    public WorldEvent? Find(long eventId) => _byId.GetValueOrDefault(eventId);

    /// <summary>直接原因事件（依原因邊順序）。</summary>
    public IReadOnlyList<WorldEvent> ParentsOf(long eventId)
        => Find(eventId) is { } e ? e.Causes.Select(c => _byId[c.ParentEventId]).ToArray() : Array.Empty<WorldEvent>();

    /// <summary>直接後果（反向索引，依事件 ID 遞增）。</summary>
    public IReadOnlyList<WorldEvent> ChildrenOf(long eventId)
        => _children.TryGetValue(eventId, out var ids) ? ids.Select(id => _byId[id]).ToArray() : Array.Empty<WorldEvent>();

    /// <summary>
    /// 讀檔重建：依週次逐批追加（與提交同一套驗證）。讀檔不信任檔內史庫，由週報＋人口重播經同一轉換器重建。
    /// </summary>
    internal static EventHistory Rebuild(IEnumerable<IReadOnlyList<WorldEvent>> batches)
    {
        var history = Empty;
        foreach (var batch in batches)
            history = history.AppendCommitted(batch);
        return history;
    }

    /// <summary>
    /// 追加一批已提交事件並逐筆驗證：ID＝上一筆＋1、時間不倒退、父 ID 存在且小於自身、同一事件不重複指向同一父、
    /// 類型已知、相關實體 ID＞0、原因邊種類合法。任何一筆不合法就丟例外，原物件不變。
    /// </summary>
    internal EventHistory AppendCommitted(IReadOnlyList<WorldEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var events = _events.ToBuilder();
        var byId = _byId.ToBuilder();
        var children = _children.ToBuilder();
        var lastId = LastEventId;
        var lastTime = _events.IsEmpty ? TimeSpan.Zero : _events[^1].WorldTime;
        foreach (var e in batch)
        {
            if (e is null)
                throw new InvalidOperationException("史庫不接受空事件。");
            if (e.EventId != lastId + 1)
                throw new InvalidOperationException($"事件 #{e.EventId} 跳號或重複（上一筆 #{lastId}）。");
            if (e.WorldTime < lastTime)
                throw new InvalidOperationException($"事件 #{e.EventId} 的世界時間倒退。");
            if (string.IsNullOrWhiteSpace(e.EventTypeId) || !DemoEventTypes.IsKnown(e.EventTypeId))
                throw new InvalidOperationException($"事件 #{e.EventId} 的類型「{e.EventTypeId}」未知。");
            if (e.RelatedEntityIds.IsDefault || e.Causes.IsDefault)
                throw new InvalidOperationException($"事件 #{e.EventId} 缺少相關實體或原因欄位。");
            if (e.RelatedEntityIds.Any(id => id <= 0))
                throw new InvalidOperationException($"事件 #{e.EventId} 的相關實體 ID 必須大於 0。");
            if (!Enum.IsDefined(e.Root))
                throw new InvalidOperationException($"事件 #{e.EventId} 的根因分類不合法。");
            var parents = new HashSet<long>();
            foreach (var edge in e.Causes)
            {
                if (!Enum.IsDefined(edge.Kind))
                    throw new InvalidOperationException($"事件 #{e.EventId} 的原因邊種類不合法。");
                if (edge.ParentEventId >= e.EventId)
                    throw new InvalidOperationException($"事件 #{e.EventId} 的原因指向自身或未來事件 #{edge.ParentEventId}。");
                if (!byId.ContainsKey(edge.ParentEventId))
                    throw new InvalidOperationException($"事件 #{e.EventId} 的原因 #{edge.ParentEventId} 不存在。");
                if (!parents.Add(edge.ParentEventId))
                    throw new InvalidOperationException($"事件 #{e.EventId} 重複指向原因 #{edge.ParentEventId}。");
            }

            events.Add(e);
            byId.Add(e.EventId, e);
            foreach (var edge in e.Causes)
                children[edge.ParentEventId] = (children.TryGetValue(edge.ParentEventId, out var list)
                    ? list : ImmutableList<long>.Empty).Add(e.EventId);
            lastId = e.EventId;
            lastTime = e.WorldTime;
        }

        return new EventHistory(events.ToImmutable(), byId.ToImmutable(), children.ToImmutable());
    }
}
