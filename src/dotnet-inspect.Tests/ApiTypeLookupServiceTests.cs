using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class ApiTypeLookupServiceTests
{
    [Fact]
    public void LookupType_MatchesSimpleTypeName()
    {
        var api = CreateSurface();

        var result = ApiTypeLookupService.LookupType(api, "JsonSerializer");

        Assert.True(result.Found);
        Assert.Equal("System.Text.Json.JsonSerializer", result.Match);
        Assert.Same(api.Types[0], result.Type);
    }

    [Fact]
    public void LookupType_MissCarriesSuggestions()
    {
        var result = ApiTypeLookupService.LookupType(CreateSurface(), "JsonSeralizer");

        Assert.False(result.Found);
        Assert.Contains("System.Text.Json.JsonSerializer", result.Suggestions);
    }

    [Fact]
    public void ValidateMemberFilters_RequiresEveryFilterToMatch()
    {
        var type = CreateSurface().Types[0];

        var result = ApiTypeLookupService.ValidateMemberFilters(type, ["Serialize", "Deserializ"]);

        Assert.False(result.IsValid);
        Assert.Equal(["Deserializ"], result.MissedFilters);
        Assert.Contains("Deserialize", result.Suggestions);
    }

    [Fact]
    public void ValidateMemberFilters_AcceptsExactAndGlobFilters()
    {
        var type = CreateSurface().Types[0];

        var result = ApiTypeLookupService.ValidateMemberFilters(type, ["Serialize", "Deserialize*"]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void FindNonPublicMatches_IdentifiesMembersThatExistOnlyInFullSet()
    {
        var matches = ApiTypeLookupService.FindNonPublicMatches(
            ["SerializeObjectInternal", "TotallyBogus"],
            ["Serialize", "Deserialize", "SerializeObjectInternal"]);

        Assert.Equal(["SerializeObjectInternal"], matches);
    }

    [Fact]
    public void FindNonPublicMatches_HonorsGlobFilters()
    {
        var matches = ApiTypeLookupService.FindNonPublicMatches(
            ["*Internal"],
            ["Serialize", "SerializeObjectInternal"]);

        Assert.Equal(["*Internal"], matches);
    }

    [Fact]
    public void WriteError_NonPublicMatch_HintsAtAllFlag()
    {
        var result = new MemberFilterValidationResult(
            ["SerializeObjectInternal"], [], ["SerializeObjectInternal"]);

        var writer = new StringWriter();
        result.WriteError(writer);
        var output = writer.ToString();

        Assert.Contains("No members matched filter 'SerializeObjectInternal'", output);
        Assert.Contains("Member 'SerializeObjectInternal' is non-public; pass --all to include it.", output);
    }

    [Fact]
    public void WriteError_NoNonPublicMatch_OmitsHint()
    {
        var result = new MemberFilterValidationResult(["Bogus"], ["Serialize"]);

        var writer = new StringWriter();
        result.WriteError(writer);
        var output = writer.ToString();

        Assert.DoesNotContain("pass --all", output);
        Assert.Contains("Did you mean:", output);
    }

    private static ApiSurface CreateSurface()
    {
        return new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "System.Text.Json",
                    Name = "JsonSerializer",
                    Members =
                    [
                        new ApiMember { Name = "Serialize", Kind = "method" },
                        new ApiMember { Name = "Deserialize", Kind = "method" }
                    ]
                },
                new ApiType
                {
                    Namespace = "System.Text.Json",
                    Name = "JsonDocument",
                    Members = [new ApiMember { Name = "Parse", Kind = "method" }]
                }
            ]
        };
    }
}
