using System.Text.Json;
using ILInspector.Metadata;

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
            Resolve(name, JsonWireNamingPolicy.SnakeCaseLower));
        Assert.Equal(
            JsonNamingPolicy.SnakeCaseUpper.ConvertName(name),
            Resolve(name, JsonWireNamingPolicy.SnakeCaseUpper));
        Assert.Equal(
            JsonNamingPolicy.KebabCaseLower.ConvertName(name),
            Resolve(name, JsonWireNamingPolicy.KebabCaseLower));
        Assert.Equal(
            JsonNamingPolicy.KebabCaseUpper.ConvertName(name),
            Resolve(name, JsonWireNamingPolicy.KebabCaseUpper));
    }

    static string Resolve(
        string name,
        JsonWireNamingPolicy namingPolicy) =>
        JsonWireContractRules.ResolvePropertyName(
            new ApiType
            {
                Name = "Contract",
                JsonPropertyNamingPolicy = namingPolicy,
            },
            new ApiMember { Name = name });
}
