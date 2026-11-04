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
                    _impacts.Add((mark, now));
            }
            _disasterMarksPrimed = true;
        }
    }

    private IReadOnlyList<DisasterMark> _disasterMarks = Array.Empty<DisasterMark>();
    private readonly HashSet<long> _seenDisasters = new();
    private bool _disasterMarksPrimed;
    private readonly List<(DisasterMark Mark, ulong StartMs)> _impacts = new();

    /// <summary>疊層種類（資料貼圖第二個位元組）。</summary>
    public enum Overlay : byte { None = 0, Fire = 1, Scorch = 2, Dry = 3, Flood = 4, Debris = 5, Cracks = 6, Crater = 7, Ore = 8 }

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
                    var flood = d < 0.5f ? d / 0.5f : d < 3f ? 1f : 1f - (d - 3f) / (TempFloodWeeks - 3f);
                    foreach (var c in FloodCells(mark))
                        Set(c, Overlay.Flood, flood);
                    break;
                case DemoSettlementEventKind.Tornado:
                    var path = TornadoPath(mark);
                    for (var k = 0; k < path.Count; k++)
                    {
                        var reached = d * path.Count; // 一週內沿路徑掃過
                        if (reached < k)
                            break;
                        Set(path[k], Overlay.Debris, d < 2f ? 1f : 1f - (d - 2f) / (TempDebrisWeeks - 2f));
                    }
                    break;
                case DemoSettlementEventKind.Earthquake:
                    foreach (var c in Ring(mark.Center, 1, landOnly: true))
                        Set(c, Overlay.Cracks, 1f - d / TempCracksWeeks);
                    break;
                case DemoSettlementEventKind.Meteorite:
                    Set(mark.Impact ?? mark.Center, Overlay.Crater, d < 4f ? 1f : 1f - (d - 4f) / (TempCraterWeeks - 4f));
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
        var (dq, dr, _) = DemoRouteNetwork.Directions[(int)(mark.EventId % 6)];
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
                for (var p = 0; p < 3; p++)
                {
                    var phase = Mathf.PosMod(seconds * 0.4f + p / 3f + k * 0.17f, 1f);
                    var puff = center + new Vector2(Mathf.Sin(phase * 6f + k) * radius * 0.25f, -phase * radius * 1.6f);
                    DrawCircle(puff, radius * (0.18f + phase * 0.3f), new Color(0.25f, 0.23f, 0.22f, 0.45f * (1f - phase)));
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
                        var tail = from.Lerp(target, Mathf.Max(0f, p / 0.45f - 0.35f));
                        DrawLine(tail, head, new Color(1f, 0.55f, 0.15f, 0.7f), Math.Max(2f, radius * 0.25f));
                        DrawCircle(head, Math.Max(2f, radius * 0.22f), new Color(1f, 0.9f, 0.6f));
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
                case DemoSettlementEventKind.Tornado:
                {
                    var path = TornadoPath(mark);
                    if (path.Count == 0)
                        break;
                    var along = p * (path.Count - 1);
                    var k = Math.Min(path.Count - 2, (int)along);
                    var pos = path.Count == 1 ? ScreenOf(start, radius, path[0])
                        : ScreenOf(start, radius, path[Math.Max(0, k)]).Lerp(ScreenOf(start, radius, path[k + 1]), along - k);
                    // 漏斗：由下往上越來越寬的旋轉橢圓。
                    for (var layer = 0; layer < 6; layer++)
                    {
                        var h = layer / 5f;
                        var sway = Mathf.Sin(seconds * 9f + layer) * radius * 0.08f * (1f + h);
                        var c = pos + new Vector2(sway, -h * radius * 1.8f);
                        DrawArc(c, radius * (0.12f + h * 0.45f), 0f, Mathf.Tau, 20, new Color(0.62f, 0.62f, 0.60f, 0.85f - h * 0.35f),
                            Math.Max(1.5f, radius * 0.1f));
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
}
