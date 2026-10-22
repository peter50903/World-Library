using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 04 章 b 節：存檔標頭、相容性檢查與選用完整驗證。</summary>
public sealed class DemoSaveManifestTests
{
    private static readonly Dictionary<string, DemoWeeklyTempConfig> Configs = new()
    {
        ["Sample"] = DemoWeeklyTempConfig.Sample,
        ["ForagingSample"] = DemoWeeklyTempConfig.ForagingSample,
        ["LivingForagingSample"] = DemoWeeklyTempConfig.LivingForagingSample,
        ["CrowdedForagingSample"] = DemoWeeklyTempConfig.CrowdedForagingSample,
        ["FamineSample"] = DemoWeeklyTempConfig.FamineSample,
        ["LocalFamineSample"] = DemoWeeklyTempConfig.LocalFamineSample,
        ["FrontierSample"] = DemoWeeklyTempConfig.FrontierSample
    };

    public static TheoryData<string, ulong> Matrix()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var name in Configs.Keys)
            for (ulong seed = 1; seed <= 5; seed++)
                data.Add(name, seed);
        return data;
    }

    private static DemoObserverWorld Run(DemoWeeklyTempConfig config, ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    private static string WithChecksum(JsonNode doc)
    {
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
        return doc.ToJsonString();
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EverySavePoint_LoadsResumesIdentically_AndVerifiesByReplay(string config, ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), Configs[config]);
        var day = 0;
        foreach (var point in new[] { 0, 7, 9, 365, 730 })
        {
            for (; day < point; day++)
                world.AdvanceOneDay();
            var json = world.ExportTempSave();
            var loaded = DemoObserverWorld.ImportTempSave(json);
            Assert.Null(loaded.LoadWarning);
            Assert.Equal(json, loaded.ExportTempSave());
            var reference = DemoObserverWorld.ImportTempSave(json);
            for (var i = 0; i < 14; i++)
            {
                loaded.AdvanceOneDay();
                reference.AdvanceOneDay();
            }
            Assert.Equal(reference.ExportTempSave(), loaded.ExportTempSave());

            var verification = DemoObserverWorld.VerifyTempSaveByReplay(json);
            Assert.True(verification.Ok, $"{config} 種子 {seed} 第 {point} 日：{verification.FirstDifference}");
        }
    }

    [Fact]
    public void SaveFile_HasOnlyVersionManifestWeeklyConfigAndState_AndAValidChecksum()
    {
        var world = Run(DemoWeeklyTempConfig.FrontierSample, 42, 30);
        var doc = JsonNode.Parse(world.ExportTempSave())!.AsObject();
        Assert.Equal(new[] { "Version", "Manifest", "WeeklyConfig", "State" }, doc.Select(p => p.Key));
        var manifest = doc["Manifest"]!;
        Assert.Equal(DemoObserverWorld.TempSaveVersion, manifest["StructureVersion"]!.GetValue<int>());
        Assert.Equal(WorldRecipe.TempRulesVersion, manifest["RulesVersion"]!.GetValue<string>());
        Assert.Equal(WorldRecipe.CurrentConfigHash, manifest["ConfigHash"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{64}$", manifest["TempConfigHash"]!.GetValue<string>());
        Assert.Equal(TimeSpan.FromDays(30).Ticks, manifest["WorldTimeTicks"]!.GetValue<long>());
        Assert.Equal(DemoSaveHeader.Checksum(Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString())),
            manifest["StateChecksum"]!.GetValue<string>());
    }

    [Fact]
    public void SameRecipe_ExportsByteIdenticalFiles()
    {
        var a = Run(DemoWeeklyTempConfig.LivingForagingSample, 3, 200).ExportTempSave();
        var b = Run(DemoWeeklyTempConfig.LivingForagingSample, 3, 200).ExportTempSave();
        Assert.Equal(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    public static TheoryData<string, string> Rejections() => new()
    {
        { "truncated", "TEMP 存檔損毀" },
        { "state-number", "校驗碼不符" },
        { "config-hash", "正式設定不符" },
        { "temp-config-hash", "TEMP 示範設定（config/demo）不符" },
        { "rules-version", "規則版本不符" },
        { "structure-version", "結構版本不符" },
        { "previous-version", "版本太舊，請建立新世界" },
        { "world-time", "不是整日" },
        { "manifest-recipe-mismatch", "標頭與世界配方不一致" }
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void BadHeaderOrContent_IsRejectedWithItsReason_AndTheLiveWorldIsUnchanged(string what, string expected)
    {
        var live = Run(DemoWeeklyTempConfig.FrontierSample, 42, 40);
        var original = live.ExportTempSave();
        var doc = JsonNode.Parse(original)!;
        var manifest = doc["Manifest"]!;
        var file = what switch
        {
            "truncated" => original[..(original.Length / 2)],
            "state-number" => NoChecksum(() => doc["State"]!["SchedulerNextSequence"] = doc["State"]!["SchedulerNextSequence"]!.GetValue<long>() + 1),
            "config-hash" => Edit(() =>
            {
                manifest["ConfigHash"] = new string('a', 64);
                manifest["Recipe"]!["ConfigHash"] = new string('a', 64);
            }),
            "temp-config-hash" => Edit(() => manifest["TempConfigHash"] = new string('b', 64)),
            "rules-version" => Edit(() =>
            {
                manifest["RulesVersion"] = "TEMP-RULES-9";
                manifest["Recipe"]!["RulesVersion"] = "TEMP-RULES-9";
            }),
            "structure-version" => Edit(() => manifest["StructureVersion"] = 99),
            "previous-version" => Edit(() => doc["Version"] = DemoObserverWorld.TempSaveVersion - 1),
            "world-time" => Edit(() => manifest["WorldTimeTicks"] = manifest["WorldTimeTicks"]!.GetValue<long>() + 1),
            "manifest-recipe-mismatch" => Edit(() => manifest["CodeVersion"] = "different"),
            _ => throw new ArgumentOutOfRangeException(nameof(what))
        };
        var ex = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(file));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Equal(original, live.ExportTempSave());

        // 存檔槽：檔頭讀得出的顯示「不相容（原因）」，讀不出的顯示「無法辨識」；檔案位元組不動。
        var directory = Path.Combine(Path.GetTempPath(), "wsim-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var path = DemoSaveSlots.PathFor(1, directory);
            File.WriteAllText(path, file, new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(path);
            var info = DemoSaveSlots.ReadInfo(1, directory);
            Assert.False(info.Compatible);
            Assert.True(info.Describe().Contains("不相容（", StringComparison.Ordinal)
                || info.Describe().Contains("無法辨識（", StringComparison.Ordinal), info.Describe());
            Assert.Throws<InvalidDataException>(() => DemoSaveSlots.Load(1, directory));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        string Edit(Action change)
        {
            change();
            return WithChecksum(doc);
        }

        string NoChecksum(Action change)
        {
            change(); // 只改內容、不改校驗碼
            return doc.ToJsonString();
        }
    }

    [Fact]
    public void ReplayVerification_FindsAForgedSharedHarvest_AndItsWeek()
    {
        var world = Run(DemoWeeklyTempConfig.ForagingSample, 2, 120);
        var doc = JsonNode.Parse(world.ExportTempSave())!;
        var weeks = doc["State"]!["WeekReports"]!.AsArray();
        var target = weeks[5]!;
        var ledger = target["Settlements"]![0]!;
        // 採收 +100，並修正存量期末（資源帳仍平衡），校驗碼也補正。
        ledger["Harvested"] = ledger["Harvested"]!.GetValue<long>() + 100;
        ledger["ReserveClosing"] = ledger["ReserveClosing"]!.GetValue<long>() - 100;
        var result = DemoObserverWorld.VerifyTempSaveByReplay(WithChecksum(doc));
        Assert.False(result.Ok);
        Assert.Equal(target["Week"]!.GetValue<int>(), result.FirstDivergentWeek);
        Assert.Contains("的 Harvested：存檔 ", result.FirstDifference, StringComparison.Ordinal);
        Assert.StartsWith($"第 {result.FirstDivergentWeek} 週 聚落 ", result.FirstDifference, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayVerification_FindsAForgedStarvationDeath_AndItsWeek()
    {
        // 找一週有餓死的人類繁衍世界（局部飢荒式的缺糧在擁擠採集情境較常見）。
        DemoObserverWorld? world = null;
        int week = 0;
        foreach (var (config, seed) in new[] { (DemoWeeklyTempConfig.CrowdedForagingSample, 1UL), (DemoWeeklyTempConfig.LivingForagingSample, 42UL),
                     (DemoWeeklyTempConfig.CrowdedForagingSample, 2UL), (DemoWeeklyTempConfig.FamineSample, 1UL) })
        {
            // 第 04-1 章：存檔只保留近期週報——停在第一次餓死的下一週存檔（一定在保留範圍內）。
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), config);
            DemoWeekReport? hit = null;
            for (var day = 0; day < 3 * 365 && hit is null; day++)
                if (probe.AdvanceOneDay().SettledWeek is { } settled && settled.Settlements.Any(l => l.StarvationDeaths > 0))
                    hit = settled;
            if (hit is null) continue;
            for (var day = 0; day < 7; day++)
                probe.AdvanceOneDay();
            world = probe;
            week = hit.Week;
            break;
        }
        Assert.NotNull(world);
        var doc = JsonNode.Parse(world!.ExportTempSave())!;
        var weekBase = doc["State"]!["WeekBase"]!.GetValue<int>();
        var ledger = doc["State"]!["WeekReports"]![week - 1 - weekBase]!["Settlements"]!.AsArray()
            .First(l => l!["Deaths"]!.GetValue<long>() > l["NaturalDeaths"]!.GetValue<long>())!;
        ledger["Deaths"] = ledger["Deaths"]!.GetValue<long>() - 1;
        ledger["PopulationClosing"] = ledger["PopulationClosing"]!.GetValue<long>() + 1;
        var result = DemoObserverWorld.VerifyTempSaveByReplay(WithChecksum(doc));
        Assert.False(result.Ok);
        Assert.Equal(week, result.FirstDivergentWeek);
        Assert.Contains("的 Deaths：存檔 ", result.FirstDifference, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayVerification_ReportsHeaderProblemsWithoutReplaying()
    {
        var doc = JsonNode.Parse(Run(DemoWeeklyTempConfig.Sample, 4, 20).ExportTempSave())!;
        doc["Manifest"]!["TempConfigHash"] = new string('c', 64);
        var result = DemoObserverWorld.VerifyTempSaveByReplay(WithChecksum(doc));
        Assert.False(result.Ok);
        Assert.Null(result.FirstDivergentWeek);
        Assert.Contains("TEMP 示範設定", result.FirstDifference, StringComparison.Ordinal);
        Assert.Contains("完整驗證槽 2：不一致——", result.Describe(2), StringComparison.Ordinal);
    }

    [Fact]
    public void HundredYearSave_LoadAndFullVerification_Timing()
    {
        // PR 說明附 100 年存檔讀檔與完整驗證耗時（寫到暫存檔，CI 以註記列出）。
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 100 * 365);
        var json = world.ExportTempSave();
        var load = Stopwatch.StartNew();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        load.Stop();
        Assert.Equal(json, loaded.ExportTempSave());
        var verification = DemoObserverWorld.VerifyTempSaveByReplay(json);
        Assert.True(verification.Ok, verification.FirstDifference);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "wsim-save-timing.txt"), string.Create(CultureInfo.InvariantCulture,
            $"100 年存檔 {Encoding.UTF8.GetByteCount(json) / 1024} KB：讀檔 {load.Elapsed.TotalSeconds:0.00} 秒，完整驗證 {verification.Elapsed.TotalSeconds:0.00} 秒"));
    }
}
