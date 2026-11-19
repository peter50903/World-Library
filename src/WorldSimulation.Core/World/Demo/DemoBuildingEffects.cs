using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-37 章：建築加成的種類（介面依此挑圖示）。</summary>
public enum DemoBonusKind
{
    Research = 0,
    Storage = 1,
    Construction = 2,
    Gathering = 3,
    Soldiers = 4,
    Healing = 5
}

/// <summary>
/// 第 08-37 章【TEMP 規則】建築的作用（參考 CK3 的建築加成；依 18D 建築活動簡化）。只算<b>完好</b>的建築（施工中、毀損的沒有作用），
/// 同一種蓋好幾座只算一次。城牆、城門、塔樓、城堡的作用見 <see cref="DemoDefense"/>（第 08-34、08-37 章）。
/// <list type="bullet">
/// <item>研究：抄寫室 +<see cref="TempScriptoriumResearch"/>%、學校 +<see cref="TempSchoolResearch"/>%、高等學院 +<see cref="TempAcademyResearch"/>%、
///   魔法學院 +<see cref="TempMagicAcademyResearch"/>%（相加）。</item>
/// <item>存糧：糧倉讓每週的存糧腐敗少 <see cref="TempGranarySpoilageCut"/>%。</item>
/// <item>施工：工坊讓建築工時 +<see cref="TempWorkshopBuild"/>%；木材場、採石場、礦場讓採木、採石、採鐵的工時減半；倉庫讓閒時備料的上限加倍。</item>
/// <item>士兵：兵營 +<see cref="TempBarrackSoldiers"/>%、靶場 +<see cref="TempRangeSoldiers"/>%、馬廄 +<see cref="TempStableSoldiers"/>%（駐軍與討伐軍；鎮以上才有士兵）。</item>
/// <item>醫護：診療所讓怪物害死的人少 <see cref="TempClinicHeal"/>%、醫院少 <see cref="TempHospitalHeal"/>%（取高者）。</item>
/// </list>
/// 只讀上一週的建築（本週蓋好的下週才生效）；不存檔（由建築算出）。
/// </summary>
public sealed record DemoBuildingBonus(int ResearchPercent, int SpoilageCutPercent, int BuildPercent, bool FastWood, bool FastStone, bool FastIron,
    int StockPercent, int SoldierPercent, int HealPercent)
{
    /// <summary>【TEMP】研究加成（百分點）。</summary>
    public const int TempScriptoriumResearch = 10, TempSchoolResearch = 15, TempAcademyResearch = 25, TempMagicAcademyResearch = 10;

    /// <summary>【TEMP】糧倉少腐敗幾 %、工坊多幾 % 工時、倉庫讓備料上限多幾 %。</summary>
    public const int TempGranarySpoilageCut = 50, TempWorkshopBuild = 20, TempWarehouseStock = 100;

    /// <summary>【TEMP】士兵加成（百分點）與醫護少死幾 %。</summary>
    public const int TempBarrackSoldiers = 20, TempRangeSoldiers = 10, TempStableSoldiers = 10, TempClinicHeal = 15, TempHospitalHeal = 30;

    public static DemoBuildingBonus None { get; } = new(0, 0, 0, false, false, false, 0, 0, 0);

    /// <summary>一個聚落的建築加成（沒有建築紀錄＝<see cref="None"/>）。</summary>
    public static DemoBuildingBonus Of(DemoHolding? holding)
    {
        if (holding is null)
            return None;
        var standing = holding.Buildings.Where(b => b.Status == DemoBuildingStatus.Standing).Select(b => b.DefId).ToHashSet(StringComparer.Ordinal);
        if (standing.Count == 0)
            return None;
        int If(string defId, int value) => standing.Contains(defId) ? value : 0;
        return new DemoBuildingBonus(
            If("BUILD_SCRIPTORIUM", TempScriptoriumResearch) + If("BUILD_SCHOOL", TempSchoolResearch) + If("BUILD_ACADEMY", TempAcademyResearch)
                + If("BUILD_MAGIC_ACADEMY", TempMagicAcademyResearch),
            If("BUILD_GRANARY", TempGranarySpoilageCut),
            If("BUILD_WORKSHOP", TempWorkshopBuild),
            standing.Contains("BUILD_LUMBERYARD"), standing.Contains("BUILD_QUARRY"), standing.Contains("BUILD_MINE"),
            If("BUILD_WAREHOUSE", TempWarehouseStock),
            If("BUILD_BARRACK", TempBarrackSoldiers) + If("BUILD_ARCHERY_RANGE", TempRangeSoldiers) + If("BUILD_STABLE", TempStableSoldiers),
            Math.Max(If("BUILD_CLINIC", TempClinicHeal), If("BUILD_HOSPITAL", TempHospitalHeal)));
    }

    public bool Any => !Equals(None);

    /// <summary>一種建築的作用（領地列表那一列與提示；沒有作用＝null）。城牆、城門、塔樓、城堡看 <see cref="DemoDefense"/>。</summary>
    public static string? EffectOf(string defId)
    {
        var inv = CultureInfo.InvariantCulture;
        return defId switch
        {
            "BUILD_SCRIPTORIUM" => string.Create(inv, $"研究 +{TempScriptoriumResearch}%"),
            "BUILD_SCHOOL" => string.Create(inv, $"研究 +{TempSchoolResearch}%"),
            "BUILD_ACADEMY" => string.Create(inv, $"研究 +{TempAcademyResearch}%"),
            "BUILD_MAGIC_ACADEMY" => string.Create(inv, $"研究 +{TempMagicAcademyResearch}%"),
            "BUILD_GRANARY" => string.Create(inv, $"存糧腐敗 −{TempGranarySpoilageCut}%"),
            "BUILD_WORKSHOP" => string.Create(inv, $"施工 +{TempWorkshopBuild}%"),
            "BUILD_LUMBERYARD" => "採木工時減半",
            "BUILD_QUARRY" => "採石工時減半",
            "BUILD_MINE" => "採鐵工時減半",
            "BUILD_WAREHOUSE" => "閒時備料上限加倍",
            "BUILD_BARRACK" => string.Create(inv, $"士兵 +{TempBarrackSoldiers}%"),
            "BUILD_ARCHERY_RANGE" => string.Create(inv, $"士兵 +{TempRangeSoldiers}%"),
            "BUILD_STABLE" => string.Create(inv, $"士兵 +{TempStableSoldiers}%"),
            "BUILD_CLINIC" => string.Create(inv, $"怪物害死的人 −{TempClinicHeal}%"),
            "BUILD_HOSPITAL" => string.Create(inv, $"怪物害死的人 −{TempHospitalHeal}%"),
            "BUILD_CASTLE" => string.Create(inv,
                $"怪物來襲時守方 +{DemoDefense.TempCastleMonsterPercent}%、圍城多 {DemoDefense.TempCastleSiegeWeeks} 週、攻城死傷 −{DemoDefense.TempCastleWarCut}%"),
            _ => null
        };
    }

    /// <summary>加成段的各項（相加後的結果；介面依種類放圖示）。</summary>
    public IReadOnlyList<(DemoBonusKind Kind, string Text)> Parts()
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<(DemoBonusKind, string)>();
        if (ResearchPercent > 0)
            parts.Add((DemoBonusKind.Research, string.Create(inv, $"研究 +{ResearchPercent}%")));
        if (SpoilageCutPercent > 0)
            parts.Add((DemoBonusKind.Storage, string.Create(inv, $"存糧腐敗 −{SpoilageCutPercent}%")));
        if (BuildPercent > 0)
            parts.Add((DemoBonusKind.Construction, string.Create(inv, $"施工 +{BuildPercent}%")));
        var fast = new List<string>();
        if (FastWood) fast.Add("採木");
        if (FastStone) fast.Add("採石");
        if (FastIron) fast.Add("採鐵");
        if (fast.Count > 0)
            parts.Add((DemoBonusKind.Gathering, string.Join("、", fast) + "工時減半"));
        if (StockPercent > 0)
            parts.Add((DemoBonusKind.Gathering, "閒時備料上限加倍"));
        if (SoldierPercent > 0)
            parts.Add((DemoBonusKind.Soldiers, string.Create(inv, $"士兵 +{SoldierPercent}%")));
        if (HealPercent > 0)
            parts.Add((DemoBonusKind.Healing, string.Create(inv, $"怪物害死的人 −{HealPercent}%")));
        return parts;
    }

    /// <summary>一行（現況頁）：「建築加成：研究 +25%、存糧腐敗 −50%」；沒有加成＝null。</summary>
    public string? Line() => Any ? "建築加成：" + string.Join("、", Parts().Select(p => p.Text)) : null;
}
