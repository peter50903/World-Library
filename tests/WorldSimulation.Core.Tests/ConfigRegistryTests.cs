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
}
