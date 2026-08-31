using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class ArrayKindIdentityTests
{
    [Fact]
    public void Extract_PreservesArrayKindAcrossApiIdentityProjections()
    {
        byte[] image = BuildArrayKindImage();
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "ArrayKinds");
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "ArrayKinds");

        ApiMember vector = Member(type, "Vector");
        ApiParameter vectorParameter =
            Assert.Single(vector.SignatureModel!.Parameters);
        Assert.Equal("int[]", vectorParameter.Type);
        Assert.Equal("int[]", vectorParameter.EffectiveCanonicalType);
        Assert.Null(vectorParameter.StructuralType);
        AssertDecodedParameterStructuralIdentity(
            reader,
            typeHandle,
            "Vector",
            "System.Int32[]");

        ApiMember md1 = Member(type, "Md1");
        ApiParameter md1Parameter =
            Assert.Single(md1.SignatureModel!.Parameters);
        Assert.Equal("int[*]", md1Parameter.Type);
        Assert.Equal("int[*]", md1Parameter.EffectiveCanonicalType);
        Assert.Equal("System.Int32[*]", md1Parameter.StructuralType);
        AssertDecodedParameterStructuralIdentity(
            reader,
            typeHandle,
            "Md1",
            "System.Int32[*]");

        Assert.Equal(
            md1Parameter.EffectiveCanonicalType,
            Assert.Single(
                Member(type, "Md1Twin").SignatureModel!.Parameters)
                .EffectiveCanonicalType);
        AssertDecodedParameterStructuralIdentity(
            reader,
            typeHandle,
            "Md1Twin",
            "System.Int32[*]");

        ApiMember md2 = Member(type, "Md2");
        ApiParameter md2Parameter =
            Assert.Single(md2.SignatureModel!.Parameters);
        Assert.Equal("int[,]", md2Parameter.Type);
        Assert.Null(md2Parameter.StructuralType);
        AssertDecodedParameterStructuralIdentity(
            reader,
            typeHandle,
            "Md2",
            "System.Int32[,]");
        Assert.False(vector.IsUnsafe);
        Assert.False(md1.IsUnsafe);
        Assert.False(md2.IsUnsafe);
        Assert.False(Member(type, "Nested").IsUnsafe);
        Assert.False(Member(type, "ByRef").IsUnsafe);
        Assert.False(Member(type, "Tuple").IsUnsafe);
        Assert.False(Member(type, "Generic").IsUnsafe);
        Assert.False(Member(type, "ModifiedVector").IsUnsafe);
        Assert.False(Member(type, "ModifiedMd1").IsUnsafe);
        Assert.False(Member(type, "ReturnMd1").IsUnsafe);
        Assert.True(Member(type, "Pointer").IsUnsafe);

        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "Nested",
            "System.Collections.Generic.List<int[*]>",
            "System.Collections.Generic.List<int[*]>",
            "System.Collections.Generic.List{System.Int32[*]}",
            emittedStructural:
                "System.Collections.Generic.List{System.Int32[*]}");
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "Pointer",
            "int[*]*",
            "int[*]*",
            "System.Int32[*]*",
            emittedStructural: "System.Int32[*]*");
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "ByRef",
            "int[*]",
            "int[*]",
            "System.Int32[*]@",
            modifier: "ref",
            emittedStructural: "System.Int32[*]@");
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "Tuple",
            "(int[*], int[])",
            "System.ValueTuple<int[*], int[]>",
            "System.ValueTuple{System.Int32[*],System.Int32[]}",
            emittedStructural:
                "System.ValueTuple{System.Int32[*],System.Int32[]}");
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "Generic",
            "T[*]",
            "T[*]",
            "M0[*]",
            emittedStructural: "M0[*]");
        const string modifiedVectorStructural =
            "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][]";
        const string modifiedMd1Structural =
            "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][*]";
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "ModifiedVector",
            "int[][]",
            "int[][]",
            modifiedVectorStructural,
            emittedStructural: modifiedVectorStructural);
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "ModifiedMd1",
            "int[][*]",
            "int[][*]",
            modifiedMd1Structural,
            emittedStructural: modifiedMd1Structural);

        ApiTypeShape vectorShape = ReturnShape(type, "ReturnVector");
        ApiTypeShape vectorTwinShape = ReturnShape(type, "ReturnVectorTwin");
        ApiTypeShape md1Shape = ReturnShape(type, "ReturnMd1");
        ApiTypeShape md1TwinShape = ReturnShape(type, "ReturnMd1Twin");
        ApiTypeShape md2Shape = ReturnShape(type, "ReturnMd2");
        Assert.Equal(ApiTypeShapeKind.SzArray, vectorShape.Kind);
        Assert.Equal(vectorShape, vectorTwinShape);
        Assert.Equal(vectorShape.GetHashCode(), vectorTwinShape.GetHashCode());
        Assert.Equal(ApiTypeShapeKind.Array, md1Shape.Kind);
        Assert.Equal(1, md1Shape.ArrayRank);
        Assert.Null(
            Member(type, "ReturnVector").SignatureModel!.StructuralReturnType);
        Assert.Equal(
            "System.Int32[*]",
            Member(type, "ReturnMd1").SignatureModel!.StructuralReturnType);
        Assert.Null(
            Member(type, "ReturnMd2").SignatureModel!.StructuralReturnType);
        AssertDecodedReturnStructuralIdentity(
            reader,
            typeHandle,
            "ReturnVector",
            "System.Int32[]");
        AssertDecodedReturnStructuralIdentity(
            reader,
            typeHandle,
            "ReturnMd1",
            "System.Int32[*]");
        AssertDecodedReturnStructuralIdentity(
            reader,
            typeHandle,
            "ReturnMd2",
            "System.Int32[,]");
        Assert.Equal(md1Shape, md1TwinShape);
        Assert.Equal(md1Shape.GetHashCode(), md1TwinShape.GetHashCode());
        Assert.NotEqual(vectorShape, md1Shape);
        Assert.NotEqual(md1Shape, md2Shape);

        var directAnchors = reader.GetTypeDefinition(typeHandle)
            .GetMethods()
            .ToDictionary(
                handle => reader.GetString(
                    reader.GetMethodDefinition(handle).Name),
                handle => ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    typeHandle,
                    reader.GetMethodDefinition(handle)));

        Assert.Contains(
            "System.Int32[]",
            directAnchors["Vector"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.Contains(
            "System.Int32[*]",
            directAnchors["Md1"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.Contains(
            "System.Int32[,]",
            directAnchors["Md2"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.NotEqual(
            directAnchors["Vector"].Fingerprint,
            directAnchors["Md1"].Fingerprint);
        Assert.NotEqual(
            directAnchors["Md1"].Fingerprint,
            directAnchors["Md2"].Fingerprint);

        var projectedAnchors = new Dictionary<string, MemberAnchor>
        {
            ["Vector"] = ApiMemberIdentity.GetMemberAnchor(type, vector),
            ["Md1"] = ApiMemberIdentity.GetMemberAnchor(type, md1),
            ["Md2"] = ApiMemberIdentity.GetMemberAnchor(type, md2),
        };
        Assert.Contains(
            "int[]",
            projectedAnchors["Vector"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.Contains(
            "int[*]",
            projectedAnchors["Md1"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.Contains(
            "int[,]",
            projectedAnchors["Md2"].CanonicalSignature,
            StringComparison.Ordinal);
        Assert.NotEqual(
            projectedAnchors["Vector"].Fingerprint,
            projectedAnchors["Md1"].Fingerprint);
        Assert.NotEqual(
            projectedAnchors["Md1"].Fingerprint,
            projectedAnchors["Md2"].Fingerprint);
    }

    [Fact]
    public void SyntheticImage_UsesValidBodylessInterfaceDeclarations()
    {
        using var peReader = new PEReader(
            new MemoryStream(BuildArrayKindImage()));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(
                handle => reader.GetString(
                    reader.GetTypeDefinition(handle).Name) == "ArrayKinds"));

        Assert.True(type.Attributes.HasFlag(TypeAttributes.Interface));
        Assert.True(type.Attributes.HasFlag(TypeAttributes.Abstract));
        Assert.True(type.BaseType.IsNil);
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            Assert.True(method.Attributes.HasFlag(MethodAttributes.Public));
            Assert.True(method.Attributes.HasFlag(MethodAttributes.Abstract));
            Assert.True(method.Attributes.HasFlag(MethodAttributes.Virtual));
            Assert.False(method.Attributes.HasFlag(MethodAttributes.Static));
            Assert.Equal(0, method.RelativeVirtualAddress);
            Assert.True(method.DecodeSignature(
                TypeNodeProvider.Instance,
                GenericContext.ForMethod(reader, type, method))
                .Header
                .IsInstance);
        }
    }

    [Fact]
    public void SyntheticImage_ResolvesEveryDeclaredSignatureTypeThroughClrLoader()
    {
        Assembly assembly = Assembly.Load(BuildArrayKindImage());
        Type type = assembly.GetType(
            "N.ArrayKinds",
            throwOnError: true)!;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);

        Assert.Equal(16, methods.Length);
        foreach (MethodInfo method in methods)
        {
            _ = method.ReturnType;
            _ = method.ReturnParameter.GetRequiredCustomModifiers();
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                _ = parameter.ParameterType;
                _ = parameter.GetRequiredCustomModifiers();
            }
        }
    }

    [Fact]
    public void ExtractedArrayIdentity_SurvivesJsonRoundTrip()
    {
        byte[] image = BuildArrayKindImage();
        ApiSurface live;
        using (var peReader = new PEReader(new MemoryStream(image)))
        {
            live = ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
        }

        ApiSurface roundTripped = JsonSerializer.Deserialize<ApiSurface>(
            JsonSerializer.Serialize(live))!;
        ApiType liveType = Assert.Single(
            live.Types,
            candidate => candidate.Name == "ArrayKinds");
        ApiType roundTrippedType = Assert.Single(
            roundTripped.Types,
            candidate => candidate.Name == "ArrayKinds");

        foreach (string name in new[]
        {
            "Vector",
            "Md1",
            "Md1Twin",
            "Md2",
            "Nested",
            "Pointer",
            "ByRef",
            "Tuple",
            "Generic",
            "ModifiedVector",
            "ModifiedMd1",
        })
        {
            ApiMember liveMember = Member(liveType, name);
            ApiMember roundTrippedMember = Member(roundTrippedType, name);
            Assert.Null(roundTrippedMember.SignatureModel);
            Assert.Equal(
                ApiMemberIdentity.GetMemberAnchor(liveType, liveMember),
                ApiMemberIdentity.GetMemberAnchor(
                    roundTrippedType,
                    roundTrippedMember));
        }
    }

    [Fact]
    public void Compare_DoesNotPairSzAndRankOneNonSzAsEqual()
    {
        ApiSurface vector = ExtractSingleMethod(Sz(Int32));
        ApiSurface md1 = ExtractSingleMethod(MdArray(Int32, rank: 1));

        ApiDiff diff = ApiDiffAnalyzer.Compare(vector, md1);

        Assert.False(diff.IsEmpty);
        TypeDiff typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Contains(
            typeDiff.Changes,
            change =>
                change.Subject?.OldMember?.Anchor?.CanonicalSignature
                    .Contains("int[]", StringComparison.Ordinal)
                == true);
        Assert.Contains(
            typeDiff.Changes,
            change =>
                change.Subject?.NewMember?.Anchor?.CanonicalSignature
                    .Contains("int[*]", StringComparison.Ordinal)
                == true);
    }

    static void AssertParameterIdentity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        ApiType type,
        string memberName,
        string display,
        string canonical,
        string structural,
        string? modifier = null,
        string? emittedStructural = null)
    {
        ApiParameter parameter =
            Assert.Single(Member(type, memberName).SignatureModel!.Parameters);
        Assert.Equal(display, parameter.Type);
        Assert.Equal(canonical, parameter.EffectiveCanonicalType);
        Assert.Equal(emittedStructural, parameter.StructuralType);
        Assert.Equal(modifier, parameter.Modifier);
        AssertDecodedParameterStructuralIdentity(
            reader,
            typeHandle,
            memberName,
            structural);
    }

    static void AssertDecodedParameterStructuralIdentity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string memberName,
        string expected)
    {
        MethodDefinition method = Method(reader, typeHandle, memberName);
        MethodSignature<TypeNode> signature = method.DecodeSignature(
            TypeNodeProvider.Instance,
            GenericContext.ForMethod(
                reader,
                reader.GetTypeDefinition(typeHandle),
                method));
        Assert.Equal(
            expected,
            Assert.Single(signature.ParameterTypes).StructuralIdentity());
    }

    static void AssertDecodedReturnStructuralIdentity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string memberName,
        string expected)
    {
        MethodDefinition method = Method(reader, typeHandle, memberName);
        MethodSignature<TypeNode> signature = method.DecodeSignature(
            TypeNodeProvider.Instance,
            GenericContext.ForMethod(
                reader,
                reader.GetTypeDefinition(typeHandle),
                method));
        Assert.Equal(expected, signature.ReturnType.StructuralIdentity());
    }

    static MethodDefinition Method(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string name) =>
        reader.GetMethodDefinition(
            reader.GetTypeDefinition(typeHandle)
                .GetMethods()
                .Single(
                    handle => reader.GetString(
                        reader.GetMethodDefinition(handle).Name) == name));

    static ApiTypeShape ReturnShape(ApiType type, string memberName) =>
        Assert.IsType<ApiTypeShape>(
            Member(type, memberName).SignatureModel?.ReturnTypeShape);

    static ApiMember Member(ApiType type, string name) =>
        Assert.Single(
            type.Members,
            member => member.Kind == "method" && member.Name == name);

    static ApiSurface ExtractSingleMethod(byte[] parameterType)
    {
        byte[] image = BuildImage(
            [
                new MethodSpec(
                    "M",
                    parameterType,
                    Void,
                    IsGeneric: false),
            ]);
        using var peReader = new PEReader(new MemoryStream(image));
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static byte[] BuildArrayKindImage()
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle list = metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
        TypeReferenceHandle valueTuple = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueTuple`2"));
        TypeReferenceHandle isVolatile = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsVolatile"));

        byte[] md1 = MdArray(Int32, rank: 1);
        byte[] sz = Sz(Int32);
        byte[] md2 = MdArray(Int32, rank: 2);
        byte[] modifiedVector = Sz(RequiredModifier(isVolatile, Int32));
        byte[] nested = GenericInstance(
            isValueType: false,
            list,
            md1);
        byte[] tuple = GenericInstance(
            isValueType: true,
            valueTuple,
            md1,
            sz);

        return BuildImage(
            [
                new("Vector", sz, Void, IsGeneric: false),
                new("Md1", md1, Void, IsGeneric: false),
                new("Md1Twin", md1, Void, IsGeneric: false),
                new("Md2", md2, Void, IsGeneric: false),
                new("Nested", nested, Void, IsGeneric: false),
                new("Pointer", Pointer(md1), Void, IsGeneric: false),
                new("ByRef", ByRef(md1), Void, IsGeneric: false),
                new("Tuple", tuple, Void, IsGeneric: false),
                new("Generic", MdArray(MethodGeneric0, rank: 1), Void, IsGeneric: true),
                new("ModifiedVector", Sz(modifiedVector), Void, IsGeneric: false),
                new("ModifiedMd1", MdArray(modifiedVector, rank: 1), Void, IsGeneric: false),
                new("ReturnVector", null, sz, IsGeneric: false),
                new("ReturnVectorTwin", null, sz, IsGeneric: false),
                new("ReturnMd1", null, md1, IsGeneric: false),
                new("ReturnMd1Twin", null, md1, IsGeneric: false),
                new("ReturnMd2", null, md2, IsGeneric: false),
            ],
            metadata);
    }

    static byte[] BuildImage(
        IReadOnlyList<MethodSpec> methods,
        MetadataBuilder? metadata = null)
    {
        metadata ??= CreateMetadata();
        var methodHandles = new List<MethodDefinitionHandle>(methods.Count);
        int parameterRow = 1;
        foreach (MethodSpec method in methods)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(method.IsGeneric ? (byte)0x30 : (byte)0x20);
            if (method.IsGeneric)
                signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(
                method.ParameterType is null ? 0 : 1);
            signature.WriteBytes(method.ReturnType);
            if (method.ParameterType is not null)
                signature.WriteBytes(method.ParameterType);

            MethodDefinitionHandle methodHandle =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(method.Name),
                    metadata.GetOrAddBlob(signature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(parameterRow));
            methodHandles.Add(methodHandle);
            if (method.ParameterType is not null)
            {
                metadata.AddParameter(
                    ParameterAttributes.None,
                    metadata.GetOrAddString("value"),
                    sequenceNumber: 1);
                parameterRow++;
            }
            if (method.IsGeneric)
            {
                metadata.AddGenericParameter(
                    methodHandle,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            }
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("ArrayKinds"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            methodHandles[0]);

        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddAssembly(
            metadata.GetOrAddString("ArrayKinds"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("ArrayKinds.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] GenericInstance(
        bool isValueType,
        TypeReferenceHandle definition,
        params byte[][] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(isValueType ? (byte)0x11 : (byte)0x12);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(definition) << 2) | 1);
        signature.WriteCompressedInteger(arguments.Length);
        foreach (byte[] argument in arguments)
            signature.WriteBytes(argument);
        return signature.ToArray();
    }

    static byte[] RequiredModifier(
        TypeReferenceHandle modifier,
        byte[] inner)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x1f);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(modifier) << 2) | 1);
        signature.WriteBytes(inner);
        return signature.ToArray();
    }

    static byte[] MdArray(byte[] elementType, int rank)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x14);
        signature.WriteBytes(elementType);
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(0);
        signature.WriteCompressedInteger(0);
        return signature.ToArray();
    }

    static byte[] Sz(byte[] elementType) => [0x1d, .. elementType];

    static byte[] Pointer(byte[] elementType) => [0x0f, .. elementType];

    static byte[] ByRef(byte[] elementType) => [0x10, .. elementType];

    static readonly byte[] Void = [0x01];
    static readonly byte[] Int32 = [0x08];
    static readonly byte[] MethodGeneric0 = [0x1e, 0x00];

    sealed record MethodSpec(
        string Name,
        byte[]? ParameterType,
        byte[] ReturnType,
        bool IsGeneric);
}
