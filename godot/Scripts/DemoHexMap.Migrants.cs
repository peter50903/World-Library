using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章第 4 節之 2（讓變化動起來）：遷徙與開拓隊伍畫成沿路線走路的小人群，前方淡淡標出剩下的路線，
/// 開拓隊的終點插一面小旗。有 <c>FX_MIGRANTS_WALK</c> 素材（4 幀）就播動畫，沒有就由程式畫小人。只影響畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】幾人畫一個小人（最多 <see cref="TempMaxWalkers"/> 個）、走路動畫每秒幾步、畫面重畫的最短間隔（毫秒）。</summary>
    public const int TempPeoplePerWalker = 8;
    public const int TempMaxWalkers = 5;
    public const float TempStepsPerSecond = 6f;
    public const ulong TempAnimateEveryMs = 66;

    private ulong _lastAnimatedMs;

    /// <summary>有動畫要播時（隊伍在走、火在燒、天災動畫中）限速重畫；在 _Process 呼叫。</summary>
    private bool WantsAnimation => Migrants.Count > 0 || _hasFire || _impacts.Count > 0
        || (_tokensMoving && CurrentLayer == ViewLayer.Regional) // 第 08-17 章：區域層的指示物來回移動
        || (_walkersDrawn && CurrentLayer == ViewLayer.Local) // 第 08-18b 章：近景小人走動
        || _movingDisaster; // 第 08-18b 章第 5 節：龍捲風、漲水

    private void DrawMigrants(Vector2 start, float radius)
    {
        var font = ThemeDB.FallbackFont;
        var seconds = Time.GetTicksMsec() / 1000f;
        foreach (var batch in Migrants)
        {
            var center = MigrantPosition(start, radius, batch);
            if (!OnScreen(center, radius * 3f))
                continue;
            DrawRouteAhead(start, radius, batch, center);
            var heading = MigrantHeading(start, radius, batch);
            var walkers = Math.Clamp((int)((batch.People + TempPeoplePerWalker - 1) / TempPeoplePerWalker), 1, TempMaxWalkers);
            if (ArtAssets.Texture(ArtAssets.FxMigrantsWalk) is { } sheet)
                DrawWalkSprite(sheet, center, radius, heading, seconds + batch.Id * 0.37f);
            else
                DrawWalkers(center, radius, heading, walkers, seconds + batch.Id * 0.37f, batch.Founding);
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

    /// <summary>隊伍行進方向（往路線下一格；到了就沿用上一段）。</summary>
    private Vector2 MigrantHeading(Vector2 start, float radius, DemoMigrationBatch batch)
    {
        var i = Math.Min(batch.StepIndex, Math.Max(0, batch.Path.Length - 2));
        if (batch.Path.Length < 2)
            return Vector2.Right;
        var a = MigrantMarkerCenter(start, radius, World!.GetCell(batch.Path[i].Column, batch.Path[i].Row));
        var b = MigrantMarkerCenter(start, radius, World.GetCell(batch.Path[i + 1].Column, batch.Path[i + 1].Row));
        return (b - a).Normalized();
    }

    /// <summary>素材版：4 幀橫排，往左走時水平翻轉。</summary>
    private void DrawWalkSprite(Texture2D sheet, Vector2 center, float radius, Vector2 heading, float time)
    {
        var frame = (int)(time * TempStepsPerSecond / 1.5f) % 4;
        var fw = sheet.GetWidth() / 4f;
        var size = new Vector2(radius * 1.4f, radius * 1.4f * sheet.GetHeight() / fw);
        var rect = new Rect2(center - new Vector2(size.X / 2f, size.Y * 0.8f), size);
        if (heading.X < 0f)
            rect = new Rect2(rect.Position + new Vector2(rect.Size.X, 0), new Vector2(-rect.Size.X, rect.Size.Y));
        DrawTextureRectRegion(sheet, rect, new Rect2(frame * fw, 0, fw, sheet.GetHeight()));
    }

    /// <summary>程式版：一列小人（頭、身體、擺動的腿），揹包袱；開拓隊最前面的人扛一面小旗。</summary>
    private void DrawWalkers(Vector2 center, float radius, Vector2 heading, int walkers, float time, bool founding)
    {
        var unit = Math.Max(1.5f, radius * 0.11f);
        var back = -heading * unit * 2.6f;
        var side = new Vector2(-heading.Y, heading.X) * unit * 0.9f;
        var lead = center + heading * unit * 2.6f * (walkers - 1) / 2f;
        for (var k = 0; k < walkers; k++)
        {
            var phase = time * TempStepsPerSecond + k * 1.7f;
            var bob = Mathf.Abs(Mathf.Sin(phase)) * unit * 0.5f;
            var foot = lead + back * k + side * ((k % 2 == 0) ? 0.5f : -0.5f);
            var hip = foot - new Vector2(0, unit * 2.2f + bob);
            var swing = Mathf.Sin(phase) * unit * 0.9f;
            var leg = new Color(0.24f, 0.18f, 0.12f);
            DrawLine(hip, foot + new Vector2(swing, 0), leg, Math.Max(1f, unit * 0.55f));
            DrawLine(hip, foot - new Vector2(swing, 0), leg, Math.Max(1f, unit * 0.55f));
            var body = new Color(0.55f + 0.06f * (k % 3), 0.42f, 0.28f);
            DrawLine(hip, hip - new Vector2(0, unit * 2.0f), body, Math.Max(1.5f, unit * 1.2f));
            DrawCircle(hip - new Vector2(0, unit * 2.7f), unit * 0.75f, new Color(0.88f, 0.72f, 0.56f));
            // 包袱：背後一個褐色小方塊。
            var pack = hip - new Vector2(0, unit * 1.4f) - heading * unit * 0.9f;
            DrawRect(new Rect2(pack - new Vector2(unit * 0.55f, unit * 0.55f), new Vector2(unit * 1.1f, unit * 1.1f)), new Color(0.42f, 0.30f, 0.18f));
            if (founding && k == 0)
            {
                var top = hip - new Vector2(0, unit * 5.2f);
                DrawLine(hip - new Vector2(0, unit * 1.0f), top, new Color(0.30f, 0.22f, 0.14f), Math.Max(1f, unit * 0.4f));
                DrawColoredPolygon(new[] { top, top + new Vector2(unit * 2.0f * Mathf.Sign(heading.X == 0 ? 1 : heading.X), unit * 0.7f), top + new Vector2(0, unit * 1.4f) },
                    new Color(0.86f, 0.30f, 0.22f));
            }
        }
    }
}
