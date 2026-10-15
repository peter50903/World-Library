using System.Text;

namespace WorldSimulation.Core.Config;

// C08：參數表（01_世界參數）文字解析搬入 Core，CLI 與 Godot 共用同一份。
// Core 只解析呼叫端傳入的文字，不讀檔、不依賴程式倉庫路徑；讀檔留在 CLI／Godot 邊界。

/// <summary>參數表文字格式錯誤（訊息為繁體中文，可直接給使用者看）。</summary>
public sealed class ParameterSheetException : Exception
{
    public ParameterSheetException(string message) : base(message)
    {
    }
}

/// <summary>CSV 文字 → <see cref="ParameterRow"/>；欄位以中文欄名定位（ParameterID、值、資料型別、Enabled）。</summary>
public static class ParameterSheet
{
    /// <summary>
    /// 解析 01 表 CSV 文字成參數列（不驗證值，驗證交給 <see cref="ConfigRegistry.ValidateAndCreate"/>）。
    /// </summary>
    /// <param name="csvText">整張表的 CSV 文字。</param>
    /// <param name="source">來源名稱（只用於錯誤訊息）。</param>
    /// <exception cref="ParameterSheetException">未關閉的引號、空表、缺欄位或 Enabled 不是 TRUE／FALSE。</exception>
    public static IReadOnlyList<ParameterRow> Parse(string csvText, string source)
    {
        List<List<string>> rows;
        try
        {
            rows = ParseCsv(csvText);
        }
        catch (FormatException)
        {
            throw new ParameterSheetException($"世界參數表有未關閉的引號：{source}");
        }

        if (rows.Count == 0)
            throw new ParameterSheetException($"世界參數表是空的：{source}");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Column(string name)
        {
            var index = header.IndexOf(name);
            if (index < 0)
                throw new ParameterSheetException($"世界參數表缺少欄位「{name}」：{source}");
            return index;
        }

        var idColumn = Column("ParameterID");
        var valueColumn = Column("值");
        var typeColumn = Column("資料型別");
        var enabledColumn = Column("Enabled");

        var parameters = new List<ParameterRow>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            string? Cell(int c) => c < row.Count ? row[c] : null;
            var line = r + 1;
            var enabledText = (Cell(enabledColumn) ?? string.Empty).Trim();
            bool enabled;
            if (enabledText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (enabledText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                enabled = false;
            else
                throw new ParameterSheetException($"世界參數表第 {line} 列的 Enabled 必須是 TRUE 或 FALSE，收到「{enabledText}」：{source}");
            parameters.Add(new ParameterRow(line, Cell(idColumn), Cell(valueColumn), Cell(typeColumn)?.Trim(), enabled));
        }

        return parameters.AsReadOnly();
    }

    /// <summary>
    /// 最小 CSV 切分：支援雙引號欄位與 "" 跳脫、略過 \r 與 BOM。
    /// </summary>
    /// <exception cref="FormatException">有未關閉的引號。</exception>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else if (c != '\r' && c != '﻿')
            {
                field.Append(c);
            }
        }

        if (inQuotes)
            throw new FormatException("CSV 有未關閉的引號。");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
