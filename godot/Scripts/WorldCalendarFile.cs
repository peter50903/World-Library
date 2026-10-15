using System.Text;
using Godot;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Time;

namespace WorldSimulation.GodotClient;

/// <summary>
/// C08：Godot 邊界讀世界曆法。從專案資料夾與目前目錄往上找程式倉庫的 config/sheets/01_世界參數.csv，
/// 交給 Core 的 <see cref="ParameterSheet"/>／<see cref="ConfigRegistry"/>／<see cref="WorldCalendarRules"/> 驗證與換算。
/// 讀不到或不一致就回報錯誤訊息，不給預設值、不退回寫死 365。
/// </summary>
public static class WorldCalendarFile
{
    public const string RelativePath = "config/sheets/01_世界參數.csv";

    /// <summary>讀檔並建立曆法；成功時 <paramref name="error"/> 為 null，失敗時回傳 null 並附繁體中文錯誤。</summary>
    public static WorldCalendarRules? TryLoad(out string? error)
    {
        var path = FindPath();
        if (path is null)
        {
            error = $"找不到 {RelativePath}";
            return null;
        }

        try
        {
            var text = System.IO.File.ReadAllText(path, Encoding.UTF8);
            var result = ConfigRegistry.ValidateAndCreate(ParameterSheet.Parse(text, path));
            if (!result.IsValid)
            {
                var first = result.Errors[0];
                error = $"世界參數表驗證失敗（{result.Errors.Count} 處）；第一處：第 {first.RowNumber} 列 "
                    + $"{first.ParameterId ?? "（無 ID）"}：{first.Reason}";
                return null;
            }

            error = null;
            return WorldCalendarRules.FromRegistry(result.Registry!, path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                       or ParameterSheetException or WorldCalendarException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string? FindPath()
    {
        foreach (var start in new[] { ProjectSettings.GlobalizePath("res://"), System.IO.Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrEmpty(start))
                continue;
            var dir = new System.IO.DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, "config", "sheets", "01_世界參數.csv");
                if (System.IO.File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
