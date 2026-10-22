using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// C07（P1.0.01-b）：CLI `cell C R` 點格查詢。輸出與 Godot 資訊框逐字相同（同一個 Core 唯讀查詢）；
/// 越界或格式錯誤只印中文說明，世界不變。
/// </summary>
internal static class CellCommand
{
    public const string Usage = "用法：cell 欄 列，例如 cell 2 2";

    /// <summary>
    /// 若 <paramref name="command"/> 是 `cell` 指令就處理並回傳 true（含錯誤說明）；否則回傳 false、不輸出。
    /// </summary>
    public static bool TryRun(DemoObserverWorld world, string command, TextWriter output)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "cell")
            return false;

        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var column)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var row))
        {
            output.WriteLine(Usage);
            return true;
        }

        if (!world.Map.Contains(column, row))
        {
            output.WriteLine(OutOfRange(world.Map));
            return true;
        }

        output.WriteLine(world.QueryCell(column, row).Text);
        return true;
    }

    public static string OutOfRange(DemoHexWorld map) =>
        $"格子超出地圖：欄 0～{map.Columns - 1}、列 0～{map.Rows - 1}";
}
