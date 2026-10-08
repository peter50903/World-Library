using System.Globalization;

namespace WorldSimulation.Core.Config;

/// <summary>Outcome of <see cref="ConfigRegistry.ValidateAndCreate"/>: a registry only when there are no errors.</summary>
public sealed class ConfigValidationResult
{
    internal ConfigValidationResult(ConfigRegistry? registry, IReadOnlyList<ConfigError> errors)
    {
        Registry = registry;
        Errors = errors;
    }

    public bool IsValid => Registry is not null;
    public ConfigRegistry? Registry { get; }
    public IReadOnlyList<ConfigError> Errors { get; }
}

/// <summary>
/// Immutable, read-only view of validated 01_世界參數 rows.
/// PARTIAL component: covers only that table's row-level checks. It is NOT the full
/// Config Preflight and provides no ConfigHash (needs all Enabled rules, definitions and parameters).
/// </summary>
public sealed class ConfigRegistry
{
    public const string Scope = "01_世界參數 row validation only (partial; no ConfigHash)";

    private readonly Dictionary<string, Parameter> _parameters;

    private readonly record struct Parameter(ConfigDataType Type, string Raw, int IntValue, bool BoolValue);

    private ConfigRegistry(Dictionary<string, Parameter> parameters)
    {
        _parameters = parameters;
    }

    /// <summary>Number of enabled parameters exposed for lookup.</summary>
    public int Count => _parameters.Count;

    /// <summary>
    /// Validates all rows and, only if every check passes, creates the registry from a snapshot of them.
    /// Rules: a non-blank ParameterID must be unique regardless of Enabled; disabled rows are never exposed
    /// and not value-checked; disabled rows without an ID are ignored as placeholders; enabled rows need an
    /// ID, a non-blank Value and a supported DataType (int / bool "0"|"1" / enum non-blank).
    /// </summary>
    public static ConfigValidationResult ValidateAndCreate(IEnumerable<ParameterRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var snapshot = rows.ToList();
        var errors = new List<ConfigError>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new Dictionary<string, Parameter>(StringComparer.Ordinal);

        foreach (var row in snapshot)
        {
            if (row is null)
            {
                errors.Add(new ConfigError(0, null, "Row is null."));
                continue;
            }

            var hasId = !string.IsNullOrWhiteSpace(row.ParameterId);
            if (!hasId)
            {
                if (row.Enabled)
                {
                    errors.Add(new ConfigError(row.RowNumber, null, "Enabled row has a blank ParameterID."));
                }
                continue;
            }

            var id = row.ParameterId!;
            if (!seenIds.Add(id))
            {
                errors.Add(new ConfigError(row.RowNumber, id, "Duplicate ParameterID."));
                continue;
            }

            if (!row.Enabled)
            {
                continue;
            }

            if (TryParse(row, out var parameter, out var reason))
            {
                parameters.Add(id, parameter);
            }
            else
            {
                errors.Add(new ConfigError(row.RowNumber, id, reason));
            }
        }

        return errors.Count == 0
            ? new ConfigValidationResult(new ConfigRegistry(parameters), Array.Empty<ConfigError>())
            : new ConfigValidationResult(null, errors);
    }

    public bool Contains(string parameterId) => parameterId is not null && _parameters.ContainsKey(parameterId);

    /// <exception cref="KeyNotFoundException">Unknown or disabled ParameterID.</exception>
    /// <exception cref="InvalidOperationException">The parameter is not of type int.</exception>
    public int GetInt(string parameterId) => Get(parameterId, ConfigDataType.Int).IntValue;

    /// <exception cref="KeyNotFoundException">Unknown or disabled ParameterID.</exception>
    /// <exception cref="InvalidOperationException">The parameter is not of type bool.</exception>
    public bool GetBool(string parameterId) => Get(parameterId, ConfigDataType.Bool).BoolValue;

    /// <exception cref="KeyNotFoundException">Unknown or disabled ParameterID.</exception>
    /// <exception cref="InvalidOperationException">The parameter is not of type enum.</exception>
    public string GetEnum(string parameterId) => Get(parameterId, ConfigDataType.Enum).Raw;

    private Parameter Get(string parameterId, ConfigDataType expected)
    {
        if (parameterId is null || !_parameters.TryGetValue(parameterId, out var parameter))
        {
            throw new KeyNotFoundException($"Parameter '{parameterId}' is not defined or not enabled.");
        }

        if (parameter.Type != expected)
        {
            throw new InvalidOperationException($"Parameter '{parameterId}' is {parameter.Type}, not {expected}.");
        }

        return parameter;
    }

    private static bool TryParse(ParameterRow row, out Parameter parameter, out string reason)
    {
        parameter = default;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(row.Value))
        {
            reason = "Enabled row has a blank Value.";
            return false;
        }

        var value = row.Value;
        switch (row.DataType)
        {
            case "int":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    reason = $"Value '{value}' is not a valid int.";
                    return false;
                }
                parameter = new Parameter(ConfigDataType.Int, value, i, false);
                return true;

            case "bool":
                if (value != "0" && value != "1")
                {
                    reason = $"Value '{value}' is not a valid bool (expected 0 or 1).";
                    return false;
                }
                parameter = new Parameter(ConfigDataType.Bool, value, 0, value == "1");
                return true;

            case "enum":
                parameter = new Parameter(ConfigDataType.Enum, value, 0, false);
                return true;

            default:
                reason = $"Unsupported DataType '{row.DataType}'.";
                return false;
        }
    }
}
