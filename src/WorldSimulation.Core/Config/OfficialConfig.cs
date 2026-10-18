using System.Globalization;
using System.Reflection;
using System.Text;

namespace WorldSimulation.Core.Config;

/// <summary>正式設定讀取失敗的一處錯誤（含分頁名）。</summary>
public sealed record OfficialConfigError(string Sheet, int RowNumber, string? ParameterId, string Reason)
{
    public string Describe() => RowNumber > 0
        ? $"{Sheet} 第 {RowNumber} 列 {ParameterId ?? "（無 ID）"}：{Reason}"
        : $"{Sheet}：{Reason}";
}

/// <summary><see cref="OfficialConfig.Load()"/> 的結果：成功時有 <see cref="Config"/>，失敗時列出全部錯誤。</summary>
public sealed class OfficialConfigLoadResult
{
    internal OfficialConfigLoadResult(OfficialConfig? config, IReadOnlyList<OfficialConfigError> errors)
    {
        Config = config;
        Errors = errors;
    }

    public bool IsValid => Config is not null;
    public OfficialConfig? Config { get; }
    public IReadOnlyList<OfficialConfigError> Errors { get; }

    /// <summary>失敗時給玩家看的一句話（第一處錯誤＋總數）。</summary>
    public string ErrorText => IsValid
        ? string.Empty
        : $"正式設定讀取失敗：{Errors[0].Describe()}（共 {Errors.Count} 處）";
}

/// <summary>
/// 第 02 章 b 節：正式設定唯一載入器（CLI 與 Godot 共用）。設定檔是編入 Core 組件的
/// config/sheets 全部 CSV 與 config/demo 全部檔案；Core 只解析、驗證、算雜湊，不讀磁碟。
/// 01 世界參數交給 <see cref="ConfigRegistry"/>，29 數值參數交給 <see cref="TunableParameterRegistry"/>，
/// 兩表 ParameterID 不得重複（參數單一權威）。載入結果快取為單例，之後不可變。
/// 本節只「載入並驗證」，尚未把任何 29 參數接進示範世界。
/// </summary>
public sealed class OfficialConfig
{
    public const string WorldSheet = "01_世界參數.csv";
    public const string TunableSheet = "29_數值參數.csv";
    internal const string SheetPrefix = "WorldSimulation.ConfigSheets/";
    internal const string DemoPrefix = "WorldSimulation.ConfigDemo/";

    private static readonly Lazy<OfficialConfigLoadResult> Cached = new(() => Load(ReadEmbedded(SheetPrefix), ReadEmbedded(DemoPrefix)));

    private OfficialConfig(ConfigRegistry world, TunableParameterRegistry tunable, string configHash, string tempConfigHash, int sheetCount)
    {
        World = world;
        Tunable = tunable;
        ConfigHash = configHash;
        TempConfigHash = tempConfigHash;
        SheetCount = sheetCount;
    }

    /// <summary>01_世界參數（世界結構常數、硬性規則與開關）。</summary>
    public ConfigRegistry World { get; }

    /// <summary>29_數值參數（可調且有上下限的數值）。</summary>
    public TunableParameterRegistry Tunable { get; }

    /// <summary>config/sheets 全部 CSV 的設定雜湊（64 碼）。</summary>
    public string ConfigHash { get; }

    /// <summary>config/demo 全部檔案的 TEMP 設定雜湊（64 碼）。C# 內寫死的 TEMP 情境不在內。</summary>
    public string TempConfigHash { get; }

    /// <summary>納入設定雜湊的分頁數。</summary>
    public int SheetCount { get; }

    /// <summary>本建置編入的正式設定（快取）。</summary>
    public static OfficialConfigLoadResult Load() => Cached.Value;

    /// <summary>本建置的正式設定；讀取失敗就丟出 <see cref="InvalidOperationException"/>（訊息為繁中）。</summary>
    public static OfficialConfig Current => Load().Config ?? throw new InvalidOperationException(Load().ErrorText);

    /// <summary>一行繁中說明（Godot 狀態欄、CLI 共用）。</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"正式設定：01 已載入 {World.Count} 列｜29 已載入 {Tunable.Count} 列（暫定 {Tunable.ProvisionalCount}：未平衡 {Tunable.UnbalancedCount}、【TEMP】{Tunable.TempMarkedCount}）｜設定雜湊 {ConfigHash[..16]}｜TEMP 設定雜湊 {TempConfigHash[..16]}");

    /// <summary>測試注入用：以指定檔案（檔名→內容）載入，不快取。</summary>
    internal static OfficialConfigLoadResult Load(
        IReadOnlyList<(string Name, byte[] Content)> sheets, IReadOnlyList<(string Name, byte[] Content)> demo)
    {
        var errors = new List<OfficialConfigError>();
        var configHash = global::WorldSimulation.Core.Config.ConfigHash.Compute(sheets);
        var tempHash = global::WorldSimulation.Core.Config.ConfigHash.Compute(demo);

        string? Text(string name)
        {
            var file = sheets.FirstOrDefault(f => f.Name == name);
            if (file.Content is null)
            {
                errors.Add(new OfficialConfigError(name, 0, null, "編入的正式設定缺少這個分頁"));
                return null;
            }
            return Encoding.UTF8.GetString(ConfigHashNormalize(file.Content));
        }

        ConfigRegistry? world = null;
        if (Text(WorldSheet) is { } worldText)
        {
            try
            {
                var result = ConfigRegistry.ValidateAndCreate(ParameterSheet.Parse(worldText, WorldSheet));
                world = result.Registry;
                errors.AddRange(result.Errors.Select(e => new OfficialConfigError(WorldSheet, e.RowNumber, e.ParameterId, e.Reason)));
            }
            catch (ParameterSheetException ex)
            {
                errors.Add(new OfficialConfigError(WorldSheet, 0, null, ex.Message));
            }
        }

        TunableParameterRegistry? tunable = null;
        if (Text(TunableSheet) is { } tunableText)
        {
            try
            {
                var result = TunableParameterRegistry.ValidateAndCreate(TunableParameterSheet.Parse(tunableText, TunableSheet));
                tunable = result.Registry;
                errors.AddRange(result.Errors.Select(e => new OfficialConfigError(TunableSheet, e.RowNumber, e.ParameterId, e.Reason)));
            }
            catch (ParameterSheetException ex)
            {
                errors.Add(new OfficialConfigError(TunableSheet, 0, null, ex.Message));
            }
        }

        // 參數單一權威：同一 ParameterID 不能同時出現在 01 與 29。
        if (world is not null && tunable is not null)
        {
            foreach (var id in tunable.ParameterIds.Where(world.Contains).OrderBy(i => i, StringComparer.Ordinal))
                errors.Add(new OfficialConfigError(TunableSheet, 0, id, "ParameterID 與 01_世界參數重複（同一參數只能有一份生效值）"));
        }

        return errors.Count == 0 && world is not null && tunable is not null
            ? new OfficialConfigLoadResult(new OfficialConfig(world, tunable, configHash, tempHash, sheets.Count), Array.Empty<OfficialConfigError>())
            : new OfficialConfigLoadResult(null, errors.AsReadOnly());
    }

    private static byte[] ConfigHashNormalize(byte[] content) => global::WorldSimulation.Core.Config.ConfigHash.Normalize(content);

    private static IReadOnlyList<(string Name, byte[] Content)> ReadEmbedded(string prefix)
    {
        var assembly = typeof(OfficialConfig).Assembly;
        var files = new List<(string, byte[])>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files.Add((resource[prefix.Length..], memory.ToArray()));
        }

        return files.AsReadOnly();
    }

    /// <summary>讀出編入組件的單一 TEMP 示範檔（config/demo/&lt;name&gt;）。</summary>
    internal static string ReadDemoText(string name)
    {
        using var stream = typeof(OfficialConfig).Assembly.GetManifestResourceStream(DemoPrefix + name)
            ?? throw new InvalidOperationException($"編入的 TEMP 示範設定缺少 config/demo/{name}。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
