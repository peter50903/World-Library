using System.Globalization;

namespace WorldSimulation.Core.Config;

/// <summary>
/// 第 02 章 b 節：29_數值參數的一列（欄 ParameterID／預設值／最小值／最大值／資料型別／說明／是否已平衡／Enabled）。
/// 生效值＝預設值；必須介於最小值～最大值。
/// </summary>
public sealed record TunableParameterRow(
    int RowNumber,
    string? ParameterId,
    string? DefaultValue,
    string? Min,
    string? Max,
    string? DataType,
    string? Description,
    string? Balanced,
    bool Enabled);

/// <summary>29 表 CSV 文字 → <see cref="TunableParameterRow"/>（依欄名取欄，沿用 <see cref="ParameterSheet.ParseCsv"/>）。</summary>
public static class TunableParameterSheet
{
    /// <exception cref="ParameterSheetException">未關閉的引號、空表、缺欄位或 Enabled 不是 TRUE／FALSE。</exception>
    public static IReadOnlyList<TunableParameterRow> Parse(string csvText, string source)
    {
        List<List<string>> rows;
        try
        {
            rows = ParameterSheet.ParseCsv(csvText);
        }
        catch (FormatException)
        {
            throw new ParameterSheetException($"數值參數表有未關閉的引號：{source}");
        }

        if (rows.Count == 0)
            throw new ParameterSheetException($"數值參數表是空的：{source}");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Column(string name)
        {
            var index = header.IndexOf(name);
            if (index < 0)
                throw new ParameterSheetException($"數值參數表缺少欄位「{name}」：{source}");
            return index;
        }

        var id = Column("ParameterID");
        var value = Column("預設值");
        var min = Column("最小值");
        var max = Column("最大值");
        var type = Column("資料型別");
        var description = Column("說明");
        var balanced = Column("是否已平衡");
        var enabled = Column("Enabled");

        var result = new List<TunableParameterRow>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            string? Cell(int c) => c < row.Count ? row[c] : null;
            var line = r + 1;
            var enabledText = (Cell(enabled) ?? string.Empty).Trim();
            bool isEnabled;
            if (enabledText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                isEnabled = true;
            else if (enabledText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                isEnabled = false;
            else
                throw new ParameterSheetException($"數值參數表第 {line} 列的 Enabled 必須是 TRUE 或 FALSE，收到「{enabledText}」：{source}");
            result.Add(new TunableParameterRow(line, Cell(id), Cell(value), Cell(min), Cell(max),
                Cell(type)?.Trim(), Cell(description), Cell(balanced)?.Trim(), isEnabled));
        }

        return result.AsReadOnly();
    }
}

/// <summary><see cref="TunableParameterRegistry.ValidateAndCreate"/> 的結果：沒有錯誤才有登錄表。</summary>
public sealed class TunableValidationResult
{
    internal TunableValidationResult(TunableParameterRegistry? registry, IReadOnlyList<ConfigError> errors)
    {
        Registry = registry;
        Errors = errors;
    }

    public bool IsValid => Registry is not null;
    public TunableParameterRegistry? Registry { get; }
    public IReadOnlyList<ConfigError> Errors { get; }
}

/// <summary>
/// 第 02 章 b 節：已驗證的 29_數值參數（不可變）。「是否已平衡＝FALSE」或說明開頭【TEMP】都是暫定值：
/// 照用，但 <see cref="IsProvisional"/> 為 true，畫面要標示暫定。
/// </summary>
public sealed class TunableParameterRegistry
{
    private const string TempMark = "【TEMP】";

    private readonly Dictionary<string, Entry> _entries;

    private readonly record struct Entry(string Type, decimal Value, bool Unbalanced, bool TempMarked);

    private TunableParameterRegistry(Dictionary<string, Entry> entries)
    {
        _entries = entries;
    }

    /// <summary>啟用中的參數數量。</summary>
    public int Count => _entries.Count;

    /// <summary>暫定（未平衡或【TEMP】）的參數數量。</summary>
    public int ProvisionalCount => _entries.Values.Count(e => e.Unbalanced || e.TempMarked);

    /// <summary>「是否已平衡＝FALSE」的參數數量。</summary>
    public int UnbalancedCount => _entries.Values.Count(e => e.Unbalanced);

    /// <summary>說明開頭標【TEMP】的參數數量。</summary>
    public int TempMarkedCount => _entries.Values.Count(e => e.TempMarked);

    public IEnumerable<string> ParameterIds => _entries.Keys;

    public bool Contains(string parameterId) => parameterId is not null && _entries.ContainsKey(parameterId);

    /// <exception cref="KeyNotFoundException">不存在或未啟用的 ParameterID。</exception>
    public bool IsProvisional(string parameterId)
    {
        var entry = Get(parameterId, null);
        return entry.Unbalanced || entry.TempMarked;
    }

    public int GetInt(string parameterId) => (int)Get(parameterId, "int").Value;

    public decimal GetNumber(string parameterId) => Get(parameterId, "number").Value;

    public bool GetBool(string parameterId) => Get(parameterId, "bool").Value == 1m;

    private Entry Get(string parameterId, string? type)
    {
        if (parameterId is null || !_entries.TryGetValue(parameterId, out var entry))
            throw new KeyNotFoundException($"數值參數「{parameterId}」不存在或未啟用。");
        if (type is not null && entry.Type != type)
            throw new InvalidOperationException($"數值參數「{parameterId}」的型別是 {entry.Type}，不是 {type}。");
        return entry;
    }

    /// <summary>
    /// 驗證全部列，全部通過才建立登錄表。規則：ID 不得前後空白、不得重複（不論是否啟用）；
    /// 未啟用且沒有 ID 的列是預留空白列，略過；啟用列需有 ID、型別為 int／number／bool，
    /// 預設、最小、最大皆可解析（bool 只收 0／1），且最小 ≤ 預設 ≤ 最大；「是否已平衡」為 TRUE／FALSE。
    /// </summary>
    public static TunableValidationResult ValidateAndCreate(IEnumerable<TunableParameterRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var errors = new List<ConfigError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (var row in rows.ToList())
        {
            if (string.IsNullOrWhiteSpace(row.ParameterId))
            {
                if (row.Enabled)
                    errors.Add(new ConfigError(row.RowNumber, null, "啟用列缺少 ParameterID"));
                continue;
            }

            var id = row.ParameterId;
            if (id.Length != id.Trim().Length)
            {
                errors.Add(new ConfigError(row.RowNumber, id, "ParameterID 前後有空白"));
                continue;
            }
            if (!seen.Add(id))
            {
                errors.Add(new ConfigError(row.RowNumber, id, "ParameterID 重複"));
                continue;
            }
            if (!row.Enabled)
                continue;

            if (Validate(row, out var entry, out var reason))
                entries.Add(id, entry);
            else
                errors.Add(new ConfigError(row.RowNumber, id, reason));
        }

        return errors.Count == 0
            ? new TunableValidationResult(new TunableParameterRegistry(entries), Array.Empty<ConfigError>())
            : new TunableValidationResult(null, errors);
    }

    private static bool Validate(TunableParameterRow row, out Entry entry, out string reason)
    {
        entry = default;
        reason = string.Empty;
        var type = row.DataType ?? string.Empty;
        if (type is not ("int" or "number" or "bool"))
        {
            reason = $"不支援的資料型別「{type}」（只接受 int、number、bool）";
            return false;
        }

        if (!TryValue(row.DefaultValue, type, out var value, out reason, "預設值")
            || !TryValue(row.Min, type, out var min, out reason, "最小值")
            || !TryValue(row.Max, type, out var max, out reason, "最大值"))
            return false;
        if (min > max)
        {
            reason = $"最小值 {row.Min} 大於最大值 {row.Max}";
            return false;
        }
        if (value < min || value > max)
        {
            reason = $"預設值 {row.DefaultValue} 超出範圍 {row.Min}～{row.Max}";
            return false;
        }

        var balancedText = row.Balanced ?? string.Empty;
        bool unbalanced;
        if (balancedText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            unbalanced = false;
        else if (balancedText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            unbalanced = true;
        else
        {
            reason = $"「是否已平衡」必須是 TRUE 或 FALSE，收到「{balancedText}」";
            return false;
        }

        var tempMarked = (row.Description ?? string.Empty).TrimStart().StartsWith(TempMark, StringComparison.Ordinal);
        entry = new Entry(type, value, unbalanced, tempMarked);
        return true;
    }

    private static bool TryValue(string? text, string type, out decimal value, out string reason, string column)
    {
        value = 0m;
        reason = string.Empty;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            reason = $"缺少{column}";
            return false;
        }

        switch (type)
        {
            case "bool":
                if (raw is "0" or "1")
                {
                    value = raw == "1" ? 1m : 0m;
                    return true;
                }
                reason = $"{column}「{raw}」不是 bool（只收 0 或 1）";
                return false;
            case "int":
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    value = i;
                    return true;
                }
                reason = $"{column}「{raw}」不是整數";
                return false;
            default:
                if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var n))
                {
                    value = n;
                    return true;
                }
                reason = $"{column}「{raw}」不是數字";
                return false;
        }
    }
}
