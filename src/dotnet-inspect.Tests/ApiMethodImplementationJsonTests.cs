using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Xunit;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class ApiMethodImplementationJsonTests
{
    static readonly Lazy<ApiType> ExtractedType = new(() =>
    {
        using var stream = File.OpenRead(typeof(ApiMember).Assembly.Location);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader).Types.Single(
            type => type.FullName == typeof(ApiMember).FullName);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeContextsPreserveImplementationEvidence(bool compact)
    {
        var context = compact ? ApiTypeCompactJsonContext.Default.ApiType : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(ExtractedType.Value, context), context)!;
        AssertFacts(restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceContextsPreserveImplementationEvidence(bool libraryContext)
    {
        var context = libraryContext ? JsonContext.Default.ApiSurface : ApiJsonContext.Default.ApiSurface;
        var surface = new ApiSurface { Types = [ExtractedType.Value] };
        ApiSurface restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(surface, context), context)!;
        AssertFacts(Assert.Single(restored.Types));
    }

    [Fact]
    public void AccessorProjectionKeepsExactMethodImplementation()
    {
        ApiType type = ExtractedType.Value;
        ApiMember property = type.Members.Single(member => member.Name == nameof(ApiMember.MethodImplementation));
        ApiMember[] accessors = ApiOutputFormatter.AccessorMethods(property, type).ToArray();
        Assert.Equal(2, accessors.Length);
        Assert.All(accessors, accessor =>
        {
            var expected = property.AccessorImplementations!.Value.Single(
                facts => facts.MethodToken == accessor.MetadataToken);
            Assert.Same(expected, accessor.MethodImplementation);
            Assert.Equal(expected.HasBodyRva, accessor.HasMethodBody);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MemberCommandPreservesMethodAndAccessorFacts(bool selectSections, bool compact)
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                AssemblyPath = typeof(ApiMember).Assembly.Location,
                TypeName = typeof(ApiMember).FullName!,
                KindFilter = selectSections ? [] : ["constructor", "property"],
                IncludeSections = selectSections ? [SectionNames.Constructors, SectionNames.Properties] : null,
                JsonOutput = true,
                CompactJson = compact,
                TipLevel = TipLevel.Quiet,
            }));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var context = compact ? ApiTypeCompactJsonContext.Default.ApiType : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(result.Output, context)!;
        Assert.Contains(restored.Members, member => member.Kind == "constructor");
        Assert.Contains(restored.Members, member => member.Kind == "property");
        AssertFacts(restored);
    }

    static void AssertFacts(ApiType restored)
    {
        Assert.NotEmpty(restored.Members);
        foreach (ApiMember member in restored.Members)
        {
            ApiMember original = ExtractedType.Value.Members.Single(candidate =>
                candidate.Name == member.Name && candidate.Signature == member.Signature);
            Assert.Equal(original.MethodImplementation, member.MethodImplementation);
            Assert.Equal(original.AccessorImplementations?.ToArray(), member.AccessorImplementations?.ToArray());
        }
        Assert.NotNull(restored.Members.Single(member => member.Kind == "constructor").MethodImplementation);
        Assert.NotNull(restored.Members.Single(
            member => member.Name == nameof(ApiMember.MethodImplementation)).AccessorImplementations);
    }
}
