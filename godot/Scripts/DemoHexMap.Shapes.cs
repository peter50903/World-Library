using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 1 節：格的外形是正方形（方格世界；人造物與所有範圍一律沿格邊，邊界是直線、轉角是直角）。
/// 每條邊切成 <see cref="TempEdgeSegments"/> 段（畫領域邊界、牆時取某一條邊用）。不用亂數、不影響世界。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】每條邊切幾段；圖塊取樣放大倍率。</summary>
    private const int TempEdgeSegments = 6;
    private const float TempShapeUvScale = 1.0f;

    private Vector2[]? _square;

    /// <summary>某格的外形（相對格中心、以半徑 1 為單位；4×段數個點，第 i 條邊（0 東、1 南、2 西、3 北）從第 i×段數 點開始）。</summary>
    private Vector2[] ShapeOf(DemoHexCell cell) => _square ??= BuildShape();

    private static Vector2[] BuildShape()
    {
        var corners = HexCoordinates.Polygon(Vector2.Zero, 1f);
        var points = new List<Vector2>(4 * TempEdgeSegments);
        for (var i = 0; i < 4; i++)
            for (var k = 0; k < TempEdgeSegments; k++)
                points.Add(corners[i].Lerp(corners[(i + 1) % 4], k / (float)TempEdgeSegments));
        return points.ToArray();
    }

    /// <summary>位置雜湊（-1～1）：只用於外形，與世界亂數無關。</summary>
    private static float Hash(int x, int y, int salt)
    {
        var v = Mathf.Sin(x * 12.9898f + y * 78.233f + salt * 37.719f) * 43758.5453f;
        return (v - Mathf.Floor(v)) * 2f - 1f;
    }

    /// <summary>格的螢幕外形點（<paramref name="scale"/>＝半徑）。</summary>
    private Vector2[] ScreenShape(DemoHexCell cell, Vector2 center, float scale)
    {
        var shape = ShapeOf(cell);
        var points = new Vector2[shape.Length];
        for (var i = 0; i < shape.Length; i++)
            points[i] = center + shape[i] * scale;
        return points;
    }

    private int[]? _fanIndices;

    /// <summary>以格中心為扇心畫出外形（不需要三角化，畫很多格也快）；有圖塊就貼圖。</summary>
    private void DrawShape(Vector2 center, Vector2[] outline, Color color, Texture2D? texture = null, float uvRadius = 0f)
    {
        var n = outline.Length;
        if (_fanIndices is null || _fanIndices.Length != n * 3)
        {
            _fanIndices = new int[n * 3];
            for (var i = 0; i < n; i++)
            {
                _fanIndices[i * 3] = 0;
                _fanIndices[i * 3 + 1] = i + 1;
                _fanIndices[i * 3 + 2] = (i + 1) % n + 1;
            }
        }
        var points = new Vector2[n + 1];
        points[0] = center;
        Array.Copy(outline, 0, points, 1, n);
        var colors = new Color[n + 1];
        Array.Fill(colors, color);
        if (texture is null)
        {
            RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _fanIndices, points, colors);
            return;
        }
        var uvs = HexUvs(points, center, uvRadius * TempShapeUvScale);
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _fanIndices, points, colors, uvs, null, null, texture.GetRid());
    }

    /// <summary>不規則外形的第 <paramref name="edge"/> 條邊（含兩端角點），畫領域邊界與牆用。</summary>
    private static Vector2[] EdgeOf(Vector2[] outline, int edge)
    {
        var points = new Vector2[TempEdgeSegments + 1];
        for (var k = 0; k <= TempEdgeSegments; k++)
            points[k] = outline[(edge * TempEdgeSegments + k) % outline.Length];
        return points;
    }
}
