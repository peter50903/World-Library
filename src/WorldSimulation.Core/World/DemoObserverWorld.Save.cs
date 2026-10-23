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
/// version 6 (第 03 章 b 節) adds State.History, the append-only formal event history (rebuilt and compared on load);
/// version 7 (第 04 章 a 節) draws births/deaths from the history seed's POP stream and drops the demography seed;
/// version 8 (第 04 章 b 節) keeps only Version, Manifest (<see cref="DemoSaveManifest"/>), WeeklyConfig and State at the top level
/// and checks the header (structure/rules/config/TEMP config/code version, world time, State checksum) before anything else;
/// version 9 (第 04 章 c 節) adds per-source weekly ledgers for shared foraging (DemoWeekReport.Sources);
/// version 10 (第 06 章 c 節) adds persistent source stocks (State.SourceStocks) and stock fields in the source ledger;
/// version 11 (第 06 章 d 節) adds spoilage, water shortage, water carriers and child/elder starvation to the settlement ledger;
/// version 12 (第 07 章 a 節) adds parents and stable partners to each person and a mixed-age opening cohort;
/// version 13 (第 07 章 b 節) adds each traveller's batch and travel births/natural deaths to the migration ledger;
/// version 14 (第 07 章 c 節) adds the ruin week to settlements;
/// version 15 (第 08 章 a 節) retires automatic grassland farmland and adds land development state.
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

    internal const int TempSaveVersion = 24; // C04 第 3 版加入實體 ID 分配器；C05 第 4 版退役每日糧食線、加入週亂數計數器；第 02 章 a 節第 5 版以世界配方取代 Scenario；第 03 章 b 節第 6 版加入正式史庫；第 04 章 a 節第 7 版人口改走 POP 流、名冊快照刪除自製種子；第 04 章 b 節第 8 版加標頭（Manifest）與校驗碼；第 04 章 c 節第 9 版週報加共享採集來源帳；第 06 章 c 節第 10 版加來源存量；第 06 章 d 節第 11 版週帳加腐敗、缺水、取水者與老幼餓死；第 07 章 a 節第 12 版名冊加父母與伴侶、混齡開局；第 07 章 b 節第 13 版名冊加所屬隊伍、隊伍帳加途中出生與自然死亡；第 07 章 c 節第 14 版聚落加遺跡週次；第 08 章 a 節第 15 版舊自動農田退場、加土地開發狀態；第 08-1 章第 16 版人口規則修正（宜居開局、開拓需伴侶、擇偶依年齡）；第 08-2 章第 17 版希望時代（不餓死、只有老死、開局存糧與分散）；第 06-1 章第 18 版海洋與河流分開、淺灘渡口；第 07-1 章第 19 版群體人口（不再存個體名冊）、第 20 版代表人物與命名；第 04-1 章第 21 版分層存檔（只存目前狀態＋近期週報，之前的只存年帳、事件與聚落小史）；第 04-2 章第 22 版出生與老死每村每週合計一筆、正式史庫不另存；第 08-5 章第 23 版代表人物只剩村長（採集、漁撈代表取消）；第 08-6 章第 24 版知識樹與時代。比目前舊的一律「版本太舊」。
    internal const int TempMaxSaveDays = 100 * 365; // TEMP（#90）：100 個正式 365 日年＝36500 日（01 表 WORLD_DAYS_PER_YEAR），避免讀檔無上限重播。
    private const int TempMaxSaveBytes = 256 * 1024 * 1024; // 【TEMP】第 04-1 章：大地圖百年的正式史庫仍大，史庫分層見第 13 章
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
        if (weekly == DemoWeeklyTempConfig.LivingForagingSample) return "希望時代：人類採集";
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
        // 第 04-1 章（使用者 2026-10-22：存檔只要能回到存檔當下的狀態，過程不需要逐週保存）：
        // 只存近期週報（SaveWindowStart 起）；之前的年份存年帳，之前的聚落事件另存，聚落小史隨聚落保存。
        var windowStart = SaveWindowStart();
        var state = new
        {
            WorldTimeTicks = _clock.Now.Ticks,
            SchedulerNextSequence = _scheduler.NextSequence,
            ScheduledWork = _scheduler.PendingInOrder().ToArray(),
            Random = _rng.Capture(),
            WeekBase = windowStart - 1,
            WeekReports = _weeks.Where(w => w.Week >= windowStart).ToArray(),
            EarlierYears = YearSummaries().Take(YearsBeforeWeek(windowStart)).ToArray(),
            EarlierEvents = SettlementEvents().Where(e => e.Week < windowStart).ToArray(),
            // 第 04-2 章：正式史庫不另存，由聚落事件經同一個轉換器重建；只有較早的途中餓死需要記下當時隊伍所在格。
            EarlierEventCells = EarlierEventCells(windowStart),
            Chronicle = _chronicle,
            DebtSources = _debtSources,
            Knowledge = _knowledge, // 第 08-6 章
            Reserves = _reserves,
            SettlementFood = _settlementFood,
            Population = _population,
            HungerDebt = _hungerDebt,
            SourceStocks = _sourceStocks, // 第 06 章 c 節
            Land = _land.Parcels, // 第 08 章 a 節：土地開發狀態
            MigrationBatches = _migrations.ToArray(),
            EntityIds = _entityIds,
            Settlements = _sites.Settlements,
            ResourcePoints = _sites.ResourcePoints,
            PopulationGroups = _sites.PopGroups,
            WorldFingerprint = DemoSaveHeader.Checksum(Encoding.UTF8.GetBytes(StateFingerprint())), // 第 07-1 章 c 節：只存雜湊（全文曾佔存檔三成以上）
            Demography = _demography?.Snapshot()
        };
        // 第 04 章 b 節：State 先單獨序列化（無縮排），校驗碼取這段 UTF-8 位元組；再原文嵌入整份檔案。
        var stateJson = JsonSerializer.Serialize(state);
        using var stateDocument = JsonDocument.Parse(stateJson);
        var manifest = new DemoSaveManifest(
            TempSaveVersion, _recipe.RulesVersion, _recipe.ConfigHash, DemoSaveHeader.CurrentTempConfigHash,
            _recipe.CodeVersion, _clock.Now.Ticks, _recipe, DemoSaveHeader.Checksum(Encoding.UTF8.GetBytes(stateJson)));
        return JsonSerializer.Serialize(new
        {
            Version = TempSaveVersion,
            Manifest = manifest,
            WeeklyConfig = _weekly,
            State = stateDocument.RootElement
        });
    }

    /// <summary>第 04 章 b 節：讀檔時程式版本與存檔不同的警告（不拒讀）；沒有就是 null。不屬於世界狀態。</summary>
    public string? LoadWarning { get; private set; }

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

            var (manifest, weekly, days, warning) = CheckHeader(root, version);

            // Current version restores directly (seed-determined parts re-derived, weekly state verified).
            var world = ImportDirectSnapshot(json, root, manifest.Recipe, weekly, days);
            world.LoadWarning = warning;
            return world;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException
            or OverflowException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            throw new InvalidDataException("TEMP 存檔損毀、格式不符或無法重播驗證。", ex);
        }
    }

    /// <summary>
    /// 第 04 章 b 節：①之後的標頭檢查（②～⑧，見 <see cref="DemoSaveHeader.Check"/>）＋已知 TEMP 生存情境。
    /// </summary>
    private static (DemoSaveManifest Manifest, DemoWeeklyTempConfig Weekly, int Days, string? Warning) CheckHeader(
        JsonElement root, int version)
    {
        var manifest = root.GetProperty("Manifest").Deserialize<DemoSaveManifest>()
            ?? throw new InvalidDataException("TEMP 存檔缺少標頭。");
        var state = root.GetProperty("State");
        var (days, warning) = DemoSaveHeader.Check(version, manifest, Encoding.UTF8.GetBytes(state.GetRawText()));
        var weekly = root.GetProperty("WeeklyConfig").Deserialize<DemoWeeklyTempConfig>()
            ?? throw new InvalidDataException("TEMP 存檔缺少生存情境。");
        // Record value equality: every field must equal a known TEMP preset exactly.
        if (!KnownTempWeeklyConfigs.Contains(weekly))
            throw new InvalidDataException("TEMP 存檔的生存情境不是已知的示範情境，已拒絕載入。");
        return (manifest, weekly, days, warning);
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
