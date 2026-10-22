using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>#105 true Godot-engine smoke, reads the same live Core ledger as F5 player.</summary>
public partial class ObserverRoot
{
    private void VerifyHumanLifecycleSmoke()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42),
            DemoWeeklyTempConfig.LivingForagingSample);
        // C02：ChildWorkers 是量測值——每次新週報出現時，用推進前的人口快照（LastWeek＝上一週）
        // 逐聚落數實際成年人，週帳申報的成年勞力超過實際成年人的部分就是「兒童被當勞力」。
        long childWorkers = 0;
        for (var day = 0; day < 3 * 365; day++)
        {
            var before = world.HumanPopulationSnapshot; // 第 07-1 章：群體人口快照
            var weeksBefore = world.WeekReports().Count;
            world.AdvanceOneDay();
            var reports = world.WeekReports();
            if (reports.Count == weeksBefore || before is null)
                continue;
            foreach (var ledger in reports[reports.Count - 1].Settlements)
            {
                var adults = before.Groups.Where(g => g.SettlementId == ledger.SettlementId && g.BatchId == 0
                    && DemoCohortDemography.AgeAt(g.BirthYear, before.LastWeek * 7) >= DemoTempDemographyRules.HumanFixture.AdultAgeYears)
                    .Sum(g => g.Count);
                childWorkers += Math.Max(0L, ledger.AdultWorkersOpening - adults);
            }
        }
        if (childWorkers > 0)
            throw new InvalidOperationException($"Human smoke: {childWorkers} child(ren) counted as adult workers.");
        var births = world.WeekReports().Sum(w => w.Settlements.Sum(l => l.Births));
        if (births <= 0 || world.HumanPopulationSnapshot is null)
            throw new InvalidOperationException("Human birth mode did not produce a real newborn.");
        var children = world.WorldPopulationStats?.Children ?? 0;
        if (children <= 0)
            throw new InvalidOperationException("No dependent children were born.");
        if (world.WeekReports().Any(w => w.Settlements.Any(l => !l.IsBalanced)))
            throw new InvalidOperationException("Real weekly population ledger failed.");
        InstallWorld(world, "人類繁衍真週帳測試");
        var location = world.Sites.Settlements.First().Id;
        var panel = FoodDiagnosticsText(_world, location);
        if (!panel.Contains("人類本週出生", StringComparison.Ordinal))
            throw new InvalidOperationException("Godot did not display the Core-based birth ledger.");
        var latest = _world.LatestWeek!;
        var own = latest.Settlements.Single(l => l.SettlementId == location);
        if (!panel.Contains($"飢餓債 {own.HungerDebtClosing}，出生 {own.Births}、自然死亡 {own.NaturalDeaths}、餓死 {own.StarvationDeaths}", StringComparison.Ordinal))
            throw new InvalidOperationException("Godot settlement panel does not show this settlement's own births, natural deaths and starvation deaths.");
        var worldStarvation = latest.Settlements.Sum(l => l.StarvationDeaths);
        if (!panel.Contains($"自然死亡 {latest.Settlements.Sum(l => l.NaturalDeaths)}、餓死 {worldStarvation}；", StringComparison.Ordinal)
            || !panel.Contains($"聚落餓死 {worldStarvation}", StringComparison.Ordinal))
            throw new InvalidOperationException("Godot world starvation total differs from the human starvation row.");
        var save = _world.ExportTempSave();
        var restored = DemoObserverWorld.ImportTempSave(save);
        if (restored.ExportTempSave() != save)
            throw new InvalidOperationException("Godot human scenario did not round-trip through TEMP save.");
        GD.Print($"WORLD_SIM_HUMAN_OK: Births={births} ChildWorkers={childWorkers} Children={children} Save=1");
    }
}
