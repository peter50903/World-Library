using System.Globalization;
using System.Security.Cryptography;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World;

/// <summary>
/// 第 04 章 b 節（44H SAVE_WORLD）：存檔標頭。結構版本＝存檔版本常數；規則版本、設定雜湊、TEMP 設定雜湊、
/// 程式版本、世界時間與世界配方；<see cref="StateChecksum"/>＝SHA-256（`State` 節點無縮排序列化的 UTF-8 位元組，64 個小寫十六進位字元）。
/// </summary>
public sealed record DemoSaveManifest(
    int StructureVersion,
    string RulesVersion,
    string ConfigHash,
    string TempConfigHash,
    string CodeVersion,
    long WorldTimeTicks,
    WorldRecipe Recipe,
    string StateChecksum);

/// <summary>
/// 第 04 章 b 節：讀檔與存檔槽共用的標頭檢查（②～⑧；①版本由呼叫端先檢查）。任一不符丟出
/// <see cref="InvalidDataException"/>，訊息為繁中並含存檔值與目前值；程式版本不同只回傳警告、不拒讀。
/// </summary>
internal static class DemoSaveHeader
{
    internal static string Checksum(ReadOnlySpan<byte> stateUtf8)
        => Convert.ToHexString(SHA256.HashData(stateUtf8)).ToLowerInvariant();

    internal static string CurrentTempConfigHash => OfficialConfig.Load().Config?.TempConfigHash ?? WorldRecipe.InvalidConfigHash;

    private static string Short(string? value, int length)
        => value is null ? "（無）" : value.Length > length ? value[..length] : value;

    /// <summary>②～⑧。回傳世界日數與（程式版本不同時的）警告。</summary>
    /// <param name="stateUtf8">檔內 `State` 節點原文；null＝檔案不完整、無法取得。</param>
    internal static (int Days, string? Warning) Check(int version, DemoSaveManifest? manifest, byte[]? stateUtf8)
    {
        if (manifest is null || manifest.Recipe is null)
            throw new InvalidDataException("TEMP 存檔缺少標頭。");
        // ② 標頭結構版本＝存檔版本。
        if (manifest.StructureVersion != version)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"存檔標頭結構版本不符（標頭第 {manifest.StructureVersion} 版，存檔第 {version} 版），已拒絕載入。"));
        var recipe = manifest.Recipe;
        recipe.Validate();
        if (manifest.RulesVersion != recipe.RulesVersion || manifest.ConfigHash != recipe.ConfigHash
            || manifest.CodeVersion != recipe.CodeVersion)
            throw new InvalidDataException("存檔標頭與世界配方不一致，已拒絕載入。");
        // ③ 規則版本。
        if (manifest.RulesVersion != WorldRecipe.TempRulesVersion)
            throw new InvalidDataException(
                $"規則版本不符（存檔 {manifest.RulesVersion}，目前 {WorldRecipe.TempRulesVersion}），已拒絕載入。");
        // ④ 正式設定雜湊。
        var currentConfig = WorldRecipe.CurrentConfigHash;
        if (manifest.ConfigHash != currentConfig)
            throw new InvalidDataException(
                $"正式設定不符（存檔設定雜湊 {Short(manifest.ConfigHash, 16)}，目前 {Short(currentConfig, 16)}），已拒絕載入。");
        // ⑤ TEMP 設定雜湊。
        var currentTemp = CurrentTempConfigHash;
        if (manifest.TempConfigHash != currentTemp)
            throw new InvalidDataException(
                $"TEMP 示範設定（config/demo）不符（存檔 {Short(manifest.TempConfigHash, 16)}，目前 {Short(currentTemp, 16)}），已拒絕載入。");
        // ⑥ 程式版本：只警告。
        string? warning = manifest.CodeVersion == WorldRecipe.CurrentCodeVersion
            ? null
            : $"⚠ 程式版本不同（存檔 {Short(manifest.CodeVersion, 12)}，目前 {Short(WorldRecipe.CurrentCodeVersion, 12)}）：仍可讀取，之後的推進以目前程式為準。";
        // ⑦ 世界時間必須是整日且在可讀範圍內。
        if (manifest.WorldTimeTicks < 0)
            throw new InvalidDataException("TEMP 存檔世界日期超出可重播範圍。");
        if (manifest.WorldTimeTicks % TimeSpan.TicksPerDay != 0)
            throw new InvalidDataException("存檔世界時間不是整日，已拒絕載入。");
        var days = manifest.WorldTimeTicks / TimeSpan.TicksPerDay;
        if (days > DemoObserverWorld.TempMaxSaveDays)
            throw new InvalidDataException("TEMP 存檔世界日期超出可重播範圍。");
        // ⑧ 校驗碼。
        if (stateUtf8 is null || Checksum(stateUtf8) != manifest.StateChecksum)
            throw new InvalidDataException("存檔內容損毀（校驗碼不符），已拒絕載入。");
        return ((int)days, warning);
    }
}
