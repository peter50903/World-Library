using WorldSimulation.Core.Config;
using Xunit;

namespace WorldSimulation.Core.Tests;

public class ConfigRegistryTests
{
    private static ParameterRow Row(int n, string? id, string? value, string? type, bool enabled = true)
        => new(n, id, value, type, enabled);

    private static ConfigRegistry Valid(params ParameterRow[] rows)
    {
        var result = ConfigRegistry.ValidateAndCreate(rows);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Reason)));
        return result.Registry!;
    }

    [Fact]
    public void ValidIntBoolEnum_AreReadable()
    {
        var registry = Valid(
            Row(2, "P_INT", "-12", "int"),
            Row(3, "P_ON", "1", "bool"),
            Row(4, "P_OFF", "0", "bool"),
            Row(5, "P_ENUM", "SomeMode", "enum"));

        Assert.Equal(-12, registry.GetInt("P_INT"));
        Assert.True(registry.GetBool("P_ON"));
        Assert.False(registry.GetBool("P_OFF"));
        Assert.Equal("SomeMode", registry.GetEnum("P_ENUM"));
        Assert.Equal(4, registry.Count);
    }

    [Fact]
    public void WrongTypeOrUnknownId_Throws()
    {
        var registry = Valid(Row(2, "P_INT", "3", "int"));
        Assert.Throws<InvalidOperationException>(() => registry.GetBool("P_INT"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetInt("MISSING"));
    }

    [Fact]
    public void DisabledRow_IsNotExposed_AndNotValueChecked()
    {
        var registry = Valid(Row(2, "P_OFF", "not-an-int", "int", enabled: false));
        Assert.False(registry.Contains("P_OFF"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetInt("P_OFF"));
    }

    [Fact]
    public void BlankDisabledPlaceholderRows_AreIgnored()
    {
        var registry = Valid(Row(2, null, null, null, enabled: false), Row(3, "  ", "", "", enabled: false));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void DuplicateId_IsRejected_EvenWhenOneIsDisabled()
    {
        var result = ConfigRegistry.ValidateAndCreate(new[]
        {
            Row(2, "P_A", "1", "int"),
            Row(3, "P_A", "2", "int", enabled: false),
        });

        Assert.False(result.IsValid);
        Assert.Null(result.Registry);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Equal("P_A", error.ParameterId);
    }

    [Fact]
    public void EnabledRowWithBlankId_IsRejected()
    {
        var result = ConfigRegistry.ValidateAndCreate(new[] { Row(7, " ", "1", "int") });
        Assert.False(result.IsValid);
        Assert.Equal(7, Assert.Single(result.Errors).RowNumber);
    }

    [Theory]
    [InlineData("abc", "int")]
    [InlineData("1.5", "int")]
    [InlineData("99999999999", "int")]
    [InlineData("2", "bool")]
    [InlineData("true", "bool")]
    [InlineData("", "enum")]
    [InlineData("1", "string")]
    [InlineData("1", "INT")]
    [InlineData("1", null)]
    public void InvalidValueOrUnsupportedType_IsRejected_WithNoPartialRegistry(string value, string? type)
    {
        var result = ConfigRegistry.ValidateAndCreate(new[]
        {
            Row(2, "P_GOOD", "1", "int"),
            Row(3, "P_BAD", value, type),
        });

        Assert.False(result.IsValid);
        Assert.Null(result.Registry);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Equal("P_BAD", error.ParameterId);
    }

    [Fact]
    public void LaterChangesToInput_DoNotAffectRegistry()
    {
        var rows = new List<ParameterRow> { Row(2, "P_A", "5", "int") };
        var registry = Valid(rows.ToArray());
        var viaList = ConfigRegistry.ValidateAndCreate(rows).Registry!;

        rows.Clear();
        rows.Add(Row(3, "P_B", "9", "int"));

        Assert.Equal(5, registry.GetInt("P_A"));
        Assert.Equal(5, viaList.GetInt("P_A"));
        Assert.False(viaList.Contains("P_B"));
    }

    [Fact]
    public void AllFiveDataTypes_AreReadableWithTypedGetters()
    {
        var registry = Valid(
            Row(2, "WORLD_FACTORY_CATEGORY_COUNT", "6", "number"),
            Row(3, "WORLD_FACTORY_TOTAL_CAPACITY_SHARE_MAX", "100", "number"),
            Row(4, "WORLD_INITIAL_CIV_STAGE_OPTIONS", "PRIMITIVE|DEVELOPING|MATURE", "enum_list"),
            Row(5, "WORLD_MATURE_APPROVED_DOMAINS", "ADVANCED_STEEL_METALLURGY|TELEGRAPH_COMMUNICATION", "enum_list"),
            Row(6, "WORLD_FACTORY_CATEGORY_IDS", "FC_METALLURGY|FC_MACHINERY|FC_CHEMICAL|FC_TEXTILE|FC_FOOD|FC_MILITARY", "enum_list"),
            Row(7, "P_INT", "3", "int"),
            Row(8, "P_BOOL", "1", "bool"),
            Row(9, "P_ENUM", "X", "enum"));

        Assert.Equal(6m, registry.GetNumber("WORLD_FACTORY_CATEGORY_COUNT"));
        Assert.Equal(100m, registry.GetNumber("WORLD_FACTORY_TOTAL_CAPACITY_SHARE_MAX"));
        Assert.Equal(new[] { "PRIMITIVE", "DEVELOPING", "MATURE" }, registry.GetEnumList("WORLD_INITIAL_CIV_STAGE_OPTIONS"));
        Assert.Equal(new[] { "ADVANCED_STEEL_METALLURGY", "TELEGRAPH_COMMUNICATION" }, registry.GetEnumList("WORLD_MATURE_APPROVED_DOMAINS"));
        Assert.Equal(
            new[] { "FC_METALLURGY", "FC_MACHINERY", "FC_CHEMICAL", "FC_TEXTILE", "FC_FOOD", "FC_MILITARY" },
            registry.GetEnumList("WORLD_FACTORY_CATEGORY_IDS"));
        Assert.Equal(3, registry.GetInt("P_INT"));
        Assert.True(registry.GetBool("P_BOOL"));
        Assert.Equal("X", registry.GetEnum("P_ENUM"));
    }

    [Fact]
    public void Number_ParsesDecimalsWithInvariantCulture()
    {
        var registry = Valid(Row(2, "P_A", "-0.25", "number"), Row(3, "P_B", "1234.5", "number"));
        Assert.Equal(-0.25m, registry.GetNumber("P_A"));
        Assert.Equal(1234.5m, registry.GetNumber("P_B"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("79228162514264337593543950336")]
    public void Number_InvalidOrNonFinite_IsRejected(string value)
    {
        var result = ConfigRegistry.ValidateAndCreate(new[] { Row(2, "P_N", value, "number") });
        Assert.False(result.IsValid);
        Assert.Null(result.Registry);
        Assert.Equal(2, Assert.Single(result.Errors).RowNumber);
    }

    [Theory]
    [InlineData("A||B")]
    [InlineData("|A")]
    [InlineData("A|")]
    [InlineData("A| |B")]
    public void EnumList_EmptyItem_IsRejected(string value)
    {
        var result = ConfigRegistry.ValidateAndCreate(new[] { Row(2, "P_L", value, "enum_list") });
        Assert.False(result.IsValid);
        Assert.Null(result.Registry);
        Assert.Equal("P_L", Assert.Single(result.Errors).ParameterId);
    }

    [Fact]
    public void EnumList_SingleItem_IsValid_AndListIsReadOnly()
    {
        var registry = Valid(Row(2, "P_L", "A|B", "enum_list"), Row(3, "P_ONE", "ONLY", "enum_list"));
        Assert.Equal(new[] { "ONLY" }, registry.GetEnumList("P_ONE"));

        var list = registry.GetEnumList("P_L");
        Assert.IsNotType<string[]>(list);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)list).Add("C"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)list)[0] = "Z");
        Assert.Equal(new[] { "A", "B" }, registry.GetEnumList("P_L"));
    }

    [Fact]
    public void NewTypedGetters_WrongType_Throw()
    {
        var registry = Valid(Row(2, "P_INT", "3", "int"), Row(3, "P_L", "A|B", "enum_list"), Row(4, "P_N", "1.5", "number"));
        Assert.Throws<InvalidOperationException>(() => registry.GetNumber("P_INT"));
        Assert.Throws<InvalidOperationException>(() => registry.GetEnumList("P_N"));
        Assert.Throws<InvalidOperationException>(() => registry.GetEnum("P_L"));
        Assert.Throws<InvalidOperationException>(() => registry.GetInt("P_N"));
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData(null, "int")]
    [InlineData("x", "enum")]
    public void DisabledRowWithoutId_ButWithValueOrType_IsRejectedWithRowNumber(string? value, string? type)
    {
        var result = ConfigRegistry.ValidateAndCreate(new[]
        {
            Row(2, "P_GOOD", "1", "int"),
            Row(9, null, value, type, enabled: false),
        });

        Assert.False(result.IsValid);
        Assert.Null(result.Registry);
        var error = Assert.Single(result.Errors);
        Assert.Equal(9, error.RowNumber);
        Assert.Null(error.ParameterId);
    }

    [Fact]
    public void TrulyBlankDisabledRows_AreStillIgnored()
    {
        var registry = Valid(
            Row(2, null, null, null, enabled: false),
            Row(3, "", "", "", enabled: false),
            Row(4, "  ", " ", " ", enabled: false));
        Assert.Equal(0, registry.Count);
    }

    [Theory]
    [InlineData(" P_A")]
    [InlineData("P_A ")]
    [InlineData("\tP_A")]
    public void ParameterId_WithLeadingOrTrailingWhitespace_IsRejected_NotTrimmed(string id)
    {
        foreach (var enabled in new[] { true, false })
        {
            var result = ConfigRegistry.ValidateAndCreate(new[] { Row(4, id, "1", "int", enabled) });
            Assert.False(result.IsValid);
            Assert.Null(result.Registry);
            var error = Assert.Single(result.Errors);
            Assert.Equal(4, error.RowNumber);
            Assert.Equal(id, error.ParameterId);
        }
    }

    [Fact]
    public void ParameterId_IsOrdinal_AndDisabledDuplicateOfEnabledIsStillRejected()
    {
        Valid(Row(2, "P_A", "1", "int"), Row(3, "p_a", "2", "int"));

        var result = ConfigRegistry.ValidateAndCreate(new[]
        {
            Row(2, "P_A", "x", "int", enabled: false),
            Row(3, "P_A", "1", "int"),
        });
        Assert.False(result.IsValid);
        Assert.Equal(3, Assert.Single(result.Errors).RowNumber);
    }
}
