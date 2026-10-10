using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #96：歷史事件搜尋與篩選（43G OBS_WHY／OBS_ENTITY 的觀察者查詢；只讀，不改任何事件）。
/// 依世界日期範圍、聚落、事件類別與關鍵字篩選聚落事件，結果一律「新的在上」（事件編號由大到小），
/// 相同輸入永遠得到相同順序。預設值（全部日期、全部聚落、全部類別、無關鍵字）是 TEMP 畫面預設，不是正式設定。
/// 關鍵字：以「#數字」開頭時只找該事件編號；否則不分大小寫比對聚落名稱、事件類別與原因文字。
/// 日期以事件的世界日（第 N 日，週結算日）計；起日大於迄日時結果為空，不自動對調。
/// </summary>
public sealed record DemoEventFilter(
    int FromDay = 0,
    int ToDay = int.MaxValue,
    int SettlementId = 0,
    DemoSettlementEventKind? Kind = null,
    string Text = "")
{
    /// <summary>不篩選（TEMP 畫面預設）。</summary>
    public static DemoEventFilter All { get; } = new();

    /// <summary>是否等同不篩選（關鍵字只有空白也算沒有）。</summary>
    public bool IsAll => FromDay <= 0 && ToDay == int.MaxValue && SettlementId == 0 && Kind is null
        && string.IsNullOrWhiteSpace(Text);

    /// <summary>事件所在的世界日（週結算日）。</summary>
    public static int DayOf(DemoSettlementEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return (int)e.WorldTime.TotalDays;
    }

    public bool Matches(DemoSettlementEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var day = DayOf(e);
        if (day < FromDay || day > ToDay)
            return false;
        if (SettlementId != 0 && e.SettlementId != SettlementId)
            return false;
        if (Kind is { } kind && e.Kind != kind)
            return false;

        var text = (Text ?? string.Empty).Trim();
        if (text.Length == 0)
            return true;
        if (text.StartsWith('#')
            && long.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return e.EventId == id;
        return e.SettlementName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || DemoCausalIndex.KindText(e.Kind).Contains(text, StringComparison.OrdinalIgnoreCase)
            || e.Cause.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>篩選結果（新的在上：事件編號由大到小；編號相同時依原順序）。不修改輸入。</summary>
    public IReadOnlyList<DemoSettlementEvent> Apply(IEnumerable<DemoSettlementEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .Select((e, order) => (e, order))
            .Where(x => Matches(x.e))
            .OrderByDescending(x => x.e.EventId)
            .ThenByDescending(x => x.order)
            .Select(x => x.e)
            .ToArray();
    }
}
