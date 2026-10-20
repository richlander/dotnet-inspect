using System.Collections.Immutable;
using System.Reflection;
using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataDeclarationTextTests
{
    [Fact]
    public void ProjectionRetainsExactOwnerIssuedText()
    {
        MetadataTypeIdentity.Primitive primitive =
            new(Text("int"));
        MetadataMethodSignatureIdentity signature =
            new(
                Header: 0,
                GenericParameterCount: 0,
                RequiredParameterCount: 0,
                ReturnType: primitive,
                ParameterTypes:
                    ImmutableArray<MetadataTypeIdentity>.Empty);
        MetadataMethodImplementationCertificate relationship =
            new(
                default,
                default,
                default,
                default,
                Text("op_Addition"),
                primitive,
                signature,
                new MetadataDeclarationDefinitionDisposition.LocalResolved(
                    default,
                    default,
                    MethodAttributes.Public),
                MetadataSpecialNameEvidence.KnownTrue);
        MetadataParameterDeclarationEvidence namedParameter =
            new(
                HasRow: true,
                Name: Name("left\u202e"u8),
                Attributes: ParameterAttributes.None,
                Markers: new(
                    IsComplete: true,
                    IsReadOnlyCount: 0,
                    RequiresLocationCount: 0,
                    ParamArrayCount: 0,
                    ParamCollectionCount: 0,
                    ScopedRefCount: 0,
                    UnscopedRefCount: 0));
        MetadataParameterDeclarationEvidence unnamedParameter =
            namedParameter with { Name = null };
        MetadataParameterDeclarationEvidence illFormedParameter =
            namedParameter with { Name = Name([(byte)'l', 0xFF]) };
        MetadataNamedTypeIdentity namedType =
            new(
                new(
                    MetadataTypeScopeKind.CurrentModule,
                    Guid.Empty,
                    null,
                    null),
                Text("System.Numerics"),
                [Text("IAdditionOperators`3"), Text("Nested")],
                [3, 0]);

        Assert.Equal(
            "op_Addition",
            MetadataDeclarationText.RenderDeclarationName(relationship));
        Assert.Equal(
            "left\\u202E",
            MetadataDeclarationText.RenderParameterName(namedParameter));
        Assert.Null(
            MetadataDeclarationText.RenderParameterName(unnamedParameter));
        Assert.Equal(
            "l\\uDCFF",
            MetadataDeclarationText.RenderParameterName(illFormedParameter));
        Assert.Equal(
            "int",
            MetadataDeclarationText.RenderPrimitiveName(primitive));
        Assert.Equal(
            "System.Numerics",
            MetadataDeclarationText.RenderNamespace(namedType));
        Assert.Equal(
            2,
            MetadataDeclarationText.GetSegmentCount(namedType));
        Assert.Equal(
            "IAdditionOperators`3",
            MetadataDeclarationText.RenderSegment(namedType, 0));
        Assert.Equal(
            "Nested",
            MetadataDeclarationText.RenderSegment(namedType, 1));
    }

    [Fact]
    public void NamedTypeDefinitionPrefixRetainsStructuredIdentity()
    {
        MetadataNamedTypeIdentity namedType =
            new(
                new(
                    MetadataTypeScopeKind.CurrentModule,
                    Guid.Empty,
                    null,
                    null),
                Text("System.Numerics"),
                [Text("IAdditionOperators`3"), Text("Nested")],
                [3, 0]);
        MetadataNamedTypeIdentity prefix =
            namedType.GetDefinitionPrefix(1);
        Assert.Equal(namedType.Scope, prefix.Scope);
        Assert.Equal(namedType.Namespace, prefix.Namespace);
        Assert.Equal([Text("IAdditionOperators`3")], prefix.Segments);
        Assert.Equal([3], prefix.IntroducedGenericParameterCounts);
        Assert.Same(namedType, namedType.GetDefinitionPrefix(2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => namedType.GetDefinitionPrefix(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => namedType.GetDefinitionPrefix(3));
    }

    static InertString Text(string value) =>
        new(TextPolicy.Field, value);

    static MetadataUtf8Name Name(ReadOnlySpan<byte> value) =>
        new(OpaqueByteRange.Copy(value));
}
