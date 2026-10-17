using System.Text;
using WorldSimulation.Core.Config;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 02 章 b 節：正式設定（01＋29）讀取、驗證與設定雜湊。</summary>
public sealed class OfficialConfigTests
{
    private static string RepoDirectory(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, relative)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }

    private static List<(string Name, byte[] Content)> DiskFiles(string relative, string pattern) =>
        Directory.GetFiles(RepoDirectory(relative), pattern)
            .Select(p => (Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToList();

    private static List<(string Name, byte[] Content)> Sheets() => DiskFiles(Path.Combine("config", "sheets"), "*.csv");

    private static List<(string Name, byte[] Content)> Demo() => DiskFiles(Path.Combine("config", "demo"), "*");

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(List<(string Name, byte[] Content)> files, string name) =>
        Encoding.UTF8.GetString(ConfigHash.Normalize(files.Single(f => f.Name == name).Content));

    private static List<(string Name, byte[] Content)> With(List<(string Name, byte[] Content)> files, string name, string content) =>
        files.Select(f => f.Name == name ? (f.Name, Utf8(content)) : f).ToList();

    private const string TunableHeader = "ParameterID,系統,參數名稱,預設值,最小值,最大值,單位,資料型別,說明,是否已平衡,Enabled\n";

    /// <summary>以最小的 29 表替換正式 29 表，回傳載入結果。</summary>
    private static OfficialConfigLoadResult LoadWithTunable(params string[] rows) =>
        OfficialConfig.Load(With(Sheets(), OfficialConfig.TunableSheet, TunableHeader + string.Join("\n", rows) + "\n"), Demo());

    [Fact]
    public void EmbeddedConfig_Loads_AndMatchesTheFilesOnDisk()
    {
        var result = OfficialConfig.Load();
        Assert.True(result.IsValid, result.ErrorText);
        var config = result.Config!;
        var sheets = Sheets();

        Assert.Equal(sheets.Count, config.SheetCount);
        Assert.Equal(ConfigHash.Compute(sheets), config.ConfigHash);
        Assert.Equal(ConfigHash.Compute(Demo()), config.TempConfigHash);
        Assert.Matches("^[0-9a-f]{64}$", config.ConfigHash);

        // 列數從磁碟重算，不寫死。
        var world = ParameterSheet.Parse(Text(sheets, OfficialConfig.WorldSheet), "01");
        Assert.Equal(world.Count(r => r.Enabled), config.World.Count);
        var tunable = TunableParameterSheet.Parse(Text(sheets, OfficialConfig.TunableSheet), "29").Where(r => r.Enabled).ToList();
        Assert.Equal(tunable.Count, config.Tunable.Count);
        Assert.Equal(tunable.Count(r => r.Balanced == "FALSE"), config.Tunable.UnbalancedCount);
        Assert.Equal(tunable.Count(r => (r.Description ?? "").TrimStart().StartsWith("【TEMP】", StringComparison.Ordinal)),
            config.Tunable.TempMarkedCount);

        Assert.True(config.Tunable.IsProvisional("P_WATER_DEHYDRATION_RATE"));
        Assert.Equal(2m, config.Tunable.GetNumber("P_WATER_DEHYDRATION_RATE"));
        Assert.Throws<KeyNotFoundException>(() => config.Tunable.GetNumber("P_NOT_A_PARAMETER"));

        var line = config.Describe();
        Assert.StartsWith($"正式設定：01 已載入 {config.World.Count} 列｜29 已載入 {config.Tunable.Count} 列（暫定 ", line, StringComparison.Ordinal);
        Assert.Contains($"設定雜湊 {config.ConfigHash[..16]}", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigHash_IgnoresLineEndingsBomAndFileOrder_ButNotContent()
    {
        var sheets = Sheets();
        var hash = ConfigHash.Compute(sheets);

        var crlf = sheets.Select(f => (f.Name, Utf8(Encoding.UTF8.GetString(f.Content).Replace("\r\n", "\n").Replace("\n", "\r\n")))).ToList();
        Assert.Equal(hash, ConfigHash.Compute(crlf));

        var bom = sheets.Select(f => (f.Name, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(ConfigHash.Normalize(f.Content)).ToArray())).ToList();
        Assert.Equal(hash, ConfigHash.Compute(bom));

        var reversed = Enumerable.Reverse(sheets).ToList();
        Assert.Equal(hash, ConfigHash.Compute(reversed));

        var text = Text(sheets, OfficialConfig.TunableSheet);
        var changed = With(sheets, OfficialConfig.TunableSheet, text.Replace("P_WATER_DEHYDRATION_RATE,生存,缺水惡化倍率,2,", "P_WATER_DEHYDRATION_RATE,生存,缺水惡化倍率,3,"));
        Assert.NotEqual(hash, ConfigHash.Compute(changed));

        // 非參數表改一字也算（設定雜湊涵蓋全部分頁），但 TEMP 雜湊不動。
        var other = sheets.First(f => f.Name != OfficialConfig.WorldSheet && f.Name != OfficialConfig.TunableSheet);
        var otherChanged = sheets.Select(f => f.Name == other.Name ? (f.Name, f.Content.Concat(Utf8("x")).ToArray()) : f).ToList();
        Assert.NotEqual(hash, ConfigHash.Compute(otherChanged));
        var tempBefore = OfficialConfig.Load(sheets, Demo()).Config!.TempConfigHash;
        Assert.Equal(tempBefore, ConfigHash.Compute(Demo()));

        // 增刪分頁都會改變。
        Assert.NotEqual(hash, ConfigHash.Compute(sheets.Skip(1)));
        Assert.NotEqual(hash, ConfigHash.Compute(sheets.Append(("99_新分頁.csv", Utf8("a\n")))));

        // 檔名也算在內：同內容換名字要變。
        var renamed = sheets.Select(f => f.Name == OfficialConfig.TunableSheet ? ("29_數值參數_改.csv", f.Content) : f).ToList();
        Assert.NotEqual(hash, ConfigHash.Compute(renamed));
    }

    [Fact]
    public void ChangingOnlyTheDemoFile_ChangesOnlyTheTempHash()
    {
        var baseline = OfficialConfig.Load(Sheets(), Demo()).Config!;
        var demo = Demo().Select(f => (f.Name, f.Content.Concat(Utf8("\n")).ToArray())).ToList();
        var changed = OfficialConfig.Load(Sheets(), demo).Config!;
        Assert.Equal(baseline.ConfigHash, changed.ConfigHash);
        Assert.NotEqual(baseline.TempConfigHash, changed.TempConfigHash);
    }

    [Fact]
    public void EmbeddedDemoSettings_AreByteForByteTheDemoFileOnDisk_SoTheWorldIsUnchanged()
    {
        var disk = Encoding.UTF8.GetString(ConfigHash.Normalize(Demo().Single(f => f.Name == "terrain-food-temp.json").Content));
        var embedded = OfficialConfig.ReadDemoText("terrain-food-temp.json").Replace("\r\n", "\n").TrimStart('\uFEFF');
        Assert.Equal(disk, embedded);
    }

    [Theory]
    [InlineData("P_X,生存,甲,6,0,5,x,number,說明,TRUE,TRUE", "超出範圍")]
    [InlineData("P_X,生存,甲,1,5,0,x,number,說明,TRUE,TRUE", "大於最大值")]
    [InlineData("P_X,生存,甲,1,0,5,x,float,說明,TRUE,TRUE", "不支援的資料型別")]
    [InlineData("P_X,生存,甲,2,0,1,x,bool,說明,TRUE,TRUE", "不是 bool")]
    [InlineData("P_X,生存,甲,1.5,0,5,x,int,說明,TRUE,TRUE", "不是整數")]
    [InlineData("P_X,生存,甲,1,0,5,x,number,說明,,TRUE", "是否已平衡")]
    [InlineData("P_X,生存,甲,,0,5,x,number,說明,TRUE,TRUE", "缺少預設值")]
    [InlineData(",生存,甲,1,0,5,x,number,說明,TRUE,TRUE", "缺少 ParameterID")]
    public void InvalidTunableRow_RejectsTheWholeConfig_WithAChineseReason(string row, string reason)
    {
        var result = LoadWithTunable(row);
        Assert.False(result.IsValid);
        Assert.Null(result.Config);
        var error = Assert.Single(result.Errors);
        Assert.Equal(OfficialConfig.TunableSheet, error.Sheet);
        Assert.Contains(reason, error.Reason, StringComparison.Ordinal);
        Assert.StartsWith("正式設定讀取失敗：29_數值參數.csv 第 2 列", result.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateIdWithin29_IsRejected_EvenWhenOneRowIsDisabled()
    {
        var result = LoadWithTunable(
            "P_X,生存,甲,1,0,5,x,number,說明,TRUE,TRUE",
            "P_X,生存,乙,1,0,5,x,number,說明,TRUE,FALSE");
        Assert.False(result.IsValid);
        Assert.Contains("重複", Assert.Single(result.Errors).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SameIdIn01And29_IsRejected()
    {
        var result = LoadWithTunable("WORLD_DAYS_PER_YEAR,時間,一年天數,365,1,1000,日,int,說明,TRUE,TRUE");
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("WORLD_DAYS_PER_YEAR", error.ParameterId);
        Assert.Contains("01_世界參數", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledBlankRows_AreIgnored_AndValidMinimalSheetLoads()
    {
        var result = LoadWithTunable(
            "P_X,生存,甲,1,1,1,x,int,【TEMP】說明,TRUE,TRUE",
            "P_Y,生存,乙,0,0,1,x,bool,說明,FALSE,TRUE",
            ",,,,,,,,,,FALSE");
        Assert.True(result.IsValid, result.ErrorText);
        var tunable = result.Config!.Tunable;
        Assert.Equal(2, tunable.Count);
        Assert.Equal(1, tunable.GetInt("P_X"));
        Assert.False(tunable.GetBool("P_Y"));
        Assert.Equal(2, tunable.ProvisionalCount);
        Assert.Equal(1, tunable.UnbalancedCount);
        Assert.Equal(1, tunable.TempMarkedCount);
        Assert.Throws<InvalidOperationException>(() => tunable.GetNumber("P_X"));
    }

    [Fact]
    public void MissingSheet_IsReported()
    {
        var sheets = Sheets().Where(f => f.Name != OfficialConfig.TunableSheet).ToList();
        var result = OfficialConfig.Load(sheets, Demo());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Sheet == OfficialConfig.TunableSheet && e.Reason.Contains("缺少", StringComparison.Ordinal));
    }
}
