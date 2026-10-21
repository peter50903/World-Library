namespace WorldSimulation.GodotClient;

/// <summary>
/// #87：觀察者畫面的播放／暫停計時器（純 C#，不引用 Godot，可單元測試）。
/// 只把「畫面經過的秒數」換算成「本幀要呼叫幾次 Core 的 AdvanceOneDay」；
/// 世界怎麼推進仍完全由 Core 決定，所以播放結果與逐日按「下一天」相同。
/// 播放速度是 TEMP 觀察者顯示值，不是世界規則，也不寫入正式設定檔。
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>
    /// TEMP 速度選項：每秒推進的世界日數（下拉選單順序＝右下 1～5 檔）。第 05 章 b2 由 3 檔擴為 5 檔；
    /// 前三檔維持 1／5／30，後兩檔約每秒一季、一年（實際受 <see cref="TempMaxDaysPerFrame"/> 與畫面幀率限制）。
    /// </summary>
    public static IReadOnlyList<int> TempDaysPerSecondOptions { get; } = new[] { 1, 5, 30, 90, 365 };

    /// <summary>
    /// TEMP：每幀最多推進的日數。畫面卡頓很久時不一次補跑大量日子（避免畫面凍結），
    /// 超出的時間直接丟棄；每一天仍逐日呼叫 Core，不會跳過任何週結算。
    /// </summary>
    public const int TempMaxDaysPerFrame = 30;

    private double _pendingDays;

    /// <summary>是否正在播放。</summary>
    public bool Playing { get; private set; }

    /// <summary>目前速度（每秒世界日數，TEMP）。</summary>
    public int DaysPerSecond { get; private set; } = TempDaysPerSecondOptions[0];

    /// <summary>開始播放。</summary>
    public void Play() => Playing = true;

    /// <summary>暫停：之後不論經過多久都不再推進，也清掉尚未累積滿一天的零頭。</summary>
    public void Pause()
    {
        Playing = false;
        _pendingDays = 0;
    }

    /// <summary>切換速度；只接受 TEMP 選項中的值。</summary>
    public void SetSpeed(int daysPerSecond)
    {
        if (!TempDaysPerSecondOptions.Contains(daysPerSecond))
            throw new ArgumentOutOfRangeException(nameof(daysPerSecond), $"播放速度只能是每秒 {string.Join("／", TempDaysPerSecondOptions)} 天（TEMP）。");
        DaysPerSecond = daysPerSecond;
        _pendingDays = 0;
    }

    /// <summary>
    /// 經過 <paramref name="deltaSeconds"/> 秒後，本幀應推進的世界日數（0～<see cref="TempMaxDaysPerFrame"/>）。
    /// 暫停、負值或非數字的時間一律回傳 0。
    /// </summary>
    public int Tick(double deltaSeconds)
    {
        if (!Playing || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0)
            return 0;

        _pendingDays += deltaSeconds * DaysPerSecond;
        if (_pendingDays >= TempMaxDaysPerFrame)
        {
            _pendingDays = 0;
            return TempMaxDaysPerFrame;
        }

        var days = (int)Math.Floor(_pendingDays);
        _pendingDays -= days;
        return days;
    }

    /// <summary>狀態列文字。</summary>
    public string StatusText => Playing ? $"播放中（每秒 {DaysPerSecond} 天，TEMP）" : "已暫停";
}
