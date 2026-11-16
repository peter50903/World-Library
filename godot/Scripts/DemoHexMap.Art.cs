using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18d 章：第五、六批素材畫在地圖上的共用工具。素材與像素地表同一密度（每格半徑 <see cref="TerrainSurface.TempTexelsPerRadius"/> 圖像素）、
/// 最近鄰取樣、底部置中；動畫是橫排 4 格；側面的素材面向右，往左走時左右翻轉。缺素材時各處退回原本程式畫的圖樣。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>畫一張素材（或動畫的第 <paramref name="frame"/> 格）：底部置中在 <paramref name="bottom"/>；<paramref name="flip"/>＝左右翻轉。</summary>
    private void DrawArt(Texture2D texture, Vector2 bottom, float radius, int frames = 1, int frame = 0, bool flip = false, float alpha = 1f, float scale = 1f)
    {
        var (width, height, density) = PixelParts.Metrics(texture); // 第 09-1e 章：寬、高、密度只問引擎一次
        var fw = width / frames;
        var size = new Vector2(fw, height) * (radius / density) * scale;
        var source = new Rect2(fw * (frame % frames), 0f, fw, height);
        DrawSetTransform(bottom, 0f, new Vector2(flip ? -1f : 1f, 1f));
        DrawTextureRectRegion(texture, new Rect2(-size.X / 2f, -size.Y, size.X, size.Y), source, new Color(1f, 1f, 1f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>名字對應的素材在就畫、回傳 true；沒有回傳 false。</summary>
    private bool DrawArt(string name, Vector2 bottom, float radius, int frames = 1, int frame = 0, bool flip = false, float alpha = 1f)
    {
        if (PixelParts.Texture(name) is not { } texture)
            return false;
        DrawArt(texture, bottom, radius, frames, frame, flip, alpha);
        return true;
    }

    /// <summary>素材底下的落地陰影（光源左上：往右下偏）。</summary>
    private void GroundShadow(Vector2 bottom, float width, float alpha = 0.32f)
    {
        DrawSetTransform(bottom + new Vector2(width * 0.08f, -width * 0.02f), 0f, new Vector2(1f, 0.3f));
        DrawCircle(Vector2.Zero, width * 0.5f, new Color(0.08f, 0.06f, 0.04f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>橫向重複用的素材（圍牆正面）。</summary>
    private static CanvasTexture? KitRepeat(string name) => PixelParts.IsBatch5(name) ? PixelParts.Texture(name, repeat: true) : null;
}
