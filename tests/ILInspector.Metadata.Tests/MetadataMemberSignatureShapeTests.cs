using CSharpText;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public unsafe class MetadataMemberSignatureShapeTests
{
    [Theory]
    [InlineData(
        nameof(ShapeSpecimens.Primitive),
        "void Primitive(int value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Nullable),
        "void Nullable(int? value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.GenericNullable),
        "void GenericNullable<T>(T? value) where T : struct;",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Arrays),
        "void Arrays(int[][,] first, int[,][] second);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Tuple),
        "void Tuple((int left, string right) value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Tuple8),
        "void Tuple8((int, int, int, int, int, int, int, int) value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.Pointer),
        "unsafe void Pointer(int* value);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.FunctionPointer),
        "unsafe void FunctionPointer(delegate* unmanaged[Cdecl]<int, string> callback);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.ByRefFunctionPointer),
        "unsafe void ByRefFunctionPointer(delegate*<ref int, void> callback);",
        SourceMemberSignatureKind.Method)]
    [InlineData(
        nameof(ShapeSpecimens.ExplicitValueTupleRest),
        """
        unsafe void ExplicitValueTupleRest(
            delegate* unmanaged<
                global::System.ValueTuple<
                    int, int, int, int, int, int, int, (short, byte)>,
                void> callback);
        """,
        SourceMemberSignatureKind.Method)]
    [InlineData(
        "op_Implicit",
        "public static implicit operator int(global::ILInspector.Metadata.Tests.ShapeSpecimens value) => 0;",
        SourceMemberSignatureKind.ConversionOperator)]
    public void SourceAndMetadataAdapters_ProduceTheSameShape(
        string methodName,
        string declaration,
        SourceMemberSignatureKind kind)
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, nameof(ShapeSpecimens), methodName);

        MemberSignatureShapeResult metadata =
            MetadataMemberSignatureShape.Create(reader, handle);
        MemberSignatureShapeResult source =
            SourceMemberSignatureShape.Create(declaration, kind);

        Assert.True(metadata.IsAvailable, metadata.UnavailableReason);
        Assert.True(source.IsAvailable, source.UnavailableReason);
        Assert.True(
            source.Shape == metadata.Shape,
            $"source={MemberSignatureShapeCodec.Encode(source.Shape!)}{Environment.NewLine}"
            + $"metadata={MemberSignatureShapeCodec.Encode(metadata.Shape!)}");
    }

    [Fact]
    public void NestedTypeAndMethodGenericParameters_UseCumulativePositions()
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, "Inner`1", "Pair");

        MemberSignatureShapeResult metadata =
            MetadataMemberSignatureShape.Create(reader, handle);
        MemberSignatureShapeResult source =
            SourceMemberSignatureShape.Create(
                "void Pair<V>(T outer, U inner, V method);",
                SourceMemberSignatureKind.Method,
                ["T", "U"]);

        Assert.True(metadata.IsAvailable, metadata.UnavailableReason);
        Assert.True(source.IsAvailable, source.UnavailableReason);
        Assert.Equal(source.Shape, metadata.Shape);
    }

    [Fact]
    public void SupplementalConventionAndExplicitTupleRest_CannotFalseUniquelyCrossMatch()
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MemberSignatureShapeResult metadataSupplemental =
            MetadataMemberSignatureShape.Create(
                reader,
                FindMethod(
                    reader,
                    nameof(ShapeSpecimens),
                    nameof(ShapeSpecimens.SupplementalTupleSyntax)));
        MemberSignatureShapeResult metadataExplicit =
            MetadataMemberSignatureShape.Create(
                reader,
                FindMethod(
                    reader,
                    nameof(ShapeSpecimens),
                    nameof(ShapeSpecimens.ExplicitValueTupleRest)));
        MemberSignatureShapeResult sourceSupplemental =
            SourceMemberSignatureShape.Create(
                """
                unsafe void SupplementalTupleSyntax(
                    delegate* unmanaged[SuppressGCTransition]<
                        (int, int, int, int, int, int, int, (short, byte)),
                        void> callback);
                """,
                SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult sourceExplicit =
            SourceMemberSignatureShape.Create(
                """
                unsafe void ExplicitValueTupleRest(
                    delegate* unmanaged<
                        global::System.ValueTuple<
                            int, int, int, int, int, int, int, (short, byte)>,
                        void> callback);
                """,
                SourceMemberSignatureKind.Method);

        Assert.True(metadataSupplemental.IsAvailable, metadataSupplemental.UnavailableReason);
        Assert.True(metadataExplicit.IsAvailable, metadataExplicit.UnavailableReason);
        Assert.False(sourceSupplemental.IsAvailable);
        Assert.True(sourceExplicit.IsAvailable, sourceExplicit.UnavailableReason);
        Assert.Equal(metadataExplicit.Shape, sourceExplicit.Shape);
        Assert.NotEqual(metadataSupplemental.Shape, sourceExplicit.Shape);

        MemberSignatureCorrespondence<string> correspondence =
            MemberSignatureShapeMatcher.Match(
                metadataSupplemental,
                [
                    ("supplemental", sourceSupplemental),
                    ("explicit", sourceExplicit),
                ]);
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, correspondence.Kind);
    }

    [Theory]
    [InlineData(nameof(ShapeSpecimens.LegacyNamed), "`0(IReadOnlyList<string>)", true)]
    [InlineData(nameof(ShapeSpecimens.LegacyNamed), "`0(List<string>)", false)]
    [InlineData(nameof(ShapeSpecimens.LegacyGeneric), "`1(T)", true)]
    public void LegacyCompatibility_ValidatesAnAlreadyIdentifiedMethodOnly(
        string methodName,
        string legacyText,
        bool expected)
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(reader, nameof(ShapeSpecimens), methodName);
        MemberSignatureShapeResult legacy = MemberSignatureShapeCodec.Decode(legacyText);

        Assert.True(legacy.IsAvailable, legacy.UnavailableReason);
        Assert.Equal(
            expected,
            MetadataMemberSignatureShape.LegacyShapeCanDescribe(
                reader,
                handle,
                legacy.Shape!));
    }

    [Fact]
    public void LegacyCompatibility_RefusesGenericNameAmplificationBeforeLargeAllocation()
    {
        byte[] image = BuildGenericMethodWithLongNames(
            genericParameterCount: 5,
            genericParameterNameLength: 900_000);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        var legacyShape = new MemberSignatureShape(
            5,
            SignatureShapeList<MemberParameterSignatureShape>.Empty,
            ConversionReturnType: null);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        bool describes = MetadataMemberSignatureShape.LegacyShapeCanDescribe(
            reader,
            MetadataTokens.MethodDefinitionHandle(1),
            legacyShape);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.False(describes);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Legacy generic-name rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void MetadataAdapter_RefusesShapeBeyondTransportDepthLimit()
    {
        using var stream = File.OpenRead(typeof(MetadataMemberSignatureShapeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = FindMethod(
            reader,
            nameof(ShapeSpecimens),
            nameof(ShapeSpecimens.DeepArray));

        MemberSignatureShapeResult result =
            MetadataMemberSignatureShape.Create(reader, handle);

        Assert.False(result.IsAvailable);
        Assert.Contains("transport safety limits", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesCyclicTypeReference()
    {
        TypeReferenceHandle reference = default;
        byte[] image = BuildOneParameterImage(
            (_, type) => type.Type(reference, isValueType: false),
            metadata =>
            {
                reference = metadata.AddTypeReference(
                    MetadataTokens.TypeReferenceHandle(1),
                    metadata.GetOrAddString("Cyclic"),
                    metadata.GetOrAddString("Self"));
            });

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("repeats handle", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesCyclicTypeDefinition()
    {
        TypeDefinitionHandle first = default;
        byte[] image = BuildOneParameterImage(
            (_, type) => type.Type(first, isValueType: false),
            metadata =>
            {
                first = AddTypeDefinition(metadata, "First");
                TypeDefinitionHandle second = AddTypeDefinition(metadata, "Second");
                metadata.AddNestedType(first, second);
                metadata.AddNestedType(second, first);
            });

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("repeats handle", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesOverflowingNestedGenericArity()
    {
        TypeReferenceHandle inner = default;
        byte[] image = BuildOneParameterImage(
            (_, type) => type
                .GenericInstantiation(inner, genericArgumentCount: 1, isValueType: false)
                .AddArgument()
                .Int32(),
            metadata =>
            {
                AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Referenced"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    0,
                    default);
                TypeReferenceHandle outer = metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Outer`2147483647"));
                inner = metadata.AddTypeReference(
                    outer,
                    default,
                    metadata.GetOrAddString("Inner`2147483647"));
            });

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("generic arity", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesErasedModifierAmplificationBeforeLargeAllocation()
    {
        byte[] image = BuildModifiedParameterImage(
            modifierCount: 500,
            modifierNameLength: 900_000);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MemberSignatureShapeResult result =
            MetadataMemberSignatureShape.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(1));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.False(result.IsAvailable);
        Assert.Contains("cumulative metadata work budget", result.UnavailableReason);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Erased custom-modifier rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void MetadataAdapter_AllowsBoundedErasedModifiers()
    {
        byte[] image = BuildModifiedParameterImage(
            modifierCount: 64,
            modifierNameLength: 128);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new PrimitiveTypeSignatureShape("System.Int32"),
            result.Shape!.Parameters.Single().Type);
    }

    [Fact]
    public void MetadataAdapter_RefusesGenericHeaderWithoutOwnedRows()
    {
        byte[] image = BuildGenericMethodImage(
            signatureGenericCount: 1,
            genericParameterIndices: []);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("generic-parameter rows", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesZeroArityGenericHeader()
    {
        byte[] image = BuildRawSignatureImage(
            [0x10, 0x00, 0x00, 0x01]);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("generic signature", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesMethodGenericPositionOutsideHeaderArity()
    {
        byte[] image = BuildRawSignatureImage(
            [0x00, 0x01, 0x01, 0x1e, 0x00]);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("generic signature", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesNonContiguousGenericParameterRows()
    {
        byte[] image = BuildGenericMethodImage(
            signatureGenericCount: 2,
            genericParameterIndices: [0, 2]);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("generic-parameter rows", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_AllowsConsistentGenericParameterRows()
    {
        byte[] image = BuildGenericMethodImage(
            signatureGenericCount: 2,
            genericParameterIndices: [0, 1]);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(2, result.Shape!.GenericArity);
    }

    [Fact]
    public void MetadataAdapter_RefusesMissingDeclaringTypeGenericRows()
    {
        byte[] image = BuildTypeGenericMethodImage(addGenericParameter: false);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("declaring TypeDef", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_AllowsConsistentDeclaringTypeGenericRows()
    {
        byte[] image = BuildTypeGenericMethodImage(addGenericParameter: true);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Type,
                0),
            result.Shape!.Parameters.Single().Type);
    }

    [Fact]
    public void MetadataAdapter_AllowsCumulativeNestedTypeGenericRows()
    {
        byte[] image = BuildNestedTypeGenericMethodImage();

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Type,
                1),
            result.Shape!.Parameters.Single().Type);
    }

    [Theory]
    [InlineData("Foo`+1")]
    [InlineData("Foo`01")]
    [InlineData("Foo` 1")]
    [InlineData("Foo`1`1")]
    public void MetadataAdapter_RefusesNoncanonicalTypeReferenceArity(
        string metadataName)
    {
        byte[] image = BuildGenericTypeReferenceImage(metadataName);

        MemberSignatureShapeResult result = ReadCraftedShape(image);

        Assert.False(result.IsAvailable);
        Assert.Contains("noncanonical generic arity", result.UnavailableReason);
    }

    [Fact]
    public void MetadataAdapter_RefusesUnrepresentableFunctionPointerHeaders()
    {
        byte[][] signatures =
        [
            [0x00, 0x01, 0x01, 0x1b, 0x20, 0x00, 0x01],
            [0x00, 0x01, 0x01, 0x1b, 0x60, 0x00, 0x01],
            [0x00, 0x01, 0x01, 0x1b, 0x10, 0x00, 0x00, 0x01],
            [0x00, 0x01, 0x01, 0x1b, 0x05, 0x00, 0x01],
        ];

        foreach (byte[] signature in signatures)
        {
            MemberSignatureShapeResult result =
                ReadCraftedShape(BuildRawSignatureImage(signature));

            Assert.False(result.IsAvailable);
            Assert.Contains(
                "unrepresentable header attributes",
                result.UnavailableReason);
        }
    }

    [Fact]
    public void LegacyCompatibility_RefusesCyclicDeclaringType()
    {
        byte[] image = BuildMethodOnCyclicDeclaringType();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        MemberSignatureShapeResult legacy = MemberSignatureShapeCodec.Decode("`0(int)");

        Assert.True(legacy.IsAvailable, legacy.UnavailableReason);
        Assert.False(MetadataMemberSignatureShape.LegacyShapeCanDescribe(
            reader,
            MetadataTokens.MethodDefinitionHandle(1),
            legacy.Shape!));
    }

    static MemberSignatureShapeResult ReadCraftedShape(byte[] image)
    {
        using var peReader = new PEReader(ImmutableArray.Create(image));
        return MetadataMemberSignatureShape.Create(
            peReader.GetMetadataReader(),
            MetadataTokens.MethodDefinitionHandle(1));
    }

    static byte[] BuildOneParameterImage(
        Action<MetadataBuilder, SignatureTypeEncoder> encodeParameter,
        Action<MetadataBuilder> prepare)
    {
        var metadata = CreateMetadataBuilder();
        prepare(metadata);

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(SignatureCallingConvention.Default, genericParameterCount: 0, isInstanceMethod: false)
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters => encodeParameter(metadata, parameters.AddParameter().Type()));
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            signatureHandle,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        return Serialize(metadata);
    }

    static byte[] BuildModifiedParameterImage(
        int modifierCount,
        int modifierNameLength)
    {
        var metadata = CreateMetadataBuilder();
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Referenced"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            default);
        TypeReferenceHandle modifier = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(new string('M', modifierNameLength)));
        int modifierCodedIndex =
            (MetadataTokens.GetRowNumber(modifier) << 2) | 1;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        for (int i = 0; i < modifierCount; i++)
        {
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(modifierCodedIndex);
        }
        signature.WriteByte(0x08);
        AddMethodAndType(metadata, metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    static byte[] BuildGenericMethodImage(
        int signatureGenericCount,
        int[] genericParameterIndices)
    {
        var metadata = CreateMetadataBuilder();
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteCompressedInteger(signatureGenericCount);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        MethodDefinitionHandle method =
            AddMethodAndType(metadata, metadata.GetOrAddBlob(signature));
        foreach (int index in genericParameterIndices)
        {
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{index}"),
                index);
        }
        return Serialize(metadata);
    }

    static byte[] BuildRawSignatureImage(
        byte[] signature,
        string typeName = "C")
    {
        var metadata = CreateMetadataBuilder();
        AddMethodAndType(
            metadata,
            metadata.GetOrAddBlob(signature),
            typeName);
        return Serialize(metadata);
    }

    static byte[] BuildTypeGenericMethodImage(bool addGenericParameter)
    {
        var metadata = CreateMetadataBuilder();
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x01, 0x01, 0x13, 0x00 }),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        if (addGenericParameter)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }
        return Serialize(metadata);
    }

    static byte[] BuildGenericTypeReferenceImage(string metadataName)
    {
        var metadata = CreateMetadataBuilder();
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Referenced"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(metadataName));
        AddMethodAndType(
            metadata,
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x01, 0x01, 0x15, 0x12, 0x05, 0x01, 0x08 }));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeGenericMethodImage()
    {
        var metadata = CreateMetadataBuilder();
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x01, 0x01, 0x13, 0x01 }),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("Inner`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        metadata.AddNestedType(inner, outer);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            index: 1);
        return Serialize(metadata);
    }

    static byte[] BuildGenericMethodWithLongNames(
        int genericParameterCount,
        int genericParameterNameLength)
    {
        var metadata = CreateMetadataBuilder();
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteCompressedInteger(genericParameterCount);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        MethodDefinitionHandle method =
            AddMethodAndType(metadata, metadata.GetOrAddBlob(signature));
        StringHandle name = metadata.GetOrAddString(
            new string('T', genericParameterNameLength));
        for (int i = 0; i < genericParameterCount; i++)
        {
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                name,
                i);
        }
        return Serialize(metadata);
    }

    static MethodDefinitionHandle AddMethodAndType(
        MetadataBuilder metadata,
        BlobHandle signature,
        string typeName = "C")
    {
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            signature,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        return method;
    }

    static byte[] BuildMethodOnCyclicDeclaringType()
    {
        var metadata = CreateMetadataBuilder();
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(SignatureCallingConvention.Default, genericParameterCount: 0, isInstanceMethod: false)
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        TypeDefinitionHandle first = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("First"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        TypeDefinitionHandle second = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("Second"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(first, second);
        metadata.AddNestedType(second, first);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadataBuilder()
    {
        var metadata = new MetadataBuilder();
        metadata.AddAssembly(
            metadata.GetOrAddString("Crafted"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("Crafted.dll"),
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

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        string name)
        => metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName)
    {
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }
        throw new Xunit.Sdk.XunitException($"Method '{typeName}.{methodName}' was not found.");
    }
}

public unsafe class ShapeSpecimens
{
    public void Primitive(int value) { }
    public void Nullable(int? value) { }
    public void GenericNullable<T>(T? value) where T : struct { }
    public void Arrays(int[][,] first, int[,][] second) { }
    public void Tuple((int left, string right) value) { }
    public void Tuple8((int, int, int, int, int, int, int, int) value) { }
    public void Pointer(int* value) { }
    public void FunctionPointer(delegate* unmanaged[Cdecl]<int, string> callback) { }
    public void ByRefFunctionPointer(delegate*<ref int, void> callback) { }
    public void SupplementalTupleSyntax(
        delegate* unmanaged[SuppressGCTransition]<
            (int, int, int, int, int, int, int, (short, byte)),
            void> callback)
    { }
    public void ExplicitValueTupleRest(
        delegate* unmanaged<
            ValueTuple<int, int, int, int, int, int, int, (short, byte)>,
            void> callback)
    { }
    public void LegacyNamed(IReadOnlyList<string> values) { }
    public void LegacyGeneric<T>(T value) { }
    public void DeepArray(
        int[][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][][]
            [] value)
    { }
    public static implicit operator int(ShapeSpecimens value) => 0;

    public class Outer<T>
    {
        public class Inner<U>
        {
            public void Pair<V>(T outer, U inner, V method) { }
        }
    }
}
