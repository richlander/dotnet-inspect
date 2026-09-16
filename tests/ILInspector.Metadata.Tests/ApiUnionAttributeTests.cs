using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class ApiUnionAttributeTests
{
    static readonly ApiSurface Surface = ExtractSurface();

    [Theory]
    [InlineData(typeof(NativeMetadataUnion), true, "struct")]
    [InlineData(typeof(NativeGenericMetadataUnion<>), true, "struct")]
    [InlineData(typeof(MetadataUnionFixture), true, "struct")]
    [InlineData(typeof(MetadataUnionLookalike), true, "class")]
    [InlineData(typeof(MetadataUnionCat), false, "class")]
    [InlineData(typeof(OtherUnionAttributeSample), false, "class")]
    public void Extract_PreservesUnionMarkerWithoutChangingTypeKind(
        Type fixture,
        bool expected,
        string kind)
    {
        ApiType type = GetType(fixture);

        Assert.Equal(expected, type.HasUnionAttribute);
        Assert.Equal(kind, type.Kind);
    }

    [Fact]
    public void Extract_NativeUnionPreservesStructuredConstructorParameters()
    {
        ApiType type = GetType(typeof(NativeMetadataUnion));
        ApiMember[] constructors = type.Members
            .Where(member => member.Kind == "constructor"
                && !member.IsStatic)
            .ToArray();

        Assert.Equal(2, constructors.Length);
        Assert.Equal(
            [typeof(MetadataUnionCat).FullName, typeof(MetadataUnionDog).FullName],
            constructors.Select(constructor =>
                Assert.Single(
                    Assert.Single(
                        Assert.IsType<ApiSignature>(constructor.SignatureModel)
                            .Parameters).TypeReferences).FullName));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Extract_UnionMarkerRequiresExactTopLevelNameNotAssemblyProvenance(
        bool nestedAttributeType,
        bool expected)
    {
        using var stream = new MemoryStream(
            JsonPropertyNameAttributeTests.BuildImage(
                "UnionAttribute",
                trustedAssembly: false,
                markerConstructor: true,
                attributeNamespace: "System.Runtime.CompilerServices",
                assemblyName: "UnionMarkerPolyfill",
                nestedAttributeType: nestedAttributeType),
            writable: false);
        using var peReader = new PEReader(stream);
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types);

        Assert.Equal(expected, type.HasUnionAttribute);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Json_PreservesUnionMarkerPresenceAndUnavailableEvidence(
        bool? marker)
    {
        var type = new ApiType
        {
            Name = "Sample",
            Kind = "struct",
            HasUnionAttribute = marker,
        };

        string json = JsonSerializer.Serialize(type);
        ApiType restored = Assert.IsType<ApiType>(
            JsonSerializer.Deserialize<ApiType>(json));

        Assert.Equal(marker, restored.HasUnionAttribute);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            marker.HasValue,
            document.RootElement.TryGetProperty(
                nameof(ApiType.HasUnionAttribute),
                out _));
    }

    [Fact]
    public void Json_OlderSurfaceDoesNotClaimUnionMarkerAbsence()
    {
        ApiType restored = Assert.IsType<ApiType>(
            JsonSerializer.Deserialize<ApiType>(
                """{"Name":"Sample","Kind":"struct"}"""));

        Assert.Null(restored.HasUnionAttribute);
    }

    [Fact]
    public void ExtractSummary_LeavesUnionMarkerUnavailable()
    {
        using var stream = File.OpenRead(typeof(NativeMetadataUnion).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        ApiType type = Assert.Single(
            summary.Types,
            candidate => candidate.FullName == typeof(NativeMetadataUnion).FullName);

        Assert.Null(type.HasUnionAttribute);
    }

    static ApiType GetType(Type fixture) =>
        Assert.Single(
            Surface.Types,
            type => type.MetadataToken == fixture.MetadataToken);

    static ApiSurface ExtractSurface()
    {
        using var stream = File.OpenRead(typeof(NativeMetadataUnion).Assembly.Location);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }
}
