using System.Globalization;
using System.Text;
using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 05-1 章：右側「史書」分頁——文字史書（第 08-10e 章）直接顯示在遊戲裡：每個世界時代一卷，
/// 有大事、人物、英雄，終章列出最大的聚落、傳說物品、家族與勢力。
/// 第 05-2 章：名稱可以點（十字軍之王的名稱底線）——聚落：地圖移過去並開詳情；人物：人物頁（過世的人也打得開）；家族：家族頁。
/// 資料全部來自 Core <see cref="DemoSaga.WriteMarked"/>，只讀不改世界。分頁開著才重寫，而且每個世界年只重寫一次（快轉時不每週重算）。
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
        column.AddChild(new Label { Text = "史書（名稱可以點）" });
        _sagaText = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectionEnabled = false
        };
        _sagaText.MetaClicked += meta => OnSagaMeta(meta.AsString());
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
        _sagaText.Text = string.Join("\n", DemoSaga.WriteMarked(_world).Select(SagaBbcode));
    }

    /// <summary>有連結標記的一行 → BBCode（正文的 [ 跳脫；連結寫成 [url=種類:編號]）。</summary>
    private static string SagaBbcode(string marked)
    {
        var sb = new StringBuilder(marked.Length + 32);
        foreach (var piece in DemoSaga.Pieces(marked))
        {
            var text = piece.Text.Replace("[", "[lb]", StringComparison.Ordinal);
            if (piece.Kind == '\0')
                sb.Append(text);
            else
                sb.Append("[url=").Append(piece.Kind).Append(':').Append(piece.Id.ToString(CultureInfo.InvariantCulture)).Append(']')
                    .Append(text).Append("[/url]");
        }
        return sb.ToString();
    }

    /// <summary>點史書裡的名稱：s＝聚落、p＝人物、h＝家族。</summary>
    private void OnSagaMeta(string meta)
    {
        var parts = meta.Split(':', 2);
        if (parts.Length != 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return;
        switch (parts[0])
        {
            case "s":
                FocusSettlement((int)id);
                break;
            case "p":
                ShowPersonDetail(id);
                break;
            case "h":
                ShowHouseDetail(id);
                break;
        }
    }

    /// <summary>冒煙測試用：目前史書分頁的文字（分頁沒開是空字串）。</summary>
    internal string SagaShown() => _rightView == RightView.Saga && _sagaText is not null ? _sagaText.GetParsedText() : string.Empty;
}
