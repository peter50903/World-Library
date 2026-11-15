using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章第 4 節之 2：遷徙與開拓隊伍的位置與剩下的路線（前方淡淡的小點），開拓隊的終點插一面小旗。
/// 第 08-20f 章起隊伍是不會走動的旗符；第 09-2 章移除小人畫法。只影響畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】有動畫時畫面重畫的最短間隔（毫秒）。</summary>
    public const ulong TempAnimateEveryMs = 66;

    private ulong _lastAnimatedMs;

    /// <summary>有動畫要播時（隊伍在走、火在燒、天災動畫中）限速重畫；在 _Process 呼叫。</summary>
    private bool WantsAnimation => Migrants.Count > 0 || _hasFire || _impacts.Count > 0
        || (_tokensMoving && CurrentLayer == ViewLayer.Regional) // 第 08-17 章：區域層的指示物來回移動
        || _movingDisaster // 第 08-18b 章第 5 節：龍捲風、漲水
        || _disasterAnimating // 試玩回饋 2026-11-08 之 4：天災過程照真實時間播
        || _battleAnimating; // 第 08-18h 章：戰鬥動畫

    private void DrawMigrants(Vector2 start, float radius)
    {
        var font = ThemeDB.FallbackFont;
        foreach (var batch in Migrants)
        {
            var center = MigrantPosition(start, radius, batch);
            if (!OnScreen(center, radius * 3f))
                continue;
            // 試玩回饋 2026-11-08 之 6：遷徙隊伍可以點。
            AddActor(center + new Vector2(0f, radius * 0.25f), radius, new MapActor(ActorKind.Migrants, batch.FromSettlementId, TokenKind.Migrants,
                batch.People, CellOfScreen(center) ?? new DemoCellRef(0, 0), batch.Id), 26f);
            DrawRouteAhead(start, radius, batch, center);
            // 第 08-20f 章：遷徙隊伍是不會走動的符號（開拓隊、遷徙隊伍各一種旗符），位置照隊伍目前所在。
            var symbol = batch.Founding ? "b11_sym_settlers" : "b11_sym_migrants"; // 第 09-1b 章：第十一批旗符優先
            if (((PixelParts.IsBatch5(symbol) ? PixelParts.Texture(symbol) : null) ?? PixelParts.Texture(batch.Founding ? "tok_settlers" : "tok_migrants")) is { } token)
                DrawArt(token, center + new Vector2(0f, radius * 0.4f), radius, scale: Math.Max(1f, 28f / PixelParts.ScreenSize(token, radius).Y));
            else
                DrawCircle(center, radius * 0.3f, MigrantTunic);
            // 人數標籤：深色底＋白字（右下角）。
            var size = Mathf.Max(8, (int)(radius * 0.32f));
            var labelPos = center + new Vector2(radius * 0.35f, radius * 0.55f);
            DrawCircle(labelPos + new Vector2(size * 0.3f, -size * 0.35f), size * 0.62f, new Color(0.12f, 0.08f, 0.05f, 0.85f));
            DrawString(font, labelPos, batch.People.ToString(), HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }

    /// <summary>剩下的路線：一串淡淡的小點，開拓隊在終點插小旗。</summary>
    private void DrawRouteAhead(Vector2 start, float radius, DemoMigrationBatch batch, Vector2 from)
    {
        var dot = new Color(0.98f, 0.94f, 0.80f, 0.55f);
        var previous = from;
        for (var k = batch.StepIndex + 1; k < batch.Path.Length; k++)
        {
            var cell = World!.GetCell(batch.Path[k].Column, batch.Path[k].Row);
            var next = MigrantMarkerCenter(start, radius, cell);
            var steps = Math.Max(2, (int)(previous.DistanceTo(next) / Math.Max(4f, radius * 0.35f)));
            for (var s = 1; s <= steps; s++)
                DrawCircle(previous.Lerp(next, s / (float)steps), Math.Max(1.2f, radius * 0.05f), dot);
            previous = next;
        }
        if (batch.Founding && batch.Path.Length > 0)
        {
            var goal = World!.GetCell(batch.Path[^1].Column, batch.Path[^1].Row);
            var flag = MigrantMarkerCenter(start, radius, goal);
            var pole = Math.Max(6f, radius * 0.6f);
            DrawLine(flag, flag - new Vector2(0, pole), new Color(0.30f, 0.22f, 0.14f), Math.Max(1.5f, radius * 0.06f));
            DrawColoredPolygon(new[] { flag - new Vector2(0, pole), flag - new Vector2(-pole * 0.55f, pole * 0.8f), flag - new Vector2(0, pole * 0.6f) },
                new Color(0.86f, 0.30f, 0.22f));
        }
    }

    private static readonly Color MigrantTunic = new(0.58f, 0.46f, 0.32f);

    private DemoCellRef? CellOfScreen(Vector2 position) => CellAt(position) is { } cell ? new DemoCellRef(cell.Column, cell.Row) : null;
}
