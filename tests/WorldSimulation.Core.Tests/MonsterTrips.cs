using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08-50 章：測試逐週擲怪物行動時，照 <see cref="DemoMonsters.Fold"/> 的規則更新地下城的遠征軍與獸潮
/// （出發記下抵達週；撤回、殺光就結束；獸潮過了抵達週、遠征軍打完最後一層就不在了）。
/// </summary>
internal static class MonsterTrips
{
    public static DemoMonsterNest After(DemoMonsterNest nest, DemoMonsters.Plan plan, int week)
    {
        var expedition = nest.Expedition;
        var swarm = nest.Swarm;
        foreach (var d in plan.Departures.Where(d => d.CellId == nest.CellId))
        {
            var trip = new DemoMonsterTrip(d.SettlementId, week, week + d.Weeks);
            if (d.Kind == DemoSettlementEventKind.ExpeditionSetOut)
                expedition = trip;
            else
                swarm = trip;
        }
        if (plan.Returns.Any(r => r.CellId == nest.CellId) || plan.Cleared.Any(c => c.CellId == nest.CellId))
            expedition = null;
        if (swarm is not null && week >= swarm.ArriveWeek)
            swarm = null;
        if (expedition is not null && week >= expedition.ArriveWeek + nest.Dungeon.Layers - 1)
            expedition = null;
        return nest with { Expedition = expedition, Swarm = swarm };
    }
}
