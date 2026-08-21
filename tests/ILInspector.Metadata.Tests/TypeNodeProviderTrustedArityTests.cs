using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

// #4507: TypeRef (Decompiler layer) recovers a nested type's true generic
// arity from GetIntroducedTypeParameterCounts when a raw metadata-name
// segment lacks its canonical `N suffix. TypeNodeProvider/TypeNode had no
// equivalent trusted-count fallback and derived nested-segment arity purely
// from the raw name string, so a locally-declared nested generic type whose
// outer segment lacks a suffix could misplace generic arguments onto the
// wrong declaring-chain segment.
public sealed class TypeNodeProviderTrustedArityTests
{
    // Outer's raw name has no canonical `N suffix even though it owns one
    // generic parameter; Inner's raw name (Inner`1) only advertises its own
    // introduced parameter, not the enclosing one. Metadata proves Outer owns
    // 1 parameter and Inner introduces 1 more (cumulative 2).
    static readonly (MetadataReader Reader, TypeDefinitionHandle Inner) Fixture = BuildFixture();

    [Fact]
    public void NestedGenericDefinition_WithMissingOuterSuffix_RendersUnboundArityOnOuterSegment()
    {
        TypeNode node = TypeNodeProvider.Instance.GetTypeFromDefinition(
            Fixture.Reader,
            Fixture.Inner,
            rawTypeKind: 0x12);

        var namedType = Assert.IsType<NamedTypeNode>(node);
        Assert.NotNull(namedType.MetadataName);
        Assert.Equal([1, 1], namedType.MetadataName!.IntroducedTypeParameterCounts);
    }

    [Fact]
    public void NestedGenericInstance_WithMissingOuterSuffix_PlacesArgumentsOnCorrectSegments()
    {
        TypeNode node = TypeNodeProvider.Instance.GetTypeFromDefinition(
            Fixture.Reader,
            Fixture.Inner,
            rawTypeKind: 0x12);

        TypeNode generic = TypeNodeProvider.Instance.GetGenericInstantiation(
            node,
            [
                new PrimitiveTypeNode("int", isReferenceType: false),
                new PrimitiveTypeNode("string", isReferenceType: true),
            ]);

        // The outer segment's trusted count (1) places the first argument on
        // Outer, and Inner's own declared suffix (`1) places the second on
        // Inner — Outer<int>.Inner<string>, not Outer.Inner<int, string> or
        // any other misplacement.
        Assert.Equal("N.Outer<int>.Inner<string>", generic.Render());
    }

    static (MetadataReader, TypeDefinitionHandle) BuildFixture()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TrustedArityFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TrustedArityFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        StringHandle ns = metadata.GetOrAddString("N");
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            ns,
            metadata.GetOrAddString("Outer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            0);

        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("Inner`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TInner"),
            1);
        metadata.AddNestedType(inner, outer);

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        byte[] bytes = image.ToArray();

        var peReader = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(bytes));
        MetadataReader reader = peReader.GetMetadataReader();

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition typeDef = reader.GetTypeDefinition(handle);
            if (reader.GetString(typeDef.Name) == "Inner`1")
                return (reader, handle);
        }

        throw new InvalidOperationException("Inner`1 was not found in the fixture.");
    }
}
