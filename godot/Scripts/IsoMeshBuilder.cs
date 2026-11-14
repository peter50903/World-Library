using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20 章：低多邊形模型拼裝器——把方塊、斜頂、圓柱或圓錐、岩塊等零件（各帶自己的位置與旋轉）合成一個網格。
/// 每個面一個法線（平面著色，看得出稜面）、每個面的顏色略有深淺（霧面材質的手工感）；顏色用頂點色，一個材質畫全部。
/// 一個聚落、一片森林各合成一個網格：零件再多也只算一次繪圖。
/// </summary>
public sealed class IsoMeshBuilder
{
    private readonly SurfaceTool _st = new();
    private int _faces;

    public IsoMeshBuilder() => _st.Begin(Mesh.PrimitiveType.Triangles);

    public int Faces => _faces;

    /// <summary>每個面的深淺變化（±）。</summary>
    public float Variation { get; set; } = 0.05f;

    /// <summary>零件的位置與水平旋轉（度）。</summary>
    public static Transform3D At(Vector3 origin, float yawDegrees = 0f)
        => new(new Basis(Vector3.Up, Mathf.DegToRad(yawDegrees)), origin);

    private Color Shade(Color color)
    {
        var k = (IsoProtoMap.Hash01(_faces, 7, 13) * 2f - 1f) * Variation;
        return k >= 0f ? color.Lightened(k) : color.Darkened(-k);
    }

    /// <summary>一個三角面（法線朝離開 <paramref name="inside"/> 的一側）。</summary>
    public void Tri(Transform3D t, Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 inside, bool shade = true)
    {
        a = t * a;
        b = t * b;
        c = t * c;
        inside = t * inside;
        var n = (b - a).Cross(c - a);
        if (n.LengthSquared() < 1e-14f)
            return;
        n = n.Normalized();
        if (n.Dot((a + b + c) / 3f - inside) < 0f)
            n = -n;
        var col = shade ? Shade(color) : color;
        _faces++;
        _st.SetColor(col);
        _st.SetNormal(n);
        _st.AddVertex(a);
        _st.SetColor(col);
        _st.SetNormal(n);
        _st.AddVertex(b);
        _st.SetColor(col);
        _st.SetNormal(n);
        _st.AddVertex(c);
    }

    /// <summary>一個四邊形面（同一個顏色）。</summary>
    public void Quad(Transform3D t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 inside)
    {
        var col = Shade(color);
        Tri(t, a, b, c, col, inside, false);
        Tri(t, a, c, d, col, inside, false);
    }

    /// <summary>方塊（零件座標的中心與大小）；<paramref name="top"/>＝頂面另一個顏色。</summary>
    public void Box(Transform3D t, Vector3 center, Vector3 size, Color color, Color? top = null)
    {
        var h = size / 2f;
        Vector3 P(float x, float y, float z) => center + new Vector3(x * h.X, y * h.Y, z * h.Z);
        Quad(t, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), top ?? color, center);
        Quad(t, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color.Darkened(0.2f), center);
        Quad(t, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), color, center);
        Quad(t, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), color, center);
        Quad(t, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color, center);
        Quad(t, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), color, center);
    }

    /// <summary>
    /// 斜頂（屋脊沿零件 Z 軸，底面在 <paramref name="bottom"/>）：兩個斜面各分 <paramref name="bands"/> 條（瓦片或茅草一排排的深淺，
    /// <paramref name="alt"/> 有給時兩色交錯＝條紋篷布），兩端的山牆用 <paramref name="gable"/>。
    /// </summary>
    public void Prism(Transform3D t, Vector3 bottom, Vector3 size, Color color, Color? gable = null, int bands = 1, Color? alt = null)
    {
        var hx = size.X / 2f;
        var hz = size.Z / 2f;
        var inside = bottom + new Vector3(0f, size.Y * 0.3f, 0f);
        var ridgeA = bottom + new Vector3(0f, size.Y, -hz);
        var ridgeB = bottom + new Vector3(0f, size.Y, hz);
        foreach (var side in new[] { -1f, 1f })
        {
            var eaveA = bottom + new Vector3(side * hx, 0f, -hz);
            var eaveB = bottom + new Vector3(side * hx, 0f, hz);
            for (var k = 0; k < bands; k++)
            {
                float f0 = k / (float)bands, f1 = (k + 1) / (float)bands;
                var band = alt is { } other ? (k % 2 == 0 ? color : other) : (k % 2 == 0 ? color : color.Darkened(0.07f));
                Quad(t, eaveA.Lerp(ridgeA, f0), eaveB.Lerp(ridgeB, f0), eaveB.Lerp(ridgeB, f1), eaveA.Lerp(ridgeA, f1), band, inside);
            }
        }
        var g = gable ?? color.Darkened(0.1f);
        foreach (var z in new[] { -hz, hz })
            Tri(t, bottom + new Vector3(-hx, 0f, z), bottom + new Vector3(hx, 0f, z), bottom + new Vector3(0f, size.Y, z), g, inside);
    }

    /// <summary>圓柱、圓台或圓錐（<paramref name="top"/>＝0 是錐）；<paramref name="sides"/> 邊。</summary>
    public void Frustum(Transform3D t, Vector3 bottom, float radius, float top, float height, int sides, Color color, float spinDegrees = 0f, Color? cap = null)
    {
        var inside = bottom + new Vector3(0f, height * 0.4f, 0f);
        var spin = Mathf.DegToRad(spinDegrees);
        Vector3 Ring(int i, float r, float y)
        {
            var a = spin + Mathf.Tau * i / sides;
            return bottom + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
        for (var i = 0; i < sides; i++)
        {
            if (top <= 0f)
                Tri(t, Ring(i, radius, 0f), Ring(i + 1, radius, 0f), bottom + new Vector3(0f, height, 0f), color, inside);
            else
            {
                Quad(t, Ring(i, radius, 0f), Ring(i + 1, radius, 0f), Ring(i + 1, top, height), Ring(i, top, height), color, inside);
                Tri(t, Ring(i, top, height), Ring(i + 1, top, height), bottom + new Vector3(0f, height, 0f), cap ?? color.Lightened(0.06f), inside);
            }
        }
    }

    /// <summary>岩塊或樹冠：兩圈六邊加上下兩點，每個頂點依 <paramref name="seed"/> 抖動（有稜角的低多邊形）。</summary>
    public void Blob(Transform3D t, Vector3 center, Vector3 radii, Color color, int seed, float roughness = 0.22f)
    {
        const int sides = 6;
        float J(int i) => 1f + (IsoProtoMap.Hash01(seed, i, 31) * 2f - 1f) * roughness;
        var topPoint = center + new Vector3(0f, radii.Y * J(90), 0f);
        var bottomPoint = center - new Vector3(0f, radii.Y * 0.8f, 0f);
        var upper = new Vector3[sides];
        var lower = new Vector3[sides];
        for (var i = 0; i < sides; i++)
        {
            var a = Mathf.Tau * i / sides + seed * 0.37f;
            var b = a + Mathf.Pi / sides;
            upper[i] = center + new Vector3(Mathf.Cos(b) * radii.X * 0.78f * J(i), radii.Y * 0.45f, Mathf.Sin(b) * radii.Z * 0.78f * J(i + 10));
            lower[i] = center + new Vector3(Mathf.Cos(a) * radii.X * J(i + 20), -radii.Y * 0.2f, Mathf.Sin(a) * radii.Z * J(i + 30));
        }
        for (var i = 0; i < sides; i++)
        {
            var n = (i + 1) % sides;
            Tri(t, upper[i], upper[n], topPoint, color.Lightened(0.06f), center);
            Tri(t, lower[i], lower[n], upper[i], color, center);
            Tri(t, lower[n], upper[n], upper[i], color, center);
            Tri(t, lower[i], lower[n], bottomPoint, color.Darkened(0.15f), center);
        }
    }

    /// <summary>合成一個節點（一個頂點色材質）。</summary>
    public MeshInstance3D Commit(Material material, bool shadows = true)
        => new()
        {
            Mesh = _st.Commit(),
            MaterialOverride = material,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off
        };
}
