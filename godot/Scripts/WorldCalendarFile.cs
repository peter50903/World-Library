using WorldSimulation.Core.Config;
using WorldSimulation.Core.Time;

namespace WorldSimulation.GodotClient;

/// <summary>
/// C08：Godot 的世界曆法。第 02 章 b 節起改由本建置編入的正式設定（<see cref="OfficialConfig"/>，與 CLI 同一份、
/// 同一個設定雜湊）建立，不再到磁碟找 CSV。讀不到或不一致就回報錯誤訊息，不給預設值、不退回寫死 365。
/// </summary>
public static class WorldCalendarFile
{
    public const string RelativePath = "config/sheets/01_世界參數.csv";

    /// <summary>建立曆法；成功時 <paramref name="error"/> 為 null，失敗時回傳 null 並附繁體中文錯誤。</summary>
    public static WorldCalendarRules? TryLoad(out string? error)
    {
        var loaded = OfficialConfig.Load();
        if (loaded.Config is not { } config)
        {
            error = loaded.ErrorText;
            return null;
        }

        try
        {
            error = null;
            return WorldCalendarRules.FromRegistry(config.World, "本建置編入的 " + RelativePath);
        }
        catch (WorldCalendarException ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
