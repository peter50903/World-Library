using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// V0-07 TEMP save (not a production format). Version 5 (第 02 章 a 節, written now) is restored directly:
/// see DemoObserverWorld.Snapshot.cs. Version 3 (C04) added the world entity-ID allocator (44H SAVE_ID);
/// version 4 (C05) retires the V0-01 daily food fields and adds per-week RNG stream counters;
/// version 5 stores the full world recipe (world/history seed, rules version, config hash, code version);
/// version 6 (第 03 章 b 節) adds State.History, the append-only formal event history (rebuilt and compared on load).
/// Every older version is rejected with ONE message, "版本太舊，請建立新世界" (user ruling 2026-10-14):
/// no converter, the file is never modified or deleted, and the running world is untouched.
/// The stored scenario and weekly config must also be one of the known TEMP presets
/// (<see cref="KnownTempWeeklyConfigs"/>); otherwise a hand-edited file with self-consistent
/// but invented numbers would replay "successfully" and be accepted.
/// </summary>
public sealed partial class DemoObserverWorld
{
    /// <summary>The actual TEMP seed of this world; may differ after loading another save.</summary>
    public ulong TempWorldSeed => _recipe.WorldSeed;

    internal const int TempSaveVersion = 6; // C04 第 3 版加入實體 ID 分配器；C05 第 4 版退役每日糧食線、加入週亂數計數器；第 02 章 a 節第 5 版以世界配方取代 Scenario；第 03 章 b 節第 6 版加入正式史庫。比目前舊的一律「版本太舊」。
    internal const int TempMaxSaveDays = 100 * 365; // TEMP（#90）：100 個正式 365 日年＝36500 日（01 表 WORLD_DAYS_PER_YEAR），避免讀檔無上限重播。
    private const int TempMaxSaveBytes = 64 * 1024 * 1024;
    internal const long TempMaxSaveFileBytes = TempMaxSaveBytes;

    /// <summary>C04：可讀的存檔版本只有目前版本；其他一律拒絕。</summary>
    internal static bool IsSupportedTempSaveVersion(int version) => version == TempSaveVersion;

    /// <summary>C04：比目前版本舊的存檔（第 1 版起）；讀檔與存檔槽都顯示同一句「版本太舊，請建立新世界」。</summary>
    internal static bool IsOutdatedTempSaveVersion(int version) => version >= 1 && version < TempSaveVersion;

    /// <summary>C04：舊版存檔的唯一拒讀訊息（Godot 通知列、存檔槽清單、CLI 共用）。</summary>
    internal static string OutdatedTempSaveMessage(int version)
        => $"版本太舊，請建立新世界（存檔第 {version} 版，目前第 {TempSaveVersion} 版）";

    /// <summary>V0-07C：已知 TEMP 生存情境的中文名稱；不是已知情境回傳 null。</summary>
    public static string? TempScenarioName(DemoWeeklyTempConfig weekly)
    {
        if (weekly == DemoWeeklyTempConfig.LivingForagingSample) return "人類繁衍與自然死亡";
        if (weekly == DemoWeeklyTempConfig.ForagingSample) return "森林河流生存";
        if (weekly == DemoWeeklyTempConfig.CrowdedForagingSample) return "森林周邊人口過密";
        if (weekly == DemoWeeklyTempConfig.Sample) return "一般示範";
        if (weekly == DemoWeeklyTempConfig.FamineSample) return "全面饑荒";
        if (weekly == DemoWeeklyTempConfig.LocalFamineSample) return "局部饑荒";
        if (weekly == DemoWeeklyTempConfig.FrontierSample) return "開拓";
        return null;
    }

    /// <summary>目前世界的 TEMP 情境名稱（給存檔槽與畫面顯示）。</summary>
    public string TempScenarioDisplayName => TempScenarioName(_weekly) ?? "自訂 TEMP 情境";

    /// <summary>TEMP presets a save may name; anything else is rejected before replay.</summary>
    private static readonly DemoWeeklyTempConfig[] KnownTempWeeklyConfigs =
    {
        DemoWeeklyTempConfig.Sample,
        DemoWeeklyTempConfig.ForagingSample,
        DemoWeeklyTempConfig.LivingForagingSample,
        DemoWeeklyTempConfig.CrowdedForagingSample,
        DemoWeeklyTempConfig.FamineSample,
        DemoWeeklyTempConfig.LocalFamineSample,
        DemoWeeklyTempConfig.FrontierSample
    };

    /// <summary>Shared TEMP save path for the Godot and text observers, not an official save slot.</summary>
    public static string DefaultTempSavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorldSimulation", "TEMP_observer_world.json");

    /// <summary>Deterministic, complete TEMP JSON checkpoint (not a formal save protocol).</summary>
    public string ExportTempSave()
    {
        var state = new
        {
            WorldTimeTicks = _clock.Now.Ticks,
            SchedulerNextSequence = _scheduler.NextSequence,
            ScheduledWork = _scheduler.PendingInOrder().ToArray(),
            Random = _rng.Capture(),
            WeekReports = _weeks.ToArray(),
            Reserves = _reserves,
            SettlementFood = _settlementFood,
            Population = _population,
            HungerDebt = _hungerDebt,
            MigrationBatches = _migrations.ToArray(),
            EntityIds = _entityIds,
            Settlements = _sites.Settlements,
            ResourcePoints = _sites.ResourcePoints,
            PopulationGroups = _sites.PopGroups,
            WorldFingerprint = StateFingerprint(),
            Demography = _demography?.Snapshot(),
            History = _history.Events.ToArray()
        };
        return JsonSerializer.Serialize(new
        {
            Version = TempSaveVersion,
            Recipe = _recipe,
            WeeklyConfig = _weekly,
            DaysElapsed = DaysOf(_clock.Now),
            State = state
        });
    }

    /// <summary>
    /// Builds a new fully verified world, leaving the caller's existing world untouched on error.
    /// Version 2 restores directly after verification; version 1 replays from the saved seed,
    /// comparing every saved state component (schedules, RNG, ledger, new settlements) before accepting.
    /// </summary>
    public static DemoObserverWorld ImportTempSave(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > TempMaxSaveBytes)
            throw new InvalidDataException("TEMP 存檔為空白或超過大小限制。");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Version", out var versionElement))
                throw new InvalidDataException("TEMP 存檔版本不受支援。");
            if (versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
                throw new InvalidDataException("TEMP 存檔版本不受支援。");
            if (IsOutdatedTempSaveVersion(version))
                throw new InvalidDataException(OutdatedTempSaveMessage(version));
            if (!IsSupportedTempSaveVersion(version))
                throw new InvalidDataException("TEMP 存檔版本不受支援。");

            var recipe = root.GetProperty("Recipe").Deserialize<WorldRecipe>()
                ?? throw new InvalidDataException("TEMP 存檔缺少世界配方。");
            recipe.Validate();
            // 規則版本不符：存檔是在不同規則下產生的，拒讀。程式版本與設定雜湊只記錄、不比對（第 04 章 b 節）。
            if (recipe.RulesVersion != WorldRecipe.TempRulesVersion)
                throw new InvalidDataException(
                    $"規則版本不符（存檔 {recipe.RulesVersion}，目前 {WorldRecipe.TempRulesVersion}），已拒絕載入。");
            var weekly = root.GetProperty("WeeklyConfig").Deserialize<DemoWeeklyTempConfig>()
                ?? throw new InvalidDataException("TEMP 存檔缺少生存情境。");
            // Record value equality: every field must equal a known TEMP preset exactly.
            if (!KnownTempWeeklyConfigs.Contains(weekly))
                throw new InvalidDataException("TEMP 存檔的生存情境不是已知的示範情境，已拒絕載入。");
            var days = root.GetProperty("DaysElapsed").GetInt32();
            if (days < 0 || days > TempMaxSaveDays)
                throw new InvalidDataException("TEMP 存檔世界日期超出可重播範圍。");

            // Current version restores directly (seed-determined parts re-derived, weekly state verified).
            return ImportDirectSnapshot(json, root, recipe, weekly, days);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException
            or OverflowException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            throw new InvalidDataException("TEMP 存檔損毀、格式不符或無法重播驗證。", ex);
        }
    }

    /// <summary>Atomic same-directory replacement so a failed write does not destroy a prior save.</summary>
    public void SaveTempFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, ExportTempSave(), new UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Read and verify first; errors do not mutate a running world.</summary>
    public static DemoObserverWorld LoadTempFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (info.Length > TempMaxSaveBytes)
            throw new InvalidDataException("TEMP 存檔超過大小限制。");
        return ImportTempSave(File.ReadAllText(path, Encoding.UTF8));
    }
}
