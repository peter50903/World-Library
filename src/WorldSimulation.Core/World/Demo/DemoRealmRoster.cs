using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-27 章（使用者 2026-11-18：各勢力要寫村、鎮……的資源儲備、重要人物、兵力）：勢力頁隸屬樹裡，每個聚落底下的幾行——
/// 人口與存糧（夠吃幾週）、兵力（戰時徵兵與常備士兵）、重要人物（領袖、專家、英雄）。只讀世界，不改世界。
/// 資源儲備目前只有糧食（模擬裡還沒有木材、石材等庫存；正式建築系統會加上建材）。
/// </summary>
public static class DemoRealmRoster
{
    /// <summary>某聚落在勢力頁的幾行（遺跡或不存在＝空）。</summary>
    public static IReadOnlyList<string> Lines(DemoObserverWorld world, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Sites.Settlements.FirstOrDefault(s => s.Id == settlementId) is not { IsRuin: false })
            return Array.Empty<string>();
        static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        var lines = new List<string>();
        var people = world.SettlementPopulation(settlementId);
        var ledger = world.LatestWeek?.Settlements.FirstOrDefault(l => l.SettlementId == settlementId);
        var food = ledger is null ? "存糧尚未結算"
            : ledger.FoodConsumed > 0 ? $"存糧 {N(ledger.FoodClosing)}（約 {ledger.FoodClosing / ledger.FoodConsumed} 週）"
            : $"存糧 {N(ledger.FoodClosing)}";
        lines.Add($"人口 {N(people)}｜{food}");
        var levy = DemoRealms.Army(people);
        var force = world.CombatForceOf(settlementId);
        lines.Add(force.Soldiers > 0
            ? $"兵力：戰時徵兵 {N(levy)} 人、常備士兵 {N(force.Soldiers)} 人（{force.SoldierLevel} 級）"
            : $"兵力：戰時徵兵 {N(levy)} 人");
        var tier = world.ChronicleOf(settlementId).Tier;
        var who = world.RepresentativesOf(settlementId)
            .OrderBy(r => r.Role == DemoRepresentativeRole.Leader ? 0 : r.Role == DemoRepresentativeRole.Hero ? 1 : 2).ThenBy(r => r.Role).ThenBy(r => r.Id)
            .Select(r => $"{DemoRepresentative.RoleName(r.Role, tier)} {r.Name}").ToList();
        if (who.Count > 0)
            lines.Add("人物：" + string.Join("、", who));
        return lines;
    }
}
