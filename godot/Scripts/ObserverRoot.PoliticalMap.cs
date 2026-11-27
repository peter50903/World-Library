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
/// 第 08-25 章：舊的像素地圖已移除，其他既有的冒煙測試也用政治地圖（地形模式，<see cref="SmokeTerrainMode"/>）。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool PoliticalSmoke = OS.HasEnvironment("WORLD_SIM_POLITICAL_SMOKE");
    private static readonly string? MapShotDir = OS.HasEnvironment("WORLD_SIM_MAP_SHOT") ? OS.GetEnvironment("WORLD_SIM_MAP_SHOT") : null;

    /// <summary>
    /// 既有的冒煙測試（任何 WORLD_SIM_*_SMOKE，政治地圖的除外）用地形模式：點一下陸地就選到那一格（勢力模式第一下選到勢力）。
    /// 不引用其他靜態欄位（分散在不同檔的靜態欄位初始化順序不固定）。
    /// </summary>
    private static readonly bool SmokeTerrainMode = !OS.HasEnvironment("WORLD_SIM_POLITICAL_SMOKE") && !OS.HasEnvironment("WORLD_SIM_MAP_SHOT")
        && System.Environment.GetEnvironmentVariables().Keys.Cast<object>().Select(k => k.ToString() ?? string.Empty)
            .Any(k => k.StartsWith("WORLD_SIM_", StringComparison.Ordinal) && k.EndsWith("_SMOKE", StringComparison.Ordinal));

    private void VerifyPoliticalSmoke()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 2 * 52 * 7; d++)
            world.AdvanceOneDay();
        InstallWorld(world, "政治地圖冒煙");
        Refresh();
        if (_hexMap.MapMode != DemoHexMap.Mode.Realm)
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
        // 第 08-59 章：地面材質（草原、森林、水面）都要讀得到，否則地圖退回純色。
        if (!_hexMap.GroundTexturesReady)
            throw new InvalidOperationException("地圖的地面材質沒有讀到（godot/assets/map/）");
        GD.Print($"WORLD_SIM_POLITICAL_OK: Provinces={check.Provinces} Realms={check.Realms} Labels={check.Labels} Hit={check.Hits}/{check.Sites} Realm=1 Tooltip=1 Ground=1");
    }

    /// <summary>第 08-59 章：陸地格的上下左右有海（截圖找一段海岸用）。</summary>
    private static bool CoastCell(DemoHexWorld map, DemoHexCell cell)
        => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => map.Contains(cell.Column + d.Item1, cell.Row + d.Item2)
            && map.GetCell(cell.Column + d.Item1, cell.Row + d.Item2) is { Sea: true });

    /// <summary>
    /// 截圖縮成 <paramref name="width"/>×<paramref name="height"/>（或取 <paramref name="crop"/> 那一塊），存成有損 WebP 後 base64：「webp:資料」。
    /// 註記一則只收 4KB，原圖太大。第 08-59 章：地面改成寫實材質後，原本 252 色量化＋zlib 的縮圖又大又失真，改用 WebP（同樣大小約小一半、顏色正確）。
    /// </summary>
    private static string Thumb(Image image, int width, int height, Rect2I? crop)
    {
        var src = crop is { } region ? image.GetRegion(region) : (Image)image.Duplicate();
        if (src.GetWidth() != width || src.GetHeight() != height)
            src.Resize(width, height, Image.Interpolation.Lanczos);
        return "webp:" + Convert.ToBase64String(src.SaveWebpToBuffer(true, TempThumbQuality));
    }

    /// <summary>【TEMP】截圖縮圖的 WebP 品質（0～1）：0.8 看得清材質，一張全圖縮圖約 3～4 則註記。</summary>
    private const float TempThumbQuality = 0.8f;

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
        // 第 08-42 章：港口、航線（封鎖幾條）與海上地下城的數量（CI 註記裡看得到這張地圖有沒有海上的東西）。
        var lanes = world.SeaLanes();
        var seaNests = world.MonsterNests(world.CommittedWeeks).Count(n => n.AtSea);
        GD.Print($"WORLD_SIM_MAP_SHOT: Simulated={years}y Ms={watch.ElapsedMilliseconds} Ports={world.Ports().Count} Lanes={lanes.Count} Blocked={lanes.Count(l => l.Blocked)} SeaNests={seaNests}");
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
            // CI 另存小縮圖（WebP、base64；第 08-59 章），轉成註記給讀不到 artifact 的工作階段看；近景另存畫面中央一塊原尺寸。
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".thumb"), Thumb(image, 288, 180, null));
            if (name.Contains("close", StringComparison.Ordinal) || name.Contains("mid", StringComparison.Ordinal)) // 第 08-26 章：中距離也另存一塊原尺寸
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + "_crop.thumb"), Thumb(image, 320, 200,
                    new Rect2I(image.GetWidth() / 2 - 160, image.GetHeight() / 2 - 100, 320, 200)));
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
        // 第 08-46 章：戰況——有進行中的征服戰爭就拉近到軍隊（行軍中）或被圍的聚落。
        var war = world.Realms().Wars.Values.OrderBy(w => w.AttackerId).FirstOrDefault();
        if (war is not null)
        {
            var status = world.WarStatus(war);
            GD.Print($"WORLD_SIM_MAP_SHOT: War={war.AttackerId}->{war.TargetId} Stage={status.Stage} March={status.MarchLeft} Siege={status.SiegeDone}/{status.SiegeNeed} Holdings={war.Holdings.Count}");
            _hexMap.ZoomToCell(world.Map.GetCell(status.ArmyAt.Column, status.ArmyAt.Row), 44f);
            await Shot("10_war_mid");
        }
        else
            GD.Print("WORLD_SIM_MAP_SHOT: War=none");
        // 第 08-23 章：商路——拉近到上週有最多聚落賣糧給它的市集。
        var market = DemoFoodTrade.Links(world).GroupBy(l => l.To).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Row).ThenBy(g => g.Key.Column)
            .Select(g => (DemoCellRef?)g.Key).FirstOrDefault();
        if (market is { } m)
        {
            _hexMap.ZoomToCell(world.Map.GetCell(m.Column, m.Row), 64f);
            await Shot("7_trade_close");
        }
        // 第 08-33 章：領地大圖——建築最多的聚落與建築最多的村莊（整張縮圖＋中央一塊原尺寸）。
        async Task HoldingShot(string name)
        {
            for (var i = 0; i < 6; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var image = GetViewport().GetTexture().GetImage();
            var screen = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
            // 第 08-52 章：聚落頁（左邊詳情：示意圖、數值、建築、戰況、現況…同一頁）；放大圖是頁面上半的原尺寸。
            var page = _detailPanel.GetGlobalRect();
            var rect = new Rect2I((Vector2I)page.Position, (Vector2I)page.Size).Intersection(screen);
            if (rect.Size.X < 32 || rect.Size.Y < 32)
                return;
            image.GetRegion(rect).SavePng(System.IO.Path.Combine(dir, name + ".png"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".thumb"), Thumb(image, 170, Math.Max(32, 170 * rect.Size.Y / rect.Size.X), rect));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + "_crop.thumb"), Thumb(image, 320, 200,
                new Rect2I((Vector2I)page.Position + new Vector2I(0, 40), new Vector2I(Math.Min(320, rect.Size.X), 200)).Intersection(screen)));
            var art = FindHoldingArt(_detailTabs.GetCurrentTabControl());
            var scene = _world.HoldingScene(_detailCell is { } cell && _world.Sites.SettlementAt(cell.Column, cell.Row) is { } shown ? shown.Id : 0);
            GD.Print($"WORLD_SIM_MAP_SHOT: {name} {rect.Size.X}x{rect.Size.Y} Art={System.IO.Path.GetFileName(art?.WantedPath)} Delivered={art?.ArtPath is not null} Tier={scene?.Tier} "
                + $"Buildings={scene?.Holding.Buildings.Count} Houses={scene?.Holding.Dwellings}+{scene?.Holding.LargeDwellings} Fields={scene?.Fields}");
        }
        var holdings = world.Sites.Settlements.Where(s => !s.IsRuin)
            .OrderByDescending(s => world.HoldingOf(s.Id)?.Buildings.Count ?? 0).ThenBy(s => s.Id).ToList();
        foreach (var (site, name) in new[]
                 {
                     (holdings.FirstOrDefault(), "8_holding_big"),
                     (holdings.FirstOrDefault(s => world.ChronicleOf(s.Id).Tier == DemoSettlementTier.Village), "9_holding_village")
                 })
        {
            if (site is null)
                continue;
            ShowDetailAt(site.Column, site.Row);
            await HoldingShot(name);
            HideDetail();
        }
        // 第 08-47 章：戰爭總覽視窗（整個視窗頁縮圖）。
        OpenWar();
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        {
            var image = GetViewport().GetTexture().GetImage();
            var screen = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
            var page = _modalPanel.GetGlobalRect();
            var rect = new Rect2I((Vector2I)page.Position, (Vector2I)page.Size).Intersection(screen);
            if (rect.Size.X >= 32 && rect.Size.Y >= 32)
            {
                image.GetRegion(rect).SavePng(System.IO.Path.Combine(dir, "11_war_window.png"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "11_war_window.thumb"), Thumb(image, 320, Math.Max(32, 320 * rect.Size.Y / rect.Size.X), rect));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "11_war_window_crop.thumb"), Thumb(image, 320, 200,
                    new Rect2I((Vector2I)page.Position, new Vector2I(320, 200)).Intersection(screen)));
            }
            GD.Print($"WORLD_SIM_MAP_SHOT: 11_war_window {rect.Size.X}x{rect.Size.Y} Wars={_wars.Count}");
        }
        CloseModal();
        // 第 08-58 章：右側的史書（整個面板縮圖）。
        SetRightView(RightView.Saga);
        _sagaKey = default;
        RefreshSaga();
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        {
            var image = GetViewport().GetTexture().GetImage();
            var screen = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
            var page = _sagaPanel.GetGlobalRect();
            var rect = new Rect2I((Vector2I)page.Position, (Vector2I)page.Size).Intersection(screen);
            if (rect.Size.X >= 32 && rect.Size.Y >= 32)
            {
                image.GetRegion(rect).SavePng(System.IO.Path.Combine(dir, "15_saga.png"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "15_saga.thumb"), Thumb(image, 200, Math.Max(32, 200 * rect.Size.Y / rect.Size.X), rect));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "15_saga_crop.thumb"), Thumb(image, 300, 200,
                    new Rect2I((Vector2I)page.Position + new Vector2I(0, 60), new Vector2I(Math.Min(300, rect.Size.X), 200)).Intersection(screen)));
            }
            GD.Print($"WORLD_SIM_MAP_SHOT: 15_saga {rect.Size.X}x{rect.Size.Y}");
        }
        SetRightView(RightView.Outliner);
        // 第 08-55 章：信仰視窗（整個視窗頁縮圖）。
        OpenFaith();
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        {
            var image = GetViewport().GetTexture().GetImage();
            var screen = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
            var page = _modalPanel.GetGlobalRect();
            var rect = new Rect2I((Vector2I)page.Position, (Vector2I)page.Size).Intersection(screen);
            if (rect.Size.X >= 32 && rect.Size.Y >= 32)
            {
                image.GetRegion(rect).SavePng(System.IO.Path.Combine(dir, "14_faith_window.png"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "14_faith_window.thumb"), Thumb(image, 300, Math.Max(32, 300 * rect.Size.Y / rect.Size.X), rect));
            }
            GD.Print($"WORLD_SIM_MAP_SHOT: 14_faith_window {rect.Size.X}x{rect.Size.Y} Faiths={_world.Culture.Faith.Faiths.Count}");
        }
        CloseModal();
        _hexMap.ShowWholeWorld();
        SetMapMode(DemoHexMap.Mode.Terrain);
        await Shot("4_terrain_far");
        // 第 08-59 章：地形模式拉近（只看地面材質，沒有勢力色）——跟定樣的近景樣張並排比；再看一段海岸（沙灘、淺水、深水）。
        if (capital is { } home)
        {
            _hexMap.ZoomToCell(home, 96f);
            await Shot("16_terrain_close");
            var coast = world.Map.Cells.Where(x => x.Terrain != DemoTerrain.Water && CoastCell(world.Map, x))
                .OrderBy(x => Math.Abs(x.Column - home.Column) + Math.Abs(x.Row - home.Row)).ThenBy(x => x.Row).ThenBy(x => x.Column)
                .FirstOrDefault();
            if (coast is not null)
            {
                _hexMap.ZoomToCell(coast, 96f);
                await Shot("17_coast_close");
            }
        }
        SetMapMode(DemoHexMap.Mode.Culture);
        await Shot("5_culture_far");
        // 第 08-54 章：信仰模式（信仰家族的顏色、名字上的符號、聖地、跟統治者不同的條紋）。
        SetMapMode(DemoHexMap.Mode.Faith);
        await Shot("13_faith_far");
        GD.Print($"WORLD_SIM_MAP_SHOT: Faiths={world.Culture.Faith.Faiths.Count(f => f.ExtinctWeek == 0)} Schisms={world.Culture.Faith.Faiths.Count(f => f.ParentId != 0)} "
            + $"HolySites={_hexMap.HolySiteFaiths.Count}");
        SetMapMode(DemoHexMap.Mode.Realm);
        // 第 12-2j 章：指定黑暗時代、推進一週，看長夜的氛圍與獸潮將至的標記。
        world.AgeSetting = DemoAge.Dark;
        for (var d = 0; d < 7; d++)
            world.AdvanceOneDay();
        Refresh();
        await Shot("6_dark_age");
        // 第 08-51 章：人類與怪物爭地——黑暗時代再推進到有遠征軍或獸潮在路上（最多一年），拉近到行動的中途。
        DemoMonsterNest? Front() => world.MonsterNests(world.CommittedWeeks)
            .Where(n => n.Swarm is not null || n.Expedition is not null).OrderByDescending(n => n.Swarm is not null).ThenBy(n => n.Dungeon.EventId).FirstOrDefault();
        var waited = 0;
        while (Front() is null && waited < 52)
        {
            for (var d = 0; d < 7; d++)
                world.AdvanceOneDay();
            waited++;
        }
        if (Front() is { } front && (front.Swarm ?? front.Expedition) is { } trip
            && world.Sites.Settlements.FirstOrDefault(s => s.Id == trip.SettlementId) is { } frontSite)
        {
            Refresh();
            var mid = new DemoCellRef((front.Dungeon.Cell.Column + frontSite.Column) / 2, (front.Dungeon.Cell.Row + frontSite.Row) / 2);
            _hexMap.ZoomToCell(world.Map.GetCell(mid.Column, mid.Row), 44f);
            GD.Print($"WORLD_SIM_MAP_SHOT: Front={(front.Swarm is not null ? "swarm" : "expedition")} Lair={front.Dungeon.Name} Site={frontSite.Name} "
                + $"Week={world.CommittedWeeks} Depart={trip.DepartWeek} Arrive={trip.ArriveWeek} Layers={front.Dungeon.Layers} Waited={waited}w "
                + $"Trips={world.MonsterNests(world.CommittedWeeks).Count(n => n.Swarm is not null || n.Expedition is not null)}");
            await Shot("12_monster_front_mid");
        }
        else
            GD.Print($"WORLD_SIM_MAP_SHOT: Front=none Waited={waited}w");
        GD.Print("WORLD_SIM_MAP_SHOT_OK");
        GetTree().Quit();
    }
}
