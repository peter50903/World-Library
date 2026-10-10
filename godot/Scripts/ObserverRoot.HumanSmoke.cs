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
        for (var day = 0; day < 3 * 365; day++) world.AdvanceOneDay();
        var births = world.WeekReports().Sum(w => w.Settlements.Sum(l => l.Births));
        if (births <= 0 || world.HumanPopulationSnapshot is null)
            throw new InvalidOperationException("Human birth mode did not produce a real newborn.");
        var people = world.HumanPopulationSnapshot.People;
        if (!people.Any(p => !p.IsAdultAt(3 * 365, DemoTempDemographyRules.HumanFixture)))
            throw new InvalidOperationException("No dependent children were born.");
        if (world.WeekReports().Any(w => w.Settlements.Any(l => !l.IsBalanced)))
            throw new InvalidOperationException("Real weekly population ledger failed.");
        InstallWorld(world, "人類繁衍真週帳測試");
        var location = world.Sites.Settlements.First().Id;
        if (!FoodDiagnosticsText(_world, location).Contains("人類本週出生", StringComparison.Ordinal))
            throw new InvalidOperationException("Godot did not display the Core-based birth ledger.");
        var save = _world.ExportTempSave();
        var restored = DemoObserverWorld.ImportTempSave(save);
        if (restored.ExportTempSave() != save)
            throw new InvalidOperationException("Godot human scenario did not round-trip through TEMP save.");
        GD.Print($"WORLD_SIM_HUMAN_OK: Births={births} ChildWorkers=0 Save=1");
    }
}
