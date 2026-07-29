using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

// Captures Console.Error, which is process-wide state.
[Collection("Console")]
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
        Assert.Null(result.ImpliedMember);
    }

    [Fact]
    public void LookupType_NamespaceQualifiedType_ResolvesWithoutPeelingMember()
    {
        // Regression for issue #1690: "System.Text.Json.JsonSerializer" is a type, not a
        // Type.Member pair, so it must resolve directly with no implied member.
        var api = CreateSurface();

        var result = ApiTypeLookupService.LookupType(api, "System.Text.Json.JsonSerializer");

        Assert.True(result.Found);
        Assert.Equal("System.Text.Json.JsonSerializer", result.Match);
        Assert.Null(result.ImpliedMember);
    }

    [Fact]
    public void LookupType_FullyQualifiedTypeMember_PeelsTrailingMember()
    {
        // Regression for issue #1690: "System.Text.Json.JsonSerializer.Serialize" must resolve
        // to the JsonSerializer type with "Serialize" surfaced as an implied member filter,
        // rather than failing because "System" was mistaken for the type.
        var api = CreateSurface();

        var result = ApiTypeLookupService.LookupType(api, "System.Text.Json.JsonSerializer.Serialize");

        Assert.True(result.Found);
        Assert.Equal("System.Text.Json.JsonSerializer", result.Match);
        Assert.Equal("Serialize", result.ImpliedMember);
    }

    [Fact]
    public void LookupType_FullyQualifiedGenericMember_PeelsTrailingMember()
    {
        var api = CreateSurface();

        var result = ApiTypeLookupService.LookupType(api, "System.Text.Json.JsonSerializer.Serialize<TValue>");

        Assert.True(result.Found);
        Assert.Equal("System.Text.Json.JsonSerializer", result.Match);
        Assert.Equal("Serialize<TValue>", result.ImpliedMember);
    }

    [Fact]
    public void LookupType_SimpleTypeMember_PeelsTrailingMember()
    {
        var api = CreateSurface();

        var result = ApiTypeLookupService.LookupType(api, "JsonSerializer.Serialize");

        Assert.True(result.Found);
        Assert.Equal("System.Text.Json.JsonSerializer", result.Match);
        Assert.Equal("Serialize", result.ImpliedMember);
    }

    [Fact]
    public void LookupType_UnresolvableDottedName_DoesNotPeel()
    {
        var result = ApiTypeLookupService.LookupType(CreateSurface(), "System.Nonexistent");

        Assert.False(result.Found);
        Assert.Null(result.ImpliedMember);
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

        var output = CaptureError(result.WriteError);

        Assert.Contains("No members matched filter 'SerializeObjectInternal'", output);
        Assert.Contains("Member 'SerializeObjectInternal' is non-public; pass --all to include it.", output);
    }

    [Fact]
    public void WriteError_NoNonPublicMatch_OmitsHint()
    {
        var result = new MemberFilterValidationResult(["Bogus"], ["Serialize"]);

        var output = CaptureError(result.WriteError);

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

    /// <summary>
    /// Captures stderr. These diagnostics now go to <c>CommandError</c>, which
    /// owns the severity prefix and the containment, so the test can no longer
    /// hand in a writer of its own.
    /// </summary>
    private static string CaptureError(Action action)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return captured.ToString();
    }
}
