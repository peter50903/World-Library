using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>第 04 章 b 節：選用完整驗證的結果。Ok＝從第 0 日重跑到存檔日，逐週週報與全檔都相同。</summary>
public sealed record DemoReplayVerification(bool Ok, int? FirstDivergentWeek, string? FirstDifference, TimeSpan Elapsed)
{
    /// <summary>給 CLI 與 Godot 的一行繁中說明。</summary>
    public string Describe(int slot) => Ok
        ? string.Create(CultureInfo.InvariantCulture, $"完整驗證槽 {slot}：一致（重跑耗時 {Elapsed.TotalSeconds:0.0} 秒）")
        : $"完整驗證槽 {slot}：不一致——{FirstDifference}";
}

public sealed partial class DemoObserverWorld
{
    /// <summary>
    /// 第 04 章 b 節（選用工具，讀檔預設不跑）：先做標頭檢查①～⑧，再以標頭配方從第 0 日逐日重跑，
    /// 每次週結算都與存檔的同一週逐欄比對，回報第一個分歧週與欄位（例如「第 W 週 聚落 X 的 Harvested：存檔 120，重算 20」）；
    /// 週報全同後再做全檔比對。不改任何世界、不寫檔。
    /// </summary>
    public static DemoReplayVerification VerifyTempSaveByReplay(string json)
    {
        var clock = Stopwatch.StartNew();
        DemoReplayVerification Fail(int? week, string why) => new(false, week, why, clock.Elapsed);
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return Fail(null, "存檔是空的");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            if (!root.TryGetProperty("Version", out var versionElement) || !versionElement.TryGetInt32(out var version))
                return Fail(null, "存檔版本不受支援");
            if (IsOutdatedTempSaveVersion(version))
                return Fail(null, OutdatedTempSaveMessage(version));
            if (!IsSupportedTempSaveVersion(version))
                return Fail(null, "存檔版本不受支援");
            var (manifest, weekly, days, _) = CheckHeader(root, version);
            var saved = ReadWeekReports(root.GetProperty("State").GetProperty("WeekReports"));

            var world = new DemoObserverWorld(manifest.Recipe, weekly);
            for (var day = 0; day < days; day++)
            {
                var advance = world.AdvanceOneDay();
                if (advance.SettledWeek is not { } week)
                    continue;
                if (week.Week > saved.Count)
                    return Fail(week.Week, $"存檔缺少第 {week.Week} 週週報（重算有）");
                if (FirstWeekDifference(saved[week.Week - 1], week) is { } difference)
                    return Fail(week.Week, difference);
            }
            if (saved.Count > world.WeekReports().Count)
                return Fail(world.WeekReports().Count + 1, $"存檔多出第 {world.WeekReports().Count + 1} 週週報（重算沒有）");

            var supplied = JsonNode.Parse(json);
            var replayed = JsonNode.Parse(world.ExportTempSave());
            return JsonNode.DeepEquals(supplied, replayed)
                ? new DemoReplayVerification(true, null, null, clock.Elapsed)
                : Fail(null, "週報全部相同，但週報以外的世界狀態（存量、名冊、隊伍、史庫或標頭）與重算不一致");
        }
        catch (InvalidDataException ex)
        {
            return Fail(null, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException
            or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            return Fail(null, $"存檔損毀或格式不符（{ex.Message}）");
        }
    }

    /// <summary>逐欄比對一週：聚落帳 → 事件 → 隊伍帳 → 亂數計數器 → 實體 ID 區間。全同回傳 null。</summary>
    private static string? FirstWeekDifference(DemoWeekReport saved, DemoWeekReport replayed)
    {
        var w = replayed.Week;
        if (saved.Settlements.Count != replayed.Settlements.Count)
            return $"第 {w} 週 聚落帳筆數：存檔 {saved.Settlements.Count}，重算 {replayed.Settlements.Count}";
        for (var i = 0; i < replayed.Settlements.Count; i++)
        {
            var r = replayed.Settlements[i];
            var s = saved.Settlements[i];
            if (FirstFieldDifference(s, r) is { } field)
                return $"第 {w} 週 聚落 {r.SettlementName} 的 {field}";
        }
        if (saved.Events.Count != replayed.Events.Count)
            return $"第 {w} 週 事件筆數：存檔 {saved.Events.Count}，重算 {replayed.Events.Count}";
        for (var i = 0; i < replayed.Events.Count; i++)
        {
            if (FirstFieldDifference(saved.Events[i], replayed.Events[i]) is { } field)
                return $"第 {w} 週 事件 #{replayed.Events[i].EventId} 的 {field}";
        }
        if (saved.Migrations.Count != replayed.Migrations.Count)
            return $"第 {w} 週 遷徙隊伍帳筆數：存檔 {saved.Migrations.Count}，重算 {replayed.Migrations.Count}";
        for (var i = 0; i < replayed.Migrations.Count; i++)
        {
            if (FirstFieldDifference(saved.Migrations[i], replayed.Migrations[i]) is { } field)
                return $"第 {w} 週 遷徙隊伍 #{replayed.Migrations[i].BatchId} 的 {field}";
        }
        if (saved.Sources.Count != replayed.Sources.Count)
            return $"第 {w} 週 來源帳筆數：存檔 {saved.Sources.Count}，重算 {replayed.Sources.Count}";
        for (var i = 0; i < replayed.Sources.Count; i++)
        {
            if (!saved.Sources[i].Equals(replayed.Sources[i]))
                return $"第 {w} 週 來源 #{replayed.Sources[i].SourceId}：存檔 {saved.Sources[i]}，重算 {replayed.Sources[i]}";
        }
        if (!saved.RngCounters.SequenceEqual(replayed.RngCounters))
            return $"第 {w} 週 亂數流計數器與重算不同";
        if (saved.EntityIdNextOpening != replayed.EntityIdNextOpening || saved.EntityIdNextClosing != replayed.EntityIdNextClosing)
            return string.Create(CultureInfo.InvariantCulture,
                $"第 {w} 週 實體 ID 區間：存檔 {saved.EntityIdNextOpening}～{saved.EntityIdNextClosing}，重算 {replayed.EntityIdNextOpening}～{replayed.EntityIdNextClosing}");
        return null;
    }

    private static string? FirstFieldDifference<T>(T saved, T replayed) where T : notnull
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            var a = property.GetValue(saved);
            var b = property.GetValue(replayed);
            if (!Equals(a, b))
                return string.Create(CultureInfo.InvariantCulture, $"{property.Name}：存檔 {a}，重算 {b}");
        }
        return null;
    }
}
