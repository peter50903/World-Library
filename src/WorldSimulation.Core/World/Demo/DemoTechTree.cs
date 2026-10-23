using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-9 章 A1：知識樹上的一項技術，以正式設定 10B～10N 的 TechID 識別（取代第 08-6 章的程式列舉）。
/// 存檔與指紋都用 TechID 文字。
/// </summary>
[JsonConverter(typeof(DemoTechnologyJsonConverter))]
public readonly record struct DemoTechnology(string Id) : IComparable<DemoTechnology>
{
    /// <summary>基礎作物栽培（第 08-6 章 a 節的「農耕」）。</summary>
    public static DemoTechnology Farming => new("TECH_CROP_CULTIVATION");

    /// <summary>基礎木工（第 08-6 章 d 節的「木工」）。</summary>
    public static DemoTechnology Woodworking => new("TECH_BASIC_CARPENTRY");

    /// <summary>基礎鍛造（第 08-6 章 e 節的「鍛造」；前置改照 10C：探礦→高溫冶煉）。</summary>
    public static DemoTechnology Smithing => new("TECH_BASIC_FORGING");

    public int CompareTo(DemoTechnology other) => string.CompareOrdinal(Id, other.Id);

    public override string ToString() => Id;
}

internal sealed class DemoTechnologyJsonConverter : JsonConverter<DemoTechnology>
{
    public override DemoTechnology Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } id
            ? new DemoTechnology(id)
            : throw new JsonException("技術必須是 TechID 文字。");

    public override void Write(Utf8JsonWriter writer, DemoTechnology value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Id);
}

/// <summary>正式設定的一列技術節點。<see cref="Index"/>＝讀入順序（從 1 起），事件的人數欄用它記技術。</summary>
public sealed record DemoTechNode(
    DemoTechnology Technology,
    int Index,
    string Name,
    string Sheet,
    int Tier,
    string Branch,
    IReadOnlyList<DemoTechnology> Parents,
    long ResearchTimeBase,
    string PracticeBoostCondition)
{
    /// <summary>第 08-9 章 A2：研究所需原料（ResearchResourceCost，例：RES_WOOD:10）。</summary>
    public IReadOnlyList<(string Resource, long Amount)> ResearchCost { get; init; } = Array.Empty<(string, long)>();

    /// <summary>第 08-9 章 A3：實踐加速（PracticeBoostEffect「研究時間-20%」→ 20）。</summary>
    public long PracticeBoostPercent { get; init; }
}

/// <summary>
/// 第 08-9 章 A1：知識樹＝正式設定 10B～10N 全部啟用的節點（08-7a）。門檻＝ResearchTimeBase × <see cref="TempThresholdPerTimeBase"/>。
/// 讀入時檢查 TechID 不重複、前置都存在、階級 1～5、研究時間為正；不合法就丟例外（正式設定錯誤）。
/// </summary>
public static class DemoTechTree
{
    /// <summary>【TEMP】門檻＝ResearchTimeBase × 325（基礎作物栽培 80 → 26000，與第 08-6 章 a 節相同）。</summary>
    public const long TempThresholdPerTimeBase = 325;

    public static readonly IReadOnlyList<string> Sheets = new[]
    {
        "10B_農業畜牧技術樹.csv", "10C_採礦冶金技術樹.csv", "10D_建築工程技術樹.csv", "10E_木工造船技術樹.csv",
        "10F_紡織皮革技術樹.csv", "10G_食品保存技術樹.csv", "10H_醫療藥學技術樹.csv", "10I_軍事工程技術樹.csv",
        "10J_書寫教育技術樹.csv", "10K_魔法工藝技術樹.csv", "10L_陶瓷玻璃技術樹.csv", "10M_珠寶精工技術樹.csv",
        "10N_運輸車輛技術樹.csv", "10R_狩獵與求生技術樹.csv"
    };

    /// <summary>第 08-9 章 A4：開局知識分頁（08-7b 第 1 節，使用者 2026-10-23 裁決）。</summary>
    public const string InitialSheet = "10S_開局知識.csv";

    private static readonly Lazy<IReadOnlySet<DemoTechnology>> InitialSet = new(() => ReadInitial(OfficialConfig.ReadSheetText(InitialSheet)));

    /// <summary>第 08-9 章 A4：開局每個聚落都已知的技術（不必研究、沒有紀錄、新建聚落也知道）。</summary>
    public static IReadOnlySet<DemoTechnology> Initial => InitialSet.Value;

    public static bool IsInitial(DemoTechnology technology) => Initial.Contains(technology);

    internal static IReadOnlySet<DemoTechnology> ReadInitial(string text)
    {
        var rows = ParameterSheet.ParseCsv(text);
        var header = rows[0].Select(h => h.Trim()).ToList();
        var (id, enabled) = (header.IndexOf("TechID"), header.IndexOf("Enabled"));
        if (id < 0 || enabled < 0)
            throw new InvalidOperationException("開局知識分頁缺少 TechID 或 Enabled 欄。");
        var set = rows.Skip(1).Where(r => r.Count > enabled && string.Equals(r[enabled].Trim(), "TRUE", StringComparison.OrdinalIgnoreCase))
            .Select(r => new DemoTechnology(r[id].Trim())).ToHashSet();
        var missing = set.Where(t => !Loaded.Value.ById.ContainsKey(t)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"開局知識不在知識樹上：{string.Join("、", missing)}");
        return set;
    }

    private static readonly Lazy<(IReadOnlyList<DemoTechNode> All, Dictionary<DemoTechnology, DemoTechNode> ById)> Loaded =
        new(() => Build(Sheets.Select(name => (name, OfficialConfig.ReadSheetText(name)))));

    /// <summary>全部節點（依分頁、列順序）。</summary>
    public static IReadOnlyList<DemoTechNode> All => Loaded.Value.All;

    public static bool Contains(DemoTechnology technology) => Loaded.Value.ById.ContainsKey(technology);

    public static DemoTechNode Of(DemoTechnology technology)
        => Loaded.Value.ById.TryGetValue(technology, out var node)
            ? node
            : throw new ArgumentOutOfRangeException(nameof(technology), technology.Id, "知識樹沒有這項技術。");

    /// <summary>事件人數欄的技術編號 → 技術；不存在回傳 null。</summary>
    public static DemoTechnology? ByIndex(long index)
        => index >= 1 && index <= All.Count ? All[(int)index - 1].Technology : null;

    public static long ThresholdOf(DemoTechnology technology) => checked(Of(technology).ResearchTimeBase * TempThresholdPerTimeBase);

    internal static (IReadOnlyList<DemoTechNode> All, Dictionary<DemoTechnology, DemoTechNode> ById) Build(
        IEnumerable<(string Name, string Text)> sheets)
    {
        var nodes = new List<DemoTechNode>();
        var parentText = new List<string>();
        foreach (var (name, text) in sheets)
        {
            var rows = ParameterSheet.ParseCsv(text);
            if (rows.Count == 0)
                throw new InvalidOperationException($"知識樹分頁是空的：{name}");
            var header = rows[0].Select(h => h.Trim()).ToList();
            int Column(string column)
            {
                var i = header.IndexOf(column);
                return i >= 0 ? i : throw new InvalidOperationException($"知識樹分頁缺少欄位「{column}」：{name}");
            }
            var (id, cn, tier, branch, parents, time, boost, enabled) = (Column("TechID"), Column("中文名稱"), Column("Tier"),
                Column("Branch"), Column("ParentTechIDs"), Column("ResearchTimeBase"), Column("PracticeBoostCondition"), Column("Enabled"));
            var cost = header.IndexOf("ResearchResourceCost");
            var effect = header.IndexOf("PracticeBoostEffect");
            foreach (var row in rows.Skip(1).Where(r => r.Count > id && !string.IsNullOrWhiteSpace(r[id])))
            {
                string Cell(int i) => i < row.Count ? row[i].Trim() : string.Empty;
                if (!string.Equals(Cell(enabled), "TRUE", StringComparison.OrdinalIgnoreCase))
                    continue;
                // 第 08-9 章 A4：第 0 階＝開局知識（不研究）。
                if (!int.TryParse(Cell(tier), NumberStyles.None, CultureInfo.InvariantCulture, out var t) || t is < 0 or > 5
                    || !long.TryParse(Cell(time), NumberStyles.None, CultureInfo.InvariantCulture, out var baseTime) || baseTime <= 0)
                    throw new InvalidOperationException($"知識樹 {name} 的 {Cell(id)}：階級須為 0～5、研究時間須為正整數。");
                nodes.Add(new DemoTechNode(new DemoTechnology(Cell(id)), nodes.Count + 1, Cell(cn), name, t, Cell(branch),
                    Array.Empty<DemoTechnology>(), baseTime, Cell(boost)) {
                    ResearchCost = ParseCost(cost < 0 ? string.Empty : Cell(cost), name, Cell(id)),
                    PracticeBoostPercent = ParseBoost(effect < 0 ? string.Empty : Cell(effect))
                });
                parentText.Add(Cell(parents));
            }
        }

        var ids = nodes.Select(n => n.Technology).ToHashSet();
        if (ids.Count != nodes.Count)
            throw new InvalidOperationException("知識樹有重複的 TechID。");
        for (var i = 0; i < nodes.Count; i++)
        {
            var parents = parentText[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => new DemoTechnology(p)).ToArray();
            if (parents.Any(p => !ids.Contains(p)))
                throw new InvalidOperationException($"知識樹 {nodes[i].Technology} 的前置不存在：{parentText[i]}");
            nodes[i] = nodes[i] with { Parents = parents };
        }
        return (nodes, nodes.ToDictionary(n => n.Technology));
    }

    /// <summary>「研究時間-20%」→ 20；空白或其他格式→ 0（沒有實踐加速）。</summary>
    private static long ParseBoost(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"研究時間-(\d+)%");
        return match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static IReadOnlyList<(string Resource, long Amount)> ParseCost(string text, string sheet, string id)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part =>
        {
            var pieces = part.Split(':');
            return pieces.Length == 2 && pieces[0].StartsWith("RES_", StringComparison.Ordinal)
                && long.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0
                ? (pieces[0], amount)
                : throw new InvalidOperationException($"知識樹 {sheet} 的 {id}：研究原料格式應為 RES_X:數量（{part}）。");
        }).ToArray();
}
