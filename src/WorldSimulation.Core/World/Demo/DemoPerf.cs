using System.Diagnostics;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-13 章第 5 節：效能剖析（只給診斷用；不影響世界、不進存檔）。世界每天推進時依序打點，
/// 把兩個打點之間的時間累加到該階段。<see cref="Enabled"/> 預設關閉，關閉時打點只做一次判斷。
/// </summary>
public static class DemoPerf
{
    public static bool Enabled { get; set; }

    private static readonly Dictionary<string, long> Ticks = new(StringComparer.Ordinal);
    private static readonly List<string> Order = new();
    private static long _last;

    public static void Begin()
    {
        if (Enabled) _last = Stopwatch.GetTimestamp();
    }

    public static void Mark(string stage)
    {
        if (!Enabled) return;
        var now = Stopwatch.GetTimestamp();
        if (!Ticks.ContainsKey(stage)) Order.Add(stage);
        Ticks[stage] = Ticks.GetValueOrDefault(stage) + (now - _last);
        _last = now;
    }

    public static void Reset()
    {
        Ticks.Clear();
        Order.Clear();
    }

    /// <summary>合計與佔 2% 以上的階段累計秒數（由大到小）。</summary>
    public static string Describe()
    {
        var total = Ticks.Values.Sum();
        if (total == 0) return "（沒有剖析資料）";
        // 只列佔 2% 以上的階段、由大到小（CI 註記有長度上限）。
        return $"合計 {total / (double)Stopwatch.Frequency:F1}s｜" + string.Join("｜", Order.Where(s => Ticks[s] * 50 >= total).OrderByDescending(s => Ticks[s])
            .Select(s => $"{s} {Ticks[s] / (double)Stopwatch.Frequency:F2}s（{Ticks[s] * 100 / total}%）"));
    }
}
