using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-7c 章：原料大類（背景層只看大類；細項是「大類＋技術」的加工品）。</summary>
public enum DemoResourceCategory
{
    Food,
    Wood,
    Stone,
    Ore,
    Metal,
    Animal,
    Plant,
    Magic
}

/// <summary>正式設定 10T 的一列：細項原料屬於哪個大類、要先會哪項技術才算有。</summary>
public sealed record DemoResourceEntry(string ResourceId, string Name, DemoResourceCategory Category, DemoTechnology? RequiredTech);

/// <summary>
/// 第 08-9 章 A6（08-7c，使用者 2026-10-23：原料簡化成大類）【TEMP 規則】大類的取得條件——
/// 糧食＝有存糧；木材＝可及森林；植物材料＝可及森林或草原，或可取水；石材、礦石（金屬需要礦石）、獸產、魔法原料目前都取不到
/// （等丘陵礦脈 D1、野生動物 C1、魔法章節）。原料暫不扣除。
/// </summary>
public static class DemoResourceCategories
{
    public const string Sheet = "10T_原料大類.csv";

    private static readonly Lazy<IReadOnlyDictionary<string, DemoResourceEntry>> Loaded =
        new(() => Build(OfficialConfig.ReadSheetText(Sheet)));

    public static IReadOnlyDictionary<string, DemoResourceEntry> All => Loaded.Value;

    public static DemoResourceEntry Of(string resourceId)
        => All.TryGetValue(resourceId, out var entry) ? entry : throw new ArgumentOutOfRangeException(nameof(resourceId), resourceId, "10T 沒有這個原料。");

    public static string CategoryName(DemoResourceCategory category) => category switch
    {
        DemoResourceCategory.Food => "糧食",
        DemoResourceCategory.Wood => "木材",
        DemoResourceCategory.Stone => "石材",
        DemoResourceCategory.Ore => "礦石",
        DemoResourceCategory.Metal => "金屬",
        DemoResourceCategory.Animal => "獸產",
        DemoResourceCategory.Plant => "植物材料",
        _ => "魔法原料"
    };

    /// <summary>聚落是否取得得到這個大類（目前的地形與存糧條件）。</summary>
    public static bool CategoryAvailable(DemoResourceCategory category, DemoSettlement settlement, DemoHexWorld? map, long food)
    {
        bool Near(DemoTerrain terrain) => map is not null && DemoKnowledge.NearTerrain(map, settlement, terrain);
        return category switch
        {
            DemoResourceCategory.Food => food > 0,
            DemoResourceCategory.Wood => Near(DemoTerrain.Forest),
            DemoResourceCategory.Plant => Near(DemoTerrain.Forest) || Near(DemoTerrain.Grassland)
                || (map is not null && map.HasWaterAccess(new DemoCellRef(settlement.Column, settlement.Row))),
            DemoResourceCategory.Metal => CategoryAvailable(DemoResourceCategory.Ore, settlement, map, food),
            _ => false // 石材、礦石、獸產、魔法原料目前取不到
        };
    }

    /// <summary>細項原料：所屬大類取得得到，且已會加工需要的技術。</summary>
    public static bool Available(string resourceId, DemoSettlement settlement, DemoHexWorld? map, long food, DemoKnowledgeState state)
    {
        var entry = Of(resourceId);
        return CategoryAvailable(entry.Category, settlement, map, food)
            && (entry.RequiredTech is not { } tech || state.Knows(settlement.Id, tech));
    }

    internal static IReadOnlyDictionary<string, DemoResourceEntry> Build(string text)
    {
        var rows = ParameterSheet.ParseCsv(text);
        var h = rows[0].Select(x => x.Trim()).ToList();
        int C(string name) => h.IndexOf(name) is var i && i >= 0 ? i : throw new InvalidOperationException($"10T 缺少欄位「{name}」。");
        var (id, name, cat, tech, enabled) = (C("ResourceID"), C("中文名稱"), C("CategoryID"), C("RequiredTechID"), C("Enabled"));
        var result = new Dictionary<string, DemoResourceEntry>();
        foreach (var r in rows.Skip(1))
        {
            string Cell(int i) => i < r.Count ? r[i].Trim() : string.Empty;
            if (Cell(id).Length == 0 || !string.Equals(Cell(enabled), "TRUE", StringComparison.OrdinalIgnoreCase))
                continue;
            var category = Cell(cat) switch
            {
                "CAT_FOOD" => DemoResourceCategory.Food,
                "CAT_WOOD" => DemoResourceCategory.Wood,
                "CAT_STONE" => DemoResourceCategory.Stone,
                "CAT_ORE" => DemoResourceCategory.Ore,
                "CAT_METAL" => DemoResourceCategory.Metal,
                "CAT_ANIMAL" => DemoResourceCategory.Animal,
                "CAT_PLANT" => DemoResourceCategory.Plant,
                "CAT_MAGIC" => DemoResourceCategory.Magic,
                var other => throw new InvalidOperationException($"10T 的 {Cell(id)}：未知的大類 {other}。")
            };
            DemoTechnology? required = Cell(tech).Length == 0 ? null : new DemoTechnology(Cell(tech));
            if (required is { } t && !DemoTechTree.Contains(t))
                throw new InvalidOperationException($"10T 的 {Cell(id)}：加工技術 {t} 不在知識樹。");
            if (!result.TryAdd(Cell(id), new DemoResourceEntry(Cell(id), Cell(name), category, required)))
                throw new InvalidOperationException($"10T 有重複的原料 {Cell(id)}。");
        }
        var missing = DemoTechTree.All.SelectMany(n => n.ResearchCost).Select(c => c.Resource).Distinct().Where(x => !result.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"知識樹用到的原料不在 10T：{string.Join("、", missing)}");
        return result;
    }
}
