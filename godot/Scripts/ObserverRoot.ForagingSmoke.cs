using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// True Godot 4.7.2 .NET headless test of the *selected playable* TEMP food world,
/// including Core-backed week reports, map colours/river markers and v2 save/load.
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool ForagingSmoke = OS.HasEnvironment("WORLD_SIM_FORAGING_SMOKE");

    private void VerifyForagingSmoke(Button nextButton)
    {
        void Fail(string why) => throw new InvalidOperationException("Foraging smoke: " + why);

        // The user-selectable selector must install the same Core world as a real F5 player.
        InstallWorld(CreateTempWorld(TempScenario.Foraging), "森林／河流試玩");
        if (!_world.UsesSharedForaging || _scenarioPicker.Selected != (int)TempScenario.Foraging)
            Fail("selectable TEMP forest/river scenario is not active");
        if (_hexMap.MainRiverTiles.Count == 0)
            Fail("TEMP main-river tiles are not visible");
        if (_world.Sites.Settlements.Any(s => _world.Map.GetCell(s.Column, s.Row).Terrain == DemoTerrain.Water))
            Fail("a settlement occupies a water/main-river hex");
        if (_world.Sites.ResourcePoints.Any(p => p.Kind == DemoResourceKind.Farmland))
            Fail("undeveloped grassland was automatically turned into farmland");

        // Click a real Core forest cell; its gold catchment outlines must be the six-neighbour geometry.
        var forest = _world.SharedFoodSources().First(s => s.Kind == DemoCatchmentSourceKind.Forest);
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true,
            Position = _hexMap.CellCenter(_world.Map.GetCell(forest.Column, forest.Row))
        });
        var eligible = DemoHexFoodCatchment.EligibleCells(_world.Map, forest);
        if (_hexMap.FoodReachCells.Count != eligible.Count)
            Fail("clicking a forest did not highlight its real Core catchment");

        var settlement = _world.Sites.Settlements[0];
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = _hexMap.CellCenter(_world.Map.GetCell(settlement.Column, settlement.Row))
        });
        if (_foodSettlementId != settlement.Id || !_foodInfo.Text.Contains("森林／河流採集生存", StringComparison.Ordinal))
            Fail("click does not select the Core-backed foraging diagnostics");

        for (var i = 0; i < 35; i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.LatestWeek?.Week != 5)
            Fail("real Godot Next button did not advance Core 5 settlement weeks");
        var harvest = _world.LatestSharedHarvest()!;
        var actual = _world.LatestWeek!.Settlements.Sum(l => l.Harvested);
        if (actual != harvest.TotalHarvested || _foodInfo.Text != FoodDiagnosticsText(_world, settlement.Id)
            || !_foodInfo.Text.Contains($"實採 {(_world.LatestWeek!.Settlements.Single(l => l.SettlementId == settlement.Id).Harvested)}",
                StringComparison.Ordinal))
            Fail("Godot food display differs from Core shared source accounting");
        foreach (var source in _world.SharedFoodSources())
            if (harvest.Harvests.Where(h => h.SourceId == source.Id).Sum(h => h.FoodHarvested) > source.FoodAvailable)
                Fail("a forest or river source has been collected more than once");
        // 第 04 章 c 節：讀帳結果＝依地圖重算；畫面「來源→採收」帶預算與未採。
        var recomputed = _world.RecomputeLatestSharedHarvest()!;
        if (_world.LatestWeek!.Sources.Count == 0
            || !harvest.Harvests.OrderBy(h => h.SourceId).ThenBy(h => h.SettlementId)
                .SequenceEqual(recomputed.Harvests.OrderBy(h => h.SourceId).ThenBy(h => h.SettlementId))
            || _world.LatestWeek.Sources.Any(x => recomputed.UnharvestedSourceFood[x.SourceId] != x.Unharvested))
            Fail("committed source ledger differs from the recomputed shared allocation");
        var sharesHere = _world.LatestWeek.Sources.Any(x => x.Shares.Any(sh => sh.SettlementId == settlement.Id));
        if (sharesHere && !_foodInfo.Text.Contains("（預算 ", StringComparison.Ordinal))
            Fail("food diagnostics does not show source budget/unharvested from the ledger");

        var save = _world.ExportTempSave();
        var restored = WorldSimulation.Core.World.DemoObserverWorld.ImportTempSave(save);
        if (!restored.UsesSharedForaging || restored.ExportTempSave() != save)
            Fail("foraging TEMP v2 save/load did not round-trip");
        InstallWorld(restored, "森林／河流讀檔完成");
        if (_scenarioPicker.Selected != (int)TempScenario.Foraging || _foodSettlementId != 0
            || _hexMap.MainRiverTiles.Count == 0)
            Fail("load did not restore the selected river mode or reset old selection");

        InstallWorld(CreateTempWorld(TempScenario.CrowdedForaging), "觀察過密人口與缺糧");
        if (_scenarioPicker.Selected != (int)TempScenario.CrowdedForaging)
            Fail("crowded food scenario picker did not select the new option");
        for (var i = 0; i < 7; i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.LatestWeek?.Settlements.Sum(l => l.FoodShortage) <= 0
            || _world.LatestWeek!.Settlements.Sum(l => l.Deaths) <= 0)
            Fail("crowded settlements did not face actual shortage/death");
        if (_world.LatestSharedHarvest()?.TotalHarvested <= 0)
            Fail("crowded mode falsified starvation by switching off all actual food sources");
        GD.Print($"WORLD_SIM_FORAGING_OK: Weeks=5 Harvest={actual} Forest20=1 River5=1 NoFreeFarm=1 SaveRoundTrip=1 CrowdedStarved=1 SourceLedger=1");
    }
}
