namespace WorldSimulation.Core.Config;

/// <summary>
/// One in-memory input row of the 01_世界參數 sheet (columns ParameterID, 值, 資料型別, Enabled).
/// Supplied by an upper-layer adapter; this type never reads files or Google APIs.
/// </summary>
/// <param name="RowNumber">Source row number, for diagnostics only.</param>
public sealed record ParameterRow(int RowNumber, string? ParameterId, string? Value, string? DataType, bool Enabled);

/// <summary>Supported DataType values of the 01_世界參數 rows handled so far.</summary>
public enum ConfigDataType
{
    Int,
    Bool,
    Enum,
}

/// <summary>A validation failure naming the row, ParameterID (if any) and reason.</summary>
public sealed record ConfigError(int RowNumber, string? ParameterId, string Reason);
