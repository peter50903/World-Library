using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 05-1 章：右側「史書」分頁——文字史書（第 08-10e 章）直接顯示在遊戲裡：每個世界時代一卷，
/// 有大事、人物、英雄，終章列出最大的聚落、傳說物品、家族與勢力。
/// 資料全部來自 Core <see cref="DemoSaga"/>，只讀不改世界。分頁開著才重寫，而且每個世界年只重寫一次（快轉時不每週重算）。
/// </summary>
public partial class ObserverRoot
{
    private PanelContainer _sagaPanel = null!;
    private RichTextLabel _sagaText = null!;
    private Button _sagaToggle = null!;
    private (object? World, int Year) _sagaKey;

    private void BuildSaga()
    {
        _sagaPanel = new PanelContainer();
        Skin(_sagaPanel);
        _hud.AddChild(_sagaPanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _sagaPanel.AddChild(column);
        column.AddChild(new Label { Text = "史書" });
        _sagaText = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectionEnabled = true
        };
        column.AddChild(_sagaText);
        _sagaPanel.Visible = false;
    }

    /// <summary>分頁開著才重寫；同一個世界、同一年只寫一次。</summary>
    private void RefreshSaga()
    {
        if (_sagaText is null || _rightView != RightView.Saga)
            return;
        var key = ((object?)_world, DemoSettlementHistory.YearOfDay(_world.Snapshot().DaysElapsed));
        if (key == _sagaKey)
            return;
        _sagaKey = key;
        _sagaText.Text = string.Join("\n", DemoSaga.Write(_world));
    }

    /// <summary>冒煙測試用：目前史書分頁的文字（分頁沒開是空字串）。</summary>
    internal string SagaShown() => _rightView == RightView.Saga && _sagaText is not null ? _sagaText.Text : string.Empty;
}
