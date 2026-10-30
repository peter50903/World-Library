using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>V0-07C：TEMP 多存檔槽（3 槽）、檔頭資訊與版本策略。</summary>
public sealed class DemoSaveSlotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wsim-slots-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static DemoObserverWorld World(ulong seed, DemoWeeklyTempConfig config, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void EmptySlots_AreListedAsEmpty()
    {
        var slots = DemoSaveSlots.ListAll(_directory);
        Assert.Equal(DemoSaveSlots.TempSlotCount, slots.Count);
        Assert.Equal(new[] { 1, 2, 3 }, slots.Select(s => s.Slot));
        Assert.All(slots, s => Assert.False(s.Exists));
        Assert.Equal("槽 2：（空）", slots[1].Describe());
    }

    [Fact]
    public void ThreeSlots_KeepIndependentWorlds_AndShowHeaderInfo()
    {
        var a = World(42, DemoWeeklyTempConfig.Sample, 9);
        var b = World(7, DemoWeeklyTempConfig.FrontierSample, 21);
        var c = World(3, DemoWeeklyTempConfig.LocalFamineSample, 30);
        DemoSaveSlots.Save(a, 1, _directory);
        DemoSaveSlots.Save(b, 2, _directory);
        DemoSaveSlots.Save(c, 3, _directory);

        var slots = DemoSaveSlots.ListAll(_directory);
        Assert.Equal((42UL, 9, "一般示範", 35), (slots[0].Seed!.Value, slots[0].DaysElapsed!.Value, slots[0].ScenarioName!, slots[0].Version!.Value));
        Assert.Equal((7UL, 21, "開拓"), (slots[1].Seed!.Value, slots[1].DaysElapsed!.Value, slots[1].ScenarioName!));
        Assert.Equal((3UL, 30, "局部饑荒"), (slots[2].Seed!.Value, slots[2].DaysElapsed!.Value, slots[2].ScenarioName!));
        Assert.Equal($"槽 2：第 21 日｜世界種子 7｜歷史種子 {WorldRecipe.ForSeeds(7).HistorySeed}（衍生）｜開拓｜設定 {WorldRecipe.CurrentConfigHash[..8]}｜存檔第 35 版｜可讀", slots[1].Describe());
        Assert.All(slots, s => Assert.True(s.Compatible));

        Assert.Equal(a.ExportTempSave(), DemoSaveSlots.Load(1, _directory).ExportTempSave());
        Assert.Equal(b.ExportTempSave(), DemoSaveSlots.Load(2, _directory).ExportTempSave());
        Assert.Equal(c.ExportTempSave(), DemoSaveSlots.Load(3, _directory).ExportTempSave());
    }

    [Fact]
    public void SavingOverASlot_ReplacesOnlyThatSlot()
    {
        DemoSaveSlots.Save(World(42, DemoWeeklyTempConfig.Sample, 5), 1, _directory);
        DemoSaveSlots.Save(World(42, DemoWeeklyTempConfig.Sample, 6), 2, _directory);
        var later = World(42, DemoWeeklyTempConfig.Sample, 15);
        DemoSaveSlots.Save(later, 1, _directory);

        Assert.Equal(15, DemoSaveSlots.ReadInfo(1, _directory).DaysElapsed);
        Assert.Equal(6, DemoSaveSlots.ReadInfo(2, _directory).DaysElapsed);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void OutOfRangeSlot_IsRejected(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoSaveSlots.PathFor(slot, _directory));
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoSaveSlots.Load(slot, _directory));
    }

    [Fact]
    public void LoadingEmptySlot_Throws()
        => Assert.Throws<FileNotFoundException>(() => DemoSaveSlots.Load(3, _directory));

    [Fact]
    public void SlotInfo_ReadsOnlyHeader_EvenWhenStateIsCorrupt()
    {
        // 檔頭完整、世界狀態被截斷：槽位資訊仍可顯示，但校驗碼無法成立→不相容（第 04 章 b 節）；真正讀檔也拒絕。
        var json = World(42, DemoWeeklyTempConfig.Sample, 12).ExportTempSave();
        var cut = json[..(json.IndexOf("\"State\"", StringComparison.Ordinal) + 20)];
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DemoSaveSlots.PathFor(2, _directory), cut, new UTF8Encoding(false));

        var info = DemoSaveSlots.ReadInfo(2, _directory);
        Assert.Null(info.Error);
        Assert.Equal(12, info.DaysElapsed);
        Assert.False(info.Compatible);
        Assert.Contains("校驗碼不符", info.IncompatibleReason, StringComparison.Ordinal);
        Assert.EndsWith("｜不相容（存檔內容損毀（校驗碼不符））", info.Describe(), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => DemoSaveSlots.Load(2, _directory));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Version\":4,\"Scenario\":{},\"State\":{}}")]
    [InlineData("{\"Version\":\"2\"}")]
    [InlineData("{\"Scenario\":{}}")]
    [InlineData("")]
    public void BrokenHeader_IsReportedAsError_NotThrown(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DemoSaveSlots.PathFor(1, _directory), content, new UTF8Encoding(false));

        var info = DemoSaveSlots.ReadInfo(1, _directory);
        Assert.True(info.Exists);
        Assert.NotNull(info.Error);
        Assert.StartsWith("槽 1：無法辨識（", info.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownScenarioInHeader_IsReportedAsError()
    {
        var doc = JsonNode.Parse(World(42, DemoWeeklyTempConfig.Sample, 3).ExportTempSave())!;
        doc["WeeklyConfig"]!["FoodPerPersonPerWeek"] = 1;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DemoSaveSlots.PathFor(1, _directory), doc.ToJsonString(), new UTF8Encoding(false));

        Assert.Equal("不是已知的示範情境", DemoSaveSlots.ReadInfo(1, _directory).Error);
    }

    // ── 版本策略（C04／C05）：只有第 35 版可讀；第 1～34 版「版本太舊，請建立新世界」；其他版本不受支援 ──

    [Theory]
    [InlineData(0)]
    [InlineData(36)]
    [InlineData(99)]
    [InlineData(-2)]
    public void UnknownVersion_IsRejected(int version)
    {
        var doc = JsonNode.Parse(World(42, DemoWeeklyTempConfig.Sample, 8).ExportTempSave())!;
        doc["Version"] = version;
        var ex = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Contains("版本不受支援", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("2.5")]
    [InlineData("null")]
    [InlineData("99999999999")]
    public void NonIntegerVersion_IsRejected(string versionJson)
    {
        var doc = JsonNode.Parse(World(42, DemoWeeklyTempConfig.Sample, 8).ExportTempSave())!;
        doc["Version"] = JsonNode.Parse(versionJson);
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Fact]
    public void MissingVersion_IsRejected()
    {
        var doc = JsonNode.Parse(World(42, DemoWeeklyTempConfig.Sample, 8).ExportTempSave())!.AsObject();
        doc.Remove("Version");
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void OlderVersions_InSlots_ShowTooOldAndStayUntouched(int version)
    {
        var world = World(42, DemoWeeklyTempConfig.FrontierSample, 40);
        var old = JsonNode.Parse(world.ExportTempSave())!;
        old["Version"] = version;
        Directory.CreateDirectory(_directory);
        var path = DemoSaveSlots.PathFor(1, _directory);
        File.WriteAllText(path, old.ToJsonString(), new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(path);
        var before = world.ExportTempSave();

        Assert.Contains("版本太舊，請建立新世界", DemoSaveSlots.ReadInfo(1, _directory).Error);
        var ex = Assert.Throws<InvalidDataException>(() => DemoSaveSlots.Load(1, _directory));
        Assert.Contains("版本太舊，請建立新世界", ex.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path)); // 原檔不改不刪
        Assert.Equal(before, world.ExportTempSave()); // 目前世界不動
    }

    [Fact]
    public void ScenarioNames_CoverAllFourPresets()
    {
        Assert.Equal("一般示範", DemoObserverWorld.TempScenarioName(DemoWeeklyTempConfig.Sample));
        Assert.Equal("全面饑荒", DemoObserverWorld.TempScenarioName(DemoWeeklyTempConfig.FamineSample));
        Assert.Equal("局部饑荒", DemoObserverWorld.TempScenarioName(DemoWeeklyTempConfig.LocalFamineSample));
        Assert.Equal("開拓", DemoObserverWorld.TempScenarioName(DemoWeeklyTempConfig.FrontierSample));
        Assert.Null(DemoObserverWorld.TempScenarioName(DemoWeeklyTempConfig.Sample with { FoodPerPersonPerWeek = 1 }));
    }
}
