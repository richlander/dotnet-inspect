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
public sealed class ApiMemorySafetyJsonTests
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
    public void TypeContextsPreserveExtractedMemorySafetyFacts(bool compact)
    {
        ApiType type = ExtractedType.Value;
        var context = compact
            ? ApiTypeCompactJsonContext.Default.ApiType
            : ApiTypeJsonContext.Default.ApiType;
        ApiType restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(type, context), context)!;

        AssertFacts(type, restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceContextPreservesExtractedMemorySafetyFacts(bool libraryContext)
    {
        ApiType type = ExtractedType.Value;
        var surface = new ApiSurface { Types = [type] };
        var context = libraryContext
            ? JsonContext.Default.ApiSurface
            : ApiJsonContext.Default.ApiSurface;
        ApiSurface restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(surface, context), context)!;

        AssertFacts(type, Assert.Single(restored.Types));
    }

    [Fact]
    public void AccessorProjectionRetainsItsOwnContractToken()
    {
        ApiType type = ExtractedType.Value;
        ApiMember property = type.Members.Single(
            member => member.Name == nameof(ApiMember.MemorySafety));
        ApiMember[] accessors = ApiOutputFormatter.AccessorMethods(property, type).ToArray();
        Assert.Equal(2, accessors.Length);
        Assert.All(accessors, accessor =>
        {
            Assert.Equal(accessor.MetadataToken, accessor.MemorySafety!.CallerContract.Evidence.MemberToken);
            Assert.Same(property.AccessorMemorySafety!.Value.Single(
                facts => facts.CallerContract.Evidence.MemberToken == accessor.MetadataToken),
                accessor.MemorySafety);
            Assert.NotSame(property.MemorySafety, accessor.MemorySafety);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MemberCommandPreservesFactsThroughJsonProjection(
        bool selectSections, bool compact)
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                AssemblyPath = typeof(ApiMember).Assembly.Location,
                TypeName = typeof(ApiMember).FullName!,
                KindFilter = selectSections ? [] : ["property"],
                IncludeSections = selectSections ? [SectionNames.Properties] : null,
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
        Assert.NotEmpty(restored.Members);
        Assert.All(restored.Members, member => Assert.Equal("property", member.Kind));
        AssertFacts(ExtractedType.Value, restored);
    }

    static void AssertFacts(ApiType original, ApiType restored)
    {
        Assert.Equal(original.Layout, restored.Layout);
        Assert.Equal(original.MemorySafety!.ModuleVersionId, restored.MemorySafety!.ModuleVersionId);
        Assert.IsType<MemorySafetyRulesResult.Available>(restored.MemorySafety.Rules);
        ApiMember member = restored.Members.Single(
            member => member.Name == nameof(ApiMember.MemorySafety));
        Assert.IsType<MemorySafetyMemberContractResult.None>(member.MemorySafety!.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Absent, member.MemorySafety.SignaturePointer);
        Assert.Equal(ApiBackingStorageState.Associated, member.BackingStorage!.State);
        Assert.Equal(2, member.AccessorMemorySafety!.Value.Length);
    }
}
