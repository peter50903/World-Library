using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-32 章：正式建築表的一列（config/sheets/18_建築.csv，科技前置與升級鏈看 18C）。
/// 建材只算三種：木（RES_WOOD）、石（RES_STONE、RES_CLAY 算石）、鐵（RES_IRON_INGOT）；其他建材（布、皮、魔力水晶、羊皮紙）第一版不計。
/// </summary>
public sealed record DemoBuildingDef(
    string Id,
    string Name,
    string Category,
    int Capacity,
    int Wood,
    int Stone,
    int Iron,
    int Hours,
    ImmutableArray<string> Techs,
    string? UpgradeTo);

/// <summary>第 08-32 章：正式建築表（只讀設定、載入後不變）。</summary>
public static class DemoBuildingCatalog
{
    private const string Sheet = "18_建築.csv";
    private const string ChainSheet = "18C_建築前置升級鏈.csv";

    private static readonly Lazy<ImmutableSortedDictionary<string, DemoBuildingDef>> Loaded = new(Load);

    /// <summary>全部啟用的建築（依 BuildingDefID 排序）。</summary>
    public static IReadOnlyDictionary<string, DemoBuildingDef> All => Loaded.Value;

    public static bool Contains(string id) => Loaded.Value.ContainsKey(id);

    public static DemoBuildingDef Get(string id)
        => Loaded.Value.TryGetValue(id, out var def) ? def : throw new KeyNotFoundException($"建築表沒有 {id}。");

    private static ImmutableSortedDictionary<string, DemoBuildingDef> Load()
    {
        var chain = Rows(ChainSheet).ToDictionary(r => r["BuildingDefID"], r => r);
        var defs = ImmutableSortedDictionary.CreateBuilder<string, DemoBuildingDef>(StringComparer.Ordinal);
        foreach (var r in Rows(Sheet))
        {
            if (!string.Equals(r["Enabled"], "TRUE", StringComparison.OrdinalIgnoreCase))
                continue;
            int wood = 0, stone = 0, iron = 0;
            foreach (var part in r["材料需求"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = part.Split(':', StringSplitOptions.TrimEntries);
                if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 0)
                    throw new InvalidOperationException($"{Sheet} 的 {r["BuildingDefID"]} 材料需求看不懂：{part}");
                switch (pair[0])
                {
                    case "RES_WOOD": wood += amount; break;
                    case "RES_STONE" or "RES_CLAY": stone += amount; break;
                    case "RES_IRON_INGOT": iron += amount; break;
                }
            }
            var id = r["BuildingDefID"];
            var c = chain.GetValueOrDefault(id);
            var techs = c is null ? ImmutableArray<string>.Empty
                : c["RequiredTechIDs"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray();
            var upgrade = c is not null && !string.IsNullOrWhiteSpace(c["UpgradeToID"]) ? c["UpgradeToID"] : null;
            defs.Add(id, new DemoBuildingDef(id, r["中文名稱"], r["分類"],
                int.Parse(r["容量"], CultureInfo.InvariantCulture), wood, stone, iron,
                Math.Max(1, int.Parse(r["工時需求（小時）"], CultureInfo.InvariantCulture)), techs, upgrade));
        }
        return defs.ToImmutable();
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> Rows(string sheet)
    {
        var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet));
        var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        foreach (var r in rows.Skip(1).Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0])))
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
                row[header[i]] = i < r.Count ? r[i].Trim() : "";
            yield return row;
        }
    }
}
