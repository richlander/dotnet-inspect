using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Output;
using ILInspector.Metadata;
using Xunit;

namespace DotnetInspector.Tests;

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

    [Fact]
    public void SurfaceContextPreservesExtractedMemorySafetyFacts()
    {
        ApiType type = ExtractedType.Value;
        var surface = new ApiSurface { Types = [type] };
        ApiSurface restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(surface, ApiJsonContext.Default.ApiSurface),
            ApiJsonContext.Default.ApiSurface)!;

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
