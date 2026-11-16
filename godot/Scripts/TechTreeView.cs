using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-10d 章 D：知識樹畫成真的樹。每個領域一區，由左到右是第 0～5 階，同一領域的前置之間畫連線；
/// 節點依狀態上色：已會（領域色）、研究中（深色加進度條）、前置已具備（亮底金框）、還缺前置（灰）。
/// 滑鼠停在節點上說明這項技術、目前狀態與來歷（世界首創者、已有幾個聚落會）。
/// 只畫 Core <see cref="DemoTechTree"/> 給的資料，不改世界。
/// </summary>
public partial class TechTreeView : Control
{
    /// <summary>【TEMP】版面：階數（欄數）、節點高、上下間距、領域標題高、字級。</summary>
    private const int Tiers = 6;
    private const float NodeHeight = 34f, RowGap = 6f, ColumnGap = 8f, HeaderHeight = 24f, BlockGap = 10f, FallbackWidth = 360f;
    private const int FontSize = 12, HeaderFontSize = 14;

    private readonly IReadOnlyList<DemoTechTreeNode> _nodes;
    private readonly Func<string, Color> _domainColor;
    private readonly Func<DemoTechTreeNode, string> _tooltip;
    private readonly Dictionary<string, Rect2> _boxes = new();
    private readonly List<(string Domain, string Name, float Y)> _headers = new();
    private float _laidOutWidth = -1f;

    /// <summary>引擎自己建立節點時用（空的樹）。</summary>
    public TechTreeView() : this(Array.Empty<DemoTechTreeNode>(), _ => Colors.Gray, n => n.Name)
    {
    }

    public TechTreeView(IReadOnlyList<DemoTechTreeNode> nodes, Func<string, Color> domainColor, Func<DemoTechTreeNode, string> tooltip)
    {
        _nodes = nodes;
        _domainColor = domainColor;
        _tooltip = tooltip;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass; // 滾輪照樣捲動外面的清單
        TooltipText = " "; // 提示文字依滑鼠所在的節點（_GetTooltip）
        Arrange(FallbackWidth);
    }

    public override void _Ready()
    {
        Resized += OnResized;
        OnResized();
    }

    private void OnResized()
    {
        var width = Size.X > 1f ? Size.X : FallbackWidth;
        if (Mathf.IsEqualApprox(width, _laidOutWidth))
            return;
        Arrange(width);
        QueueRedraw();
    }

    /// <summary>排版：每個領域一區（依領域代號），區內每一階一欄，同一階的節點由上往下排。</summary>
    private void Arrange(float width)
    {
        _laidOutWidth = width;
        _boxes.Clear();
        _headers.Clear();
        var columnWidth = Math.Max(40f, (width - ColumnGap * (Tiers - 1)) / Tiers);
        var y = 0f;
        foreach (var domain in _nodes.GroupBy(n => n.Domain).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            _headers.Add((domain.Key, domain.First().DomainName, y));
            y += HeaderHeight;
            var rows = 0;
            foreach (var tier in domain.GroupBy(n => Math.Clamp(n.Tier, 0, Tiers - 1)))
            {
                var row = 0;
                foreach (var node in tier)
                {
                    _boxes[node.Id] = new Rect2(tier.Key * (columnWidth + ColumnGap), y + row * (NodeHeight + RowGap), columnWidth, NodeHeight);
                    row++;
                }
                rows = Math.Max(rows, row);
            }
            y += rows * (NodeHeight + RowGap) + BlockGap;
        }
        CustomMinimumSize = new Vector2(0f, y);
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        foreach (var node in _nodes)
            if (_boxes.TryGetValue(node.Id, out var box) && box.HasPoint(atPosition))
                return _tooltip(node);
        return "";
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var ink = new Color(0.20f, 0.14f, 0.08f);
        foreach (var (domain, name, y) in _headers)
        {
            DrawRect(new Rect2(0f, y + 3f, 4f, HeaderHeight - 8f), _domainColor(domain));
            DrawString(font, new Vector2(10f, y + HeaderHeight - 7f), name, HorizontalAlignment.Left, -1, HeaderFontSize, ink);
        }
        // 連線先畫（在節點下面）：同一領域裡，前置的右緣連到這一項的左緣；兩邊都會的線比較亮。
        var byId = _nodes.ToDictionary(n => n.Id);
        foreach (var node in _nodes)
        {
            if (!_boxes.TryGetValue(node.Id, out var dst))
                continue;
            foreach (var parentId in node.ParentIds)
            {
                if (!byId.TryGetValue(parentId, out var parent) || parent.Domain != node.Domain || !_boxes.TryGetValue(parentId, out var src))
                    continue;
                var lit = parent.Status == DemoTechStatus.Known && node.Status == DemoTechStatus.Known;
                var color = lit ? _domainColor(node.Domain) : new Color(0.45f, 0.40f, 0.34f, 0.55f);
                var a = new Vector2(src.End.X, src.Position.Y + src.Size.Y / 2f);
                var b = new Vector2(dst.Position.X, dst.Position.Y + dst.Size.Y / 2f);
                DrawLine(a, b, color, lit ? 2.5f : 1.5f, true);
            }
        }
        foreach (var node in _nodes)
        {
            if (!_boxes.TryGetValue(node.Id, out var box))
                continue;
            var domainColor = _domainColor(node.Domain);
            var (fill, border, text) = node.Status switch
            {
                DemoTechStatus.Known => (domainColor, new Color(0.10f, 0.08f, 0.06f), Colors.White),
                DemoTechStatus.Researching => (domainColor.Darkened(0.45f), new Color(0.10f, 0.08f, 0.06f), new Color(0.95f, 0.92f, 0.85f)),
                DemoTechStatus.Ready => (new Color(0.96f, 0.91f, 0.76f), new Color(0.85f, 0.62f, 0.15f), ink),
                _ => (new Color(0.62f, 0.60f, 0.56f, 0.55f), new Color(0.40f, 0.38f, 0.35f, 0.7f), new Color(0.25f, 0.23f, 0.20f, 0.8f))
            };
            DrawRect(box, fill);
            DrawRect(box, border, false, node.Status == DemoTechStatus.Ready ? 2f : 1f);
            if (node.Status == DemoTechStatus.Researching)
                DrawRect(new Rect2(box.Position.X, box.End.Y - 4f, box.Size.X * node.Percent / 100f, 4f), new Color(0.95f, 0.80f, 0.35f));
            // 有其他領域的前置：左上角一個小點（連線只畫同一領域的）。
            if (node.ParentIds.Any(p => byId.TryGetValue(p, out var other) && other.Domain != node.Domain))
                DrawCircle(box.Position + new Vector2(5f, 5f), 2.5f, border);
            DrawMultilineString(font, box.Position + new Vector2(4f, FontSize + 2f), node.Name, HorizontalAlignment.Center, box.Size.X - 8f, FontSize, 2, text);
        }
    }
}
