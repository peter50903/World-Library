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
    string? Error = null)
{
    /// <summary>給觀察者畫面與文字介面的一行繁體中文說明。</summary>
    public string Describe()
    {
        if (!Exists)
            return $"槽 {Slot}：（空）";
        if (Error is not null)
            return $"槽 {Slot}：無法辨識（{Error}）";
        return string.Create(CultureInfo.InvariantCulture,
            $"槽 {Slot}：第 {DaysElapsed} 日｜種子 {Seed}｜{ScenarioName}｜存檔第 {Version} 版");
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

    internal static DemoSaveSlotInfo ReadHeader(int slot, string path, byte[] bytes)
    {
        int? version = null;
        int? days = null;
        DemoScenario? scenario = null;
        DemoWeeklyTempConfig? weekly = null;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return Bad("不是 TEMP 存檔");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (name == "State")
                    break; // 檔頭結束；不解析世界狀態。
                if (!reader.Read())
                    return Bad("檔頭不完整");
                switch (name)
                {
                    case "Version":
                        version = reader.GetInt32();
                        break;
                    case "DaysElapsed":
                        days = reader.GetInt32();
                        break;
                    case "Scenario":
                        scenario = JsonSerializer.Deserialize<DemoScenario>(ref reader);
                        break;
                    case "WeeklyConfig":
                        weekly = JsonSerializer.Deserialize<DemoWeeklyTempConfig>(ref reader);
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
            return Bad("檔頭損毀");
        }

        if (version is not null && DemoObserverWorld.IsOutdatedTempSaveVersion(version.Value))
            return Bad(DemoObserverWorld.OutdatedTempSaveMessage(version.Value));
        if (version is null || !DemoObserverWorld.IsSupportedTempSaveVersion(version.Value))
            return Bad("存檔版本不受支援");
        if (scenario is null || weekly is null || days is null || days < 0)
            return Bad("檔頭缺少世界資訊");
        var scenarioName = DemoObserverWorld.TempScenarioName(weekly);
        if (scenarioName is null || scenario != DemoScenario.Sample(scenario.Seed))
            return Bad("不是已知的示範情境");
        return new DemoSaveSlotInfo(slot, path, true, version, scenario.Seed, days, scenarioName);

        DemoSaveSlotInfo Bad(string reason) => new(slot, path, true, Error: reason);
    }

    private static void CheckSlot(int slot)
    {
        if (slot is < 1 or > TempSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot,
                $"存檔槽必須是 1～{TempSlotCount}。");
    }
}
