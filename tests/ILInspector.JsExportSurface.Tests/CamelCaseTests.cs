using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>Verifies <see cref="CamelCase.FromPascalCase"/>.</summary>
public sealed class CamelCaseTests
{
    [Theory]
    [InlineData("Name", "name")]
    [InlineData("DisplayName", "displayName")]
    [InlineData("GetWidgetAsync", "getWidgetAsync")]
    [InlineData("A", "a")]
    [InlineData("", "")]
    public void FromPascalCase_ConvertsFirstCharacterToLowercase(string input, string expected)
    {
        Assert.Equal(expected, CamelCase.FromPascalCase(input));
    }
}
