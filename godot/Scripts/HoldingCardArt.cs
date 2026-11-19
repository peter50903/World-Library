using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-38 章：建築卡片上的插圖（CK3 的建築格插畫）——依分區上色的天空與地面，中間是建築的正面像素圖（整數倍放大、像素清楚）；
/// 施工中是淡淡的建築加上工地，毀損是廢墟，城牆、農田是一整排重複的牆面、田地。左上角是狀態圖示，右上角是數量。
/// </summary>
public partial class HoldingCardArt : Control
{
    private string? _sprite;
    private string? _overlay;
    private bool _tiled;
    private Color _backdrop = new(0.42f, 0.33f, 0.22f);
    private DemoBuildingStatus _status = DemoBuildingStatus.Standing;
    private bool _damaged;
    private int _count = 1;
    private string? _badge;

    /// <summary>Godot 重新載入腳本時需要無參數建構子。</summary>
    public HoldingCardArt()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Linear; // 縮小的大建築用線性取樣（放大的照樣用最近鄰，見 DrawSprite）
    }

    /// <summary>設定要畫的東西（<paramref name="overlay"/>＝施工中蓋在前面的工地；<paramref name="tiled"/>＝城牆、田地一整排）。</summary>
    public void SetArt(string? sprite, string? overlay, bool tiled, Color backdrop, DemoBuildingStatus status, bool damaged, int count, string? badge)
    {
        (_sprite, _overlay, _tiled, _backdrop, _status, _damaged, _count, _badge) = (sprite, overlay, tiled, backdrop, status, damaged, count, badge);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
            return;
        // 天空：上亮下暗；地面一條。
        var top = _backdrop.Lightened(0.28f);
        var bottom = _backdrop.Darkened(0.18f);
        DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) }, new[] { top, top, bottom, bottom });
        var groundY = MathF.Round(size.Y * 0.80f);
        DrawRect(new Rect2(0, groundY, size.X, size.Y - groundY), _backdrop.Darkened(0.42f));
        DrawRect(new Rect2(0, groundY, size.X, 2), _backdrop.Darkened(0.28f));
        var fade = _status == DemoBuildingStatus.Building ? new Color(1, 1, 1, 0.35f)
            : _status == DemoBuildingStatus.Ruined ? new Color(0.85f, 0.8f, 0.75f)
            : _damaged ? new Color(0.82f, 0.74f, 0.68f) : Colors.White;
        DrawSprite(_sprite, groundY, fade);
        if (_overlay is not null)
            DrawSprite(_overlay, groundY, Colors.White);
        if (_status == DemoBuildingStatus.Ruined)
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.15f, 0.02f, 0.0f, 0.18f)); // 毀損：整張偏暗紅
        if (_badge is not null && PixelParts.Texture(_badge) is { } badge)
            DrawTextureRect(badge, new Rect2(3, 3, 16, 16), false);
        if (_count > 1)
        {
            var font = ThemeDB.FallbackFont;
            var text = $"×{_count}";
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
            var box = new Rect2(size.X - width - 10, 3, width + 7, 17);
            DrawRect(box, new Color(0.08f, 0.06f, 0.04f, 0.78f));
            DrawString(font, new Vector2(box.Position.X + 3.5f, box.Position.Y + 13), text, HorizontalAlignment.Left, -1, 13, new Color(1f, 0.92f, 0.7f));
        }
    }

    /// <summary>畫一張素材：站在地面上、置中；能整數倍放大就整數倍（最多 4 倍），太大就縮小到放得下。多格動畫取第一格。</summary>
    private void DrawSprite(string? name, float groundY, Color modulate)
    {
        if (name is null || PixelParts.Texture(name, _tiled) is not { } texture)
            return;
        var frames = HoldingSprites.FramesOf(name);
        var frame = new Vector2(texture.GetWidth() / (float)frames, texture.GetHeight());
        if (frame.X <= 0 || frame.Y <= 0)
            return;
        var room = new Vector2(Size.X - 10, groundY - 6);
        if (_tiled)
        {
            // 一整排：高度佔天空的六成，寬度鋪滿。
            var s = Math.Clamp(MathF.Floor(room.Y * 0.6f / frame.Y), 1f, 4f);
            var height = frame.Y * s;
            DrawTextureRectRegion(texture, new Rect2(5, groundY + 2 - height, Size.X - 10, height), new Rect2(0, 0, (Size.X - 10) / s, frame.Y), modulate);
            return;
        }
        var fit = Math.Min(room.X / frame.X, room.Y / frame.Y);
        var scale = fit >= 1f ? Math.Min(4f, MathF.Floor(fit)) : fit;
        var drawn = frame * scale;
        var at = new Vector2(MathF.Round((Size.X - drawn.X) / 2f), MathF.Round(groundY + 3 - drawn.Y));
        // 放大：像素素材（最近鄰）；縮小：原圖跟著這個控制項用線性取樣，不會掉像素。
        Texture2D source = scale < 1f && texture.DiffuseTexture is { } raw ? raw : texture;
        DrawTextureRectRegion(source, new Rect2(at, drawn), new Rect2(0, 0, frame.X, frame.Y), modulate);
    }
}
