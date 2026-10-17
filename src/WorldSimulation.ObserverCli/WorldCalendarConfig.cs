using System.Text;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Time;

namespace WorldSimulation.ObserverCli;

// #82：正式曆法接入（最小單頁讀取）。只在文字觀察者邊界讀 config/sheets/01_世界參數.csv，
// 用 Core 的 ParameterSheet／ConfigRegistry 驗證整張 01 表，再由 Core 的 WorldCalendarRules 取出曆法（C08）；
// Core 不讀檔、不依賴程式倉庫路徑，也不另存第二份年長常數。找不到、未啟用或格式錯誤一律明確失敗。

/// <summary>從正式設定讀到的曆法：一年日數、Core 換算規則與來源檔案。</summary>
public sealed record WorldCalendar(WorldCalendarRules Rules, string Source)
{
    /// <summary>一年日數（＝<see cref="WorldCalendarRules.DaysPerYear"/>，保留給既有呼叫端）。</summary>
    public int DaysPerYear => Rules.DaysPerYear;

    /// <summary>給報表標頭用的一行說明。</summary>
    public string Describe()
        => $"曆法：{WorldCalendarConfig.RelativePath} 的 {WorldCalendarConfig.DaysPerYearId}＝{DaysPerYear}"
         + $"、{Rules.DescribeMonths()}"
         + $"（一年 {DaysPerYear} 日；週結算每 7 日一次、跨年不重置，歸屬結算日所在年份）";
}

/// <summary>讀取 config/sheets/01_世界參數.csv 的曆法參數。</summary>
public static class WorldCalendarConfig
{
    public const string RelativePath = "config/sheets/01_世界參數.csv";
    public const string DaysPerYearId = WorldCalendarRules.DaysPerYearId;

    /// <summary>從目前目錄與程式所在目錄往上找程式倉庫的 01 CSV；找不到回傳 null。</summary>
    public static string? FindDefaultPath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "config", "sheets", "01_世界參數.csv");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

        /// <summary>
    /// 第 02 章 b 節：本建置編入的正式設定（<see cref="OfficialConfig"/>）的 01 表建立曆法——與 Godot 同一份、同一個雜湊。
    /// 正式設定讀取失敗就丟出 <see cref="WorldCalendarException"/>。
    /// </summary>
    public static WorldCalendar LoadDefault()
    {
        var loaded = OfficialConfig.Load();
        if (loaded.Config is not { } config)
            throw new WorldCalendarException(loaded.ErrorText);
        const string source = "本建置編入的 " + RelativePath;
        return new WorldCalendar(WorldCalendarRules.FromRegistry(config.World, source), source);
    }

    /// <summary>讀外部檔（--world-params，非本建置設定；只供檢查其他版本的 01 表與錯誤測試）。</summary>
    public static WorldCalendar Load(string path)
    {
        if (!File.Exists(path))
            throw new WorldCalendarException($"找不到世界參數表：{path}");
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorldCalendarException($"無法讀取世界參數表 {path}：{ex.Message}");
        }

        return Parse(text, path);
    }

    /// <summary>解析 01 表內容，整張表通過 ConfigRegistry 驗證後交給 Core 的 <see cref="WorldCalendarRules"/>。</summary>
    public static WorldCalendar Parse(string text, string source)
    {
        IReadOnlyList<ParameterRow> parameters;
        try
        {
            parameters = ParameterSheet.Parse(text, source);
        }
        catch (ParameterSheetException ex)
        {
            throw new WorldCalendarException(ex.Message);
        }

        var result = ConfigRegistry.ValidateAndCreate(parameters);
        if (!result.IsValid)
        {
            var first = result.Errors[0];
            throw new WorldCalendarException(
                $"世界參數表驗證失敗（{result.Errors.Count} 處）；第一處：第 {first.RowNumber} 列 {first.ParameterId ?? "（無 ID）"}：{first.Reason}：{source}");
        }

        return new WorldCalendar(WorldCalendarRules.FromRegistry(result.Registry!, source), source);
    }
}
