using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 5 節介面第一項：聚落與地點的滑鼠提示改成「小圖示＋數字」（第五批 ic_stat_* 圖示、ui_tooltip 底框）。
/// 提示的文字（<see cref="_GetTooltip"/>）照舊不變，冒煙測試比對得到；畫出來的是依同一格整理出的圖示列。素材缺少時退回文字提示。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    private readonly List<(string Icon, string Text)> _tipRows = new();
    private string _tipTitle = "";

    // 第五批數值小圖示（assets/manifest.csv B5_IC_STAT_*）。
    private const string IcPeople = "ic_stat_01", IcFood = "ic_stat_10", IcRealm = "ic_stat_13",
        IcKnowledge = "ic_stat_14", IcCulture = "ic_stat_15", IcFaith = "ic_stat_16", IcTier = "ic_stat_22", IcDisaster = "ic_stat_31",
        IcDungeon = "ic_stat_33", IcPlace = "ic_stat_36";

    /// <summary>整理滑鼠所在格的圖示列（標題＋每列一個圖示與一段字）。</summary>
    private void BuildTipRows(DemoHexCell cell, string title, string status)
    {
        _tipRows.Clear();
        _tipTitle = title;
        var at = new DemoCellRef(cell.Column, cell.Row);
        if (VisibleSite(cell) is { } site && SettlementScale.TryGetValue(site.Id, out var scale))
        {
            _tipRows.Add((IcTier, DemoSettlementHistory.TierName(scale.Tier)));
            _tipRows.Add((IcPeople, $"{scale.Population} 人"));
            _tipRows.Add((IcFood, FoodStatuses.GetValueOrDefault(site.Id) switch
            {
                FoodStatus.Starving => "缺糧，有人挨餓",
                FoodStatus.Tight => "糧食吃緊",
                FoodStatus.Ample => "糧食充足",
                _ => "還沒有收成紀錄"
            }));
            if (CultureOf.TryGetValue(site.Id, out var culture))
                _tipRows.Add((IcCulture, CultureNames.GetValueOrDefault(culture, $"#{culture}")));
            if (FaithOf.TryGetValue(site.Id, out var faith))
                _tipRows.Add((IcFaith, FaithNames.GetValueOrDefault(faith, $"#{faith}") + (HolySites.Contains(site.Id) ? "（聖地）" : "")));
            _tipRows.Add((IcKnowledge, $"技術 {KnowledgeCounts.GetValueOrDefault(site.Id)} 項"));
        }
        if (Territory.Cells.TryGetValue(at, out var owner) && SiteName(owner.RealmId) is { } realm)
            _tipRows.Add((IcRealm, owner.RealmId == owner.SettlementId && VisibleSite(cell)?.Id == owner.SettlementId ? $"{realm}（勢力之首）" : $"{realm}的勢力"));
        else if (VisibleSite(cell) is null)
            _tipRows.Add((IcPlace, "無主之地"));
        foreach (var nest in MonsterNests)
            if (nest.Dungeon.Cell == at)
                _tipRows.Add((IcDungeon, nest.Dormant ? $"{nest.Dungeon.Name}：沉寂中" : $"{nest.Dungeon.Name}：怪物 {nest.Monsters}"));
            else if (nest.Cells.Contains(at))
                _tipRows.Add((IcDungeon, $"{nest.Dungeon.Name}的怪物出沒"));
        if (status.Length > 0)
            _tipRows.Add((IcDisaster, status));
    }

    private string? SiteName(int id)
        => Sites?.Settlements.FirstOrDefault(s => s.Id == id) is { } s && SettlementScale.TryGetValue(id, out var scale)
            ? DemoSettlementHistory.TitledName(s, scale.Tier) : null;

    private StyleBoxTexture? _tipStyle;
    private bool _tipStyleTried;

    /// <summary>ui_tooltip 的深色金邊底框（只取框的部分，下方的尖角不拉伸）；沒有素材回傳 null。</summary>
    private StyleBoxTexture? TipStyle()
    {
        if (_tipStyleTried)
            return _tipStyle;
        _tipStyleTried = true;
        if (PixelParts.IsBatch5("ui_tooltip") && PixelParts.Texture("ui_tooltip") is { } texture)
            _tipStyle = ArtAssets.Track(new StyleBoxTexture
            {
                Texture = texture,
                RegionRect = new Rect2(18f, 21f, 64f, 50f), // 【TEMP】素材框的範圍
                TextureMarginLeft = 8, TextureMarginRight = 8, TextureMarginTop = 8, TextureMarginBottom = 8,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6
            });
        return _tipStyle;
    }

    public override GodotObject? _MakeCustomTooltip(string forText)
    {
        if (_tipRows.Count == 0 || PixelParts.Texture(IcPeople) is null)
            return null; // 沒有圖示素材：照舊文字提示
        var panel = new PanelContainer();
        if (TipStyle() is { } style)
            panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        panel.AddChild(column);
        var title = new Label { Text = _tipTitle };
        title.AddThemeColorOverride("font_color", new Color(0.98f, 0.88f, 0.60f));
        title.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(title);
        foreach (var (icon, text) in _tipRows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            if (PixelParts.Texture(icon) is { } texture)
                row.AddChild(new TextureRect
                {
                    Texture = texture, CustomMinimumSize = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest
                });
            var label = new Label { Text = text };
            label.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 0.88f));
            label.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(label);
            column.AddChild(row);
        }
        return panel;
    }
}
