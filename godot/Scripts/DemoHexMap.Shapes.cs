using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-15 章第 3 節：不規則邊界（使用者 2026-11-01：格子內容都被六邊形限制，缺少真實地圖的不規則感）。
/// 規則仍是六角格；只是畫的時候每個角點、每條邊依位置固定抖動——相鄰兩格算出同一條邊的同一組點，所以拼起來沒有縫也不重疊。
/// 海岸、林緣、領域邊界因此是彎曲的。抖動只取決於格的位置（同一張地圖每次一樣），不用亂數、不影響世界。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】角點抖動、邊中段抖動（格半徑的倍數）、每條邊切幾段；圖塊取樣放大倍率（抖動到格外時仍取到圖塊內的不透明像素）。</summary>
    private const float TempCornerJitter = 0.10f;
    private const float TempEdgeJitter = 0.11f;
    private const int TempEdgeSegments = 4;
    private const float TempShapeUvScale = 1.22f;

    private DemoHexWorld? _shapeWorld;
    private Vector2[][]? _shapes;
    private Dictionary<(int, int), int>? _shapeIndex;

    /// <summary>某格的不規則外形（相對格中心、以半徑 1 為單位；6×段數個點，第 i 條邊從第 i×段數 點開始）。</summary>
    private Vector2[] ShapeOf(DemoHexCell cell)
    {
        if (!ReferenceEquals(_shapeWorld, World) || _shapes is null)
        {
            _shapeWorld = World;
            _shapes = new Vector2[World!.Cells.Count][];
            _shapeIndex = new Dictionary<(int, int), int>(World.Cells.Count);
            for (var i = 0; i < World.Cells.Count; i++)
                _shapeIndex[(World.Cells[i].Column, World.Cells[i].Row)] = i;
        }
        var index = _shapeIndex![(cell.Column, cell.Row)];
        return _shapes[index] ??= BuildShape(cell);
    }

    private static Vector2[] BuildShape(DemoHexCell cell)
    {
        var center = HexCoordinates.Center(cell.AxialQ, cell.AxialR, 1f);
        var raw = HexCoordinates.Polygon(center, 1f);
        var keys = raw.Select(Key).ToArray();
        var corners = raw.Select((c, i) => JitterCorner(c, keys[i])).ToArray();
        var points = new List<Vector2>(6 * TempEdgeSegments);
        for (var i = 0; i < 6; i++)
        {
            var j = (i + 1) % 6;
            points.Add(corners[i] - center);
            foreach (var p in EdgeInterior(corners[i], corners[j], keys[i], keys[j]))
                points.Add(p - center);
        }
        return points.ToArray();
    }

    /// <summary>角點由三格共用：抖動只看角點本身的位置。</summary>
    private static Vector2 JitterCorner(Vector2 corner, (int, int) key)
    {
        var (kx, ky) = key;
        return corner + new Vector2(Hash(kx, ky, 1), Hash(kx, ky, 2)) * TempCornerJitter;
    }

    /// <summary>邊的中段點：依兩端角點的固定順序計算（兩側的格算出同一組點），再依本格的走向排列。</summary>
    private static IEnumerable<Vector2> EdgeInterior(Vector2 a, Vector2 b, (int, int) ka, (int, int) kb)
    {
        var forward = ka.CompareTo(kb) < 0;
        var (from, to) = forward ? (a, b) : (b, a);
        var (kf, kt) = forward ? (ka, kb) : (kb, ka);
        var normal = (to - from).Orthogonal().Normalized();
        var list = new List<Vector2>(TempEdgeSegments - 1);
        for (var k = 1; k < TempEdgeSegments; k++)
        {
            var t = k / (float)TempEdgeSegments;
            var amount = Hash(kf.Item1 * 31 + kt.Item1, kf.Item2 * 17 + kt.Item2, 3 + k) * TempEdgeJitter * Mathf.Sin(Mathf.Pi * t);
            list.Add(from.Lerp(to, t) + normal * amount);
        }
        if (!forward)
            list.Reverse();
        return list;
    }

    /// <summary>角點在六角格點陣上的整數座標（x 是 √3/2 的倍數、y 是 0.5 的倍數），相鄰格算出同一個值，不受浮點誤差影響。</summary>
    private static (int, int) Key(Vector2 p) => (Mathf.RoundToInt(p.X * 2f / Mathf.Sqrt(3f)), Mathf.RoundToInt(p.Y * 2f));

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
