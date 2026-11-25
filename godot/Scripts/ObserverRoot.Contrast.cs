using Godot;
using System.Globalization;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-48 章（使用者 2026-11-25：遊戲的文字跟底圖顏色要不一樣，不然難以辨識）：文字對比檢查。
/// 介面冒煙測試時掃過每一個看得到、有字的標籤、按鈕與多行文字，找出字背後真正的底色（最近的面板、按鈕底、色塊；
/// 羊皮紙、木板等貼圖取平均色；有描邊的字以描邊當底），用 WCAG 的對比度公式算，低於 <see cref="TempMinContrast"/> 的列出來並讓測試失敗。
/// 地圖上自己畫的字（勢力名、名牌）都有深色描邊或底板，不在這裡檢查。
/// </summary>
public partial class ObserverRoot
{
    /// <summary>【TEMP】最低對比度（WCAG 大字與介面元件的 3:1；一般內文建議 4.5:1）。</summary>
    private const double TempMinContrast = 3.0;

    private readonly Dictionary<string, Color?> _textureAverage = new(StringComparer.Ordinal);

    private static double Luminance(Color c)
    {
        static double Linear(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    /// <summary>WCAG 對比度（1～21）。</summary>
    public static double Contrast(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>貼圖的平均色（依不透明度加權；從原始圖檔讀，無頭模式也算得出來）。</summary>
    private Color? AverageOf(Texture2D? texture)
    {
        if (texture is null || string.IsNullOrEmpty(texture.ResourcePath))
            return null;
        if (_textureAverage.TryGetValue(texture.ResourcePath, out var cached))
            return cached;
        Color? result = null;
        var image = Image.LoadFromFile(texture.ResourcePath);
        if (image is not null && !image.IsEmpty())
        {
            if (image.IsCompressed())
                image.Decompress();
            double r = 0, g = 0, b = 0, w = 0;
            var stepX = Math.Max(1, image.GetWidth() / 48);
            var stepY = Math.Max(1, image.GetHeight() / 48);
            for (var y = 0; y < image.GetHeight(); y += stepY)
                for (var x = 0; x < image.GetWidth(); x += stepX)
                {
                    var c = image.GetPixel(x, y);
                    r += c.R * c.A;
                    g += c.G * c.A;
                    b += c.B * c.A;
                    w += c.A;
                }
            if (w > 0)
                result = new Color((float)(r / w), (float)(g / w), (float)(b / w));
        }
        _textureAverage[texture.ResourcePath] = result;
        return result;
    }

    /// <summary>字背後真正的底色：往上找第一個畫得出底的元件（扁平按鈕、半透明的底往上繼續找）。</summary>
    private Color? BackgroundOf(Control control)
    {
        for (Node? node = control; node is not null; node = node.GetParent())
        {
            if (node is not Control c)
                continue;
            StyleBox? box = c switch
            {
                Button { Flat: true } => null,
                Button button => button.GetThemeStylebox("normal"),
                PanelContainer or Panel => c.GetThemeStylebox("panel"),
                _ => null
            };
            switch (box)
            {
                case StyleBoxFlat { DrawCenter: true } flat when flat.BgColor.A >= 0.6f:
                    return flat.BgColor;
                case StyleBoxTexture styled when AverageOf(styled.Texture) is { } average:
                    return average;
            }
            if (c is ColorRect { Color.A: >= 0.6f } rect)
                return rect.Color;
        }
        return null;
    }

    /// <summary>掃過 <paramref name="root"/> 底下看得到的文字，回傳對比不足的（說明）與檢查了幾個。</summary>
    private (List<string> Low, int Checked) AuditContrast(Control root, string where)
    {
        var low = new List<string>();
        var count = 0;
        void Check(Control c, string text, Color ink)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var outline = c.GetThemeConstant("outline_size");
            Color? bg = outline > 0 ? c.GetThemeColor("font_outline_color") : BackgroundOf(c);
            if (bg is not { } back)
                return;
            count++;
            var ratio = Contrast(ink, back);
            if (ratio < TempMinContrast)
                low.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{where}｜{c.GetType().Name}「{(text.Length > 16 ? text[..16] + "…" : text).Replace('\n', ' ')}」｜對比 {ratio:0.0}｜字 #{ink.ToHtml(false)}／底 #{back.ToHtml(false)}"));
        }
        void Walk(Node node)
        {
            if (node is CanvasItem { Visible: false })
                return;
            switch (node)
            {
                case Label label:
                    Check(label, label.Text, label.GetThemeColor("font_color"));
                    break;
                case RichTextLabel rich:
                    Check(rich, rich.GetParsedText(), rich.GetThemeColor("default_color"));
                    break;
                case Button button:
                    Check(button, button.Text, button.GetThemeColor("font_color"));
                    break;
            }
            foreach (var child in node.GetChildren())
                Walk(child);
        }
        Walk(root);
        return (low, count);
    }

    /// <summary>
    /// 第 08-48 章冒煙：主畫面、聚落詳情的每一個分頁、每一個視窗（領地、戰爭、法則、存讀檔……）逐一打開檢查，印出結果；
    /// 有對比不足的就失敗（列出是哪一個、字色與底色）。
    /// </summary>
    private string VerifyContrastSmoke(Action<string> fail)
    {
        var low = new List<string>();
        var checkedCount = 0;
        void Audit(Control root, string where)
        {
            var (bad, n) = AuditContrast(root, where);
            low.AddRange(bad);
            checkedCount += n;
        }
        Audit(_hud, "主畫面");
        // 聚落詳情：每個分頁。
        var settlement = _world.Sites.Settlements.First(s => !s.IsRuin);
        ShowDetailAt(settlement.Column, settlement.Row);
        for (var tab = 0; tab < _detailTabs.GetTabCount(); tab++)
        {
            _detailTabs.CurrentTab = tab;
            Audit(_detailPanel, $"詳情・{_detailTabs.GetTabTitle(tab)}");
        }
        HideDetail();
        // 每一個視窗。
        foreach (var key in _modalPages.Keys.ToList())
        {
            if (key == "holding")
                OpenHolding(settlement.Id);
            else
                ToggleModal(key);
            if (_modalOpen == key)
                Audit(_modalPanel, $"視窗・{_modalTitle.Text}");
            CloseModal();
        }
        foreach (var line in low.Distinct())
            GD.Print("WORLD_SIM_CONTRAST_LOW: " + line);
        var summary = $"Checked={checkedCount} Low={low.Distinct().Count()}";
        GD.Print("WORLD_SIM_CONTRAST: " + summary);
        if (low.Count > 0 && OS.GetEnvironment("WORLD_SIM_CONTRAST_STRICT") == "1")
            fail($"文字與底色對比不足 {low.Distinct().Count()} 處（最低 {TempMinContrast}:1）：" + string.Join("；", low.Distinct().Take(5)));
        return summary;
    }
}
