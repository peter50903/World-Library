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

    private sealed record SimBatch(DemoObserverWorld World, int Advanced, DemoDayAdvance? Last, DemoSettlementEvent? Alert, Exception? Error);

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
        _simTask = System.Threading.Tasks.Task.Run(() => RunBatch(world, count, autoPause));
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
        AfterPlayback(batch.Advanced, batch.Last, batch.Alert);
    }

    /// <summary>等背景這一批算完並交給畫面（輸入、存檔等要碰世界之前呼叫）。</summary>
    private void WaitForSimulation()
    {
        if (_simTask is { } task && !task.IsCompleted)
            task.Wait();
        FinishSimulation();
    }

    /// <summary>任何按鍵、點擊之前先等背景算完：處理輸入時世界一定是完整的一天。</summary>
    public override void _Input(InputEvent @event)
    {
        if (_simTask is not null && @event is InputEventMouseButton or InputEventKey or InputEventScreenTouch)
            WaitForSimulation();
    }

    /// <summary>狀態列：實際速度（電腦算不了那麼快時低於選定速度）。</summary>
    private string ActualSpeedText => _playback.Playing && _actualDaysPerSecond > 0 && _actualDaysPerSecond < _playback.DaysPerSecond * 0.9
        ? $"（實際約每秒 {_actualDaysPerSecond:0} 天）" : "";
}
