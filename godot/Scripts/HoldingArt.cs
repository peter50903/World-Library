using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-40 章：領地示意圖（CK3 的領地插圖；使用者 2026-11-19：不要拼出來的領地圖，直接放符合等級的示意圖，要多做幾張）。
/// 依等級（營、村、鎮、城、都）與周圍地形（海邊＞丘陵＞森林＞平原）挑第十三批的示意圖，同一組有 A、B 兩款（依聚落編號），
/// 遺跡另有大、小兩種；那一張還沒交件就往下找（另一款、平原），都沒有時畫暫代圖（等級圖示）。
/// 下緣是 CK3 式的名牌（名稱與一行說明）；縮圖點一下打開大圖。只讀，不改世界。
/// </summary>
public partial class HoldingArt : Control
{
    private Texture2D? _art;
    private Texture2D? _crest;
    private string _caption = "";
    private string _subcaption = "";
    private bool _clickable;

    /// <summary>點了（打開領地大圖）。</summary>
    public event Action? Opened;

    /// <summary>Godot 重新載入腳本時需要無參數建構子。</summary>
    public HoldingArt()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    /// <summary>目前畫的示意圖路徑（null＝還沒有交件，畫暫代圖）。</summary>
    public string? ArtPath { get; private set; }

    /// <summary>照理要用的那一張（冒煙與截圖印出來，交件前也看得出選得對不對）。</summary>
    public string WantedPath { get; private set; } = "";

    public void ShowScene(DemoHoldingScene scene, string caption, string subcaption, bool clickable)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var candidates = Candidates(scene);
        WantedPath = candidates[0];
        ArtPath = candidates.FirstOrDefault(p => ArtAssets.Texture(p) is not null);
        _art = ArtPath is null ? null : ArtAssets.Texture(ArtPath);
        _crest = ArtAssets.Texture(ArtAssets.SettlementTierIcon(scene.Tier));
        (_caption, _subcaption, _clickable) = (caption, subcaption, clickable);
        MouseDefaultCursorShape = clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        TooltipText = clickable ? "點一下打開領地" : _art is null ? "示意圖（第十三批）還沒交件，暫時畫等級圖示" : " ";
        QueueRedraw();
    }

    /// <summary>
    /// 這個聚落依序可以用的示意圖：先找自己這組（等級、地形、款式），再找另一款，再找平原的兩款；遺跡看規模（鎮以上算大）。
    /// </summary>
    public static IReadOnlyList<string> Candidates(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var variant = scene.Site.Id % 2 == 0 ? "a" : "b";
        var other = variant == "a" ? "b" : "a";
        if (scene.Site.IsRuin)
        {
            var size = scene.Tier >= DemoSettlementTier.Town ? "large" : "small";
            return new[] { ArtFile($"ruin_{size}_{variant}"), ArtFile($"ruin_{size}_{other}") };
        }
        var tier = TierKey(scene.Tier);
        var land = LandKey(scene.Nearby);
        var list = new List<string> { ArtFile($"{tier}_{land}_{variant}"), ArtFile($"{tier}_{land}_{other}") };
        if (land != "plains")
            list.AddRange(new[] { ArtFile($"{tier}_plains_{variant}"), ArtFile($"{tier}_plains_{other}") });
        return list;
    }

    public static string TierKey(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => "camp",
        DemoSettlementTier.Village => "village",
        DemoSettlementTier.Town => "town",
        DemoSettlementTier.City => "city",
        _ => "capital"
    };

    /// <summary>周圍地形：一格內有海＝海邊；兩格內有丘陵＝丘陵；有森林＝森林；其餘＝平原。</summary>
    public static string LandKey(DemoBuildingNearby nearby) => nearby.Coast ? "coast" : nearby.Hills ? "hills" : nearby.Forest ? "forest" : "plains";

    private static string ArtFile(string key) => $"res://assets/holding/b13_{key}.png";

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (_art is not null)
        {
            // 鋪滿（裁掉多的部分，置中）。
            var art = new Vector2(_art.GetWidth(), _art.GetHeight());
            var scale = Math.Max(size.X / art.X, size.Y / art.Y);
            var shown = size / scale;
            DrawTextureRectRegion(_art, new Rect2(Vector2.Zero, size), new Rect2((art - shown) / 2f, shown));
        }
        else
            DrawPlaceholder(size);
        // 下緣的名牌（CK3 的領地名稱）。
        var font = ThemeDB.FallbackFont;
        var band = Math.Clamp(size.Y * 0.30f, 34f, 58f);
        DrawPolygon(new[] { new Vector2(0, size.Y - band), new Vector2(size.X, size.Y - band), size, new Vector2(0, size.Y) },
            new[] { new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.72f), new Color(0, 0, 0, 0.72f) });
        var big = band >= 50f ? 24 : 17;
        var small = band >= 50f ? 14 : 12;
        var x = 10f;
        if (_crest is not null)
        {
            var c = band * 0.62f;
            DrawTextureRect(_crest, new Rect2(8, size.Y - c - 6, c, c), false);
            x = 14 + c;
        }
        var line1 = size.Y - (_subcaption.Length > 0 ? small + 10 : 8);
        DrawString(font, new Vector2(x + 1, line1 + 1), _caption, HorizontalAlignment.Left, size.X - x - 8, big, new Color(0, 0, 0, 0.85f));
        DrawString(font, new Vector2(x, line1), _caption, HorizontalAlignment.Left, size.X - x - 8, big, new Color(1f, 0.9f, 0.62f));
        if (_subcaption.Length > 0)
            DrawString(font, new Vector2(x, size.Y - 7), _subcaption, HorizontalAlignment.Left, size.X - x - 8, small, new Color(0.96f, 0.92f, 0.82f));
        // 金色細框。
        DrawRect(new Rect2(Vector2.One, size - Vector2.One * 2), new Color(0.78f, 0.62f, 0.34f), false, 2f);
    }

    /// <summary>示意圖還沒交件：天空與地面的漸層，中間放大的等級圖示。</summary>
    private void DrawPlaceholder(Vector2 size)
    {
        var sky = new Color(0.55f, 0.66f, 0.74f);
        var ground = new Color(0.36f, 0.46f, 0.26f);
        var horizon = size.Y * 0.62f;
        DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X, 0), new Vector2(size.X, horizon), new Vector2(0, horizon) },
            new[] { sky.Lightened(0.2f), sky.Lightened(0.2f), sky, sky });
        DrawPolygon(new[] { new Vector2(0, horizon), new Vector2(size.X, horizon), size, new Vector2(0, size.Y) },
            new[] { ground.Lightened(0.1f), ground.Lightened(0.1f), ground.Darkened(0.3f), ground.Darkened(0.3f) });
        if (_crest is null)
            return;
        var c = Math.Min(size.Y * 0.62f, 140f);
        DrawTextureRect(_crest, new Rect2(size.X / 2 - c / 2, horizon - c * 0.78f, c, c), false);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_clickable && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            Opened?.Invoke();
            AcceptEvent();
        }
    }
}
