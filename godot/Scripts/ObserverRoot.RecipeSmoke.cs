using Godot;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 02 章 a 節 CI 冒煙：用真實的世界種子／歷史種子輸入框與按鈕訊號，驗證
/// 同配方重建相同世界、只換歷史種子時地圖與聚落相同、錯誤輸入不改世界、已推進世界需確認、
/// 存檔槽不被觸碰。只在 CI 設定 WORLD_SIM_RECIPE_SMOKE 時執行。
/// </summary>
public partial class ObserverRoot
{
    private void VerifyRecipeSmoke(Button nextButton)
    {
        void Fail(string why) => throw new InvalidOperationException("Recipe smoke: " + why);
        void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        string Slots() => string.Join("\n", DemoSaveSlots.ListAll().Select(i => i.Describe()));
        void Type(string world, string history)
        {
            _worldSeedInput.Text = world;
            _historySeedInput.Text = history;
        }

        var slotsBefore = Slots();

        // 1. 第 0 日：輸入 123／456 直接建立，與 Core 依同配方建立的世界完全相同。
        Type("123", "456");
        Press(_seedStartButton);
        var reference = new DemoObserverWorld(WorldRecipe.ForSeeds(123, 456), _world.WeeklyConfig);
        if (_world.Recipe != WorldRecipe.ForSeeds(123, 456)
            || _world.ExportTempSave() != reference.ExportTempSave()
            || !_status.Text.Contains("世界種子：123", StringComparison.Ordinal)
            || !_status.Text.Contains("歷史種子：456（自訂）", StringComparison.Ordinal)
            || !_status.Text.Contains($"設定雜湊：{OfficialConfig.Current.ConfigHash[..16]}", StringComparison.Ordinal))
            Fail("seed 123/456 did not rebuild the same Core world or status.");
        var sameRecipe = 1;

        // 2. 錯誤輸入：世界與存檔槽都不變。
        var beforeBad = _world.ExportTempSave();
        foreach (var (w, h) in new[] { ("abc", ""), ("-1", ""), ("18446744073709551616", ""), ("5", "x") })
        {
            Type(w, h);
            Press(_seedStartButton);
            if (_world.ExportTempSave() != beforeBad || _confirmRow.Visible
                || !_notice.Text.StartsWith("種子必須是 0～18446744073709551615 的整數", StringComparison.Ordinal))
                Fail($"bad seed input '{w}'/'{h}' changed the world.");
        }

        // 3. 已推進的世界：要先確認；取消不改世界；確認後只換歷史種子 → 地圖與聚落相同、指紋不同。
        for (var i = 0; i < 3; i++)
            Press(nextButton);
        var day3 = _world.ExportTempSave();
        Type("123", "789");
        Press(_seedStartButton);
        if (_world.ExportTempSave() != day3 || !_confirmRow.Visible)
            Fail("an advanced world was replaced without confirmation.");
        Press(_cancelScenarioButton);
        if (_world.ExportTempSave() != day3 || _confirmRow.Visible)
            Fail("cancel changed the world.");
        Press(_seedStartButton);
        Press(_confirmScenarioButton);
        if (_world.Recipe != WorldRecipe.ForSeeds(123, 789) || _world.Snapshot().DaysElapsed != 0)
            Fail("confirmed seed 123/789 was not built.");
        var mapSame = true;
        for (var row = 0; row < _world.Map.Rows; row++)
            for (var col = 0; col < _world.Map.Columns; col++)
                mapSame &= _world.Map.GetCell(col, row) == reference.Map.GetCell(col, row);
        if (!mapSame
            || !_world.Sites.Settlements.SequenceEqual(reference.Sites.Settlements)
            || !_world.Sites.PopGroups.SequenceEqual(reference.Sites.PopGroups)
            || _world.RngState().SequenceEqual(reference.RngState()))
            Fail("changing only the history seed changed the map/sites or left the history RNG streams unchanged.");

        // 4. 存檔槽完全沒被動到。
        if (Slots() != slotsBefore)
            Fail("seed inputs touched a save slot.");

        GD.Print($"WORLD_SIM_RECIPE_OK: World=123 History=456 SameRecipe={sameRecipe} HistoryOnlyMapSame=1 " +
            "BadSeedRejected=1 ConfirmRequired=1 SlotsUnchanged=1");
    }
}
