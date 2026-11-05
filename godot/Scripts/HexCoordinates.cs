using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18a 章：方格世界的畫面座標（名稱暫時保留，之後統一改名）。一格是邊長 2×<c>radius</c> 的正方形，
/// 第 (q, r) 格（q＝欄、r＝列）的中心在 (2q, 2r)×radius。整張地圖是一張棋盤，沒有扭曲、沒有偏移。
/// 所有格與畫面位置的換算都在這個檔。畫面相鄰不代表可通行（通行看 Core 的路線網）。
/// </summary>
public static class HexCoordinates
{
    /// <summary>一格的邊長（畫面像素）。</summary>
    public static float Side(float radius) => 2f * radius;

    public static Vector2 Center(int q, int r, float radius) => new(q * Side(radius), r * Side(radius));

    /// <summary>格的四個角（順時針：右上、右下、左下、左上），第 i 條邊從第 i 個角開始：0 東、1 南、2 西、3 北。</summary>
    public static Vector2[] Polygon(Vector2 center, float radius)
        => new[]
        {
            center + new Vector2(radius, -radius), center + new Vector2(radius, radius),
            center + new Vector2(-radius, radius), center + new Vector2(-radius, -radius)
        };

    /// <summary>往鄰格的位移對應哪一條邊（0 東、1 南、2 西、3 北）；斜角不是邊，回傳 -1。</summary>
    public static int EdgeToward(int dq, int dr)
        => (dq, dr) switch { (1, 0) => 0, (0, 1) => 1, (-1, 0) => 2, (0, -1) => 3, _ => -1 };

    /// <summary>整張地圖的外框：左上角相對第 0 格中心的位移與大小。</summary>
    public static (Vector2 Offset, Vector2 Size) Frame(int columns, int rows, float radius)
        => (new Vector2(-radius, -radius), new Vector2(columns, rows) * Side(radius));

    /// <summary>相對第 0 格中心的畫面位置落在哪一格（欄、列；可能在地圖外）。</summary>
    public static Vector2I CellOf(Vector2 local, float radius)
        => new(Mathf.FloorToInt(local.X / Side(radius) + 0.5f), Mathf.FloorToInt(local.Y / Side(radius) + 0.5f));
}
