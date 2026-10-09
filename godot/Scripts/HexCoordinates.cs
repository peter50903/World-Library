using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// Presentation-only pointy-top axial hex-to-pixel converter. The integers are
/// V0-02 DEMO coordinates, NOT authoritative map coordinates or world distances.
/// Keep all axial/screen conversion in this file for later replacement by the
/// formal topology rules. Adjacent cells are not automatically traversable.
/// </summary>
public static class HexCoordinates
{
    /// <summary>
    /// TEMP presentation grid mapping: odd-r offset columns to axial q/r.
    /// The official map coordinate/orientation system is NOT yet specified.
    /// Offset rows form a near-rectangular 12x8 viewport (half-hex stagger),
    /// while axial coordinates stay available for later Core-backed hex data.
    /// </summary>
    public static Vector2I OddRowOffsetToAxial(int column, int row)
    {
        return new Vector2I(column - (row - (row & 1)) / 2, row);
    }

    public static Vector2 Center(int q, int r, float radius)
    {
        return new Vector2(
            radius * Mathf.Sqrt(3.0f) * (q + r / 2.0f),
            radius * 1.5f * r);
    }

    public static Vector2[] Polygon(Vector2 center, float radius)
    {
        var vertices = new Vector2[6];
        for (var i = 0; i < vertices.Length; i++)
        {
            var angle = Mathf.DegToRad(60.0f * i - 30.0f);
            vertices[i] = center + new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);
        }
        return vertices;
    }
}
