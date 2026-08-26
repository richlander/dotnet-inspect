using System.Text.Json;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonNamingPoliciesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("SimpleName")]
    [InlineData("URLValue")]
    [InlineData("ǅValue")]
    [InlineData("😀Value")]
    public void PoliciesMatchSystemTextJson(string name)
    {
        Assert.Equal(
            JsonNamingPolicy.SnakeCaseLower.ConvertName(name),
            tsbindgen.JsonNamingPolicies.SnakeCaseLower(name));
        Assert.Equal(
            JsonNamingPolicy.SnakeCaseUpper.ConvertName(name),
            tsbindgen.JsonNamingPolicies.SnakeCaseUpper(name));
        Assert.Equal(
            JsonNamingPolicy.KebabCaseLower.ConvertName(name),
            tsbindgen.JsonNamingPolicies.KebabCaseLower(name));
        Assert.Equal(
            JsonNamingPolicy.KebabCaseUpper.ConvertName(name),
            tsbindgen.JsonNamingPolicies.KebabCaseUpper(name));
    }
}
