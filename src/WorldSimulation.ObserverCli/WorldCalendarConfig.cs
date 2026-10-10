using System.Globalization;
using System.Text;
using WorldSimulation.Core.Config;

namespace WorldSimulation.ObserverCli;

// #82：正式曆法接入（最小單頁讀取）。只在文字觀察者邊界讀 config/sheets/01_世界參數.csv，
// 用 Core 的 ConfigRegistry 驗證整張 01 表後取出 WORLD_DAYS_PER_YEAR，再以參數傳入 Core；
// Core 不讀檔、不依賴程式倉庫路徑，也不另存第二份年長常數。找不到、未啟用或格式錯誤一律明確失敗。

/// <summary>曆法設定讀取失敗（訊息為繁體中文，可直接給使用者看）。</summary>
public sealed class WorldCalendarException : Exception
{
    public WorldCalendarException(string message) : base(message)
    {
    }
}

/// <summary>從正式設定讀到的曆法：一年日數與來源檔案。</summary>
public sealed record WorldCalendar(int DaysPerYear, string Source)
{
    /// <summary>給報表標頭用的一行說明。</summary>
    public string Describe()
        => $"曆法：{WorldCalendarConfig.RelativePath} 的 {WorldCalendarConfig.DaysPerYearId}＝{DaysPerYear}"
         + $"（一年 {DaysPerYear} 日；週結算每 7 日一次、跨年不重置，歸屬結算日所在年份）";
}

/// <summary>讀取 config/sheets/01_世界參數.csv 的曆法參數。</summary>
public static class WorldCalendarConfig
{
    public const string RelativePath = "config/sheets/01_世界參數.csv";
    public const string DaysPerYearId = "WORLD_DAYS_PER_YEAR";

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

    /// <summary>找預設路徑並讀取；找不到就丟出 <see cref="WorldCalendarException"/>。</summary>
    public static WorldCalendar LoadDefault()
    {
        var path = FindDefaultPath()
            ?? throw new WorldCalendarException($"找不到 {RelativePath}；請在程式倉庫內執行，或用 --world-params 指定路徑。");
        return Load(path);
    }

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

    /// <summary>解析 01 表內容，整張表通過 ConfigRegistry 驗證後回傳一年日數。</summary>
    public static WorldCalendar Parse(string text, string source)
    {
        List<List<string>> rows;
        try
        {
            rows = M1CheckCatalog.ParseCsv(text, source);
        }
        catch (M1CatalogException)
        {
            throw new WorldCalendarException($"世界參數表有未關閉的引號：{source}");
        }

        if (rows.Count == 0)
            throw new WorldCalendarException($"世界參數表是空的：{source}");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Column(string name)
        {
            var index = header.IndexOf(name);
            if (index < 0)
                throw new WorldCalendarException($"世界參數表缺少欄位「{name}」：{source}");
            return index;
        }

        var idColumn = Column("ParameterID");
        var valueColumn = Column("值");
        var typeColumn = Column("資料型別");
        var enabledColumn = Column("Enabled");

        var parameters = new List<ParameterRow>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            string? Cell(int c) => c < row.Count ? row[c] : null;
            var line = r + 1;
            var enabledText = (Cell(enabledColumn) ?? string.Empty).Trim();
            bool enabled;
            if (enabledText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (enabledText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                enabled = false;
            else
                throw new WorldCalendarException($"世界參數表第 {line} 列的 Enabled 必須是 TRUE 或 FALSE，收到「{enabledText}」：{source}");
            parameters.Add(new ParameterRow(line, Cell(idColumn), Cell(valueColumn), Cell(typeColumn)?.Trim(), enabled));
        }

        var result = ConfigRegistry.ValidateAndCreate(parameters);
        if (!result.IsValid)
        {
            var first = result.Errors[0];
            throw new WorldCalendarException(
                $"世界參數表驗證失敗（{result.Errors.Count} 處）；第一處：第 {first.RowNumber} 列 {first.ParameterId ?? "（無 ID）"}：{first.Reason}：{source}");
        }

        var registry = result.Registry!;
        if (!registry.Contains(DaysPerYearId))
            throw new WorldCalendarException($"世界參數表沒有已啟用的 {DaysPerYearId}：{source}");

        int days;
        try
        {
            days = registry.GetInt(DaysPerYearId);
        }
        catch (InvalidOperationException)
        {
            throw new WorldCalendarException($"{DaysPerYearId} 的資料型別必須是 int：{source}");
        }

        if (days < 1)
            throw new WorldCalendarException(
                $"{DaysPerYearId} 必須是正整數，收到 {days.ToString(CultureInfo.InvariantCulture)}：{source}");
        return new WorldCalendar(days, source);
    }
}
