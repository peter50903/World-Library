using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-47 章：一場仗（同一週攻守雙方的交戰合成一筆）。<see cref="Field"/>＝野戰；<see cref="Place"/>＝地點的說法。</summary>
public sealed record DemoWarBattle(int Week, bool Field, int Besieged, string Place, long AttackerDeaths, long DefenderDeaths)
{
    /// <summary>攻方死得比守方少＝攻方佔上風。</summary>
    public bool AttackerWon => AttackerDeaths < DefenderDeaths;
}

/// <summary>
/// 第 08-47 章：一場征服戰爭從宣戰到結束的紀錄（由事件重建，不另存）：會戰、攻陷、結局。
/// <see cref="EndWeek"/>＝0 表示還在打。
/// </summary>
public sealed record DemoWarReport(int AttackerId, int TargetId, int StartWeek, string Cause, long DeclaredEventId)
{
    public IReadOnlyList<DemoWarBattle> Battles { get; init; } = Array.Empty<DemoWarBattle>();

    /// <summary>攻陷與佔領（週次、聚落），依先後。</summary>
    public IReadOnlyList<(int Week, int SettlementId)> Occupations { get; init; } = Array.Empty<(int, int)>();

    public int EndWeek { get; init; }
    public long EndEventId { get; init; }

    /// <summary>以征服（含議和割讓）結束。</summary>
    public bool Won { get; init; }

    public string EndText { get; init; } = "";

    public long AttackerDeaths => Battles.Sum(b => b.AttackerDeaths);
    public long DefenderDeaths => Battles.Sum(b => b.DefenderDeaths);
}

/// <summary>
/// 第 08-47 章（戰爭表現三章之三）：從事件重建每場征服戰爭的經過——戰爭總覽視窗的會戰清單、史書裡一場戰爭寫成完整的一段。
/// 交戰事件的配對：攻方那一筆在攻方首都；守方那一筆的原因寫「抵抗／迎擊某某的大軍」。
/// </summary>
public static class DemoWarReports
{
    private sealed class Builder
    {
        public required DemoSettlementEvent Declared { get; init; }
        public readonly SortedDictionary<int, (long Attack, long Defend, bool Field, int Besieged, string Place)> Weeks = new();
        public readonly List<(int, int)> Occupied = new();
    }

    /// <summary>全部的征服戰爭（已結束的依結束先後，還在打的排在最後）。<paramref name="sites"/>＝找野戰地點附近的聚落。</summary>
    public static IReadOnlyList<DemoWarReport> All(IEnumerable<DemoSettlementEvent> events, DemoWorldSites sites)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(sites);
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var open = new SortedDictionary<int, Builder>();
        var done = new List<DemoWarReport>();
        DemoWarReport Close(Builder b, int endWeek = 0, long endEvent = 0, bool won = false, string text = "")
        {
            var battles = b.Weeks.Select(kv => new DemoWarBattle(kv.Key, kv.Value.Field, kv.Value.Besieged, kv.Value.Place ?? "", kv.Value.Attack, kv.Value.Defend)).ToList();
            return new DemoWarReport(b.Declared.SettlementId, (int)b.Declared.Count, b.Declared.Week, DemoPlayerDetail.Clean(b.Declared.Cause), b.Declared.EventId)
            {
                Battles = battles,
                Occupations = b.Occupied.ToList(),
                EndWeek = endWeek,
                EndEventId = endEvent,
                Won = won,
                EndText = text
            };
        }
        foreach (var e in events.OrderBy(e => e.Week).ThenBy(e => e.EventId))
        {
            switch (e.Kind)
            {
                case DemoSettlementEventKind.WarDeclared:
                    open[e.SettlementId] = new Builder { Declared = e };
                    break;
                case DemoSettlementEventKind.ConquestBattle:
                {
                    var field = e.Cause.StartsWith("野戰", StringComparison.Ordinal);
                    if (open.TryGetValue(e.SettlementId, out var mine))
                    {
                        var w = mine.Weeks.GetValueOrDefault(e.Week);
                        mine.Weeks[e.Week] = w with { Attack = w.Attack + e.Count, Field = w.Field || field };
                        break;
                    }
                    foreach (var (attacker, b) in open)
                    {
                        var foe = Name(attacker) + "的大軍";
                        if (!e.Cause.Contains("抵抗" + foe, StringComparison.Ordinal) && !e.Cause.Contains("迎擊" + foe, StringComparison.Ordinal))
                            continue;
                        var w = b.Weeks.GetValueOrDefault(e.Week);
                        var place = field ? FieldPlace(e.Cause, sites) : e.SettlementName.Length > 0 ? e.SettlementName : Name(e.SettlementId);
                        b.Weeks[e.Week] = (w.Attack, w.Defend + e.Count, w.Field || field, field ? 0 : e.SettlementId, place);
                        break;
                    }
                    break;
                }
                case DemoSettlementEventKind.Occupied when open.TryGetValue((int)e.Count, out var siege):
                    siege.Occupied.Add((e.Week, e.SettlementId));
                    break;
                case DemoSettlementEventKind.Conquered when open.TryGetValue((int)e.Count, out var winner) && (int)winner.Declared.Count == e.SettlementId:
                    done.Add(Close(winner, e.Week, e.EventId, true, DemoPlayerDetail.Clean(e.Cause)));
                    open.Remove((int)e.Count);
                    break;
                case DemoSettlementEventKind.WarEnded when open.TryGetValue(e.SettlementId, out var loser):
                    done.Add(Close(loser, e.Week, e.EventId, false, DemoPlayerDetail.Clean(e.Cause)));
                    open.Remove(e.SettlementId);
                    break;
            }
        }
        done.AddRange(open.Values.Select(b => Close(b)));
        return done;
    }

    /// <summary>野戰地點：原因裡的 (欄,列) → 「某某附近」（最近的聚落；找不到就寫荒野）。</summary>
    private static string FieldPlace(string cause, DemoWorldSites sites)
    {
        var m = System.Text.RegularExpressions.Regex.Match(cause, @"\((\d+),(\d+)\)");
        if (!m.Success)
            return "荒野";
        var at = new DemoCellRef(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        var near = sites.Settlements.Where(s => !s.IsRuin)
            .Select(s => (s.Name, Distance: DemoHexWorld.Distance(at, new DemoCellRef(s.Column, s.Row))))
            .OrderBy(x => x.Distance).ThenBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
        return near.Name is null ? "荒野" : near.Distance == 0 ? near.Name : $"{near.Name}附近";
    }

    /// <summary>
    /// 第 08-47 章：史書的一段——「這場戰爭前後打了 38 週：在米爾丘附近野戰得勝（守方死 24 人）；圍城後攻陷蒼西灣，又佔領黛恩灣；雙方共死 120 人（攻方 50、守方 70）。」
    /// </summary>
    public static string Story(DemoWarReport report, Func<int, string> name)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(name);
        var parts = new List<string>();
        foreach (var b in report.Battles.Where(b => b.Field))
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"在{b.Place}野戰{(b.AttackerWon ? "得勝" : "失利")}（攻方死 {b.AttackerDeaths}、守方死 {b.DefenderDeaths} 人）"));
        var sieges = report.Battles.Count(b => !b.Field);
        var taken = report.Occupations.Where(o => o.SettlementId == report.TargetId).Select(o => o.Week).DefaultIfEmpty(0).First();
        if (taken > 0)
            parts.Add($"圍城後攻陷{name(report.TargetId)}");
        else if (sieges > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"圍攻{name(report.TargetId)}未能攻下"));
        var more = report.Occupations.Where(o => o.SettlementId != report.TargetId).Select(o => name(o.SettlementId)).Distinct().ToList();
        if (more.Count > 0)
            parts.Add($"又佔領{string.Join("、", more)}");
        var weeks = Math.Max(0, (report.EndWeek > 0 ? report.EndWeek : report.Battles.Select(b => b.Week).DefaultIfEmpty(report.StartWeek).Max()) - report.StartWeek);
        var total = report.AttackerDeaths + report.DefenderDeaths;
        var tail = total > 0 ? string.Create(CultureInfo.InvariantCulture, $"雙方共死 {total} 人（攻方 {report.AttackerDeaths}、守方 {report.DefenderDeaths}）") : "雙方沒有傷亡";
        return string.Create(CultureInfo.InvariantCulture, $"這場戰爭前後打了 {weeks} 週：")
            + (parts.Count == 0 ? "" : string.Join("；", parts) + "；") + tail + "。";
    }
}
