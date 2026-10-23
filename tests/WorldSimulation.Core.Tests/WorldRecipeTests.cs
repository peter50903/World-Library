using System.Text.Json.Nodes;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 02 章 a 節：世界配方（世界種子＋歷史種子＋規則版本＋設定雜湊＋程式版本）。</summary>
public sealed class WorldRecipeTests
{
    private static DemoObserverWorld Run(WorldRecipe recipe, DemoWeeklyTempConfig weekly, int days)
    {
        var world = new DemoObserverWorld(recipe, weekly);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void SameRecipe_GivesTheSameWorld_AndStaysTheSameAfterTwoYears()
    {
        var a = new DemoObserverWorld(WorldRecipe.ForSeeds(123, 456), DemoWeeklyTempConfig.Sample);
        var b = new DemoObserverWorld(WorldRecipe.ForSeeds(123, 456), DemoWeeklyTempConfig.Sample);
        Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        for (var i = 0; i < 730; i++)
        {
            a.AdvanceOneDay();
            b.AdvanceOneDay();
        }
        Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
    }

    [Fact]
    public void DifferentWorldSeeds_GiveDifferentMapsOrSettlements()
    {
        var worlds = Enumerable.Range(1, 10)
            .Select(seed => new DemoObserverWorld(WorldRecipe.ForSeeds((ulong)seed), DemoWeeklyTempConfig.Sample))
            .ToArray();
        var distinct = worlds
            .Select(w => string.Join(";", w.Map.Cells.Select(c => c.Terrain)) + "|" + string.Join(";", w.Sites.Settlements))
            .Distinct()
            .Count();
        Assert.True(distinct > 1);
    }

    [Theory]
    [InlineData(DemoWeeklyTempConfigName.Sample)]
    [InlineData(DemoWeeklyTempConfigName.LivingForaging)]
    public void OnlyTheHistorySeedChanged_KeepsMapSitesAndWorldStreams_ButChangesHistoryStreams(DemoWeeklyTempConfigName name)
    {
        var weekly = Config(name);
        var a = new DemoObserverWorld(WorldRecipe.ForSeeds(123, 456), weekly);
        var b = new DemoObserverWorld(WorldRecipe.ForSeeds(123, 789), weekly);

        Assert.Equal(a.Map.Cells.ToArray(), b.Map.Cells.ToArray());
        Assert.Equal(a.Sites.Settlements.ToArray(), b.Sites.Settlements.ToArray());
        Assert.Equal(a.Sites.ResourcePoints.ToArray(), b.Sites.ResourcePoints.ToArray());
        Assert.Equal(a.Sites.PopGroups.ToArray(), b.Sites.PopGroups.ToArray());

        var ra = a.RngState().ToDictionary(s => s.StreamId);
        var rb = b.RngState().ToDictionary(s => s.StreamId);
        foreach (var id in RngStreams.WorldStreams)
            Assert.Equal(ra[id], rb[id]);
        foreach (var id in RngStreams.HistoryStreams)
            Assert.NotEqual(ra[id].State, rb[id].State);
        Assert.NotEqual(a.StateFingerprint(), b.StateFingerprint());
    }

    [Fact]
    public void BlankHistorySeed_IsDerived_AndMatchesTheLegacyScenarioConstructor()
    {
        var recipe = WorldRecipe.ForSeeds(42);
        Assert.True(recipe.HistorySeedDerived);
        Assert.Null(recipe.HistorySeedInput);
        Assert.Contains("由世界種子衍生", recipe.Describe(), StringComparison.Ordinal);

        var viaRecipe = new DemoObserverWorld(recipe, DemoWeeklyTempConfig.Sample);
        var viaScenario = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        Assert.Equal(viaScenario.StateFingerprint(), viaRecipe.StateFingerprint());
        Assert.Equal(viaScenario.ExportTempSave(), viaRecipe.ExportTempSave());
        Assert.Equal(recipe, viaScenario.Recipe);
    }

    [Fact]
    public void ExplicitHistorySeedEqualToTheDerivedOne_IsStillRecordedAsCustom()
    {
        var derived = WorldRecipe.ForSeeds(42).HistorySeed;
        var custom = WorldRecipe.ForSeeds(42, derived);
        Assert.False(custom.HistorySeedDerived);
        Assert.Equal(derived, custom.HistorySeed);
        Assert.Contains("（自訂）", custom.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_MarksRulesVersionAsTemp_ShowsTheRealConfigHash_AndNeverInventsACodeVersion()
    {
        var recipe = WorldRecipe.ForSeeds(5, 6);
        var line = recipe.Describe();
        var hash = WorldSimulation.Core.Config.OfficialConfig.Current.ConfigHash;
        Assert.StartsWith("世界配方｜世界種子 5｜歷史種子 6（自訂）", line, StringComparison.Ordinal);
        Assert.Contains("規則版本 【TEMP】", line, StringComparison.Ordinal);
        Assert.Equal(hash, recipe.ConfigHash);
        Assert.Contains($"設定雜湊 {hash[..16]}", line, StringComparison.Ordinal);
        Assert.False(recipe.ConfigHashIsTemp);
        var code = WorldRecipe.CurrentCodeVersion;
        Assert.True(code == WorldRecipe.UnknownCodeVersion || (code.Length >= 7 && code.All(Uri.IsHexDigit)), code);
        Assert.Contains($"設定雜湊：{hash[..16]}", recipe.DescribeLines(), StringComparison.Ordinal);
        // 舊（第 02 章 a 節）存檔裡的佔位字串仍可辨識為未計算，不會被當成正式雜湊。
        Assert.True((recipe with { ConfigHash = "TEMP-CONFIG-HASH-PENDING" }).ConfigHashIsTemp);
    }

    [Fact]
    public void CustomHistorySeedWorld_SaveLoadResume_MatchesTheUninterruptedRun()
    {
        var recipe = WorldRecipe.ForSeeds(123, 456);
        var saved = Run(recipe, DemoWeeklyTempConfig.LivingForagingSample, 30);
        var restored = DemoObserverWorld.ImportTempSave(saved.ExportTempSave());
        Assert.Equal(recipe, restored.Recipe);

        var straight = Run(recipe, DemoWeeklyTempConfig.LivingForagingSample, 60);
        for (var i = 0; i < 30; i++)
            restored.AdvanceOneDay();
        Assert.Equal(straight.ExportTempSave(), restored.ExportTempSave());
        Assert.Equal(straight.SettlementEvents(), restored.SettlementEvents());
    }

    [Theory]
    [InlineData("HistorySeedInput", "999")]
    [InlineData("HistorySeedInput", "null")]
    [InlineData("RulesVersion", "\"TEMP-RULES-99\"")]
    [InlineData("WorldSeed", "124")]
    [InlineData("RulesVersion", "\"\"")]
    public void TamperedRecipe_IsRejected_AndTheRunningWorldIsUntouched(string field, string valueJson)
    {
        var live = Run(WorldRecipe.ForSeeds(123, 456), DemoWeeklyTempConfig.Sample, 14);
        var original = live.ExportTempSave();
        var doc = JsonNode.Parse(original)!;
        doc["Manifest"]!["Recipe"]![field] = JsonNode.Parse(valueJson);
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Equal(original, live.ExportTempSave());
    }

    [Fact]
    public void RulesVersionMismatch_SaysSo()
    {
        var doc = JsonNode.Parse(Run(WorldRecipe.ForSeeds(1), DemoWeeklyTempConfig.Sample, 7).ExportTempSave())!;
        doc["Manifest"]!["RulesVersion"] = "TEMP-RULES-99";
        doc["Manifest"]!["Recipe"]!["RulesVersion"] = "TEMP-RULES-99";
        var ex = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Contains("規則版本不符（存檔 TEMP-RULES-99，目前 TEMP-RULES-0）", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeVersionMismatch_OnlyWarns_SoAnotherBuildCanStillLoad()
    {
        // 第 04 章 b 節：程式版本不同只警告、不拒讀；設定雜湊不同則拒讀（見 DemoSaveManifestTests）。
        var world = Run(WorldRecipe.ForSeeds(9, 10), DemoWeeklyTempConfig.Sample, 9);
        var doc = JsonNode.Parse(world.ExportTempSave())!;
        doc["Manifest"]!["CodeVersion"] = "0123456789abcdef0123456789abcdef01234567";
        doc["Manifest"]!["Recipe"]!["CodeVersion"] = "0123456789abcdef0123456789abcdef01234567";
        var other = doc.ToJsonString();
        var restored = DemoObserverWorld.ImportTempSave(other);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", restored.Recipe.CodeVersion);
        Assert.Contains("程式版本不同", restored.LoadWarning, StringComparison.Ordinal);
        Assert.Null(DemoObserverWorld.ImportTempSave(world.ExportTempSave()).LoadWarning);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(other), JsonNode.Parse(restored.ExportTempSave())));
    }

    [Fact]
    public void SaveSlotHeader_ShowsACustomHistorySeed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wsim-recipe-" + Guid.NewGuid().ToString("N"));
        try
        {
            DemoSaveSlots.Save(Run(WorldRecipe.ForSeeds(7, 8), DemoWeeklyTempConfig.Sample, 3), 1, directory);
            DemoSaveSlots.Save(Run(WorldRecipe.ForSeeds(7), DemoWeeklyTempConfig.Sample, 3), 2, directory);
            var slots = DemoSaveSlots.ListAll(directory);
            var config = WorldRecipe.CurrentConfigHash[..8];
            var derived = WorldRecipe.ForSeeds(7).HistorySeed;
            Assert.Equal($"槽 1：第 3 日｜世界種子 7｜歷史種子 8｜一般示範｜設定 {config}｜存檔第 27 版｜可讀", slots[0].Describe());
            Assert.Equal($"槽 2：第 3 日｜世界種子 7｜歷史種子 {derived}（衍生）｜一般示範｜設定 {config}｜存檔第 27 版｜可讀", slots[1].Describe());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    public enum DemoWeeklyTempConfigName { Sample, LivingForaging }

    private static DemoWeeklyTempConfig Config(DemoWeeklyTempConfigName name) => name switch
    {
        DemoWeeklyTempConfigName.LivingForaging => DemoWeeklyTempConfig.LivingForagingSample,
        _ => DemoWeeklyTempConfig.Sample
    };
}
