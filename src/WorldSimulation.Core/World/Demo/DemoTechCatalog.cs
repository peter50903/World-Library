using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-7a 章 A1：知識樹的一個節點（正式設定 10B～10N 與 10R 的一列）。
/// <see cref="Domain"/>＝分頁代號（10B…10N）；10R 依 Branch 分成 OPENING／HUNT／SOCIETY／FAITH。
/// </summary>
public sealed record DemoTechNode(
    int Index,
    string Id,
    string Name,
    int Tier,
    string Domain,
    string Branch,
    ImmutableArray<string> Parents,
    long ResearchTimeBase,
    int PracticeBoostPercent,
    ImmutableArray<string> Facilities)
{
    /// <summary>第 0 階＝開局知識（08-7b 第 1 節）。</summary>
    public bool IsOpening => Tier == 0;
}

/// <summary>
/// 第 08-7a 章 A1：知識樹目錄——讀正式設定 10B～10N（十三個工藝領域）與 10R（開局知識、狩獵、社會、信仰）。
/// 順序＝分頁順序再依列順序，<see cref="DemoTechnology.Index"/> 就是在此清單的位置（事件人數欄存它）。
/// </summary>
public static class DemoTechCatalog
{
    /// <summary>讀取的分頁（10A 是領域總表、10O～10Q 是規則，不含節點）。</summary>
    public static readonly ImmutableArray<string> Sheets = ImmutableArray.Create(
        "10B_農業畜牧技術樹.csv", "10C_採礦冶金技術樹.csv", "10D_建築工程技術樹.csv", "10E_木工造船技術樹.csv",
        "10F_紡織皮革技術樹.csv", "10G_食品保存技術樹.csv", "10H_醫療藥學技術樹.csv", "10I_軍事工程技術樹.csv",
        "10J_書寫教育技術樹.csv", "10K_魔法工藝技術樹.csv", "10L_陶瓷玻璃技術樹.csv", "10M_珠寶精工技術樹.csv",
        "10N_運輸車輛技術樹.csv", "10R_新增知識領域.csv");

    private static readonly Lazy<(ImmutableArray<DemoTechNode> All, ImmutableDictionary<string, DemoTechNode> ById)> Loaded = new(Load);

    public static ImmutableArray<DemoTechNode> All => Loaded.Value.All;

    public static DemoTechNode Get(string id)
        => Loaded.Value.ById.TryGetValue(id, out var node) ? node : throw new KeyNotFoundException($"知識樹沒有 {id}。");

    public static bool Contains(string id) => Loaded.Value.ById.ContainsKey(id);

    private static (ImmutableArray<DemoTechNode>, ImmutableDictionary<string, DemoTechNode>) Load()
    {
        var nodes = new List<DemoTechNode>();
        foreach (var sheet in Sheets)
        {
            var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet));
            var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
            string Cell(IReadOnlyList<string> r, string column)
            {
                var i = header.IndexOf(column);
                if (i < 0) throw new InvalidOperationException($"{sheet} 缺少欄位 {column}。");
                return i < r.Count ? r[i].Trim() : "";
            }
            static ImmutableArray<string> List(string text)
                => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray();

            foreach (var r in rows.Skip(1).Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0])))
            {
                if (!string.Equals(Cell(r, "Enabled"), "TRUE", StringComparison.OrdinalIgnoreCase))
                    continue;
                var branch = Cell(r, "Branch");
                var code = sheet[..3];
                var domain = code != "10R" ? code : branch switch
                {
                    "開局" => "OPENING",
                    "狩獵" => "HUNT",
                    "社會" => "SOCIETY",
                    "信仰" => "FAITH",
                    _ => throw new InvalidOperationException($"10R 的 Branch「{branch}」不在已知領域內。")
                };
                var boost = Cell(r, "PracticeBoostEffect");
                var percent = boost.StartsWith("研究時間-", StringComparison.Ordinal) && boost.EndsWith('%')
                    ? int.Parse(boost["研究時間-".Length..^1], CultureInfo.InvariantCulture) : 0;
                nodes.Add(new DemoTechNode(nodes.Count, Cell(r, "TechID"), Cell(r, "中文名稱"),
                    int.Parse(Cell(r, "Tier"), CultureInfo.InvariantCulture), domain, branch, List(Cell(r, "ParentTechIDs")),
                    long.Parse(Cell(r, "ResearchTimeBase"), CultureInfo.InvariantCulture), percent, List(Cell(r, "ApplicationFacilityIDs"))));
            }
        }

        var byId = nodes.ToImmutableDictionary(n => n.Id); // 重複 TechID 在此丟例外
        foreach (var n in nodes)
        {
            if (n.Tier is < 0 or > 5 || (n.Tier > 0) != (n.ResearchTimeBase > 0))
                throw new InvalidOperationException($"{n.Id} 的 Tier 或 ResearchTimeBase 不合法。");
            if (n.Parents.FirstOrDefault(p => !byId.ContainsKey(p)) is { } missing)
                throw new InvalidOperationException($"{n.Id} 的前置 {missing} 不在知識樹內。");
        }
        return (nodes.ToImmutableArray(), byId);
    }
}

/// <summary>
/// 第 08-7a 章 A1：知識樹上的一項技術（TechID 取代原本的程式列舉）。<see cref="Index"/>＝在 <see cref="DemoTechCatalog.All"/> 的位置，
/// 事件人數欄存它；存檔以 TechID 字串保存。
/// </summary>
[JsonConverter(typeof(DemoTechnologyJsonConverter))]
public readonly record struct DemoTechnology(int Index) : IComparable<DemoTechnology>
{
    public DemoTechNode Node => DemoTechCatalog.All[Index];

    public string Id => Node.Id;

    public static DemoTechnology Of(string id) => new(DemoTechCatalog.Get(id).Index);

    public static bool IsDefined(long code) => code >= 0 && code < DemoTechCatalog.All.Length;

    public static explicit operator DemoTechnology(long code)
        => IsDefined(code) ? new DemoTechnology((int)code) : throw new ArgumentOutOfRangeException(nameof(code), "不是知識樹上的技術。");

    public static IEnumerable<DemoTechnology> All => DemoTechCatalog.All.Select(n => new DemoTechnology(n.Index));

    /// <summary>10B 基礎作物栽培：可開墾農田；農師。</summary>
    public static DemoTechnology CropCultivation => Of("TECH_CROP_CULTIVATION");

    /// <summary>10E 基礎木工：木匠；聚落升為村莊的條件。</summary>
    public static DemoTechnology BasicCarpentry => Of("TECH_BASIC_CARPENTRY");

    /// <summary>10C 基礎鍛造：鐵匠；工具階梯第二級。前置照 10C（探礦→高溫冶煉）。</summary>
    public static DemoTechnology BasicForging => Of("TECH_BASIC_FORGING");

    public int CompareTo(DemoTechnology other) => Index.CompareTo(other.Index);

    public override string ToString() => Id;
}

/// <summary>存檔以 TechID 字串保存技術（分頁列順序改變時舊存檔仍讀得到同一項技術）。</summary>
public sealed class DemoTechnologyJsonConverter : JsonConverter<DemoTechnology>
{
    public override DemoTechnology Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var id = reader.GetString() ?? throw new JsonException("技術 ID 不可為 null。");
        return DemoTechCatalog.Contains(id) ? DemoTechnology.Of(id) : throw new JsonException($"知識樹沒有 {id}。");
    }

    public override void Write(Utf8JsonWriter writer, DemoTechnology value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Id);
}
