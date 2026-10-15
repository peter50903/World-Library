using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World;

/// <summary>
/// 第 02 章 a 節（40F VER_WORLD_RECIPE）：世界配方＝規則版本＋設定雜湊＋程式版本＋世界種子＋歷史種子。
/// 世界種子決定地圖、聚落與初始人口；歷史種子決定之後的歷史（41A SEED_WORLD、SEED_HISTORY）。
/// <see cref="HistorySeedInput"/> 為 null 時歷史種子由世界種子衍生。
/// 規則版本與設定雜湊目前是 TEMP 佔位（正式設定雜湊在第 02 章 b 節計算），不得當成已計算的正式值。
/// </summary>
public sealed record WorldRecipe(
    ulong WorldSeed,
    ulong? HistorySeedInput,
    string RulesVersion,
    string ConfigHash,
    string CodeVersion)
{
    /// <summary>TEMP：尚無正式規則版號制度。</summary>
    public const string TempRulesVersion = "TEMP-RULES-0";

    /// <summary>TEMP：正式設定雜湊在第 02 章 b 節（正式設定讀取）完成前不計算。</summary>
    public const string TempConfigHashPlaceholder = "TEMP-CONFIG-HASH-PENDING";

    /// <summary>取不到 Git 提交雜湊時的程式版本（不編造）。</summary>
    public const string UnknownCodeVersion = "未知（本機建置）";

    /// <summary>實際使用的歷史種子（輸入值，或由世界種子衍生）。</summary>
    [JsonIgnore]
    public ulong HistorySeed => HistorySeedInput ?? SeedDerivation.HistorySeed(WorldSeed);

    [JsonIgnore]
    public bool HistorySeedDerived => HistorySeedInput is null;

    [JsonIgnore]
    public bool ConfigHashIsTemp => ConfigHash == TempConfigHashPlaceholder;

    /// <summary>以目前建置的規則版本、設定雜湊與程式版本組成配方。</summary>
    public static WorldRecipe ForSeeds(ulong worldSeed, ulong? historySeed = null)
        => new(worldSeed, historySeed, TempRulesVersion, TempConfigHashPlaceholder, CurrentCodeVersion);

    /// <summary>
    /// 本建置的 Git 提交雜湊：取 Core 組件 AssemblyInformationalVersion 中「+」之後的部分
    /// （.NET 8 SDK 的 Source Link 會自動帶入）；取不到就回傳 <see cref="UnknownCodeVersion"/>。
    /// </summary>
    public static string CurrentCodeVersion { get; } = ReadCodeVersion();

    private static string ReadCodeVersion()
    {
        var informational = typeof(WorldRecipe).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
            return UnknownCodeVersion;
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0 || plus == informational.Length - 1)
            return UnknownCodeVersion;
        var hash = informational[(plus + 1)..];
        return hash.All(Uri.IsHexDigit) ? hash : UnknownCodeVersion;
    }

    /// <summary>配方各欄位必須是可讀的短字串（讀檔時檢查，防止損毀資料進入畫面）。</summary>
    internal void Validate()
    {
        static void Check(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new InvalidDataException($"世界配方的{name}缺漏或格式不符。");
        }
        Check(RulesVersion, "規則版本");
        Check(ConfigHash, "設定雜湊");
        Check(CodeVersion, "程式版本");
    }

    private string ConfigHashText => ConfigHashIsTemp
        ? "【TEMP】尚未計算（正式設定讀取完成後提供）"
        : ConfigHash.Length > 16 ? ConfigHash[..16] : ConfigHash;

    private string RulesVersionText => RulesVersion == TempRulesVersion ? $"【TEMP】{RulesVersion}" : RulesVersion;

    private string HistorySeedText => HistorySeedDerived
        ? string.Create(CultureInfo.InvariantCulture, $"{HistorySeed}（由世界種子衍生）")
        : string.Create(CultureInfo.InvariantCulture, $"{HistorySeed}（自訂）");

    private string CodeVersionText => CodeVersion.Length > 12 && CodeVersion.All(Uri.IsHexDigit)
        ? CodeVersion[..12]
        : CodeVersion;

    /// <summary>一行繁體中文（CLI `s` 與存檔說明共用）。</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"世界配方｜世界種子 {WorldSeed}｜歷史種子 {HistorySeedText}｜規則版本 {RulesVersionText}｜設定雜湊 {ConfigHashText}｜程式版本 {CodeVersionText}");

    /// <summary>多行版（Godot 狀態欄）。</summary>
    public string DescribeLines() => string.Join('\n',
        string.Create(CultureInfo.InvariantCulture, $"世界種子：{WorldSeed}"),
        $"歷史種子：{HistorySeedText}",
        $"規則版本：{RulesVersionText}",
        $"設定雜湊：{ConfigHashText}",
        $"程式版本：{CodeVersionText}");
}
