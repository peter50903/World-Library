using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>地圖上的一次天災（事件編號、種類、發生週、受災聚落格；隕石另帶落點）。</summary>
public sealed record DisasterMark(long EventId, DemoSettlementEventKind Kind, int Week, DemoCellRef Center, DemoCellRef? Impact = null);

/// <summary>
/// 試玩回饋 2026-11-03 之 3（天災沒有過程，只是突然換地塊素材）：天災改成看得見的過程。只影響畫面，規則與世界不變。
/// ① 依遊戲時間逐漸變化的地表疊層（交給 <see cref="TerrainSurface"/> 著色器）：野火逐格延燒→焦土→兩年內慢慢長回、
///    熱浪讓草地漸漸枯黃再轉綠、洪水淹上田地再退去、龍捲風留下破壞痕跡、地震裂縫、隕石坑淡去後露出礦脈。
/// ② 事件發生當下的兩秒動畫（真實時間，快轉也看得到）：隕石劃過天空墜落閃光、龍捲風漏斗掃過、洪水與地震的波紋；燃燒處冒煙。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>近一年的天災（由觀察者介面每次刷新時給）。</summary>
    public IReadOnlyList<DisasterMark> DisasterMarks
    {
        get => _disasterMarks;
        set
        {
            _disasterMarks = value;
            var now = Time.GetTicksMsec();
            foreach (var mark in value)
            {
                // 剛發生（本週或上週）而且第一次看到的才播動畫；讀檔看到的舊天災不播。
                if (_seenDisasters.Add(mark.EventId) && mark.Week >= CurrentWeek - 1 && _disasterMarksPrimed)
                {
                    _impacts.Add((mark, now));
                    PlaySfx(mark.Kind switch
                    {
                        DemoSettlementEventKind.Earthquake => ArtAssets.SfxEarthquake,
                        DemoSettlementEventKind.Meteorite => ArtAssets.SfxMeteor,
                        DemoSettlementEventKind.Flood => ArtAssets.SfxFlood,
                        _ => ArtAssets.SfxTornado
                    });
                }
            }
            _disasterMarksPrimed = true;
        }
    }

    private IReadOnlyList<DisasterMark> _disasterMarks = Array.Empty<DisasterMark>();
    private readonly HashSet<long> _seenDisasters = new();
    private bool _disasterMarksPrimed;
    private readonly List<(DisasterMark Mark, ulong StartMs)> _impacts = new();

    /// <summary>疊層種類（資料貼圖第二個位元組）。</summary>
    public enum Overlay : byte { None = 0, Fire = 1, Scorch = 2, Dry = 3, Flood = 4, Debris = 5, Cracks = 6, Crater = 7, Ore = 8, Scar = 9 }

    /// <summary>【TEMP】第 08-18b 章第 5 節：龍捲風走完路徑要幾週（遊戲時間）、留下的刮痕幾週淡去；洪水高水位維持幾週。</summary>
    public const float TempTornadoWeeks = 1f;
    public const float TempScarWeeks = 26f;
    public const float TempFloodHighWeeks = 3f;

    /// <summary>有正在移動的天災物體（龍捲風、漲水）——要持續重畫。</summary>
    private bool _movingDisaster;

    /// <summary>【TEMP】動畫長度（毫秒）、野火每格延燒間隔（週）、各疊層的持續週數。</summary>
    public const ulong TempImpactMs = 2000;
    public const float TempFireSpreadWeeks = 0.6f;
    public const float TempFireBurnWeeks = 3.5f;
    public const float TempScorchHoldWeeks = 52f;
    public const float TempDryRampWeeks = 6f;
    public const float TempFloodWeeks = 8f;
    public const float TempDebrisWeeks = 10f;
    public const float TempCracksWeeks = 12f;
    public const float TempCraterWeeks = 26f;

    /// <summary>遊戲時間（週，含本週已過的部分）。</summary>
    private float GameWeeks => CurrentWeek + WeekFraction;

    public override void _Process(double delta)
    {
        UpdateFireLoop();
        if (_surfaceStale && Time.GetTicksMsec() - _surfaceBuiltMs >= TempSurfaceRefreshMs)
            QueueRedraw();
        if (!WantsAnimation)
            return;
        var now = Time.GetTicksMsec();
        _impacts.RemoveAll(x => now - x.StartMs > TempImpactMs);
        if (now - _lastAnimatedMs < TempAnimateEveryMs)
            return; // 第 08-16 章：動畫限速重畫（大地圖不每幀重畫）
        _lastAnimatedMs = now;
        QueueRedraw();
    }

    private bool _hasFire;

    /// <summary>每格的疊層（種類、強度 0～1）；依格的索引（列×欄數＋欄）。</summary>
    private void FillOverlays(byte[] data)
    {
        var t = GameWeeks;
        var columns = World!.Columns;
        _hasFire = false;
        _movingDisaster = false;
        _decals.Clear();
        // 有素材的地面痕跡（坑、裂縫、殘骸）改畫成貼圖（DrawDisasterEffects），沒有才交給著色器。
        void Decal(DemoCellRef c, Overlay kind, float amount)
        {
            if (amount <= 0f || !World!.Contains(c.Column, c.Row))
                return;
            if (ArtAssets.Texture(DecalPath(kind)) is not null)
                _decals.Add((c, kind, Mathf.Clamp(amount, 0f, 1f)));
            else
                Set(c, kind, amount);
        }
        void Set(DemoCellRef c, Overlay kind, float amount)
        {
            if (!World.Contains(c.Column, c.Row) || amount <= 0f)
                return;
            var i = (c.Row * columns + c.Column) * 4;
            data[i + 1] = (byte)kind;
            data[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(amount * 255f), 0, 255);
            if (kind == Overlay.Fire)
                _hasFire = true;
        }

        // 隕石礦脈（不在丘陵上的也看得到）。
        foreach (var ore in MeteoriteOre)
            if (World.Contains(ore.Column, ore.Row) && World.GetCell(ore.Column, ore.Row).Terrain != DemoTerrain.Hills)
                Set(ore, Overlay.Ore, 1f);

        foreach (var spell in Spells)
        {
            var d = t - spell.FromWeek;
            if (d < 0f || t > spell.ToWeek)
                continue;
            if (spell.Kind == DemoSettlementEventKind.Wildfire)
            {
                // 逐格延燒：第 k 格晚 k×0.6 週點燃，燒約 3.5 週後熄成焦土；一年後兩年內慢慢長回（結束時剛好恢復）。
                for (var k = 0; k < spell.Cells.Count; k++)
                {
                    var dk = d - k * TempFireSpreadWeeks;
                    if (dk < 0f)
                        continue;
                    if (dk < TempFireBurnWeeks + 1f)
                        Set(spell.Cells[k], Overlay.Fire, dk < 0.5f ? dk / 0.5f : dk < TempFireBurnWeeks ? 1f : TempFireBurnWeeks + 1f - dk);
                    else
                    {
                        var regrow = (t - (spell.FromWeek + TempScorchHoldWeeks)) / Math.Max(1f, spell.ToWeek - spell.FromWeek - TempScorchHoldWeeks);
                        Set(spell.Cells[k], Overlay.Scorch, 1f - Mathf.Clamp(regrow, 0f, 1f));
                    }
                }
            }
            else if (spell.Kind == DemoSettlementEventKind.Heatwave)
            {
                var amount = Mathf.Min(1f, Mathf.Min(d / TempDryRampWeeks, (spell.ToWeek - t) / TempDryRampWeeks));
                foreach (var c in spell.Cells)
                    if (World.Contains(c.Column, c.Row) && (World.GetCell(c.Column, c.Row).Terrain == DemoTerrain.Grassland
                        || Land.StateOf(c) is DemoLandDevelopment.Farmland))
                        Set(c, Overlay.Dry, amount);
            }
        }

        foreach (var mark in _disasterMarks)
        {
            var d = t - mark.Week;
            if (d < 0f)
                continue;
            switch (mark.Kind)
            {
                case DemoSettlementEventKind.Flood:
                    // 河水漫上岸：半週淹滿、維持到第 3 週、之後退去。
                    // 退水時剩下一灘灘積水（地表依強度畫成塊狀的水窪，第 08-18b 章第 5 節）。
                    var flood = d < 0.5f ? d / 0.5f : d < TempFloodHighWeeks ? 1f : 1f - (d - TempFloodHighWeeks) / (TempFloodWeeks - TempFloodHighWeeks);
                    foreach (var c in FloodCells(mark))
                        Set(c, Overlay.Flood, flood);
                    if (d < TempFloodHighWeeks)
                        _movingDisaster = true;
                    break;
                case DemoSettlementEventKind.Tornado:
                    // 第 08-18b 章第 5 節：龍捲風沿路徑走過，經過的格留下刮痕（地面刮淡、樹變枯枝），半年內淡去。
                    var path = TornadoPath(mark);
                    for (var k = 0; k < path.Count; k++)
                    {
                        if (d / TempTornadoWeeks * path.Count < k)
                            break;
                        Set(path[k], Overlay.Scar, d < 2f ? 1f : 1f - (d - 2f) / (TempScarWeeks - 2f));
                    }
                    if (d < TempTornadoWeeks)
                        _movingDisaster = true;
                    break;
                case DemoSettlementEventKind.Earthquake:
                    foreach (var c in Ring(mark.Center, 1, landOnly: true))
                        Decal(c, Overlay.Cracks, 1f - d / TempCracksWeeks);
                    break;
                case DemoSettlementEventKind.Meteorite:
                    Decal(mark.Impact ?? mark.Center, Overlay.Crater, d < 4f ? 1f : 1f - (d - 4f) / (TempCraterWeeks - 4f));
                    break;
            }
        }
    }

    private IEnumerable<DemoCellRef> Ring(DemoCellRef center, int range, bool landOnly)
    {
        var origin = World!.GetCell(center.Column, center.Row);
        foreach (var cell in World.Cells)
            if (DemoWorldSites.HexDistance(origin, cell) <= range && (!landOnly || cell.Terrain != DemoTerrain.Water))
                yield return new DemoCellRef(cell.Column, cell.Row);
    }

    /// <summary>洪水淹的格：聚落與相鄰陸地中靠河的格（含聚落本身）。</summary>
    private IEnumerable<DemoCellRef> FloodCells(DisasterMark mark)
        => Ring(mark.Center, 1, landOnly: true).Where(c =>
        {
            var cell = World!.GetCell(c.Column, c.Row);
            return (c == mark.Center) || DemoRouteNetwork.Directions.Any(d => World.AtAxial(cell.AxialQ + d.Dq, cell.AxialR + d.Dr) is { MainWaterway: true });
        });

    /// <summary>龍捲風路徑：依事件編號決定方向，從聚落前方 2 格掃到後方 2 格（只走陸地）。</summary>
    private List<DemoCellRef> TornadoPath(DisasterMark mark)
    {
        var (dq, dr, _) = DemoRouteNetwork.Directions[(int)(mark.EventId % DemoRouteNetwork.Directions.Count)];
        var origin = World!.GetCell(mark.Center.Column, mark.Center.Row);
        var path = new List<DemoCellRef>();
        for (var k = -2; k <= 2; k++)
            if (World.AtAxial(origin.AxialQ + dq * k, origin.AxialR + dr * k) is { } cell && cell.Terrain != DemoTerrain.Water)
                path.Add(new DemoCellRef(cell.Column, cell.Row));
        return path;
    }

    private Vector2 ScreenOf(Vector2 start, float radius, DemoCellRef c)
    {
        var cell = World!.GetCell(c.Column, c.Row);
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    /// <summary>事件當下的動畫與燃燒的煙（畫在地形與聚落上面）。</summary>
    private void DrawDisasterEffects(Vector2 start, float radius)
    {
        var now = Time.GetTicksMsec();
        var seconds = now / 1000f;
        _fireOnScreen = false;
        // 地面痕跡貼圖：隕石坑、地震裂縫、龍捲風殘骸（依時間淡去）。
        foreach (var (cell, kind, amount) in _decals)
        {
            var center = ScreenOf(start, radius, cell);
            if (!OnScreen(center, radius * 2f))
                continue;
            // 第 08-16 章第 12 節：像素零件版的坑、裂縫、殘骸（原像素大小、最近鄰），沒有才用舊特效圖。
            if (PixelParts.Texture(PixelDecal(kind)) is { } px)
            {
                var pxSize = PixelParts.ScreenSize(px, radius);
                DrawTextureRect(px, new Rect2(center - pxSize / 2f, pxSize), false, new Color(1, 1, 1, amount));
                continue;
            }
            if (ArtAssets.Texture(DecalPath(kind)) is not { } decal)
                continue;
            var size = radius * (kind == Overlay.Crater ? 1.5f : 1.8f);
            DrawTextureRect(decal, new Rect2(center - new Vector2(size, size) / 2f, new Vector2(size, size)), false, new Color(1, 1, 1, amount));
        }
        // 燃燒處冒煙：每格三團往上飄、漸淡的灰煙。
        foreach (var spell in Spells.Where(s => s.Kind == DemoSettlementEventKind.Wildfire))
        {
            var d = GameWeeks - spell.FromWeek;
            for (var k = 0; k < spell.Cells.Count; k++)
            {
                var dk = d - k * TempFireSpreadWeeks;
                if (dk < 0f || dk > TempFireBurnWeeks + 1f || !World!.Contains(spell.Cells[k].Column, spell.Cells[k].Row))
                    continue;
                var center = ScreenOf(start, radius, spell.Cells[k]);
                if (!OnScreen(center, radius * 3f))
                    continue;
                if (dk < TempFireBurnWeeks)
                    _fireOnScreen = true;
                // 美術素材（2026-11-04）：火焰動畫兩叢、煙霧貼圖；沒有素材時畫圓形的煙。
                if (dk < TempFireBurnWeeks && PixelParts.Texture("px_fire") is { } pxFire)
                {
                    // 像素火焰（4 格動畫，原像素大小）：每格三叢。
                    var frame = (int)(seconds * TempFireFps + k) % 4;
                    var w = PixelParts.ScreenSize(pxFire, radius, 4).X;
                    DrawStripFrame(pxFire, frame, center + new Vector2(-radius * 0.35f, radius * 0.4f), w);
                    DrawStripFrame(pxFire, (frame + 2) % 4, center + new Vector2(radius * 0.3f, radius * 0.15f), w);
                    DrawStripFrame(pxFire, (frame + 1) % 4, center + new Vector2(0f, radius * 0.6f), w);
                }
                else if (dk < TempFireBurnWeeks && ArtAssets.Texture(ArtAssets.FxFire) is { } fire)
                {
                    var frame = (int)(seconds * TempFireFps + k) % 4;
                    DrawStripFrame(fire, frame, center + new Vector2(-radius * 0.3f, radius * 0.35f), radius * 0.9f);
                    DrawStripFrame(fire, (frame + 2) % 4, center + new Vector2(radius * 0.32f, radius * 0.1f), radius * 0.75f);
                }
                var smoke = (Texture2D?)PixelParts.Texture("px_smoke") ?? ArtAssets.Texture(ArtAssets.FxSmoke);
                for (var p = 0; p < 3; p++)
                {
                    var phase = Mathf.PosMod(seconds * 0.4f + p / 3f + k * 0.17f, 1f);
                    var puff = center + new Vector2(Mathf.Sin(phase * 6f + k) * radius * 0.25f, -phase * radius * 1.6f);
                    var size = radius * (0.18f + phase * 0.3f);
                    if (smoke is not null)
                        DrawTextureRect(smoke, new Rect2(puff - new Vector2(size, size) * 1.3f, new Vector2(size, size) * 2.6f), false, new Color(1, 1, 1, 0.7f * (1f - phase)));
                    else
                        DrawCircle(puff, size, new Color(0.25f, 0.23f, 0.22f, 0.45f * (1f - phase)));
                }
            }
        }

        // 第 08-18b 章第 5 節：比格子大的會動物體——龍捲風沿路徑走（遊戲時間一週走完，剛發生時也照真實時間播一次）、洪水高水位時水面翻浪。
        foreach (var mark in _disasterMarks)
        {
            var d = GameWeeks - mark.Week;
            if (d < 0f)
                continue;
            if (mark.Kind == DemoSettlementEventKind.Tornado)
            {
                var progress = d / TempTornadoWeeks;
                foreach (var (impact, startMs) in _impacts)
                    if (impact.EventId == mark.EventId)
                        progress = Math.Min(progress, (now - startMs) / (float)TempImpactMs);
                if (progress >= 1f)
                    continue;
                var path = TornadoPath(mark);
                if (path.Count == 0)
                    continue;
                var along = Mathf.Clamp(progress, 0f, 1f) * (path.Count - 1);
                var k = Math.Max(0, Math.Min(path.Count - 2, (int)along));
                var pos = path.Count == 1 ? ScreenOf(start, radius, path[0])
                    : ScreenOf(start, radius, path[k]).Lerp(ScreenOf(start, radius, path[k + 1]), along - k);
                if (OnScreen(pos, radius * 4f))
                    DrawTornado(pos, radius, seconds);
            }
            else if (mark.Kind == DemoSettlementEventKind.Flood && d < TempFloodHighWeeks)
                foreach (var c in FloodCells(mark))
                {
                    var center = ScreenOf(start, radius, c);
                    if (!OnScreen(center, radius * 2f))
                        continue;
                    // 翻浪：每格三道往下游方向（右下）移動的白色浪花線。
                    for (var w = 0; w < 3; w++)
                    {
                        var q = Mathf.PosMod(seconds * 0.35f + w / 3f + (c.Column * 7 + c.Row * 3) * 0.13f, 1f);
                        var a = center + new Vector2(-radius * 0.8f + q * radius * 1.2f, -radius * 0.6f + w * radius * 0.55f);
                        DrawLine(a, a + new Vector2(radius * 0.45f, radius * 0.08f), new Color(0.92f, 0.97f, 1f, 0.75f * Mathf.Sin(q * Mathf.Pi)),
                            Math.Max(1.5f, radius * 0.06f));
                    }
                }
        }

        foreach (var (mark, startMs) in _impacts)
        {
            if (!World!.Contains(mark.Center.Column, mark.Center.Row))
                continue;
            var p = Mathf.Clamp((now - startMs) / (float)TempImpactMs, 0f, 1f);
            var center = ScreenOf(start, radius, mark.Center);
            switch (mark.Kind)
            {
                case DemoSettlementEventKind.Meteorite:
                {
                    var target = ScreenOf(start, radius, mark.Impact ?? mark.Center);
                    var from = target + new Vector2(radius * 7f, -radius * 9f);
                    if (p < 0.45f)
                    {
                        // 劃過天空：拖著尾巴的火球。
                        var head = from.Lerp(target, p / 0.45f);
                        if (ArtAssets.Texture(ArtAssets.FxMeteor) is { } meteor)
                        {
                            // 素材的火球在左下、尾巴在右上（與飛行方向一致）。
                            var size = radius * 3f;
                            DrawTextureRect(meteor, new Rect2(head - new Vector2(size * 0.25f, size * 0.75f), new Vector2(size, size)), false);
                        }
                        else
                        {
                            var tail = from.Lerp(target, Mathf.Max(0f, p / 0.45f - 0.35f));
                            DrawLine(tail, head, new Color(1f, 0.55f, 0.15f, 0.7f), Math.Max(2f, radius * 0.25f));
                            DrawCircle(head, Math.Max(2f, radius * 0.22f), new Color(1f, 0.9f, 0.6f));
                        }
                    }
                    else
                    {
                        // 撞擊閃光與衝擊波。
                        var q = (p - 0.45f) / 0.55f;
                        DrawCircle(target, radius * (0.4f + q * 1.2f), new Color(1f, 0.85f, 0.5f, 0.6f * (1f - q)));
                        DrawArc(target, radius * (0.6f + q * 2.4f), 0f, Mathf.Tau, 40, new Color(1f, 0.6f, 0.25f, 1f - q), Math.Max(1.5f, radius * 0.12f));
                    }
                    break;
                }
                case DemoSettlementEventKind.Flood:
                case DemoSettlementEventKind.Earthquake:
                {
                    var color = mark.Kind == DemoSettlementEventKind.Flood ? new Color(0.45f, 0.70f, 0.95f) : new Color(0.55f, 0.40f, 0.25f);
                    for (var ring = 0; ring < 3; ring++)
                    {
                        var q = Mathf.PosMod(p * 1.5f - ring * 0.25f, 1f);
                        DrawArc(center, radius * (0.4f + q * 2.2f), 0f, Mathf.Tau, 40, new Color(color, 1f - q), Math.Max(1.5f, radius * 0.1f));
                    }
                    break;
                }
            }
        }
    }

    private readonly List<(DemoCellRef Cell, Overlay Kind, float Amount)> _decals = new();

    /// <summary>龍捲風：比格子大很多的旋轉漏斗（約兩格高），底部揚起塵土、周圍繞著碎屑；有素材時用素材（放大到兩格寬）。</summary>
    private void DrawTornado(Vector2 bottom, float radius, float seconds)
    {
        DrawSetTransform(bottom + new Vector2(radius * 0.3f, 0f), 0f, new Vector2(1f, 0.32f));
        DrawCircle(Vector2.Zero, radius * 1.3f, new Color(0f, 0f, 0f, 0.28f)); // 落地陰影
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (ArtAssets.Texture(ArtAssets.FxTornado) is { } art)
        {
            DrawStripFrame(art, (int)(seconds * 12f) % 4, bottom + new Vector2(0f, radius * 0.3f), radius * 3.2f);
            return;
        }
        for (var ring = 0; ring < 3; ring++) // 底部塵土圈
        {
            var q = Mathf.PosMod(seconds * 1.2f + ring / 3f, 1f);
            DrawSetTransform(bottom, 0f, new Vector2(1f, 0.35f));
            DrawArc(Vector2.Zero, radius * (0.5f + q * 1.1f), 0f, Mathf.Tau, 28, new Color(0.62f, 0.54f, 0.42f, 0.6f * (1f - q)), Math.Max(2f, radius * 0.12f));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        const int layers = 12;
        for (var layer = 0; layer < layers; layer++)
        {
            var h = layer / (layers - 1f);
            var sway = Mathf.Sin(seconds * 6f + h * 3.5f) * radius * 0.25f * h;
            var c = bottom + new Vector2(sway, -h * radius * 4f);
            var w = radius * (0.15f + h * h * 1.0f + h * 0.25f);
            DrawSetTransform(c, 0f, new Vector2(1f, 0.32f));
            DrawCircle(Vector2.Zero, w, new Color(0.55f, 0.56f, 0.58f, 0.55f - h * 0.2f));
            DrawArc(Vector2.Zero, w, seconds * 8f + layer, seconds * 8f + layer + Mathf.Pi * 1.2f, 16, new Color(0.85f, 0.86f, 0.88f, 0.7f), Math.Max(1.5f, radius * 0.08f));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        for (var bit = 0; bit < 8; bit++) // 繞著飛的碎屑（木片、草）
        {
            var angle = seconds * 5f + bit * Mathf.Tau / 8f;
            var hgt = (bit % 4 + 1) / 5f;
            var at = bottom + new Vector2(Mathf.Cos(angle) * radius * (0.5f + hgt * 0.8f), -hgt * radius * 3f + Mathf.Sin(angle) * radius * 0.2f);
            DrawRect(new Rect2(at, new Vector2(2f, 2f) * Math.Max(1f, radius * 0.05f)), bit % 2 == 0 ? new Color(0.45f, 0.30f, 0.16f) : new Color(0.40f, 0.55f, 0.25f));
        }
    }

    /// <summary>【TEMP】火焰動畫每秒幾格。</summary>
    public const float TempFireFps = 8f;

    private static string PixelDecal(Overlay kind) => kind switch
    {
        Overlay.Crater => "px_crater",
        Overlay.Cracks => "px_crack",
        _ => "px_debris"
    };

    private static string DecalPath(Overlay kind) => kind switch
    {
        Overlay.Crater => ArtAssets.FxCrater,
        Overlay.Cracks => ArtAssets.FxCracks,
        _ => ArtAssets.FxDebris
    };

    /// <summary>畫 4 幀橫排動畫的某一格：底部置中在 <paramref name="bottom"/>，寬 <paramref name="width"/>（高依比例）。</summary>
    private void DrawStripFrame(Texture2D sheet, int frame, Vector2 bottom, float width)
    {
        var fw = sheet.GetWidth() / 4f;
        var height = width * sheet.GetHeight() / fw;
        DrawTextureRectRegion(sheet, new Rect2(bottom - new Vector2(width / 2f, height), new Vector2(width, height)),
            new Rect2(fw * (frame % 4), 0, fw, sheet.GetHeight()));
    }

    /// <summary>無畫面模式與冒煙測試靜音（由觀察者介面設定）。</summary>
    public bool AudioMuted { get; set; }

    /// <summary>【TEMP】天災音效、火燒循環音量（dB）。
    /// 試玩回饋 2026-11-04 之 2（音效好吵）：調低，同一種音效至少隔 <see cref="TempSfxMinGapMs"/> 毫秒、全部音效至少隔 <see cref="TempSfxAnyGapMs"/> 毫秒。</summary>
    public const float TempSfxVolumeDb = -16f;
    public const float TempFireLoopVolumeDb = -26f;
    public const ulong TempSfxMinGapMs = 8000;
    public const ulong TempSfxAnyGapMs = 1500;

    private AudioStreamPlayer? _fireLoop;
    private readonly Dictionary<string, ulong> _sfxLastMs = new();
    private ulong _sfxAnyLastMs;
    private bool _fireOnScreen;

    /// <summary>播一次音效（每次一個播放器，播完自行移除）；太密的略過。</summary>
    public void PlaySfx(string path)
    {
        if (AudioMuted || !IsInsideTree() || ArtAssets.Audio(path) is not { } stream)
            return;
        var now = Time.GetTicksMsec();
        if ((_sfxLastMs.TryGetValue(path, out var last) && now - last < TempSfxMinGapMs) || (_sfxAnyLastMs > 0 && now - _sfxAnyLastMs < TempSfxAnyGapMs))
            return;
        _sfxLastMs[path] = now;
        _sfxAnyLastMs = now;
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = TempSfxVolumeDb };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>畫面上看得到火（近看或中景、火在畫面內）時才循環播放火燒聲。</summary>
    private void UpdateFireLoop()
    {
        var want = _hasFire && _fireOnScreen && !AudioMuted && CurrentLayer != ViewLayer.Strategic;
        if (want && _fireLoop is null && ArtAssets.Audio(ArtAssets.SfxFire) is { } stream)
        {
            if (stream is AudioStreamOggVorbis ogg)
                ogg.Loop = true;
            _fireLoop = new AudioStreamPlayer { Stream = stream, VolumeDb = TempFireLoopVolumeDb };
            AddChild(_fireLoop);
        }
        if (_fireLoop is null)
            return;
        if (want && !_fireLoop.Playing)
            _fireLoop.Play();
        else if (!want && _fireLoop.Playing)
            _fireLoop.Stop();
    }
}
