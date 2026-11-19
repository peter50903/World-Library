using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-36 章：升下一級的一項本級設施——完成、施工中、毀損待重建、可以開工、還沒解鎖（缺專家或技術）。</summary>
public enum DemoTierStepState
{
    Done = 0,
    Building = 1,
    Ruined = 2,
    Ready = 3,
    Locked = 4
}

/// <summary>
/// 第 08-36 章：一項本級設施與它要蓋的建築（<see cref="Building"/>＝領地面板上的名稱；<see cref="Percent"/>：施工中＝進度；
/// <see cref="Lacking"/>：還沒解鎖時缺的專家或技術）。
/// </summary>
public sealed record DemoTierStep(DemoTierFacility Facility, string Building, DemoTierStepState State, int Percent, string? Lacking)
{
    public string StateText => State switch
    {
        DemoTierStepState.Done => "完成",
        DemoTierStepState.Building => string.Create(CultureInfo.InvariantCulture, $"施工中 {Percent}%"),
        DemoTierStepState.Ruined => "毀損，等待重建",
        DemoTierStepState.Ready => "可以開工",
        _ => "缺" + Lacking
    };

    /// <summary>一行：設施名＋狀態；建築名跟設施名完全不同（圖書館＝高等學院）或要第 2 座時加括號。</summary>
    public string Line => (Building.Intersect(Facility.Name).Any() && !Building.StartsWith('第') ? Facility.Name : $"{Facility.Name}（{Building}）")
        + "：" + StateText;
}

/// <summary>
/// 第 08-36 章：升下一級還差什麼——人口門檻、本級設施（任兩種：專家或技術解鎖，而且建築蓋好）、城市升首都另需王權。
/// 首都沒有下一級（<see cref="Of"/> 回傳 null）。只讀，不改世界。
/// </summary>
public sealed record DemoTierProgress(DemoSettlementTier Tier, long Population, IReadOnlyList<DemoTierStep> Steps, bool? Kingship)
{
    public DemoSettlementTier Next => Tier + 1;

    public long NeedPopulation => DemoSettlementHistory.TempUpPopulation(Next);

    public int Done => Steps.Count(s => s.State == DemoTierStepState.Done);

    public static int Need => DemoSettlementHistory.TempFacilitiesNeeded;

    /// <summary>條件都夠了（下週結算時升級）。</summary>
    public bool Ready => Population >= NeedPopulation && Done >= Need && Kingship != false;

    /// <summary>標題：「升為城鎮：人口 47/60、本級設施 1/2」（首都另加王權）。</summary>
    public string Headline
    {
        get
        {
            var text = string.Create(CultureInfo.InvariantCulture,
                $"升為{DemoSettlementHistory.TierName(Next)}：人口 {Population}/{NeedPopulation}、本級設施 {Math.Min(Done, Need)}/{Need}");
            if (Kingship is { } kingship)
                text += kingship ? "、已知王權" : "、缺「王權」";
            return Ready ? text + "（條件已足）" : text;
        }
    }

    public static DemoTierProgress? Of(DemoSettlementTier tier, long population, DemoHolding? holding,
        Func<DemoRepresentativeRole, bool> hasRole, Func<string, bool> knows)
    {
        ArgumentNullException.ThrowIfNull(hasRole);
        ArgumentNullException.ThrowIfNull(knows);
        if (tier >= DemoSettlementTier.Capital)
            return null;
        var buildings = holding?.Buildings ?? Array.Empty<DemoBuilding>();
        var steps = DemoSettlementHistory.FacilitiesOf(tier).Select(f => StepOf(f, tier, buildings, hasRole, knows)).ToList();
        bool? kingship = tier + 1 == DemoSettlementTier.Capital ? knows(DemoSettlementHistory.CapitalTechId) : null;
        return new DemoTierProgress(tier, population, steps, kingship);
    }

    private static DemoTierStep StepOf(DemoTierFacility f, DemoSettlementTier tier, IReadOnlyList<DemoBuilding> buildings,
        Func<DemoRepresentativeRole, bool> hasRole, Func<string, bool> knows)
    {
        var (defId, count) = DemoBuildings.FacilityBuildings[f.Name];
        var plain = defId == DemoBuildings.WallWood ? "城牆" : DemoHoldingReport.Name(defId, tier);
        var name = count > 1 ? string.Create(CultureInfo.InvariantCulture, $"第 {count} 座{plain}") : plain;
        var lacking = f.Role is { } role
            ? hasRole(role) ? null : DemoRepresentative.RoleName(role)
            : f.TechId is { } tech && !knows(tech) ? $"「{DemoKnowledge.Name(DemoTechnology.Of(tech))}」" : null;
        if (lacking is not null)
            return new DemoTierStep(f, name, DemoTierStepState.Locked, 0, lacking);
        if (DemoBuildings.FacilityBuilt(buildings, f, standingOnly: true))
            return new DemoTierStep(f, name, DemoTierStepState.Done, 100, null);
        var family = buildings.Where(b => DemoBuildings.SameFamily(b.DefId, defId)).ToList();
        if (family.FirstOrDefault(b => b.Status == DemoBuildingStatus.Building) is { } underway)
            return new DemoTierStep(f, name, DemoTierStepState.Building,
                Math.Clamp(underway.Progress * 100 / Math.Max(1, underway.Def.Hours), 0, 99), null);
        return family.Any(b => b.Status == DemoBuildingStatus.Ruined)
            ? new DemoTierStep(f, name, DemoTierStepState.Ruined, 0, null)
            : new DemoTierStep(f, name, DemoTierStepState.Ready, 0, null);
    }
}
