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
    static readonly byte[] SignatureImage =
        ArrayKindSignatureFixture.BuildImage();

    const string ModifiedVectorStructural =
        "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][]";
    const string ModifiedMd1Structural =
        "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][*]";

    public static TheoryData<string, string> TypeNodeParameterExpectations => new()
    {
        { "Vector", "System.Int32[]" },
        { "Md1", "System.Int32[*]" },
        { "Md1Twin", "System.Int32[*]" },
        { "Md2", "System.Int32[,]" },
        { "Nested", "System.Collections.Generic.List{System.Int32[*]}" },
        { "Pointer", "System.Int32[*]*" },
        { "ByRef", "System.Int32[*]@" },
        { "Tuple", "System.ValueTuple{System.Int32[*],System.Int32[]}" },
        { "Generic", "M0[*]" },
        { "ModifiedVector", ModifiedVectorStructural },
        { "ModifiedMd1", ModifiedMd1Structural },
    };

    public static TheoryData<string, string> TypeNodeReturnExpectations => new()
    {
        { "ReturnVector", "System.Int32[]" },
        { "ReturnVectorTwin", "System.Int32[]" },
        { "ReturnMd1", "System.Int32[*]" },
        { "ReturnMd1Twin", "System.Int32[*]" },
        { "ReturnMd2", "System.Int32[,]" },
    };

    public static TheoryData<string, string, string, string?, string?>
        ApiParameterExpectations => new()
        {
            { "Vector", "int[]", "int[]", null, null },
            { "Md1", "int[*]", "int[*]", "System.Int32[*]", null },
            { "Md1Twin", "int[*]", "int[*]", "System.Int32[*]", null },
            { "Md2", "int[,]", "int[,]", null, null },
            {
                "Nested",
                "System.Collections.Generic.List<int[*]>",
                "System.Collections.Generic.List<int[*]>",
                "System.Collections.Generic.List{System.Int32[*]}",
                null
            },
            { "Pointer", "int[*]*", "int[*]*", "System.Int32[*]*", null },
            { "ByRef", "int[*]", "int[*]", "System.Int32[*]@", "ref" },
            {
                "Tuple",
                "(int[*], int[])",
                "System.ValueTuple<int[*], int[]>",
                "System.ValueTuple{System.Int32[*],System.Int32[]}",
                null
            },
            { "Generic", "T[*]", "T[*]", "M0[*]", null },
            {
                "ModifiedVector",
                "int[][]",
                "int[][]",
                ModifiedVectorStructural,
                null
            },
            {
                "ModifiedMd1",
                "int[][*]",
                "int[][*]",
                ModifiedMd1Structural,
                null
            },
        };

    public static TheoryData<string, string, string, string?>
        ApiReturnExpectations => new()
        {
            { "ReturnVector", "int[]", "int[]", null },
            { "ReturnVectorTwin", "int[]", "int[]", null },
            { "ReturnMd1", "int[*]", "int[*]", "System.Int32[*]" },
            { "ReturnMd1Twin", "int[*]", "int[*]", "System.Int32[*]" },
            { "ReturnMd2", "int[,]", "int[,]", null },
        };

    [Theory]
    [MemberData(nameof(TypeNodeParameterExpectations))]
    public void TypeNodeParameterProjection_MatchesRecordedExpectation(
        string memberName,
        string expected)
    {
        using var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = TypeHandle(reader);
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

    [Theory]
    [MemberData(nameof(TypeNodeReturnExpectations))]
    public void TypeNodeReturnProjection_MatchesRecordedExpectation(
        string memberName,
        string expected)
    {
        using var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = TypeHandle(reader);
        MethodDefinition method = Method(reader, typeHandle, memberName);

        MethodSignature<TypeNode> signature = method.DecodeSignature(
            TypeNodeProvider.Instance,
            GenericContext.ForMethod(
                reader,
                reader.GetTypeDefinition(typeHandle),
                method));

        Assert.Equal(expected, signature.ReturnType.StructuralIdentity());
    }

    [Theory]
    [MemberData(nameof(ApiParameterExpectations))]
    public void ApiParameterProjection_MatchesRecordedExpectation(
        string memberName,
        string expectedDisplay,
        string expectedCanonical,
        string? expectedStructural,
        string? expectedModifier)
    {
        using var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false));
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == ArrayKindSignatureFixture.TypeName);

        ApiParameter parameter =
            Assert.Single(Member(type, memberName).SignatureModel!.Parameters);

        Assert.Equal(expectedDisplay, parameter.Type);
        Assert.Equal(expectedCanonical, parameter.EffectiveCanonicalType);
        Assert.Equal(expectedStructural, parameter.StructuralType);
        Assert.Equal(expectedModifier, parameter.Modifier);
    }

    [Theory]
    [MemberData(nameof(ApiReturnExpectations))]
    public void ApiReturnProjection_MatchesRecordedExpectation(
        string memberName,
        string expectedDisplay,
        string expectedCanonical,
        string? expectedStructural)
    {
        using var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false));
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == ArrayKindSignatureFixture.TypeName);
        ApiSignature signature = Member(type, memberName).SignatureModel!;

        Assert.Equal(expectedDisplay, signature.ReturnType);
        Assert.Equal(expectedCanonical, signature.EffectiveCanonicalReturnType);
        Assert.Equal(expectedStructural, signature.StructuralReturnType);
    }

    [Fact]
    public void Extract_PreservesArrayKindAcrossApiIdentityProjections()
    {
        using var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == ArrayKindSignatureFixture.TypeName);
        TypeDefinitionHandle typeHandle = TypeHandle(reader);

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
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "ModifiedVector",
            "int[][]",
            "int[][]",
            ModifiedVectorStructural,
            emittedStructural: ModifiedVectorStructural);
        AssertParameterIdentity(
            reader,
            typeHandle,
            type,
            "ModifiedMd1",
            "int[][*]",
            "int[][*]",
            ModifiedMd1Structural,
            emittedStructural: ModifiedMd1Structural);

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
            new MemoryStream(SignatureImage, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            TypeHandle(reader));

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
        Assembly assembly = Assembly.Load(SignatureImage);
        Type type = assembly.GetType(
            $"N.{ArrayKindSignatureFixture.TypeName}",
            throwOnError: true)!;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);

        Assert.Equal(ArrayKindSignatureFixture.MethodCount, methods.Length);
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
        ApiSurface live;
        using (var peReader = new PEReader(
            new MemoryStream(SignatureImage, writable: false)))
        {
            live = ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
        }

        ApiSurface roundTripped = JsonSerializer.Deserialize<ApiSurface>(
            JsonSerializer.Serialize(live))!;
        ApiType liveType = Assert.Single(
            live.Types,
            candidate => candidate.Name == ArrayKindSignatureFixture.TypeName);
        ApiType roundTrippedType = Assert.Single(
            roundTripped.Types,
            candidate => candidate.Name == ArrayKindSignatureFixture.TypeName);

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
        ApiSurface vector = ExtractSingleMethod(
            ArrayKindSignatureFixture.BuildSingleVectorParameterImage());
        ApiSurface md1 = ExtractSingleMethod(
            ArrayKindSignatureFixture.BuildSingleRankOneNonSzParameterImage());

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

    static TypeDefinitionHandle TypeHandle(MetadataReader reader) =>
        reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name)
                == ArrayKindSignatureFixture.TypeName);

    static ApiTypeShape ReturnShape(ApiType type, string memberName) =>
        Assert.IsType<ApiTypeShape>(
            Member(type, memberName).SignatureModel?.ReturnTypeShape);

    static ApiMember Member(ApiType type, string name) =>
        Assert.Single(
            type.Members,
            member => member.Kind == "method" && member.Name == name);

    static ApiSurface ExtractSingleMethod(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image));
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }
}
