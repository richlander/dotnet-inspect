using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
}
