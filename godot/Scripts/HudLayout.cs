namespace WorldSimulation.GodotClient;

/// <summary>第 05 章：版面矩形（像素，左上角為原點）。純 C#，不引用 Godot，可單元測試。</summary>
public readonly record struct HudRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Inside(int width, int height)
        => X >= 0 && Y >= 0 && Width >= 1 && Height >= 1 && Right <= width && Bottom <= height;

    public bool Overlaps(HudRect other)
        => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>第 05 章：一次版面計算的結果。左右面板關閉時寬度為 0（不顯示）。
/// 第 08-3 章：底部事件列移到右側「世界紀錄」分頁；地圖模式併入工具列，與時間控制並排置底。</summary>
public readonly record struct HudFrame(
    HudRect TopBar,
    HudRect Detail,
    HudRect Console,
    HudRect Time,
    int MapVisibleWidth,
    HudRect Toolbar = default);

/// <summary>
/// 第 05 章：觀察者介面版面（全螢幕地圖＋疊加面板；參考大戰略／世界模擬遊戲的常見配置）。
/// 頂部資訊列、左側詳情、右側面板（聚落清單／世界紀錄）、右下時間控制、左下功能列（地圖模式＋工具）。
/// 輸入視窗尺寸與左右面板是否展開，輸出各面板矩形；保證全部在視窗內、頂部列與時間控制不被蓋住、
/// 左右面板展開時地圖可見寬度不少於視窗寬 40%。所有尺寸常數都是 TEMP 顯示參數，不是世界規則。
/// </summary>
public static class HudLayout
{
    public const int TempMargin = 8;          // TEMP：面板與視窗邊緣的間距
    public const int TempTopBarHeight = 44;   // TEMP
    public const int TempTimeWidth = 360;     // TEMP：右下時間控制（播放、+1 日、5 檔速度）
    public const int TempTimeHeight = 64;     // TEMP：圖示按鈕 36＋羊皮紙框上下內距 14×2
    public const int TempDetailMin = 260;     // TEMP：左側詳情寬
    public const int TempDetailMax = 420;     // TEMP
    public const double TempDetailShare = 0.24;
    public const int TempConsoleMin = 300;    // TEMP：右側面板寬
    public const int TempConsoleMax = 540;    // TEMP
    public const double TempConsoleShare = 0.30;
    public const double TempMapMinShare = 0.40; // 地圖至少保留的可見寬度比例
    public const int TempBarHeight = 72;      // TEMP：功能列高（圖示按鈕 64＋木板上下 4）
    public const int TempToolbarWidth = 830;  // TEMP：十一顆 64 圖示按鈕（地形、糧食、家系、知識、文化、格線｜情境、存讀檔、說明、圖表、開發）＋間距 8×10＋木板左右邊（第 08-11 章：原 600 放不下；第 08-8 章 B1 加文化）

    public static HudFrame Compute(int width, int height, bool detailOpen, bool consoleOpen)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        var m = Math.Min(TempMargin, Math.Max(0, Math.Min(width, height) / 20));

        var topH = Math.Min(TempTopBarHeight, Math.Max(1, height / 6));
        var topBar = new HudRect(0, 0, width, topH);

        var timeW = Math.Max(1, Math.Min(TempTimeWidth, width - 2 * m));
        var timeH = Math.Max(1, Math.Min(TempTimeHeight, (height - topH) / 4));
        var time = new HudRect(width - m - timeW, height - m - timeH, timeW, timeH);

        // 功能列：左下角，底緣與時間控制同高；放不下時（窄視窗）改放時間控制上方、佔滿寬度。
        var barH = Math.Max(1, Math.Min(TempBarHeight, (height - topH) / 6));
        var besideW = width - 3 * m - timeW;
        var toolbar = besideW >= TempToolbarWidth
            ? new HudRect(m, height - m - barH, Math.Min(TempToolbarWidth, besideW), barH)
            : new HudRect(m, Math.Min(Math.Max(topH, time.Y - m - barH), Math.Max(0, height - barH)), Math.Max(1, Math.Min(TempToolbarWidth, width - 2 * m)), barH);

        var sideTop = topH + m;
        var sideBottom = Math.Min(toolbar.Y, time.Y) - m;
        var sideH = Math.Max(1, sideBottom - sideTop);

        var detailW = detailOpen ? Math.Clamp((int)Math.Round(width * TempDetailShare), TempDetailMin, TempDetailMax) : 0;
        var consoleW = consoleOpen ? Math.Clamp((int)Math.Round(width * TempConsoleShare), TempConsoleMin, TempConsoleMax) : 0;
        var maxPanels = (int)Math.Floor(width * (1 - TempMapMinShare)) - 4 * m;
        if (detailW + consoleW > maxPanels && detailW + consoleW > 0)
        {
            var scale = Math.Max(0, maxPanels) / (double)(detailW + consoleW);
            detailW = detailOpen ? Math.Max(1, (int)Math.Floor(detailW * scale)) : 0;
            consoleW = consoleOpen ? Math.Max(1, (int)Math.Floor(consoleW * scale)) : 0;
        }

        var detail = detailOpen ? new HudRect(m, sideTop, detailW, sideH) : new HudRect(m, sideTop, 0, 0);
        var console = consoleOpen
            ? new HudRect(width - m - consoleW, sideTop, consoleW, sideH)
            : new HudRect(width - m, sideTop, 0, 0);

        var mapVisible = width - (detailOpen ? detail.Right : 0) - (consoleOpen ? width - console.X : 0);
        return new HudFrame(topBar, detail, console, time, mapVisible, toolbar);
    }
}
