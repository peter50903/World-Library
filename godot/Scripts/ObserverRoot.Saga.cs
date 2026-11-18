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
        _sagaText.Text = SagaLayout(DemoSaga.WriteMarked(_world));
    }

    /// <summary>史書目錄：每一卷的標題與它在內文的段落位置（點目錄捲過去）。</summary>
    private readonly List<int> _sagaVolumeParagraphs = new();

    /// <summary>【TEMP】史書的要事：寫到這些字的大事以金色◆標出（王國、征服、英雄、傳說、信仰創立…）。</summary>
    private static readonly string[] TempSagaKeyWords =
    {
        "王國", "征服", "收服", "滅亡", "英雄", "傳說", "肅清", "攻克", "創立", "內戰", "篡位", "獨立", "第一", "世界進入", "王朝"
    };

    private const string SagaGold = "#e2bd6b", SagaInk = "#e6dfd0", SagaDim = "#a39a8a", SagaYear = "#d9a95a";

    /// <summary>
    /// 第 08-21 章（使用者 2026-11-17：史書排版不佳，沒有重點）：分頁裡的史書排版——
    /// 頂端一列各卷的目錄（點了捲到那一卷）；卷名與終章是金色大字；每條大事前面的年份上色、寫到王國征服英雄傳說等要事的整條加亮並標◆；
    /// 每卷末的統計（技術、各地、生死）縮小變淡；終章「某某：」的標籤加粗。文字內容與 Core 的史書相同。
    /// </summary>
    private string SagaLayout(IReadOnlyList<string> marked)
    {
        var body = new List<string>();
        var volumes = new List<(string Title, int Paragraph)>();
        foreach (var line in marked)
        {
            var plain = string.Concat(DemoSaga.Pieces(line).Select(p => p.Text));
            var code = SagaBbcode(line);
            if (plain.StartsWith("===", StringComparison.Ordinal))
            {
                body.Add($"[center][font_size=19][b][color={SagaGold}]{code.Trim('=', ' ')}[/color][/b][/font_size][/center]");
                continue;
            }
            if (plain.StartsWith("卷 ", StringComparison.Ordinal) || plain.StartsWith("終章", StringComparison.Ordinal))
            {
                var parts = code.Split('　', 2);
                var head = parts[0] + (plain.StartsWith("卷 ", StringComparison.Ordinal) && parts.Length > 1 ? "　" + parts[1].Split('　')[0] : "");
                var rest = parts.Length > 1 && plain.StartsWith("卷 ", StringComparison.Ordinal) ? string.Join("　", parts[1].Split('　').Skip(1)) : parts.Length > 1 ? parts[1] : "";
                body.Add("");
                volumes.Add((string.Concat(DemoSaga.Pieces(head).Select(p => p.Text)), body.Count));
                body.Add($"[font_size=20][b][color={SagaGold}]{head}[/color][/b][/font_size]");
                if (rest.Length > 0)
                    body.Add($"[i][color={SagaDim}]{rest}[/color][/i]");
                body.Add($"[color={SagaDim}]────────────────[/color]");
                continue;
            }
            if (plain.StartsWith("序", StringComparison.Ordinal))
            {
                body.Add($"[i][color={SagaInk}]{code}[/color][/i]");
                continue;
            }
            if (plain.StartsWith("　世人學會了", StringComparison.Ordinal) || plain.StartsWith("　各地：", StringComparison.Ordinal)
                || plain.StartsWith("　這些年裡", StringComparison.Ordinal) || plain.StartsWith("　另有", StringComparison.Ordinal))
            {
                body.Add($"[font_size=13][color={SagaDim}]{code}[/color][/font_size]");
                continue;
            }
            var year = System.Text.RegularExpressions.Regex.Match(code, "^　第 (\\d+) 年，");
            if (year.Success)
            {
                var key = TempSagaKeyWords.Any(k => plain.Contains(k, StringComparison.Ordinal));
                var text = code[year.Length..];
                var mark = key ? $"[color={SagaGold}]◆[/color] " : "　";
                var color = key ? "#f4e6c2" : SagaInk;
                body.Add($"{mark}[color={SagaYear}][b]第 {year.Groups[1].Value} 年[/b][/color]　[color={color}]{(key ? "[b]" + text + "[/b]" : text)}[/color]");
                continue;
            }
            var label = System.Text.RegularExpressions.Regex.Match(code, "^　([^：\\[]{1,8})：");
            if (label.Success)
            {
                body.Add($"　[color={SagaGold}][b]{label.Groups[1].Value}[/b][/color]：[color={SagaInk}]{code[label.Length..]}[/color]");
                continue;
            }
            body.Add($"[color={SagaInk}]{code}[/color]");
        }
        // 目錄放最上面：卷名可以點，捲到那一卷。
        var toc = volumes.Count == 0 ? "" : $"[color={SagaDim}]目錄：[/color]" + string.Join($"[color={SagaDim}]　·　[/color]",
            volumes.Select((v, i) => $"[url=vol:{i}]{v.Title.Replace("[", "[lb]", StringComparison.Ordinal)}[/url]"));
        _sagaVolumeParagraphs.Clear();
        _sagaVolumeParagraphs.AddRange(volumes.Select(v => v.Paragraph + (toc.Length > 0 ? 1 : 0)));
        return (toc.Length > 0 ? toc + "\n" : "") + string.Join("\n", body);
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
            case "vol" when id < _sagaVolumeParagraphs.Count:
                _sagaText.ScrollToParagraph(_sagaVolumeParagraphs[(int)id]); // 第 08-21 章：目錄
                break;
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
