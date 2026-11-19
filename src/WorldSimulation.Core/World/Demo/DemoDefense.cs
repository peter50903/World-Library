using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-34 章【TEMP 規則】建築的作用第一版——城防（參考 CK3 的城堡等級：城牆讓圍城變久、守方佔便宜）。由完好的城牆、城門、塔樓算出：
/// <list type="bullet">
/// <item><b>怪物來襲</b>（襲擊、獸潮）：守方戰力 +<see cref="MonsterPercent"/>%——木牆 <see cref="TempWoodWallPercent"/>%、石牆 <see cref="TempStoneWallPercent"/>%
///   （乘上耐久度；沒有城門或城門毀損時減半），每座前哨塔 +<see cref="TempOutpostPercent"/>%、石造塔樓 +<see cref="TempStoneTowerPercent"/>%（最多算 <see cref="TempMaxTowers"/> 座）；
///   有城牆時一次襲擊最多死 5%（沒有時 10%）。</item>
/// <item><b>獸潮攻不破</b>（<see cref="HoldsSwarm"/>）：城牆耐久度三成以上、城門完好時，獸潮只能打城牆，不能讓全村覆滅。</item>
/// <item><b>大軍攻城</b>：攻方對這裡造成的死傷 ×<see cref="WarDamagePercent"/>%（木牆 −20%、石牆 −35%，乘上耐久度）；
///   圍城週數：沒有城牆 −1、木牆 +1、石牆 +3。</item>
/// <item>第 08-37 章：<b>城堡</b>另加怪物來襲時守方 +<see cref="TempCastleMonsterPercent"/>%、圍城 +<see cref="TempCastleSiegeWeeks"/> 週、
///   攻城死傷 −<see cref="TempCastleWarCut"/>%（沒有城牆也算）。</item>
/// </list>
/// 只讀上一週的建築（本週被打壞的下週才生效）；不存檔（由建築算出）。
/// </summary>
public sealed record DemoDefense(int MonsterPercent, bool HoldsSwarm, int WarDamagePercent, int SiegeWeeks, int Towers, string? Wall, bool Castle = false)
{
    /// <summary>【TEMP】城牆、塔樓對怪物的守方戰力加成（百分點）。</summary>
    public const int TempWoodWallPercent = 40, TempStoneWallPercent = 80, TempOutpostPercent = 5, TempStoneTowerPercent = 10, TempMaxTowers = 4;

    /// <summary>【TEMP】城牆讓攻方死傷少幾 %、圍城多幾週（沒有城牆少 <see cref="TempOpenSiegeWeeks"/> 週）、獸潮攻不破要的耐久度（千分比）。</summary>
    public const int TempWoodWallWarCut = 20, TempStoneWallWarCut = 35, TempWoodSiegeWeeks = 1, TempStoneSiegeWeeks = 3, TempOpenSiegeWeeks = 1,
        TempSwarmHoldDurability = 300;

    /// <summary>【TEMP】第 08-37 章：完好的城堡讓守方戰力多幾 %、圍城多幾週、攻城死傷少幾 %。</summary>
    public const int TempCastleMonsterPercent = 20, TempCastleSiegeWeeks = 2, TempCastleWarCut = 15;

    /// <summary>沒有任何防禦建築。</summary>
    public static DemoDefense None { get; } = new(0, false, 100, -TempOpenSiegeWeeks, 0, null);

    /// <summary>一個聚落的城防（沒有建築紀錄＝<see cref="None"/>）。</summary>
    public static DemoDefense Of(DemoHolding? holding)
    {
        if (holding is null)
            return None;
        var wall = holding.Buildings.FirstOrDefault(b => b.DefId is DemoBuildings.WallWood or DemoBuildings.WallStone
            && b.Status == DemoBuildingStatus.Standing && b.Durability > 0);
        var gateStands = holding.Buildings.Any(b => b.DefId is DemoBuildings.GateWood or DemoBuildings.GateStone && b.Status == DemoBuildingStatus.Standing);
        var standingTowers = holding.Buildings.Where(b => b.Status == DemoBuildingStatus.Standing && b.DefId is DemoBuildings.Outpost or DemoBuildings.StoneTower)
            .OrderByDescending(b => b.DefId == DemoBuildings.StoneTower).ThenBy(b => b.Id).Take(TempMaxTowers).ToList();
        var towers = standingTowers.Sum(b => b.DefId == DemoBuildings.StoneTower ? TempStoneTowerPercent : TempOutpostPercent);
        // 第 08-37 章：城堡（沒有城牆也算）。
        var castle = holding.Buildings.Any(b => b.DefId == "BUILD_CASTLE" && b.Status == DemoBuildingStatus.Standing);
        var (keepPercent, keepWeeks, keepCut) = castle ? (TempCastleMonsterPercent, TempCastleSiegeWeeks, TempCastleWarCut) : (0, 0, 0);
        if (wall is null)
            return new DemoDefense(towers + keepPercent, false, 100 - keepCut, -TempOpenSiegeWeeks + keepWeeks, standingTowers.Count, null, castle);
        var stone = wall.DefId == DemoBuildings.WallStone;
        var wallPercent = (stone ? TempStoneWallPercent : TempWoodWallPercent) * wall.Durability / 1000 * (gateStands ? 100 : 50) / 100;
        var warCut = (stone ? TempStoneWallWarCut : TempWoodWallWarCut) * wall.Durability / 1000;
        return new DemoDefense(wallPercent + towers + keepPercent, gateStands && wall.Durability >= TempSwarmHoldDurability, 100 - warCut - keepCut,
            (stone ? TempStoneSiegeWeeks : TempWoodSiegeWeeks) + keepWeeks, standingTowers.Count, DemoHoldingReport.Name(wall.DefId, DemoSettlementTier.City), castle);
    }

    /// <summary>有沒有任何防禦建築（城牆、塔樓或城堡）。</summary>
    public bool Any => Wall is not null || Towers > 0 || Castle;

    /// <summary>一行說明（介面的領地列表、現況頁）。</summary>
    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        if (!Any)
            return "城防：沒有城牆與塔樓（大軍圍城少 1 週）";
        var parts = new List<string>
        {
            string.Create(inv, $"怪物來襲時守方戰力 +{MonsterPercent}%")
        };
        if (HoldsSwarm)
            parts.Add("獸潮攻不破");
        if (Wall is not null || Castle)
            parts.Add(string.Create(inv, $"大軍圍城多 {SiegeWeeks} 週、攻城死傷 −{100 - WarDamagePercent}%"));
        var what = new List<string>();
        if (Castle)
            what.Add("城堡"); // 第 08-37 章
        if (Wall is not null)
            what.Add(Wall);
        if (Towers > 0)
            what.Add(string.Create(inv, $"塔樓 {Towers} 座"));
        return $"城防（{string.Join("、", what)}）：" + string.Join("｜", parts);
    }
}
