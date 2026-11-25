using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 試玩回饋 2026-11-06 之 8（巨大地圖 5 倍速很卡）：實際遊玩時，世界的逐日推進改在背景執行緒做。
/// 巨大地圖一次週結算要 70～120 毫秒，原本在主執行緒上做，每隔幾幀畫面就停一下；現在畫面照常每幀重畫，
/// 世界算完一批才把新狀態交給地圖與面板（地圖持有的都是 Core 的不可變快照）。
/// 規則：背景推進時主執行緒不碰世界——任何滑鼠按鍵或鍵盤輸入都先等這一批算完（最多約一次週結算），
/// 所以點格、開詳情、存讀檔、切換情境看到的都是完整的一天。推進結果與逐日按「下一天」完全相同。
/// 電腦算不了那麼快時，實際速度會低於選定速度（狀態列顯示實際每秒幾天）。CI 冒煙照舊同步推進。
/// </summary>
public partial class ObserverRoot
{
    /// <summary>【TEMP】背景一批最多算多少毫秒（超過就先交回畫面，剩下的日子下一批再算）。</summary>
    private const double TempSimBatchMs = 250;

    private sealed record SimBatch(DemoObserverWorld World, int Advanced, DemoDayAdvance? Last, DemoSettlementEvent? Alert, Exception? Error)
    {
        /// <summary>第 08-52 章：換了一週時順便在背景算好的世界總覽（同 <see cref="OverviewText"/> 的快取鍵）；沒算＝null。</summary>
        public (int Week, int Year, bool Debug, string Text)? Overview { get; init; }
    }

    private System.Threading.Tasks.Task<SimBatch>? _simTask;
    private int _queuedDays;
    private readonly System.Diagnostics.Stopwatch _speedWatch = System.Diagnostics.Stopwatch.StartNew();
    private int _speedDays;
    private double _actualDaysPerSecond;

    private bool SimBusy => _simTask is { IsCompleted: false };


    /// <summary>每幀：把這一幀累積的日子排進下一批；上一批算完了就先交給畫面，再開始下一批。</summary>
    private void QueueSimulation(int days)
    {
        _queuedDays = Math.Min(_queuedDays + days, PlaybackClock.TempMaxDaysPerFrame);
        if (SimBusy)
            return;
        FinishSimulation();
        if (!_playback.Playing || _queuedDays == 0)
            return;
        var count = _queuedDays;
        _queuedDays = 0;
        var world = _world;
        var autoPause = _autoPauseOnMajor.ButtonPressed;
        // 第 08-52 章：世界總覽（右側清單上方）換週時要重算整份年帳與人口結構，大地圖約 0.2 秒——改在背景這一批算完時順便算。
        var overview = (DaysPerYear: _calendar?.DaysPerYear ?? 365, Debug: _showDebugInfo, Wanted: _rightView == RightView.Outliner);
        _simTask = System.Threading.Tasks.Task.Run(() => RunBatch(world, count, autoPause) is var batch && batch.Error is null && overview.Wanted
            ? batch with { Overview = BackgroundOverview(world, overview.DaysPerYear, overview.Debug) }
            : batch);
    }

    private SimBatch RunBatch(DemoObserverWorld world, int days, bool autoPause)
    {
        DemoDayAdvance? last = null;
        var advanced = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < days; i++)
            {
                if (advanced > 0 && watch.Elapsed.TotalMilliseconds > TempSimBatchMs)
                    break;
                last = world.AdvanceOneDay();
                advanced++;
                if (TakeNewMajorEvent() is { } major && autoPause)
                    return new SimBatch(world, advanced, last, major, null);
            }
            return new SimBatch(world, advanced, last, null, null);
        }
        catch (Exception ex)
        {
            return new SimBatch(world, advanced, last, null, ex);
        }
    }

    /// <summary>第 08-52 章：在背景執行緒算世界總覽（這時主執行緒不碰世界）；同一週算過就不再算。</summary>
    private (int Week, int Year, bool Debug, string Text)? BackgroundOverview(DemoObserverWorld world, int daysPerYear, bool debug)
    {
        var week = world.LatestWeek?.Week ?? 0;
        var year = Math.Max(0, world.Snapshot().DaysElapsed - 1) / daysPerYear;
        if (_overviewKey is { } cached && ReferenceEquals(cached.World, world) && cached.Week == week && cached.Year == year && cached.Debug == debug)
            return null;
        var o = DemoWorldOverview.Of(world, daysPerYear);
        return (week, year, debug, string.Join("\n", debug ? o.Lines() : o.PlayerLines()));
    }

    /// <summary>上一批算完了就在主執行緒交給畫面（沒算完什麼都不做）。</summary>
    private void FinishSimulation()
    {
        if (_simTask is not { IsCompleted: true } task)
            return;
        _simTask = null;
        var batch = task.Result;
        if (!ReferenceEquals(batch.World, _world))
            return; // 算到一半換了世界（不會發生：輸入都先等算完），丟掉
        if (batch.Error is { } error)
        {
            PausePlayback();
            _notice.Text = error is InvalidOperationException or OverflowException
                ? $"推進失敗（無狀態偽造），已暫停：{error.Message}"
                : $"推進失敗，已暫停：{error.Message}";
            return;
        }
        _speedDays += batch.Advanced;
        if (_speedWatch.Elapsed.TotalSeconds >= 1)
        {
            _actualDaysPerSecond = _speedDays / _speedWatch.Elapsed.TotalSeconds;
            _speedDays = 0;
            _speedWatch.Restart();
        }
        if (batch.Overview is { } ready)
        {
            _overviewKey = (batch.World, ready.Week, ready.Year, ready.Debug); // 第 08-52 章：背景算好的總覽直接用
            _overviewCache = ready.Text;
        }
        AfterPlayback(batch.Advanced, batch.Last, batch.Alert);
    }

    /// <summary>等背景這一批算完並交給畫面（輸入、存檔等要碰世界之前呼叫）。</summary>
    private void WaitForSimulation()
    {
        if (_simTask is { } task && !task.IsCompleted)
            task.Wait();
        FinishSimulation();
    }

    /// <summary>
    /// 任何按鍵、點擊之前先等背景算完：處理輸入時世界一定是完整的一天。
    /// 第 09-1e 章：滑鼠在地圖上時的滾輪與中鍵只動鏡頭（縮放、拖曳），不碰世界，不等——原本快轉時每滾一格就停到這一批算完。
    /// 滑鼠在面板上時照舊等（面板裡的數字框會跟著滾輪變動、重新查世界）。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (_simTask is null || @event is not (InputEventMouseButton or InputEventKey or InputEventScreenTouch))
            return;
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.Middle or MouseButton.Right }
            && GetViewport().GuiGetHoveredControl() == _hexMap)
            return; // 第 08-52 章：右鍵在地圖上也只是拖曳畫面（原本按下右鍵要等背景這一批算完，最多約 0.25 秒）
        WaitForSimulation();
    }

    /// <summary>狀態列：實際速度（電腦算不了那麼快時低於選定速度）。</summary>
    private string ActualSpeedText => _playback.Playing && _actualDaysPerSecond > 0 && _actualDaysPerSecond < _playback.DaysPerSecond * 0.9
        ? $"（實際約每秒 {_actualDaysPerSecond:0} 天）" : "";
}
