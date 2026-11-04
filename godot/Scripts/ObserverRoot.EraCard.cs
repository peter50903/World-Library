using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章（時代轉換要有份量）：世界進入新時代時，全畫面顯示該時代的插圖與名稱數秒，再淡出；點一下可提早關閉。
/// 插圖缺少或無畫面模式時不顯示。只影響畫面。
/// </summary>
public partial class ObserverRoot
{
    /// <summary>【TEMP】時代插圖停留與淡入淡出的秒數。</summary>
    private const double TempEraCardHoldSeconds = 3.5;
    private const double TempEraCardFadeSeconds = 0.8;

    private Control? _eraCard;

    public static string EraArt(DemoEra era) => era switch
    {
        DemoEra.Hope => "res://assets/ui/era_hope.png",
        DemoEra.Pioneer => "res://assets/ui/era_pioneer.png",
        _ => "res://assets/ui/era_conquest.png"
    };

    private static string EraLine(DemoEra era) => era switch
    {
        DemoEra.Hope => "大地豐饒，人們在河邊與林間安居",
        DemoEra.Pioneer => "斧與犁走進荒野，危險與希望一同到來",
        _ => "城牆升起，王旗飄揚"
    };

    private void ShowEraCard(DemoEra era)
    {
        if (_audioMuted || ArtAssets.Texture(EraArt(era)) is not { } art)
            return;
        _eraCard?.QueueFree();
        var card = new Control { MouseFilter = MouseFilterEnum.Stop, Modulate = new Color(1, 1, 1, 0) };
        card.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        card.AddChild(dim);
        var picture = new TextureRect
        {
            Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore
        };
        picture.SetAnchorsPreset(LayoutPreset.FullRect);
        card.AddChild(picture);
        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        text.SetAnchorsPreset(LayoutPreset.BottomWide);
        text.OffsetTop = -170;
        text.OffsetBottom = -40;
        Label Line(string s, int size)
        {
            var label = new Label { Text = s, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", TempParchmentText);
            label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.06f, 0.04f));
            label.AddThemeConstantOverride("outline_size", 8);
            return label;
        }
        text.AddChild(Line($"— 世界進入{DemoKnowledge.EraName(era)} —", 44));
        text.AddChild(Line(EraLine(era), 22));
        card.AddChild(text);
        AddChild(card);
        _eraCard = card;
        var tween = card.CreateTween();
        tween.TweenProperty(card, "modulate:a", 1.0, TempEraCardFadeSeconds);
        tween.TweenInterval(TempEraCardHoldSeconds);
        tween.TweenProperty(card, "modulate:a", 0.0, TempEraCardFadeSeconds);
        tween.TweenCallback(Callable.From(() => CloseEraCard(card)));
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
                CloseEraCard(card);
        };
    }

    private void CloseEraCard(Control card)
    {
        if (ReferenceEquals(_eraCard, card))
            _eraCard = null;
        if (GodotObject.IsInstanceValid(card))
            card.QueueFree();
    }
}
