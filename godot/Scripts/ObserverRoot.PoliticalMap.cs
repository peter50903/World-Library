using System.Globalization;
using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-21 章：政治地圖的冒煙測試與截圖。
/// <list type="bullet">
/// <item><c>WORLD_SIM_POLITICAL_SMOKE</c>（無畫面）：中地圖兩年，省份、勢力配色、勢力名、點選（滑鼠下的格屬於哪個省份、點空地選到勢力）、滑鼠提示。</item>
/// <item><c>WORLD_SIM_MAP_SHOT</c>＝輸出資料夾（要有畫面，CI 用 xvfb）：大地圖跑 <c>WORLD_SIM_MAP_SHOT_YEARS</c> 年（預設 30），
///   拍整個世界、中距離、近距離、地形模式、文化模式五張 PNG，給人看畫面長怎樣。</item>
/// </list>
/// 其他既有的冒煙測試照舊用舊地圖（<see cref="LegacyMapSmoke"/>），點選規則與座標不變。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool PoliticalSmoke = OS.HasEnvironment("WORLD_SIM_POLITICAL_SMOKE");
    private static readonly string? MapShotDir = OS.HasEnvironment("WORLD_SIM_MAP_SHOT") ? OS.GetEnvironment("WORLD_SIM_MAP_SHOT") : null;

    /// <summary>
    /// 既有的冒煙測試（任何 WORLD_SIM_*_SMOKE，政治地圖的除外）照舊用舊地圖的地形模式。
    /// 不引用其他靜態欄位（分散在不同檔的靜態欄位初始化順序不固定）。
    /// </summary>
    private static readonly bool LegacyMapSmoke = !OS.HasEnvironment("WORLD_SIM_POLITICAL_SMOKE") && !OS.HasEnvironment("WORLD_SIM_MAP_SHOT")
        && System.Environment.GetEnvironmentVariables().Keys.Cast<object>().Select(k => k.ToString() ?? string.Empty)
            .Any(k => k.StartsWith("WORLD_SIM_", StringComparison.Ordinal) && k.EndsWith("_SMOKE", StringComparison.Ordinal));

    private void VerifyPoliticalSmoke()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 2 * 52 * 7; d++)
            world.AdvanceOneDay();
        InstallWorld(world, "政治地圖冒煙");
        Refresh();
        if (!_hexMap.Political || _hexMap.MapMode != DemoHexMap.Mode.Realm)
            throw new InvalidOperationException("政治地圖不是預設（應為政治地圖的勢力模式）");
        var check = _hexMap.PoliticalSmokeCheck();
        var realmClicks = 0;
        _hexMap.RealmSelected += _ => realmClicks++;
        // 點一塊不是聚落格、但屬於某個省份的陸地：勢力模式第一下選到勢力。
        var land = check.LandPoint ?? throw new InvalidOperationException("政治地圖找不到可以點的省份陸地");
        _hexMap._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = land });
        if (realmClicks != 1)
            throw new InvalidOperationException($"點省份陸地應選到勢力（實際 {realmClicks} 次）");
        if (string.IsNullOrWhiteSpace(_hexMap._GetTooltip(land)))
            throw new InvalidOperationException("政治地圖的滑鼠提示是空的");
        GD.Print($"WORLD_SIM_POLITICAL_OK: Provinces={check.Provinces} Realms={check.Realms} Labels={check.Labels} Hit={check.Hits}/{check.Sites} Realm=1 Tooltip=1");
    }

    /// <summary>【TEMP】截圖的種子與年數（大地圖）。</summary>
    private const ulong TempShotSeed = 7;
    private const int TempShotYears = 30;

    private async void RunMapShot(string dir)
    {
        var years = int.TryParse(OS.GetEnvironment("WORLD_SIM_MAP_SHOT_YEARS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : TempShotYears;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(TempShotSeed, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < years * 52 * 7; d++)
            world.AdvanceOneDay();
        GD.Print($"WORLD_SIM_MAP_SHOT: Simulated={years}y Ms={watch.ElapsedMilliseconds}");
        InstallWorld(world, "截圖");
        Refresh();
        System.IO.Directory.CreateDirectory(dir);
        async Task Shot(string name)
        {
            _hexMap.QueueRedraw();
            for (var i = 0; i < 4; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var image = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.Combine(dir, name + ".png");
            image.SavePng(path);
            // CI 另存縮小的 JPG（轉成註記給讀不到 artifact 的工作階段看）。
            var small = (Image)image.Duplicate();
            small.Resize(1120, image.GetHeight() * 1120 / Math.Max(1, image.GetWidth()), Image.Interpolation.Lanczos);
            small.SaveJpg(System.IO.Path.Combine(dir, name + ".jpg"), 0.72f);
            GD.Print($"WORLD_SIM_MAP_SHOT: {path} {image.GetWidth()}x{image.GetHeight()}");
        }
        _hexMap.ShowWholeWorld();
        await Shot("1_realms_far");
        var capital = _hexMap.LargestRealmCapital();
        if (capital is { } c)
        {
            _hexMap.ZoomToCell(c, 44f);
            await Shot("2_realms_mid");
            _hexMap.ZoomToCell(c, 96f);
            await Shot("3_realms_close");
        }
        _hexMap.ShowWholeWorld();
        SetMapMode(DemoHexMap.Mode.Terrain);
        await Shot("4_terrain_far");
        SetMapMode(DemoHexMap.Mode.Culture);
        await Shot("5_culture_far");
        SetMapMode(DemoHexMap.Mode.Realm);
        GD.Print("WORLD_SIM_MAP_SHOT_OK");
        GetTree().Quit();
    }
}
