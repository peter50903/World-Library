using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// V0-03B: Renders the Core TEMP hex map and TEMP settlements injected by
/// ObserverRoot from the observer world's single RngRegistry. This view NEVER
/// generates terrain or sites and never draws from RNG.
/// </summary>
public partial class DemoHexMap : Control
{
    /// <summary>Injected by ObserverRoot; nothing is drawn until it is set.</summary>
    public DemoHexWorld? World { get; set; }

    /// <summary>Injected by ObserverRoot; settlement markers come only from Core.</summary>
    public DemoWorldSites? Sites { get; set; }


    public event Action<DemoHexCell>? CellSelected;

    /// <summary>第 05 章 b 節：地圖模式（只影響顯示）。</summary>
    public enum Mode
    {
        Terrain,
        Food,
        Lineage, // 第 08-10a 章 V2：家系
        Knowledge, // 第 08-10a 章 V4：知識（已習得幾項技術）
        Culture, // 第 08-10a 章 V5、08-8 章 B1：文化（各聚落的主要文化）
        Faith, // 第 08-8 章 B3：信仰（各聚落的主要信仰；無信仰灰色，聖地標記）
        Realm // 第 08-19 章：勢力（勢力範圍與隸屬）
    }

    /// <summary>第 08-8 章 B3：聚落的主要信仰編號（沒有信徒的聚落不在表內）。</summary>
    public IReadOnlyDictionary<int, int> FaithOf { get; set; } = new Dictionary<int, int>();

    /// <summary>第 08-16 章：各文化的紋章、各信仰的符號（文化與信仰模式畫在聚落上）。</summary>
    public IReadOnlyDictionary<int, DemoEmblem> CultureEmblems { get; set; } = new Dictionary<int, DemoEmblem>();
    public IReadOnlyDictionary<int, string> FaithSymbolsById { get; set; } = new Dictionary<int, string>();

    /// <summary>第 08-8 章 B3：信仰的固定順序（信仰編號），決定配色。</summary>
    public IReadOnlyList<int> FaithOrder { get; set; } = Array.Empty<int>();

    /// <summary>第 08-8 章 B3：現存信仰的創立聚落（聖地）。</summary>
    public IReadOnlySet<int> HolySites { get; set; } = new HashSet<int>();

    /// <summary>無信仰聚落的顏色。</summary>
    public static readonly Color TempNoFaithColor = new(0.55f, 0.55f, 0.55f);

    public Color FaithColor(int faithId)
    {
        if (faithId == 0)
            return TempNoFaithColor;
        // 第 08-54 章：同一家族（分裂出來的）顏色相近；源頭信仰與家系錯開四分之一圈。
        return FamilyFaithColor(faithId);
    }

    /// <summary>第 08-8 章 B1：聚落的主要文化編號（文化模式著色）。</summary>
    public IReadOnlyDictionary<int, int> CultureOf { get; set; } = new Dictionary<int, int>();

    /// <summary>第 08-8 章 B1：文化的固定順序（文化編號），決定配色。</summary>
    public IReadOnlyList<int> CultureOrder { get; set; } = Array.Empty<int>();

    public Color CultureColor(int cultureId)
    {
        var index = 0;
        for (var i = 0; i < CultureOrder.Count; i++)
            if (CultureOrder[i] == cultureId) index = i;
        // 與家系錯開半圈，兩個模式切換時顏色不會一樣。
        return TempLineagePalette[(index + TempLineagePalette.Length / 2) % TempLineagePalette.Length];
    }

    private Mode _mode = Mode.Terrain;

    public Mode MapMode
    {
        get => _mode;
        set
        {
            _mode = value;
            QueueRedraw();
        }
    }

    /// <summary>第 08-10a 章 V2：每一格的家系歸屬（Core <see cref="DemoLineageMap.Compute"/>，ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<DemoCellRef, DemoLineageCell> Lineage { get; set; } = new Dictionary<DemoCellRef, DemoLineageCell>();

    /// <summary>第 08-10a 章 V2：家系的固定順序（開局聚落編號），決定配色。</summary>
    public IReadOnlyList<int> LineageRoots { get; set; } = Array.Empty<int>();

    /// <summary>第 08-10a 章 V4：聚落已習得的技術數（知識模式著色）。</summary>
    public IReadOnlyDictionary<int, int> KnowledgeCounts { get; set; } = new Dictionary<int, int>();

    // TEMP 顯示配色：知識（第 08-7a 章 A1 起技術變多，改依習得數分五段：0 灰褐、1～4 淡金、5～9 橙、10～14 赭紅、15 以上深紅）。
    public static readonly int[] KnowledgeBandStarts = { 0, 1, 5, 10, 15 }; // TEMP 分段

    public static int KnowledgeBand(int count) => Array.FindLastIndex(KnowledgeBandStarts, start => count >= start) is var i and >= 0 ? i : 0;

    /// <summary>第 08-13 章：聚落最擅長的領域（知識模式的色相）；沒有＝不在表內。</summary>
    public IReadOnlyDictionary<int, string> KnowledgeDomain { get; set; } = new Dictionary<int, string>();

    /// <summary>TEMP 顯示配色（第 08-13 章）：知識模式各領域的色相（飽和、彼此區隔）。</summary>
    public static Color DomainMapColor(string? domain) => domain switch
    {
        "10B" => new Color(0.45f, 0.75f, 0.20f), "10C" => new Color(0.55f, 0.55f, 0.60f), "10D" => new Color(0.80f, 0.62f, 0.30f),
        "10E" => new Color(0.55f, 0.35f, 0.15f), "10F" => new Color(0.85f, 0.40f, 0.65f), "10G" => new Color(0.95f, 0.70f, 0.15f),
        "10H" => new Color(0.20f, 0.75f, 0.60f), "10I" => new Color(0.75f, 0.15f, 0.15f), "10J" => new Color(0.25f, 0.45f, 0.90f),
        "10K" => new Color(0.60f, 0.30f, 0.90f), "10L" => new Color(0.90f, 0.45f, 0.25f), "10M" => new Color(0.95f, 0.85f, 0.30f),
        "10N" => new Color(0.50f, 0.42f, 0.30f), "HUNT" => new Color(0.20f, 0.50f, 0.25f), "SOCIETY" => new Color(0.45f, 0.35f, 0.75f),
        "FAITH" => new Color(0.95f, 0.95f, 0.75f),
        _ => new Color(0.45f, 0.42f, 0.38f)
    };

    /// <summary>第 08-13 章：知識模式顏色＝擅長領域的色相，技術越少越淡。</summary>
    public Color KnowledgeTint(int settlementId)
    {
        var count = KnowledgeCounts.GetValueOrDefault(settlementId);
        var hue = DomainMapColor(KnowledgeDomain.GetValueOrDefault(settlementId));
        return KnowledgeBand(count) switch
        {
            0 => DomainMapColor(null),
            1 => hue.Lerp(Colors.White, 0.45f),
            2 => hue.Lerp(Colors.White, 0.2f),
            _ => hue
        };
    }

    public static Color KnowledgeColor(int count) => KnowledgeBand(count) switch
    {
        0 => new Color(0.45f, 0.42f, 0.38f),
        1 => new Color(0.92f, 0.82f, 0.45f),
        2 => new Color(0.90f, 0.58f, 0.22f),
        3 => new Color(0.72f, 0.28f, 0.16f),
        _ => new Color(0.50f, 0.12f, 0.14f)
    };

    /// <summary>第 08-10a 章 V2：聚落人口與是否為村莊（決定圖示大小）。</summary>
    public IReadOnlyDictionary<int, (long Population, DemoSettlementTier Tier)> SettlementScale { get; set; } = new Dictionary<int, (long, DemoSettlementTier)>();

    // TEMP 顯示配色：家系（低飽和、羊皮紙地圖風格，12 色循環）。
    private static readonly Color[] TempLineagePalette =
    {
        new(0.78f, 0.33f, 0.27f), new(0.27f, 0.47f, 0.70f), new(0.85f, 0.65f, 0.24f), new(0.42f, 0.62f, 0.33f),
        new(0.58f, 0.38f, 0.65f), new(0.25f, 0.62f, 0.62f), new(0.80f, 0.48f, 0.62f), new(0.55f, 0.45f, 0.30f),
        new(0.40f, 0.40f, 0.75f), new(0.70f, 0.70f, 0.35f), new(0.50f, 0.25f, 0.25f), new(0.30f, 0.50f, 0.45f)
    };

    /// <summary>第 08-10a 章 V2：某家系的顏色（依開局聚落順序；同一世界不變）。</summary>
    public Color LineageColor(int rootId)
    {
        var index = 0;
        for (var i = 0; i < LineageRoots.Count; i++)
            if (LineageRoots[i] == rootId) index = i;
        return TempLineagePalette[index % TempLineagePalette.Length];
    }


    /// <summary>聚落 ID → 糧食狀況（ObserverRoot 每次刷新從 Core 週帳算好注入）。</summary>
    public IReadOnlyDictionary<int, FoodStatus> FoodStatuses { get; set; } = new Dictionary<int, FoodStatus>();

    // TEMP 顯示配色：糧食狀況（充足綠、緊張黃、飢餓紅、未結算灰）。
    public static Color FoodStatusColor(FoodStatus status) => status switch
    {
        FoodStatus.Ample => new Color(0.30f, 0.75f, 0.30f),
        FoodStatus.Tight => new Color(0.95f, 0.78f, 0.20f),
        FoodStatus.Starving => new Color(0.88f, 0.22f, 0.18f),
        _ => new Color(0.65f, 0.65f, 0.65f)
    };


    /// <summary>第 08-11 章：已消失的遺跡（不重要的遺跡荒廢數年後還原成原本的土地，地圖不再畫）。</summary>
    public IReadOnlySet<int> FadedRuins { get; set; } = new HashSet<int>();

    /// <summary>第 08-23 章：最近一週的商路（賣糧的村莊 → 市集；政治地圖中距離以上畫成流動的金色虛線）。</summary>
    public IReadOnlyList<(DemoCellRef From, DemoCellRef To)> TradeLinks { get; set; } = Array.Empty<(DemoCellRef, DemoCellRef)>();

    /// <summary>第 08-42 章：航線（港口之間的海路；被海上怪物擋住的畫成紅色、標出封鎖的地方）。</summary>
    public IReadOnlyList<DemoSeaLane> SeaLanes { get; set; } = Array.Empty<DemoSeaLane>();

    /// <summary>第 12-2j 章：目前的世界時代（政治地圖的氛圍：黑暗時代長夜降臨）。</summary>
    public DemoAge WorldAge { get; set; } = DemoAge.Hope;

    /// <summary>目前週次（戰鬥標記看本週與上週）。</summary>
    public int CurrentWeek { get; set; }

    /// <summary>第 12-1 章 C4：地下城所在格（近景與區域層畫地下城標記）。</summary>
    public IReadOnlyCollection<DemoCellRef> Dungeons { get; set; } = Array.Empty<DemoCellRef>();


    /// <summary>畫面上看得到的聚落（已消失的遺跡＝null）。</summary>
    private DemoSettlement? VisibleSite(DemoHexCell cell)
        => Sites?.SettlementAt(cell.Column, cell.Row) is { } s && !FadedRuins.Contains(s.Id) ? s : null;


    /// <summary>#89：事件詳情選到的聚落格（偏移座標），畫亮色外框；null＝不標示。只影響顯示。</summary>
    public (int Column, int Row)? Highlight
    {
        get => _highlight;
        set
        {
            _highlight = value;
            // 第 08-21 章：換了框選的格，政治地圖上選到的勢力、文化、信仰也一併取消（由點選另外設定）。
            _selectedRealm = _selectedCulture = _selectedFaith = 0;
            _selectedNest = 0; // 第 08-31 章
        }
    }

    private (int Column, int Row)? _highlight;

    // TEMP display-only controls. Never change simulation state or formal settings.
    private const float TempZoomStep = 1.2f;
    private const float TempMinZoom = 0.6f;
    private DemoHexWorld? _viewWorld;
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private bool _middleDragging;

    /// <summary>第 08-21 章：右鍵拖曳也能移動地圖（CK3 的操作）。</summary>
    private bool _rightDragging;

    public DemoHexMap()
    {
        CustomMinimumSize = new Vector2(540, 460);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = " "; // 第 08-5 章：提示文字依滑鼠所在格（_GetTooltip）
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        MouseExited += () => _middleDragging = _rightDragging = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        _plateHits.Clear(); // 第 08-18i 章：名牌的點選範圍
        using var perf = PerfMonitors.MapDraw(); // 第 09-1e 章：監看值「地圖重畫毫秒」
        if (World is null)
        {
            HidePolitical();
            return;
        }

        var (start, radius) = ViewLayout();

        _animatedOnScreen = false; // 第 09-1e 章：這一次重畫有沒有畫到會動的東西，由各畫法重新登記
        // 第 08-21 章：CK3 式政治地圖；第 08-25 章：舊的像素地圖已移除，只剩這一種畫法。
        DrawPolitical(start, radius);
    }

    /// <summary>第 08-52 章：正在拖曳地圖（中鍵或右鍵按著），或剛滾過滾輪（毫秒內）；快轉時這段時間先不做整套畫面更新。</summary>
    public bool Panning => _middleDragging || _rightDragging || Time.GetTicksMsec() - _lastWheelMs < TempPanQuietMs;

    /// <summary>【TEMP】滾輪縮放後多久內還算在移動畫面（毫秒）。</summary>
    private const ulong TempPanQuietMs = 400;

    private ulong _lastWheelMs;

    public override void _GuiInput(InputEvent @event)
    {
        if (World is null)
            return;

        if (@event is InputEventMouseMotion motion)
        {
            // Clear a missed release (e.g. when the pointer leaves the map).
            if (_middleDragging && !motion.ButtonMask.HasFlag(MouseButtonMask.Middle))
                _middleDragging = false;
            if (_rightDragging && !motion.ButtonMask.HasFlag(MouseButtonMask.Right))
                _rightDragging = false;
            if (_middleDragging || _rightDragging)
            {
                _pan += motion.Relative;
                QueueRedraw();
                AcceptEvent();
            }
            // 第 08-18i 章：滑鼠移到名牌上時整條加亮（看得出可以點）。
            var plate = PlateUnder(motion.Position);
            if (plate != _hoverPlate)
            {
                _hoverPlate = plate;
                MouseDefaultCursorShape = plate is null ? CursorShape.Arrow : CursorShape.PointingHand;
                QueueRedraw();
            }
            return;
        }

        if (@event is not InputEventMouseButton button)
            return;

        if (button.ButtonIndex == MouseButton.Middle)
        {
            _middleDragging = button.Pressed;
            AcceptEvent();
            return;
        }

        if (button.ButtonIndex == MouseButton.Right)
        {
            _rightDragging = button.Pressed;
            AcceptEvent();
            return;
        }

        if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _lastWheelMs = Time.GetTicksMsec();
            // Cursor-anchored zoom: the point under the pointer does not jump.
            var next = Mathf.Clamp(
                _zoom * (button.ButtonIndex == MouseButton.WheelUp ? TempZoomStep : 1f / TempZoomStep),
                TempMinZoom, Mathf.Max(1f, TempPoliticalMaxRadius / Layout().Radius));
            if (!Mathf.IsEqualApprox(next, _zoom))
            {
                var cursorFromCenter = button.Position - Size * 0.5f;
                _pan = cursorFromCenter - (cursorFromCenter - _pan) * (next / _zoom);
                _zoom = next;
                QueueRedraw();
            }
            AcceptEvent();
            return;
        }

        if (!button.Pressed || button.ButtonIndex != MouseButton.Left || _middleDragging)
            return;

        var clicked = CellAt(button.Position);
        LastClickCell = clicked is null ? null : new DemoCellRef(clicked.Column, clicked.Row);
        if (clicked is { } hit)
        {
            // 第 08-5 章（使用者 2026-10-23）：點選時框出選到的東西（第 08-31 章：框勢力範圍，不框格子）。
            // 試玩回饋 2026-11-07 之 2：依遠近點到勢力、文化、信仰或聚落、格，並把選到的移到畫面中央（DemoHexMap.Select.cs）。
            Select(button.Position, hit);
            AcceptEvent();
        }
    }

    /// <summary>
    /// 第 08-31 章：上一次左鍵點到的格（冒煙測試檢查平移、縮放後的點選換算；地塊本身已不能選，點到的格決定選到哪一片範圍）。
    /// 點在地圖外＝null。只是紀錄，不影響畫面與世界。
    /// </summary>
    public DemoCellRef? LastClickCell { get; private set; }

    /// <summary>畫面位置所在的格（格與格之間的縫隙不算）；不在任何格上回傳 null。</summary>
    private DemoHexCell? CellAt(Vector2 position)
    {
        if (World is null)
            return null;
        // 第 08-21 章：政治地圖照著色器的省份邊界（邊界是扭曲過的，不是方格）。
        if (_provinces is not null)
            return PoliticalCellAt(position);
        var (start, radius) = ViewLayout();
        // 第 08-18a 章：方格沒有縫隙，直接換算欄列。
        var at = HexCoordinates.CellOf(position - start, radius);
        return World.Contains(at.X, at.Y) ? World.GetCell(at.X, at.Y) : null;
    }

    /// <summary>
    /// 第 08-5 章（使用者 2026-10-23）：滑鼠停在格上只說明這一格是什麼（森林、大海、某某村莊…）。
    /// </summary>
    public override string _GetTooltip(Vector2 atPosition)
    {
        if (World is null || CellAt(atPosition) is not { } cell)
            return "";
        return PoliticalTooltip(cell);
    }

    /// <summary>
    /// #89：把指定格移到地圖中央並標示外框（只改顯示用的平移，不改縮放、不改世界狀態）。
    /// </summary>
    public void FocusCell(DemoHexCell cell)
    {
        RememberView();
        _pan += Size * 0.5f - CellCenter(cell);
        Highlight = (cell.Column, cell.Row);
        QueueRedraw();
    }

    /// <summary>第 08-18i 章（GPT 試玩回饋 12：縮到最小時沒有方向感）：定位前的鏡頭（「回上一個位置」用；沒有＝null）。</summary>
    private (float Zoom, Vector2 Pan)? _previousView;

    public bool HasPreviousView => _previousView is not null;

    private void RememberView() => _previousView = (_zoom, _pan);

    /// <summary>一鍵看全世界：整張地圖置中放進畫面（第 08-13 章的基準大小）。</summary>
    public void ShowWholeWorld()
    {
        RememberView();
        // 大地圖的基準格可能比放得下的還大（基準至少 4 像素）：縮到真的整張放得進畫面。
        var (_, baseRadius) = Layout();
        var columns = World?.Columns ?? DemoHexWorld.SmallColumns;
        var rows = World?.Rows ?? DemoHexWorld.SmallRows;
        var fit = Mathf.Min((Size.X - 56f) / (2f * columns), (Size.Y - 56f) / (2f * rows));
        _zoom = Mathf.Min(1f, Mathf.Max(0.05f, fit / baseRadius));
        _pan = Vector2.Zero;
        QueueRedraw();
    }

    /// <summary>回到定位或看全世界之前的鏡頭。</summary>
    public void ReturnToPreviousView()
    {
        if (_previousView is not { } view)
            return;
        _previousView = (_zoom, _pan);
        (_zoom, _pan) = view;
        QueueRedraw();
    }

    /// <summary>Used by the in-engine smoke test to inject a TEMP cell click.</summary>
    public Vector2 CellCenter(DemoHexCell cell)
    {
        var (start, radius) = ViewLayout();
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    /// <summary>Shared geometry for drawing, hit-testing and all Core cell markers (TEMP view only).</summary>
    private (Vector2 Start, float Radius) ViewLayout()
    {
        var (baseStart, baseRadius) = Layout();
        // 第 08-13 章第 5 節：換了世界就重設鏡頭；全圖塞進畫面時每格太小（大地圖），先拉近到看得清楚的大小（地圖中央）。
        if (!ReferenceEquals(_viewWorld, World) && World is not null && Size.X > 0f && Size.Y > 0f)
        {
            _viewWorld = World;
            // 第 08-21 章：政治地圖一開始看整個世界（CK3）；舊地圖照舊拉近到看得清楚的大小。
            _zoom = 1f;
            _pan = Vector2.Zero;
        }
        var viewportCenter = Size * 0.5f;
        return (viewportCenter + (baseStart - viewportCenter) * _zoom + _pan,
            baseRadius * _zoom);
    }

    private bool OnScreen(Vector2 center, float radius)
        => center.X > -radius * 2f && center.Y > -radius * 2f && center.X < Size.X + radius * 2f && center.Y < Size.Y + radius * 2f;

    private (Vector2 Start, float Radius) Layout()
    {
        const float padding = 28f;
        var columns = World?.Columns ?? DemoHexWorld.SmallColumns;
        var rows = World?.Rows ?? DemoHexWorld.SmallRows;
        // 第 08-18a 章：方格，一格邊長＝2×radius。
        var widthUnits = 2f * columns;
        var heightUnits = 2f * rows;
        var radius = Mathf.Max(4f, Mathf.Min(
            (Size.X - 2f * padding) / widthUnits,
            (Size.Y - 2f * padding) / heightUnits));

        var start = new Vector2(
            (Size.X - widthUnits * radius) / 2f + radius,
            (Size.Y - heightUnits * radius) / 2f + radius);
        return (start, radius);
    }
}
