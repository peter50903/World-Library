using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-35 章：建設大事——重要建築建成、毀損寫進聚落小史與世界編年史。</summary>
public sealed class DemoBuildingHistoryTests
{
    private static DemoBuilding B(int id, string defId, DemoBuildingStatus status) => new(id, defId, status, 0, status == DemoBuildingStatus.Standing ? 1000 : 0, 1);

    private static DemoBuildingsState State(int settlementId, params DemoBuilding[] buildings)
        => new(new[] { DemoHolding.EmptyFor(settlementId) with { Buildings = buildings } }, 100);

    [Fact]
    public void LandmarksThatFinishOrFallBecomeEvents()
    {
        var sites = new[] { new DemoSettlement(1, "霍克堡", 3, 3), new DemoSettlement(2, "林谷", 6, 6) };
        var before = new DemoBuildingsState(new[]
        {
            DemoHolding.EmptyFor(1) with
            {
                Buildings = new[]
                {
                    B(1, DemoBuildings.WallStone, DemoBuildingStatus.Building), B(2, "BUILD_CASTLE", DemoBuildingStatus.Standing),
                    B(3, "BUILD_SMITHY", DemoBuildingStatus.Building), B(4, "BUILD_CHURCH", DemoBuildingStatus.Standing)
                }
            },
            DemoHolding.EmptyFor(2) with { Buildings = new[] { B(5, "BUILD_CHURCH", DemoBuildingStatus.Standing) } }
        }, 6);
        var after = new DemoBuildingsState(new[]
        {
            DemoHolding.EmptyFor(1) with
            {
                Buildings = new[]
                {
                    B(1, DemoBuildings.WallStone, DemoBuildingStatus.Standing), B(2, "BUILD_CASTLE", DemoBuildingStatus.Ruined),
                    B(3, "BUILD_SMITHY", DemoBuildingStatus.Standing), B(4, "BUILD_CHURCH", DemoBuildingStatus.Standing)
                }
            },
            DemoHolding.EmptyFor(2) with { Buildings = new[] { B(5, "BUILD_CHURCH", DemoBuildingStatus.Ruined) } }
        }, 6);
        var battle = new DemoSettlementEvent(10, TimeSpan.FromDays(70), 1, "霍克堡", DemoSettlementEventKind.ConquestBattle, 12, "征服戰爭", EventId: 40);
        var ruin = new DemoSettlementEvent(10, TimeSpan.FromDays(70), 2, "林谷", DemoSettlementEventKind.SettlementRuined, 0, "人口歸零", EventId: 41);
        var events = DemoBuildings.Milestones(before, after, 10, TimeSpan.FromDays(70), sites, _ => DemoSettlementTier.City, new[] { battle, ruin }, 42);

        // 石牆建成（鐵匠鋪不是重要建築，不記）；城堡毀於戰火、直接原因是那場交戰；成為遺跡的聚落不另記。
        Assert.Equal(2, events.Count);
        var built = events[0];
        Assert.Equal((DemoSettlementEventKind.BuildingCompleted, 1, 1L, "建成石牆", 42L, 0L),
            (built.Kind, built.SettlementId, built.Count, built.Cause, built.EventId, built.CauseEventId));
        var fallen = events[1];
        Assert.Equal((DemoSettlementEventKind.BuildingRuined, 1, 2L, "城堡毀於戰火", 43L, 40L),
            (fallen.Kind, fallen.SettlementId, fallen.Count, fallen.Cause, fallen.EventId, fallen.CauseEventId));
        // 沒有變化就沒有事件。
        Assert.Empty(DemoBuildings.Milestones(after, after, 11, TimeSpan.FromDays(77), sites, _ => DemoSettlementTier.City,
            Array.Empty<DemoSettlementEvent>(), 50));
        // 營、村的神殿叫祭壇。
        var shrine = DemoBuildings.Milestones(State(2, B(5, "BUILD_CHURCH", DemoBuildingStatus.Building)), State(2, B(5, "BUILD_CHURCH", DemoBuildingStatus.Standing)),
            12, TimeSpan.FromDays(84), sites, _ => DemoSettlementTier.Village, Array.Empty<DemoSettlementEvent>(), 60);
        Assert.Equal("建成祭壇", Assert.Single(shrine).Cause);
    }

    [Fact]
    public void BuildingEventsAreRegisteredAndValid()
    {
        Assert.Equal("TEMP_EV_BUILDING_COMPLETED", DemoEventTypes.Of(DemoSettlementEventKind.BuildingCompleted).TypeId);
        Assert.Equal("TEMP_EV_BUILDING_RUINED", DemoEventTypes.Of(DemoSettlementEventKind.BuildingRuined).TypeId);
        Assert.True(DemoBuildings.IsLandmark(DemoBuildings.WallWood));
        Assert.True(DemoBuildings.IsLandmark("BUILD_CASTLE"));
        Assert.False(DemoBuildings.IsLandmark("BUILD_SMITHY"));
        Assert.False(DemoBuildings.IsLandmark(DemoBuildings.GateWood));
        Assert.Contains("建成城堡", DemoWorldChronicle.WorldFirstBuildings);
    }

    [Fact]
    public void RealWorldsRecordTheirLandmarksInHistory()
    {
        // 中地圖跑到第一座重要建築蓋好為止（中地圖四十年多半還沒有；最多一百年）：
        // 它有「建成」事件，聚落小史與世界編年史都看得到，因果合法。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        DemoBuilding? landmark = null;
        for (var year = 0; year < 100 && landmark is null; year++)
        {
            for (var d = 0; d < TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
            landmark = world.Buildings.Holdings.SelectMany(h => h.Buildings)
                .FirstOrDefault(b => DemoBuildings.IsLandmark(b.DefId) && b.Status == DemoBuildingStatus.Standing);
        }
        Assert.NotNull(landmark);
        var completed = world.SettlementEvents().Where(e => e.Kind == DemoSettlementEventKind.BuildingCompleted && e.Count == landmark!.Id).ToList();
        Assert.NotEmpty(completed); // 毀損後重建、木牆改石牆會再記一次
        var first = completed[0];
        Assert.Contains(world.ChronicleOf(first.SettlementId).Milestones, l => l.Text == first.Cause);
        Assert.Contains(DemoWorldChronicle.Build(world).SelectMany(y => y.Entries), x => x.Category == DemoChronicleCategory.Building);
        Assert.Empty(world.BuildCausalIndex().Problems);
    }
}
