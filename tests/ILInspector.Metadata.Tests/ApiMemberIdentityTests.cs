using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public class ApiMemberIdentityTests
{
    [Theory]
    [InlineData(".ctor", false, ".ctor")]
    [InlineData("op_Addition", false, "operator:op_Addition")]
    [InlineData("IFoo.Bar", false, "explicit:IFoo.Bar")]
    [InlineData("Twice", true, "extension:Twice")]
    [InlineData("M", false, "M")]
    public void GetMemberSelectorName_PreservesMemberIndexPrefixes(string metadataName, bool isExtension, string expected)
    {
        Assert.Equal(expected, ApiMemberIdentity.GetMemberSelectorName(metadataName, isExtension));
    }

    [Fact]
    public void CreateMethodAnchor_UsesMetadataCanonicalSignature()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);

        var anchor = ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method);

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiMemberIdentityTests.ApiMemberIdentityFixture<T>.M<U>(System.Int32,U)",
            anchor.CanonicalSignature);
        Assert.Equal("ILInspector.Metadata.Tests.ApiMemberIdentityTests.ApiMemberIdentityFixture<T>", anchor.TypeFullName);
        Assert.Equal("M<U>", anchor.MemberName);
        Assert.StartsWith("M~", anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal(MemberAnchor.ComputeFingerprint(anchor.CanonicalSignature), anchor.Fingerprint);
    }

    [Fact]
    public void CreateMethodAnchorInfo_IncludesCanonicalReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);

        var identity = ApiMemberIdentity.CreateMethodAnchorInfo(reader, typeHandle, method);

        Assert.Equal("System.Void", identity.ReturnType);
        Assert.Equal(
            ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method),
            identity.Anchor);
    }

    [Fact]
    public void GetMemberAnchor_DisambiguatesConversionOperatorsByReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var type = surface.Types.Single(t => t.Name.EndsWith(nameof(ConversionOperatorFixture), StringComparison.Ordinal));
        var conversions = type.Members
            .Where(member => member.Kind == "operator" && member.Name == "op_Explicit")
            .ToList();

        // Two explicit conversions that differ ONLY by return type (op_Explicit(Fixture)).
        Assert.Equal(2, conversions.Count);

        var anchors = conversions
            .Select(member => ApiMemberIdentity.GetMemberAnchor(type, member))
            .ToList();

        // Return type must be part of the canonical identity, so the two conversions
        // get distinct canonical signatures and fingerprints (no ambiguous anchor).
        Assert.Equal(2, anchors.Select(anchor => anchor.CanonicalSignature).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, anchors.Select(anchor => anchor.Fingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.All(anchors, anchor => Assert.Contains("op_Explicit", anchor.CanonicalSignature, StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.EndsWith("~int", StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.EndsWith("~long", StringComparison.Ordinal));
    }

    [Fact]
    public void GetMemberAnchor_DisambiguatesOverloadedIndexersByParameterType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var type = surface.Types.Single(t => t.Name.EndsWith(nameof(IndexerFixture), StringComparison.Ordinal));
        var indexers = type.Members
            .Where(member => member.Kind == "property" && member.SignatureModel is { Parameters.Count: > 0 })
            .ToList();

        // Two indexer overloads that differ only by parameter type: this[int] and this[string].
        Assert.Equal(2, indexers.Count);

        var anchors = indexers
            .Select(member => ApiMemberIdentity.GetMemberAnchor(type, member))
            .ToList();

        // Parameter types must be part of the canonical identity, so the two indexers get
        // distinct canonical signatures and fingerprints instead of colliding on "P:Type.Item"
        // and being paired by declaration order.
        Assert.Equal(2, anchors.Select(anchor => anchor.CanonicalSignature).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, anchors.Select(anchor => anchor.Fingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.Contains("(int)", StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.Contains("(string)", StringComparison.Ordinal));
    }

    [Fact]
    public void GetCanonicalSignature_OrdinaryPropertyFormatIsUnchangedByIndexerFix()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var property = new ApiMember
        {
            Name = "Name",
            Kind = "property",
            Signature = "string Name { get; set; }",
        };

        Assert.Equal("P:N.C.Name", ApiMemberIdentity.GetCanonicalSignature(type, property));
        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, property, out var tryResult));
        Assert.Equal("P:N.C.Name", tryResult);
    }

    [Fact]
    public void GetMemberAnchor_DisambiguatesDegradedPlaceholderFromGenuineSignature()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var genuine = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "object M()",
        };
        var degraded = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "object M()",
            SignatureDecodeStatus = SignatureDecodeStatus.Degraded,
        };

        var genuineAnchor = ApiMemberIdentity.GetMemberAnchor(type, genuine);
        var degradedAnchor = ApiMemberIdentity.GetMemberAnchor(type, degraded);

        Assert.Equal(genuineAnchor.CanonicalSignature, degradedAnchor.CanonicalSignature);
        Assert.NotEqual(genuineAnchor.Fingerprint, degradedAnchor.Fingerprint);
        Assert.NotEqual(genuineAnchor.StableSelector, degradedAnchor.StableSelector);
        Assert.Equal(
            MemberAnchor.ComputeFingerprint(genuineAnchor.CanonicalSignature),
            genuineAnchor.Fingerprint);
        Assert.Equal(
            MemberAnchor.ComputeFingerprint(degradedAnchor.CanonicalSignature, isDegraded: true),
            degradedAnchor.Fingerprint);
    }

    [Fact]
    public void CreateMethodAnchor_DisambiguatesConversionOperatorsByReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var conversions = FindConversionOperatorMethods(reader);
        Assert.Equal(2, conversions.Count);

        var canonicals = conversions
            .Select(entry => ApiMemberIdentity.CreateMethodAnchor(reader, entry.TypeHandle, entry.Method).CanonicalSignature)
            .ToList();

        // The SRM-direct producer must also disambiguate the two op_Explicit conversions
        // that differ only by return type (its own full-name vocabulary: ~System.Int32/~System.Int64).
        Assert.Equal(2, canonicals.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~System.Int32", StringComparison.Ordinal));
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~System.Int64", StringComparison.Ordinal));
    }

    [Fact]
    public void FallbackCanonicalSignature_DisambiguatesConversionOperators_AfterJsonRoundTrip()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Round-trip through JSON. SignatureModel is [JsonIgnore], so the deserialized
        // members have no SignatureModel and exercise the GetCanonicalSignature fallback.
        var json = JsonSerializer.Serialize(surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;

        var type = roundTripped.Types.Single(t => t.Name.EndsWith(nameof(ConversionOperatorFixture), StringComparison.Ordinal));
        var conversions = type.Members
            .Where(member => member.Kind == "operator" && member.Name == "op_Explicit")
            .ToList();
        Assert.Equal(2, conversions.Count);
        Assert.All(conversions, member => Assert.Null(member.SignatureModel));

        // The fallback must still disambiguate by return type on the round-tripped surface,
        // using the persisted ApiMember.ReturnType.
        var canonicals = conversions
            .Select(member => ApiMemberIdentity.GetCanonicalSignature(type, member))
            .ToList();
        Assert.Equal(2, canonicals.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~int", StringComparison.Ordinal));
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~long", StringComparison.Ordinal));
    }

    static (TypeDefinitionHandle TypeHandle, MethodDefinition Method) FindFixtureMethod(MetadataReader reader)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != "ApiMemberIdentityFixture`1")
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "M")
                    return (typeHandle, method);
            }
        }

        throw new InvalidOperationException("Fixture method not found.");
    }

    static List<(TypeDefinitionHandle TypeHandle, MethodDefinition Method)> FindConversionOperatorMethods(MetadataReader reader)
    {
        var result = new List<(TypeDefinitionHandle, MethodDefinition)>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != nameof(ConversionOperatorFixture))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "op_Explicit")
                    result.Add((typeHandle, method));
            }
        }

        return result;
    }

    sealed class ApiMemberIdentityFixture<T>
    {
        public void M<U>(int value, U item)
        {
        }
    }

    readonly struct ConversionOperatorFixture
    {
        public static explicit operator int(ConversionOperatorFixture value) => 0;

        public static explicit operator long(ConversionOperatorFixture value) => 0;
    }

    sealed class IndexerFixture
    {
        public int this[int index] => index;

        public int this[string key] => key.Length;
    }
}
