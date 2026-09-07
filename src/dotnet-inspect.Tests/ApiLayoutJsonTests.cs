using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class ApiLayoutJsonTests
{
    const string FixtureNamespace = "ILInspector.Metadata.MemorySafetyFixtures";
    const string ExplicitTypeName = "LayoutFactsExplicitFixture";
    const string DefaultTypeName = "LayoutFactsDefaultFixture";

    static string FixturePath => FixtureCatalog.MetadataMemorySafety.AssemblyPath();

    static readonly Lazy<ApiSurface> Surface = new(() =>
    {
        using var stream = File.OpenRead(FixturePath);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader, includeAll: true);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeContextsPreserveLayoutFacts(bool compact)
    {
        ApiType original = Type(ExplicitTypeName);
        var context = compact
            ? ApiTypeCompactJsonContext.Default.ApiType
            : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, context), context)!;

        AssertFacts(original, restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceContextsPreserveLayoutFacts(bool libraryContext)
    {
        ApiType[] types =
        [
            Type(ExplicitTypeName),
            Type("LayoutFactsSequentialFixture"),
            Type(DefaultTypeName),
            Type($"{DefaultTypeName}.Nested"),
        ];
        var original = new ApiSurface { Types = [.. types] };
        var context = libraryContext
            ? JsonContext.Default.ApiSurface
            : ApiJsonContext.Default.ApiSurface;
        ApiSurface restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, context), context)!;

        Assert.Equal(types.Length, restored.Types.Count);
        foreach (ApiType type in types)
        {
            AssertFacts(
                type,
                restored.Types.Single(candidate => candidate.Name == type.Name));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOlderJsonLeavesLayoutFactsUnavailable(bool compact)
    {
        const string json =
            """{"name":"Old","kind":"class","members":[{"name":"Value","kind":"field"}]}""";
        var context = compact
            ? ApiTypeCompactJsonContext.Default.ApiType
            : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(json, context)!;

        Assert.Null(restored.LayoutDetails);
        Assert.Null(Assert.Single(restored.Members).FieldLayout);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CommandsPreserveLayoutFactsThroughJsonProjection(
        bool selectFieldsSection, bool compact)
    {
        string fullTypeName = $"{FixtureNamespace}.{ExplicitTypeName}";
        var result = await ConsoleCapture.RunAsync(() =>
            selectFieldsSection
                ? TypeCommand.ExecuteAsync(new TypeOptions
                {
                    AssemblyPath = FixturePath,
                    TypeName = fullTypeName,
                    IncludeSections = [SectionNames.Fields],
                    JsonOutput = true,
                    CompactJson = compact,
                    TipLevel = TipLevel.Quiet,
                })
                : MemberCommand.ExecuteAsync(new MemberOptions
                {
                    AssemblyPath = FixturePath,
                    TypeName = fullTypeName,
                    KindFilter = ["field"],
                    JsonOutput = true,
                    CompactJson = compact,
                    TipLevel = TipLevel.Quiet,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var context = compact
            ? ApiTypeCompactJsonContext.Default.ApiType
            : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(result.Output, context)!;

        ApiType original = Type(ExplicitTypeName);
        Assert.Equal(original.LayoutDetails, restored.LayoutDetails);
        Assert.Equal(["Nonzero", "Static", "Zero"],
            restored.Members.Select(member => member.Name).Order().ToArray());
        foreach (ApiMember field in restored.Members)
        {
            Assert.Equal(
                original.Members.Single(member => member.Name == field.Name).FieldLayout,
                field.FieldLayout);
        }
        Assert.Equal(0, restored.Members.Single(member => member.Name == "Zero").FieldLayout!.Offset);
        Assert.Equal(12, restored.Members.Single(member => member.Name == "Nonzero").FieldLayout!.Offset);
        Assert.Null(restored.Members.Single(member => member.Name == "Static").FieldLayout!.Offset);
    }

    static ApiType Type(string name)
        => Surface.Value.Types.Single(type => type.Name == name);

    static void AssertFacts(ApiType original, ApiType restored)
    {
        Assert.Equal(original.Layout, restored.Layout);
        Assert.Equal(
            Assert.IsType<ApiTypeLayoutFacts>(original.LayoutDetails),
            Assert.IsType<ApiTypeLayoutFacts>(restored.LayoutDetails));
        foreach (ApiMember originalMember in original.Members)
        {
            ApiMember restoredMember = restored.Members.Single(member =>
                member.Name == originalMember.Name
                && member.Kind == originalMember.Kind);
            Assert.Equal(originalMember.FieldLayout, restoredMember.FieldLayout);
        }
    }
}
