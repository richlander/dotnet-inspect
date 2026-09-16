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
    static readonly (
        MetadataReader Reader,
        TypeDefinitionHandle Inner,
        MethodDefinitionHandle Method,
        byte[] Image) Fixture = BuildFixture();

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
        MethodDefinition method =
            Fixture.Reader.GetMethodDefinition(Fixture.Method);
        MethodSignature<TypeNode> signature = GuardedProviderDecode.Method(
            Fixture.Reader,
            method,
            TypeNodeProvider.Instance,
            context: (GenericContext?)null,
            fallbackReturn: (TypeNode)new DegradedTypeNode());
        TypeNode generic = Assert.Single(signature.ParameterTypes);

        // The outer segment's trusted count (1) places the first argument on
        // Outer, and Inner's own declared suffix (`1) places the second on
        // Inner — Outer<int>.Inner<string>, not Outer.Inner<int, string> or
        // any other misplacement.
        Assert.Equal("N.Outer<int>.Inner<string>", generic.Render());
    }

    [Fact]
    public void NestedGenericSignature_WithMalformedOwnership_IsReportedAsInspectionFailure()
    {
        var fixture = BuildFixture(innerSecondParameterIndex: 2);
        using var stream = new MemoryStream(fixture.Image);

        ApiSurface? surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true);

        Assert.NotNull(surface);
        Assert.DoesNotContain(
            surface.Types,
            type => type.Name == "Consumer");
        ApiSurfaceInspectionFailure failure = Assert.Single(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x02000004);
        Assert.Equal("type row", failure.Operation);
        Assert.Equal("MalformedMetadata", failure.Kind);
    }

    [Fact]
    public void SameModuleTypeReference_DefinitionIndexWorkIsSharedAcrossProviders()
    {
        using LocalReferenceFixture fixture =
            BuildLocalReferenceFixture(definitionCount: 512);
        int firstWork = 0;
        TypeNode first = new TypeNodeProvider(
            beforeMaterialize: amount => firstWork += amount)
            .GetTypeFromReference(
                fixture.Reader,
                fixture.Reference,
                rawTypeKind: 0x12);
        int secondWork = 0;
        TypeNode second = new TypeNodeProvider(
            beforeMaterialize: amount => secondWork += amount)
            .GetTypeFromReference(
                fixture.Reader,
                fixture.Reference,
                rawTypeKind: 0x12);

        Assert.Equal([1], Assert.IsType<NamedTypeNode>(first)
            .MetadataName!.IntroducedTypeParameterCounts);
        Assert.Equal([1], Assert.IsType<NamedTypeNode>(second)
            .MetadataName!.IntroducedTypeParameterCounts);
        Assert.True(
            firstWork > 8_000,
            $"first local TypeDef index build charged only {firstWork:N0} units");
        Assert.True(
            secondWork < firstWork / 4,
            $"cached local TypeDef lookup charged {secondWork:N0} of "
            + $"{firstWork:N0} first-build units");
    }

    static (
        MetadataReader Reader,
        TypeDefinitionHandle Inner,
        MethodDefinitionHandle Method,
        byte[] Image) BuildFixture(
            ushort innerSecondParameterIndex = 1)
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

        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("Inner`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            ns,
            metadata.GetOrAddString("Consumer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TInner"),
            innerSecondParameterIndex);

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters =>
                {
                    GenericTypeArgumentsEncoder arguments = parameters
                        .AddParameter()
                        .Type()
                        .GenericInstantiation(
                            inner,
                            genericArgumentCount: 2,
                            isValueType: false);
                    arguments.AddArgument().Int32();
                    arguments.AddArgument().String();
                });
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

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
            {
                return (
                    reader,
                    handle,
                    MetadataTokens.MethodDefinitionHandle(1),
                    bytes);
            }
        }

        throw new InvalidOperationException("Inner`1 was not found in the fixture.");
    }

    static LocalReferenceFixture BuildLocalReferenceFixture(
        int definitionCount)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("LocalReferenceFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("LocalReferenceFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle reference = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Referenced"));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle referenced = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Referenced"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            referenced,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        for (int i = 0; i < definitionCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Fillers"),
                metadata.GetOrAddString($"Filler{i:D4}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return new LocalReferenceFixture(image.ToArray(), reference);
    }

    sealed class LocalReferenceFixture : IDisposable
    {
        readonly PEReader _peReader;

        internal LocalReferenceFixture(
            byte[] image,
            TypeReferenceHandle reference)
        {
            _peReader = new PEReader(
                ImmutableCollectionsMarshal.AsImmutableArray(image));
            Reader = _peReader.GetMetadataReader();
            Reference = reference;
        }

        internal MetadataReader Reader { get; }
        internal TypeReferenceHandle Reference { get; }

        public void Dispose() => _peReader.Dispose();
    }
}
