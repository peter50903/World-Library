using Xunit;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>V0-07: TEMP replay save must restore the *entire* deterministic world.</summary>
public sealed class DemoReplaySaveTests
{
    [Theory]
    [InlineData(42UL, false, 5, 30)]
    [InlineData(7UL, false, 14, 60)]
    [InlineData(42UL, true, 14, 84)]
    [InlineData(42UL, true, 35, 84)]
    public void SaveRestoreAndContinue_MatchesUninterruptedWorld(
        ulong seed, bool frontier, int checkpointDay, int lastDay)
    {
        var config = frontier ? DemoWeeklyTempConfig.FrontierSample : DemoWeeklyTempConfig.Sample;
        var direct = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        var saving = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < checkpointDay; i++)
        {
            direct.AdvanceOneDay();
            saving.AdvanceOneDay();
        }

        var json = saving.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(json, loaded.ExportTempSave());
        Assert.Equal(json, DemoObserverWorld.ImportTempSave(json).ExportTempSave());

        for (var i = checkpointDay; i < lastDay; i++)
        {
            direct.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }

        Assert.Equal(direct.ExportTempSave(), loaded.ExportTempSave());
        Assert.Equal(direct.WorldPopulation, loaded.WorldPopulation);
        Assert.Equal(direct.Sites.Settlements.Count, loaded.Sites.Settlements.Count);
        Assert.Equal(direct.MigrationsInTransit().Count, loaded.MigrationsInTransit().Count);
        Assert.Equal(direct.WeekReports().Count, loaded.WeekReports().Count);
    }

    [Fact]
    public void BadVersionSeedWorldOrCounter_IsRejectedWithoutMutatingOriginalWorld()
    {
        var live = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.FrontierSample);
        for (var i = 0; i < 35; i++)
            live.AdvanceOneDay();
        var original = live.ExportTempSave();

        foreach (var mutate in new Action<JsonNode>[]
        {
            doc => doc["Version"] = 99,
            doc => doc["Scenario"]!["Seed"] = 99UL,
            doc => doc["State"]!["WeekReports"]![0]!["RngCounters"]![1]!["Before"] = 5UL,
            doc => doc["State"]!["Random"]!["Streams"]![0]!["DrawCounter"] = ulong.MaxValue,
            doc => doc["State"]!["EntityIds"]!["NextValue"] = 1UL,
            doc => doc["State"]!["Settlements"]![0]!["Id"] = 88
        })
        {
            var bad = JsonNode.Parse(original)!;
            mutate(bad);
            Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(bad.ToJsonString()));
            Assert.Equal(original, live.ExportTempSave());
        }

        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(original[..(original.Length / 2)]));
        Assert.Equal(original, live.ExportTempSave());
    }

    [Fact]
    public void SelfConsistentSaveWithUnknownTempNumbers_IsRejected()
    {
        // A file whose replay matches itself is still rejected when its scenario or weekly
        // config is not one of the known TEMP presets (no invented world numbers on load).
        var inventedWeekly = DemoWeeklyTempConfig.Sample with { YieldPerWorkerPerWeek = 50 };
        var forgedWeekly = new DemoObserverWorld(DemoScenario.Sample(42), inventedWeekly);
        for (var i = 0; i < 9; i++)
            forgedWeekly.AdvanceOneDay();

        var weeklyError = Assert.Throws<InvalidDataException>(
            () => DemoObserverWorld.ImportTempSave(forgedWeekly.ExportTempSave()));
        Assert.Contains("生存情境", weeklyError.Message, StringComparison.Ordinal);

        // C05：DemoScenario 只剩種子；舊每日帳欄位（例如 Population）出現在檔案裡就與重新匯出不同 → 拒絕。
        var plain = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        for (var i = 0; i < 9; i++)
            plain.AdvanceOneDay();
        var doc = JsonNode.Parse(plain.ExportTempSave())!;
        doc["Scenario"]!["Population"] = 6;
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Theory]
    [InlineData("Sample")]
    [InlineData("Famine")]
    [InlineData("LocalFamine")]
    [InlineData("Frontier")]
    public void EveryKnownTempPreset_StillRoundTrips(string preset)
    {
        var config = preset switch
        {
            "Famine" => DemoWeeklyTempConfig.FamineSample,
            "LocalFamine" => DemoWeeklyTempConfig.LocalFamineSample,
            "Frontier" => DemoWeeklyTempConfig.FrontierSample,
            _ => DemoWeeklyTempConfig.Sample
        };
        var world = new DemoObserverWorld(DemoScenario.Sample(7), config);
        for (var i = 0; i < 15; i++)
            world.AdvanceOneDay();
        var json = world.ExportTempSave();
        Assert.Equal(json, DemoObserverWorld.ImportTempSave(json).ExportTempSave());
    }

    [Fact]
    public void FileSaveAndLoad_DoesNotCorruptPreviousSaveOnFailedRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "worldsim-save-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filename = Path.Combine(directory, "world.temp.json");
        try
        {
            var live = new DemoObserverWorld(DemoScenario.Sample(42));
            for (var i = 0; i < 9; i++)
                live.AdvanceOneDay();
            live.SaveTempFile(filename);
            var original = File.ReadAllText(filename);
            live.AdvanceOneDay();
            Assert.Equal(original, DemoObserverWorld.LoadTempFile(filename).ExportTempSave());
            Assert.NotEqual(original, live.ExportTempSave());

            File.WriteAllText(filename, "{\"Version\":0}");
            Assert.Throws<InvalidDataException>(() => DemoObserverWorld.LoadTempFile(filename));
            Assert.NotEqual(original, live.ExportTempSave());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
