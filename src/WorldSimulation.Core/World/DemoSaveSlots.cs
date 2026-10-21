using System.Globalization;
using System.Text.Json;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// V0-07C：一個 TEMP 存檔槽的檔頭資訊（不是正式存檔槽規格）。
/// 只讀檔頭（版本、世界情境、生存情境、世界日期），不驗證整份存檔；
/// 真正讀檔仍由 <see cref="DemoObserverWorld.LoadTempFile"/> 整份驗證。
/// </summary>
public sealed record DemoSaveSlotInfo(
    int Slot,
    string Path,
    bool Exists,
    int? Version = null,
    ulong? Seed = null,
    int? DaysElapsed = null,
    string? ScenarioName = null,
    string? Error = null,
    ulong? HistorySeedInput = null,
    WorldRecipe? Recipe = null,
    bool Compatible = false,
    string? IncompatibleReason = null,
    string? Warning = null)
{
    /// <summary>
    /// 給觀察者畫面與文字介面的一行繁體中文說明（第 04 章 b 節）：
    /// 「槽 N：第 D 日｜世界種子 W｜歷史種子 H｜情境｜設定 前8碼｜存檔第 V 版｜可讀」或「…｜不相容（原因）」；
    /// 檔頭本身讀不出來時「槽 N：無法辨識（原因）」。
    /// </summary>
    public string Describe()
    {
        if (!Exists)
            return $"槽 {Slot}：（空）";
        if (Error is not null || Recipe is null)
            return $"槽 {Slot}：無法辨識（{Error ?? "檔頭缺少世界資訊"}）";
        var history = Recipe.HistorySeedDerived
            ? $"歷史種子 {Recipe.HistorySeed}（衍生）"
            : $"歷史種子 {Recipe.HistorySeed}";
        var config = Recipe.ConfigHash.Length > 8 ? Recipe.ConfigHash[..8] : Recipe.ConfigHash;
        return string.Create(CultureInfo.InvariantCulture,
            $"槽 {Slot}：第 {DaysElapsed} 日｜世界種子 {Seed}｜{history}｜{ScenarioName}｜設定 {config}｜存檔第 {Version} 版｜{(Compatible ? "可讀" : $"不相容（{IncompatibleReason}）")}");
    }
}

/// <summary>
/// V0-07C TEMP 多存檔槽：槽數（3）與資料夾都是 TEMP，不是正式設定。
/// 每槽一個檔案 <c>TEMP_slot_N.json</c>；寫入沿用 <see cref="DemoObserverWorld.SaveTempFile"/> 的
/// 暫存檔＋取代，讀取沿用整份驗證，失敗時執行中的世界不變。
/// </summary>
public static class DemoSaveSlots
{
    /// <summary>TEMP 存檔槽數量（未定案）。</summary>
    public const int TempSlotCount = 3;

    /// <summary>可用環境變數 WORLD_SIM_TEMP_SAVE_DIR 改到別的資料夾（CI 與測試用）。</summary>
    public static string DefaultDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("WORLD_SIM_TEMP_SAVE_DIR");
            return string.IsNullOrWhiteSpace(overridden)
                ? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WorldSimulation", "TEMP_slots")
                : overridden;
        }
    }

    /// <summary>槽號 1～<see cref="TempSlotCount"/>；其他值丟出 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static string PathFor(int slot, string? directory = null)
    {
        CheckSlot(slot);
        return System.IO.Path.Combine(directory ?? DefaultDirectory,
            string.Create(CultureInfo.InvariantCulture, $"TEMP_slot_{slot}.json"));
    }

    public static void Save(DemoObserverWorld world, int slot, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.SaveTempFile(PathFor(slot, directory));
    }

    /// <summary>整份驗證後回傳新世界；任何錯誤都丟出例外，呼叫端原本的世界不受影響。</summary>
    public static DemoObserverWorld Load(int slot, string? directory = null)
    {
        var path = PathFor(slot, directory);
        if (!File.Exists(path))
            throw new FileNotFoundException($"存檔槽 {slot} 是空的。", path);
        return DemoObserverWorld.LoadTempFile(path);
    }

    /// <summary>第 04 章 b 節：選用完整驗證（從第 0 日重跑逐週比對）；空槽回傳不一致並說明。不改世界、不寫檔。</summary>
    public static DemoReplayVerification Verify(int slot, string? directory = null)
    {
        var path = PathFor(slot, directory);
        if (!File.Exists(path))
            return new DemoReplayVerification(false, null, $"存檔槽 {slot} 是空的", TimeSpan.Zero);
        if (new FileInfo(path).Length > DemoObserverWorld.TempMaxSaveFileBytes)
            return new DemoReplayVerification(false, null, "檔案超過大小限制", TimeSpan.Zero);
        return DemoObserverWorld.VerifyTempSaveByReplay(File.ReadAllText(path, System.Text.Encoding.UTF8));
    }

    public static IReadOnlyList<DemoSaveSlotInfo> ListAll(string? directory = null)
        => Enumerable.Range(1, TempSlotCount).Select(slot => ReadInfo(slot, directory)).ToArray();

    /// <summary>只讀檔頭；遇到 State 欄位就停止，不解析世界狀態。檔頭壞掉回傳 Error，不丟例外。</summary>
    public static DemoSaveSlotInfo ReadInfo(int slot, string? directory = null)
    {
        var path = PathFor(slot, directory);
        if (!File.Exists(path))
            return new DemoSaveSlotInfo(slot, path, Exists: false);
        try
        {
            if (new FileInfo(path).Length > DemoObserverWorld.TempMaxSaveFileBytes)
                return new DemoSaveSlotInfo(slot, path, true, Error: "檔案超過大小限制");
            return ReadHeader(slot, path, File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DemoSaveSlotInfo(slot, path, true, Error: "無法讀取檔案");
        }
    }

    /// <summary>
    /// 第 04 章 b 節：讀檔頭並做標頭檢查①～⑧（不解析世界狀態，只取 State 原文算校驗碼）。
    /// 檔頭讀不出來回傳 Error；讀得出來但②～⑧不符回傳 Compatible＝false 與原因。不丟例外。
    /// </summary>
    internal static DemoSaveSlotInfo ReadHeader(int slot, string path, byte[] bytes)
    {
        int? version = null;
        DemoSaveManifest? manifest = null;
        DemoWeeklyTempConfig? weekly = null;
        byte[]? state = null;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return Bad("不是 TEMP 存檔");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                    return Bad("檔頭不完整");
                switch (name)
                {
                    case "Version":
                        version = reader.GetInt32();
                        break;
                    case "Manifest":
                        manifest = JsonSerializer.Deserialize<DemoSaveManifest>(ref reader);
                        break;
                    case "WeeklyConfig":
                        weekly = JsonSerializer.Deserialize<DemoWeeklyTempConfig>(ref reader);
                        break;
                    case "State":
                        // 只截取原文算校驗碼；檔案不完整時 state 維持 null（⑧ 判為損毀，檔頭資訊仍可顯示）。
                        var start = (int)reader.TokenStartIndex;
                        try
                        {
                            reader.Skip();
                            state = bytes[start..(int)reader.BytesConsumed];
                        }
                        catch (JsonException)
                        {
                            state = null;
                            goto done;
                        }
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException
            or NotSupportedException or ArgumentException)
        {
            // 版本讀得到就繼續往下判斷（缺標頭→「檔頭缺少世界資訊」）；連版本都讀不到才是檔頭損毀。
            if (version is null)
                return Bad("檔頭損毀");
        }
        done:

        // ① 版本。
        if (version is not null && DemoObserverWorld.IsOutdatedTempSaveVersion(version.Value))
            return Bad(DemoObserverWorld.OutdatedTempSaveMessage(version.Value));
        if (version is null || !DemoObserverWorld.IsSupportedTempSaveVersion(version.Value))
            return Bad("存檔版本不受支援");
        if (manifest?.Recipe is null || weekly is null)
            return Bad("檔頭缺少世界資訊");
        try
        {
            manifest.Recipe.Validate();
        }
        catch (InvalidDataException)
        {
            return Bad("世界配方損毀");
        }
        var scenarioName = DemoObserverWorld.TempScenarioName(weekly);
        if (scenarioName is null)
            return Bad("不是已知的示範情境");

        var recipe = manifest.Recipe;
        var days = manifest.WorldTimeTicks >= 0 && manifest.WorldTimeTicks % TimeSpan.TicksPerDay == 0
            ? (int?)checked((int)Math.Min(manifest.WorldTimeTicks / TimeSpan.TicksPerDay, int.MaxValue))
            : null;
        var info = new DemoSaveSlotInfo(slot, path, true, version, recipe.WorldSeed, days, scenarioName,
            HistorySeedInput: recipe.HistorySeedInput, Recipe: recipe);
        try
        {
            var (_, warning) = DemoSaveHeader.Check(version.Value, manifest, state);
            return info with { Compatible = true, Warning = warning };
        }
        catch (InvalidDataException ex)
        {
            return info with { IncompatibleReason = ex.Message.Replace("，已拒絕載入。", "", StringComparison.Ordinal).TrimEnd('。') };
        }

        DemoSaveSlotInfo Bad(string reason) => new(slot, path, true, Error: reason);
    }

    private static void CheckSlot(int slot)
    {
        if (slot is < 1 or > TempSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot,
                $"存檔槽必須是 1～{TempSlotCount}。");
    }
}
