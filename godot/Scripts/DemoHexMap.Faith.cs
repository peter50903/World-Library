using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-54 章（試玩回饋 2026-11-26 之 7：信仰模式直接抄 CK3，更直覺、更圖像化）：
/// <list type="bullet">
/// <item>信仰家族的顏色：分裂出來的信仰沿用母信仰的色相，只調深淺（CK3 同一宗教的各派顏色相近），一眼看得出誰從誰分出去。</item>
/// <item>信仰名字上面畫信仰符號（深色圓底＋信仰色外圈）。</item>
/// <item>聖地：信仰誕生的聚落畫金色光圈與信仰符號、寫「聖地」；點選某個信仰時，它的聖地光圈放大閃動（CK3 點信仰會標出聖地）。</item>
/// <item>條紋：聚落信的跟最上層統治者不一樣時，畫統治者信仰色的斜紋（CK3 信仰模式的條紋）。</item>
/// </list>
/// 只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第 08-54 章：每個信仰的母信仰（0＝自己是源頭；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<int, int> FaithParents { get; set; } = new Dictionary<int, int>();

    /// <summary>第 08-54 章：聖地（信仰誕生的聚落 → 信仰；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<int, int> HolySiteFaiths { get; set; } = new Dictionary<int, int>();

    /// <summary>【TEMP】分裂出來的信仰每一代色相偏多少、明度變多少。</summary>
    private const float TempSchismHueStep = 0.035f, TempSchismValueStep = 0.16f;

    /// <summary>第 08-55 章：從信仰視窗「在地圖上看」——框起這個信仰的範圍、聖地閃動（鏡頭不動）。</summary>
    public void SelectFaith(int faithId)
    {
        var holy = HolySiteFaiths.FirstOrDefault(kv => kv.Value == faithId).Key;
        Highlight = holy != 0 && SiteCell(holy) is { } cell ? (cell.Column, cell.Row) : null;
        _selectedFaith = faithId; // 要在 Highlight 之後（換框選會清掉選到的信仰）
        _areaPick = null;
        QueueRedraw();
    }

    /// <summary>信仰的源頭與第幾代（最多追 8 代）。</summary>
    private (int Root, int Depth) FaithRoot(int faithId)
    {
        var root = faithId;
        var depth = 0;
        while (depth < 8 && FaithParents.TryGetValue(root, out var parent) && parent != 0 && parent != root)
        {
            root = parent;
            depth++;
        }
        return (root, depth);
    }

    /// <summary>
    /// 第 08-54 章：信仰色——源頭信仰依出現順序分到調色盤的顏色；分裂出來的信仰用母信仰的色相，
    /// 奇數代偏亮、偶數代偏暗，同一代的兄弟再錯開一點色相。
    /// </summary>
    private Color FamilyFaithColor(int faithId)
    {
        var (root, depth) = FaithRoot(faithId);
        var roots = FaithOrder.Where(f => FaithRoot(f).Depth == 0).ToList();
        var index = Math.Max(0, roots.IndexOf(root));
        var basis = TempLineagePalette[(index * 3 + TempLineagePalette.Length / 4) % TempLineagePalette.Length];
        if (depth == 0)
            return basis;
        var parent = FaithParents.GetValueOrDefault(faithId);
        var siblings = FaithOrder.Where(f => FaithParents.GetValueOrDefault(f) == parent).ToList();
        var k = Math.Max(0, siblings.IndexOf(faithId));
        var hue = basis.H + TempSchismHueStep * (k + 1) * (depth % 2 == 1 ? 1f : -1f);
        var value = Mathf.Clamp(basis.V + TempSchismValueStep * (depth % 2 == 1 ? 1f : -1f), 0.25f, 0.95f);
        return Color.FromHsv(hue - Mathf.Floor(hue), basis.S * 0.85f, value);
    }

    /// <summary>信仰符號（白色剪影染信仰色）；沒有素材＝null。</summary>
    private Texture2D? FaithSign(int faithId)
        => FaithSymbolsById.TryGetValue(faithId, out var symbol) ? Heraldry.FaithSymbol(symbol, FaithColor(faithId)) : null;

    /// <summary>深色圓底、信仰色外圈的信仰符號。</summary>
    private void DrawFaithBadge(Vector2 center, float size, int faithId, float alpha)
    {
        var color = FaithColor(faithId);
        DrawCircle(center, size * 0.56f, new Color(0.10f, 0.07f, 0.05f, 0.85f * alpha));
        DrawArc(center, size * 0.56f, 0f, Mathf.Tau, 32, new Color(color.Lightened(0.15f), alpha), Math.Max(2f, size * 0.07f), true);
        if (FaithSign(faithId) is { } sign)
            DrawTextureRect(sign, new Rect2(center - new Vector2(size, size) * 0.4f, new Vector2(size, size) * 0.8f), false, new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>第 08-54 章：信仰模式的信仰名字上面畫符號（跟名字一起遠看清楚、拉近淡出）。</summary>
    private void DrawFaithLabelBadges(Vector2 origin, float cellPx)
    {
        if (_mode != Mode.Faith || _labels.Count == 0)
            return;
        var fade = 1f - Mathf.SmoothStep(TempMidCellPx * 1.1f, TempCloseCellPx * 1.15f, cellPx);
        if (fade <= 0.02f)
            return;
        var view = new Rect2(Vector2.Zero, Size).Grow(64f);
        foreach (var label in _labels)
        {
            var px = Math.Min(label.Size * cellPx, TempLabelMaxPx);
            if (px < TempLabelMinPx || label.Positions.Length == 0)
                continue;
            var middle = label.Positions.Aggregate(Vector2.Zero, (a, p) => a + p) / label.Positions.Length;
            var top = label.Positions.Min(p => p.Y);
            var at = origin + new Vector2(middle.X, top) * cellPx - new Vector2(0f, px * 1.25f);
            if (!view.HasPoint(at))
                continue;
            DrawFaithBadge(at, Mathf.Clamp(px * 1.5f, 26f, 64f), (int)label.Group, fade);
        }
    }

    /// <summary>第 08-54 章：聖地——金色光圈、信仰符號與「聖地」；選到的信仰，它的聖地光圈放大閃動。</summary>
    private void DrawHolySites(Vector2 origin, float cellPx)
    {
        if (_mode != Mode.Faith || World is null || Sites is null || HolySiteFaiths.Count == 0)
            return;
        var font = MapFont();
        var view = new Rect2(Vector2.Zero, Size).Grow(64f);
        var pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 260f);
        foreach (var (siteId, faithId) in HolySiteFaiths)
        {
            if (Sites.Settlements.FirstOrDefault(s => s.Id == siteId) is not { } site || !World.Contains(site.Column, site.Row))
                continue;
            var center = PoliticalCenter(origin, cellPx, site.Column, site.Row);
            if (!view.HasPoint(center))
                continue;
            var chosen = _selectedFaith == faithId;
            if (chosen)
                _animatedOnScreen = true;
            var size = Mathf.Clamp(cellPx * 0.9f, 22f, 48f) * (chosen ? 1.25f : 1f);
            var badge = center - new Vector2(0f, size * 0.95f);
            var gold = new Color(0.98f, 0.82f, 0.34f);
            DrawCircle(badge, size * 0.75f, new Color(gold, chosen ? 0.18f + 0.22f * pulse : 0.16f));
            DrawArc(badge, size * 0.68f, 0f, Mathf.Tau, 40, new Color(gold, 0.95f), chosen ? 3f + 2f * pulse : 2.5f, true);
            DrawFaithBadge(badge, size, faithId, 1f);
            const string text = "聖地";
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X;
            var at = badge + new Vector2(-width / 2f, size * 0.75f + 12f);
            DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, 12, 4, new Color(0.10f, 0.07f, 0.04f, 0.9f));
            DrawString(font, at, text, HorizontalAlignment.Left, -1, 12, gold);
        }
    }

    /// <summary>
    /// 第 08-54 章：信仰模式的條紋——聚落的主要信仰跟最上層統治者（勢力首都）不一樣時，用統治者的信仰色畫斜紋（CK3）。
    /// 回傳（聚落→斜紋色）；其他模式空。
    /// </summary>
    private IEnumerable<(int Settlement, Color Color)> FaithStripes()
    {
        if (_mode != Mode.Faith)
            yield break;
        foreach (var (settlement, faith) in FaithOf)
        {
            var top = Territory.RealmOf(settlement);
            if (top == settlement || faith == 0 || !FaithOf.TryGetValue(top, out var ruler) || ruler == 0 || ruler == faith)
                continue;
            yield return (settlement, FaithColor(ruler));
        }
    }
}
