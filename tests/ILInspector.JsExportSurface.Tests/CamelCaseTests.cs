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
    [InlineData("URLValue", "urlValue")]
    public void FromPascalCase_MatchesJsonNamingPolicyCamelCase(string input, string expected)
    {
        Assert.Equal(expected, CamelCase.FromPascalCase(input));
        Assert.Equal(
            System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(input),
            CamelCase.FromPascalCase(input));
    }
}
