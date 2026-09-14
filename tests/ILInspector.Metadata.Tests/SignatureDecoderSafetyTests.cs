using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class SignatureDecoderSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_SIGNATURE_DECODER_WORKER";

    [Fact]
    public void SelfReferentialTypeSpec_IsContainedInChildProcess()
        => RunWorker(nameof(SelfReferentialTypeSpecWorker));

    [Fact]
    public void DeepTypeSpec_IsContainedInChildProcess()
        => RunWorker(nameof(DeepTypeSpecWorker));

    [Fact]
    public void DeepMethodSignatureGateway_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureGatewayWorker));

    [Fact]
    public void CyclicTypeSpec_ThroughApiSurfaceExtractor_IsContainedInChildProcess()
        => RunWorker(nameof(CyclicTypeSpecThroughApiSurfaceExtractorWorker));

    [Fact]
    public void DeepFieldSignature_ThroughApiSurfaceExtractor_IsContainedInChildProcess()
        => RunWorker(nameof(DeepFieldSignatureThroughApiSurfaceExtractorWorker));

    [Fact]
    public void DeepMethodSignature_ThroughApiSurfaceExtractor_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureThroughApiSurfaceExtractorWorker));

    [Fact]
    public void DeepTypeSpec_ThroughCanonicalIl_IsContainedInChildProcess()
        => RunWorker(nameof(DeepTypeSpecThroughCanonicalIlWorker));

    [Fact]
    public void DeepMethodSignature_ThroughPointerDetector_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureThroughPointerDetectorWorker));

    [Fact]
    public void DeepMethodSignature_ThroughAnchorProvider_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureThroughAnchorProviderWorker));

    [Fact]
    public void DeepMethodSignature_ThroughSpellability_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureThroughSpellabilityWorker));

    [Fact]
    public void DeepEnumField_ThroughAttributeDecoder_IsContainedInChildProcess()
        => RunWorker(nameof(DeepEnumFieldThroughAttributeDecoderWorker));

    [Fact]
    public void ValidPointerSignature_IsDetectedWithoutDegradation()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        signature.WriteByte(0x0f); // PTR return type
        signature.WriteByte(0x08); // I4
        var image = BuildSurfacePe(fieldSignature: null, methodSignature: signature);
        using var peReader = new PEReader(new MemoryStream(image));

        var flags = AssemblyDetailScanner.ScanPresenceFlags(peReader);

        Assert.True(flags.HasUnsafeCode);
        Assert.Null(flags.UnsafeSignatureDecodeStatus);
    }

    [Fact]
    public void PointerInCustomModifier_IsDetectedWithoutDegradation()
    {
        var typeSpecification = new BlobBuilder();
        typeSpecification.WriteByte(0x0f); // PTR
        typeSpecification.WriteByte(0x08); // I4

        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        signature.WriteByte(0x20); // CMOD_OPT
        signature.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
        signature.WriteByte(0x01); // VOID
        var image = BuildSurfacePe(
            fieldSignature: null,
            methodSignature: signature,
            typeSpecification: typeSpecification);
        using var peReader = new PEReader(new MemoryStream(image));

        var flags = AssemblyDetailScanner.ScanPresenceFlags(peReader);

        Assert.True(flags.HasUnsafeCode);
        Assert.Null(flags.UnsafeSignatureDecodeStatus);
    }

    [Fact]
    public void SignatureBlobGuard_OldAssemblyIdentity_IsForwarded()
        => Assert.Equal(
            typeof(SignatureBlobGuard),
            Type.GetType("ILInspector.Metadata.SignatureBlobGuard, ILInspector.Metadata"));

    [Fact]
    public void EmptyTypeSpec_DisposesGuardScopeAfterDecodeFailure()
    {
        var reader = BuildTypeSpec(_ => { });
        var handle = MetadataTokens.TypeSpecificationHandle(1);

        for (int i = 0; i <= 256; i++)
        {
            Assert.Throws<BadImageFormatException>(
                () => TypeResolver.GetTypeNameFromSpecification(reader, handle));
        }
    }

    [Fact]
    public void CopiedTypeSpecScope_CannotExitTwice()
    {
        var reader = BuildTypeSpec(signature => signature.WriteByte(0x08));
        var handle = MetadataTokens.TypeSpecificationHandle(1);

        Assert.True(TypeSpecGuard.TryEnter(reader, handle, out var scope));
        var copy = scope;
        scope.Dispose();
        copy.Dispose();

        AssertDepthLimit(reader, handle, depth: 0);
    }

    static void AssertDepthLimit(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        int depth)
    {
        if (depth == 256)
        {
            Assert.False(TypeSpecGuard.TryEnter(reader, handle, out _));
            return;
        }

        Assert.True(TypeSpecGuard.TryEnter(reader, handle, out var scope));
        using (scope)
        {
            AssertDepthLimit(reader, handle, depth + 1);
        }
    }

    [Fact]
    public void SelfReferentialTypeSpecWorker()
    {
        if (!IsSelectedWorker(nameof(SelfReferentialTypeSpecWorker)))
            return;

        var reader = BuildTypeSpec(signature =>
        {
            signature.WriteByte(0x1f); // CMOD_REQD
            signature.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
            signature.WriteByte(0x08); // I4
        });

        AssertRejected(
            TypeResolver.DecodeTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)),
            SignatureDecodeRejectionKind.TypeSpecificationBudget);
        var strict = Assert.IsType<MetadataTypeNameResult.Rejected>(
            TypeResolver.ResolveTypeName(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)));
        Assert.Equal(
            MetadataTypeNameFailureMechanism.TypeSpecification,
            strict.Failure.Mechanism);
        Assert.Equal(
            SignatureDecodeRejectionKind.TypeSpecificationBudget,
            strict.Failure.SignatureKind);
    }

    [Fact]
    public void DeepTypeSpecWorker()
    {
        if (!IsSelectedWorker(nameof(DeepTypeSpecWorker)))
            return;

        var reader = BuildTypeSpec(signature =>
        {
            for (int i = 0; i < 100_000; i++)
                signature.WriteByte(0x1d); // SZARRAY
            signature.WriteByte(0x08);     // I4
        });

        AssertRejected(
            TypeResolver.DecodeTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)),
            SignatureDecodeRejectionKind.TypeSpecificationBudget);
    }

    [Fact]
    public void DeepMethodSignatureGatewayWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureGatewayWorker)))
            return;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY return type
        signature.WriteByte(0x08);     // I4

        MethodDefinitionHandle methodHandle = default;
        TypeDefinitionHandle typeHandle = default;
        var reader = BuildAssembly(metadata =>
        {
            methodHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                methodHandle);
        });

        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetMethodReturnType(
                reader,
                reader.GetTypeDefinition(typeHandle),
                reader.GetMethodDefinition(methodHandle)));
    }

    [Fact]
    public void WideTypeSpec_AboveLegacyPerBlobCap_IsDecoded()
    {
        const int argumentCount = 1_500;
        var reader = BuildTypeSpec(signature =>
        {
            signature.WriteByte(0x15); // GENERICINST
            signature.WriteByte(0x12); // CLASS
            signature.WriteByte(0x04); // TypeDef row 1
            signature.WriteCompressedInteger(argumentCount);
            for (int i = 0; i < argumentCount; i++)
                signature.WriteByte(0x08); // I4
        });

        var handle = MetadataTokens.TypeSpecificationHandle(1);
        var decoded = Assert.IsType<SignatureDecodeResult<string>.Decoded>(
            TypeResolver.DecodeTypeNameFromSpecification(
                reader,
                handle));
        var gateway = Assert.IsType<SignatureDecodeResult<string>.Decoded>(
            GuardedSignatureText.TypeSpecText(reader, handle, context: null));

        Assert.StartsWith("<Module><int, int", decoded.Value, StringComparison.Ordinal);
        Assert.Equal(decoded.Value, gateway.Value);
        Assert.Equal(
            decoded.Value,
            TypeResolver.GetTypeNameFromSpecification(reader, handle));
        Assert.True(
            reader.GetBlobReader(
                reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(1)).Signature)
                .Length > 1_024);
    }

    [Fact]
    public void SelfTypeSignature_RejectsCyclicDeclaringType()
    {
        TypeDefinitionHandle typeHandle = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Loop"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(typeHandle, typeHandle);
        });

        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.SelfTypeSignature(
                reader,
                reader.GetTypeDefinition(typeHandle)));
    }

    [Fact]
    public void SelfTypeSignature_RejectsOversizedRootNameBeforeMaterialization()
    {
        TypeDefinitionHandle typeHandle = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString(new string('X', 100_000)),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        });
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.SelfTypeSignature(
                reader,
                reader.GetTypeDefinition(typeHandle)));

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 64 * 1024,
            $"Oversized root type name allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void SignatureDecoder_ReusesEmptyNameWithoutRetentionCollision()
    {
        TypeReferenceHandle first = default;
        TypeReferenceHandle second = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            first = metadata.AddTypeReference(
                default,
                default,
                default);
            second = metadata.AddTypeReference(
                default,
                default,
                default);
        });
        var decoder = new SignatureDecoder();

        Assert.Equal(
            "",
            decoder.GetTypeFromReference(reader, first, 0));
        Assert.Equal(
            "",
            decoder.GetTypeFromReference(reader, second, 0));
    }

    [Fact]
    public void SignatureDecoder_DoesNotRetainAcceptedNamesPastCacheBudget()
    {
        int count =
            SignatureDecoder.MaxAcceptedNameCacheCharacters
                / ((MetadataSafetyPolicy.MaxTypeNameCharacters - 8) * 2)
            + 2;
        TypeReferenceHandle[] handles = new TypeReferenceHandle[count];
        MetadataReader reader = BuildAssembly(metadata =>
        {
            StringHandle sharedName = metadata.GetOrAddString(
                new string(
                    'A',
                    MetadataSafetyPolicy.MaxTypeNameCharacters - 8));
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = metadata.AddTypeReference(
                    default,
                    default,
                    sharedName);
            }
        });

        var decoder = new SignatureDecoder();
        (WeakReference<string> first, WeakReference<string> last) =
            DecodeAcceptedNames(decoder, reader, handles);
        ForceCollection();

        Assert.True(first.TryGetTarget(out _));
        Assert.False(last.TryGetTarget(out _));
        GC.KeepAlive(decoder);
        GC.KeepAlive(reader);
    }

    [Fact]
    public void SignatureDecoder_DoesNotRetainRejectionsPastCacheBudget()
    {
        int count = SignatureDecoder.MaxAcceptedNameCacheEntries + 1;
        TypeReferenceHandle[] handles =
            new TypeReferenceHandle[count];
        MetadataReader reader = BuildAssembly(metadata =>
        {
            StringHandle sharedName = metadata.GetOrAddString(
                new string(
                    'A',
                    MetadataSafetyPolicy.MaxTypeNameCharacters + 1));
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = metadata.AddTypeReference(
                    default,
                    default,
                    sharedName);
            }
        });
        var decoder = new SignatureDecoder();

        foreach (TypeReferenceHandle handle in handles)
        {
            AssertRejected(
                SignatureDecoder.Decode(
                    () => decoder.GetTypeFromReference(
                        reader,
                        handle,
                        rawTypeKind: 0)),
                SignatureDecodeRejectionKind.NameBudget);
        }

        Assert.True(
            decoder.GetCachedEntryCount(reader)
                <= SignatureDecoder.MaxAcceptedNameCacheEntries);
        Assert.True(
            decoder.GetCachedEntryCount(reader) < handles.Length);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static (WeakReference<string> First, WeakReference<string> Last)
        DecodeAcceptedNames(
            SignatureDecoder decoder,
            MetadataReader reader,
            IReadOnlyList<TypeReferenceHandle> handles)
    {
        WeakReference<string>? first = null;
        WeakReference<string>? last = null;
        for (int i = 0; i < handles.Count; i++)
        {
            string name = decoder.GetTypeFromReference(
                reader,
                handles[i],
                rawTypeKind: 0);
            if (i == 0)
                first = new(name);
            if (i == handles.Count - 1)
                last = new(name);
        }
        return (first!, last!);
    }

    static void ForceCollection()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    [Fact]
    public void SignatureDecoder_RejectsExactNameBeforeOversizedSegmentDecode()
    {
        TypeReferenceHandle leaf = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            StringHandle sharedName =
                metadata.GetOrAddString(new string('A', 1024 * 1024));
            EntityHandle scope = default;
            for (int i = 0;
                i < MetadataSafetyPolicy.MaxRelationshipNodes;
                i++)
            {
                leaf = metadata.AddTypeReference(
                    scope,
                    default,
                    sharedName);
                scope = leaf;
            }
        });
        int decoderWork = 0;
        var decoder = new SignatureDecoder(length => decoderWork += length);

        long before = GC.GetAllocatedBytesForCurrentThread();
        AssertRejected(
            SignatureDecoder.Decode(
                () => decoder.GetTypeFromReference(reader, leaf, 0)),
            SignatureDecodeRejectionKind.NameBudget);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < 1024 * 1024,
            $"Oversized exact-name rejection allocated {allocated:N0} bytes.");
        int firstDecoderWork = decoderWork;
        long secondDecodeBefore =
            GC.GetAllocatedBytesForCurrentThread();
        AssertRejected(
            SignatureDecoder.Decode(
                () => decoder.GetTypeFromReference(reader, leaf, 0)),
            SignatureDecodeRejectionKind.NameBudget);
        long secondDecodeAllocation =
            GC.GetAllocatedBytesForCurrentThread()
            - secondDecodeBefore;
        Assert.True(decoderWork > firstDecoderWork);
        Assert.True(
            secondDecodeAllocation < 64 * 1024,
            $"Cached rejection allocated {secondDecodeAllocation:N0} bytes.");

        int providerWork = 0;
        var provider = new TypeNodeProvider(
            beforeMaterialize: length => providerWork += length);
        Assert.IsType<DegradedTypeNode>(
            provider.GetTypeFromReference(reader, leaf, 0));
        int firstProviderWork = providerWork;
        long secondProviderBefore =
            GC.GetAllocatedBytesForCurrentThread();
        Assert.IsType<DegradedTypeNode>(
            provider.GetTypeFromReference(reader, leaf, 0));
        long secondProviderAllocation =
            GC.GetAllocatedBytesForCurrentThread()
            - secondProviderBefore;
        Assert.True(providerWork > firstProviderWork);
        Assert.True(
            secondProviderAllocation < 64 * 1024,
            $"Cached node rejection allocated {secondProviderAllocation:N0} bytes.");
    }

    [Fact]
    public void SignatureDecoder_AcceptsUtf8ExpansionWithinCharacterBudget()
    {
        string name = new('あ', 2000);
        TypeReferenceHandle handle = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            handle = metadata.AddTypeReference(
                default,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(name));
        });
        var decoder = new SignatureDecoder();

        var decoded = Assert.IsType<SignatureDecodeResult<string>.Decoded>(
            SignatureDecoder.Decode(
                () => decoder.GetTypeFromReference(reader, handle, 0)));

        Assert.Equal($"N.{name}", decoded.Value);
    }

    [Fact]
    public void TypeSpec_AboveCumulativeBudget_IsRejected()
    {
        var reader = BuildTypeSpec(signature =>
        {
            signature.WriteByte(0x15); // GENERICINST
            signature.WriteByte(0x12); // CLASS
            signature.WriteByte(0x04); // TypeDef row 1
            signature.WriteCompressedInteger(TypeSpecGuard.MaxCumulativeBytes);
            for (int i = 0; i < TypeSpecGuard.MaxCumulativeBytes; i++)
                signature.WriteByte(0x08); // I4
        });

        var handle = MetadataTokens.TypeSpecificationHandle(1);
        AssertRejected(
            TypeResolver.DecodeTypeNameFromSpecification(
                reader,
                handle),
            SignatureDecodeRejectionKind.TypeSpecificationBudget);
        AssertRejected(
            GuardedSignatureText.TypeSpecText(reader, handle, context: null),
            SignatureDecodeRejectionKind.TypeSpecificationBudget);
        Assert.Throws<BadImageFormatException>(
            () => TypeResolver.GetTypeNameFromSpecification(reader, handle));
    }

    [Fact]
    public void MalformedTypeSpecHandle_IsRejected()
    {
        var reader = BuildTypeSpec(signature =>
        {
            signature.WriteByte(0x12); // CLASS
            signature.WriteByte(0x05); // TypeRef row 1, which does not exist
        });

        AssertRejected(
            TypeResolver.DecodeTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)),
            SignatureDecodeRejectionKind.MalformedMetadata);
        AssertRejected(
            GuardedSignatureText.TypeSpecText(
                reader,
                MetadataTokens.TypeSpecificationHandle(1),
                context: null),
            SignatureDecodeRejectionKind.MalformedMetadata);
    }

    [Fact]
    public void MalformedSignatureBlobHandle_IsRejectedBeforeDecode()
    {
        var reader = BuildAssembly(_ => { });
        bool decodeCalled = false;

        var result = GuardedSignatureDecoder.Decode(
            reader,
            MetadataTokens.BlobHandle(0x1000),
            SignatureBlobGuard.Kind.Field,
            () =>
            {
                decodeCalled = true;
                return "int";
            });

        AssertRejected(result, SignatureDecodeRejectionKind.MalformedMetadata);
        Assert.False(decodeCalled);
    }

    [Fact]
    public void GuardedGateways_RejectEveryUnsafeSignatureKind()
    {
        MethodDefinitionHandle methodHandle = default;
        FieldDefinitionHandle fieldHandle = default;
        PropertyDefinitionHandle propertyHandle = default;
        MethodSpecificationHandle methodSpecHandle = default;
        TypeSpecificationHandle typeSpecHandle = default;
        var reader = BuildAssembly(metadata =>
        {
            methodHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(DeepMethodSignature()),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));

            var fieldSignature = new BlobBuilder();
            fieldSignature.WriteByte(0x06); // FIELD
            WriteDeepType(fieldSignature);
            fieldHandle = metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("F"),
                metadata.GetOrAddBlob(fieldSignature));

            var propertySignature = new BlobBuilder();
            propertySignature.WriteByte(0x08); // PROPERTY
            propertySignature.WriteByte(0x00); // zero parameters
            WriteDeepType(propertySignature);
            propertyHandle = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("P"),
                metadata.GetOrAddBlob(propertySignature));

            var methodSpecSignature = new BlobBuilder();
            methodSpecSignature.WriteByte(0x0a); // GENERICINST
            methodSpecSignature.WriteByte(0x01); // one argument
            WriteDeepType(methodSpecSignature);
            methodSpecHandle = metadata.AddMethodSpecification(
                methodHandle,
                metadata.GetOrAddBlob(methodSpecSignature));

            var typeSpecSignature = new BlobBuilder();
            for (int i = 0; i < 600; i++)
                typeSpecSignature.WriteByte(0x1d); // SZARRAY
            typeSpecSignature.WriteByte(0x08);     // I4
            typeSpecHandle = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        });

        AssertRejected(
            GuardedSignatureText.MethodText(
                reader,
                reader.GetMethodDefinition(methodHandle),
                context: null),
            SignatureDecodeRejectionKind.UnsafeStructure);
        AssertRejected(
            GuardedSignatureText.FieldText(
                reader,
                reader.GetFieldDefinition(fieldHandle),
                context: null),
            SignatureDecodeRejectionKind.UnsafeStructure);
        AssertRejected(
            GuardedSignatureText.PropertyText(
                reader,
                reader.GetPropertyDefinition(propertyHandle),
                context: null),
            SignatureDecodeRejectionKind.UnsafeStructure);
        AssertRejected(
            GuardedSignatureText.MethodSpecTypeArgs(
                reader,
                reader.GetMethodSpecification(methodSpecHandle),
                context: null),
            SignatureDecodeRejectionKind.UnsafeStructure);
        AssertRejected(
            GuardedSignatureText.TypeSpecText(reader, typeSpecHandle, context: null),
            SignatureDecodeRejectionKind.UnsafeStructure);
    }

    [Fact]
    public void ExtensionPropertyAnchor_RejectsDegradedPropertySignature()
    {
        TypeDefinitionHandle typeHandle = default;
        MethodDefinitionHandle markerHandle = default;
        PropertyDefinitionHandle propertyHandle = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            var markerSignature = new BlobBuilder();
            markerSignature.WriteByte(0x00);
            markerSignature.WriteByte(0x01);
            markerSignature.WriteByte(0x01);
            markerSignature.WriteByte(0x08);
            markerHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("get_P"),
                metadata.GetOrAddBlob(markerSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));

            var propertySignature = new BlobBuilder();
            propertySignature.WriteByte(0x08);
            propertySignature.WriteByte(0x00);
            WriteDeepType(propertySignature);
            propertyHandle = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("P"),
                metadata.GetOrAddBlob(propertySignature));

            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Extensions"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                markerHandle);
        });

        Assert.Throws<BadImageFormatException>(
            () => ApiMemberIdentity.CreateExtensionPropertyDeclarationAnchorInfo(
                reader,
                typeHandle,
                reader.GetTypeDefinition(typeHandle),
                reader.GetMethodDefinition(markerHandle),
                reader.GetPropertyDefinition(propertyHandle)));
    }

    [Fact]
    public void NestedTypeSpecRejection_RejectsContainingMethod()
    {
        MethodDefinitionHandle methodHandle = default;
        var reader = BuildAssembly(metadata =>
        {
            var cyclicTypeSpec = new BlobBuilder();
            cyclicTypeSpec.WriteByte(0x1f); // CMOD_REQD
            cyclicTypeSpec.WriteByte(0x06); // TypeSpec row 1
            cyclicTypeSpec.WriteByte(0x08); // I4
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(cyclicTypeSpec));

            var methodSignature = new BlobBuilder();
            methodSignature.WriteByte(0x00); // default method signature
            methodSignature.WriteByte(0x00); // zero parameters
            methodSignature.WriteByte(0x1f); // CMOD_REQD
            methodSignature.WriteByte(0x06); // TypeSpec row 1
            methodSignature.WriteByte(0x08); // I4
            methodHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(methodSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        });

        AssertRejected(
            GuardedSignatureText.MethodText(
                reader,
                reader.GetMethodDefinition(methodHandle),
                context: null),
            SignatureDecodeRejectionKind.TypeSpecificationBudget);
    }

    [Fact]
    public void CyclicTypeSpecThroughApiSurfaceExtractorWorker()
    {
        if (!IsSelectedWorker(nameof(CyclicTypeSpecThroughApiSurfaceExtractorWorker)))
            return;

        var image = BuildApiSurfaceTypeSpecCycle();
        using var peReader = new PEReader(new MemoryStream(image));
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var member = Assert.Single(Assert.Single(surface.Types).Members);
        Assert.Equal(SignatureDecodeStatus.Degraded, member.SignatureDecodeStatus);
        Assert.Equal(
            SignatureDecodeStatus.Degraded,
            ExtractDeclarationQueryMember(peReader).SignatureDecodeStatus);
    }

    [Fact]
    public void DeepFieldSignatureThroughApiSurfaceExtractorWorker()
    {
        if (!IsSelectedWorker(nameof(DeepFieldSignatureThroughApiSurfaceExtractorWorker)))
            return;

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06); // field signature
        for (int i = 0; i < 100_000; i++)
            fieldSignature.WriteByte(0x1d); // SZARRAY
        fieldSignature.WriteByte(0x08);     // I4

        var image = BuildSurfacePe(fieldSignature: fieldSignature, methodSignature: null);
        using var peReader = new PEReader(new MemoryStream(image));
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var member = Assert.Single(Assert.Single(surface.Types).Members);
        Assert.Equal(SignatureDecodeStatus.Degraded, member.SignatureDecodeStatus);
        Assert.Equal(
            SignatureDecodeStatus.Degraded,
            ExtractDeclarationQueryMember(peReader).SignatureDecodeStatus);
    }

    [Fact]
    public void DeepMethodSignatureThroughApiSurfaceExtractorWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureThroughApiSurfaceExtractorWorker)))
            return;

        var image = BuildSurfacePe(
            fieldSignature: null,
            methodSignature: DeepMethodSignature());
        using var peReader = new PEReader(new MemoryStream(image));
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var member = Assert.Single(Assert.Single(surface.Types).Members);
        Assert.Equal(SignatureDecodeStatus.Degraded, member.SignatureDecodeStatus);
        Assert.Equal(
            SignatureDecodeStatus.Degraded,
            ExtractDeclarationQueryMember(peReader).SignatureDecodeStatus);
    }

    [Fact]
    public void DeepTypeSpecThroughCanonicalIlWorker()
    {
        if (!IsSelectedWorker(nameof(DeepTypeSpecThroughCanonicalIlWorker)))
            return;

        var reader = BuildTypeSpec(signature =>
        {
            for (int i = 0; i < 100_000; i++)
                signature.WriteByte(0x1d); // SZARRAY
            signature.WriteByte(0x08);     // I4
        });

        Assert.Equal(
            "object",
            CanonicalIL.ResolveTypeHandle(reader, MetadataTokens.TypeSpecificationHandle(1)));
    }

    [Fact]
    public void DeepMethodSignatureThroughPointerDetectorWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureThroughPointerDetectorWorker)))
            return;

        var image = BuildSurfacePe(fieldSignature: null, methodSignature: DeepPointerMethodSignature());
        using var peReader = new PEReader(new MemoryStream(image));
        var flags = AssemblyDetailScanner.ScanPresenceFlags(peReader);

        Assert.False(flags.HasUnsafeCode);
        Assert.Equal(SignatureDecodeStatus.Degraded, flags.UnsafeSignatureDecodeStatus);
    }

    [Fact]
    public void DeepMethodSignatureThroughAnchorProviderWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureThroughAnchorProviderWorker)))
            return;

        var (reader, typeHandle, methodHandle) = BuildTypeWithMethod(DeepMethodSignature());
        Assert.Throws<BadImageFormatException>(
            () => ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                typeHandle,
                reader.GetMethodDefinition(methodHandle)));
    }

    [Fact]
    public void DeepMethodSignatureThroughSpellabilityWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureThroughSpellabilityWorker)))
            return;

        var (reader, typeHandle, methodHandle) = BuildTypeWithMethod(DeepMethodSignature());
        var method = reader.GetMethodDefinition(methodHandle);
        var spellability = new SignatureSpellability(new NullReferenceResolver());
        var result = spellability.InspectMethod(
            reader, method, GenericContext.ForMethod(reader, reader.GetTypeDefinition(typeHandle), method));

        Assert.False(result.CanSpell);
        Assert.Equal(SignatureDecodeStatus.Degraded, result.DecodeStatus);
    }

    [Fact]
    public void DeepEnumFieldThroughAttributeDecoderWorker()
    {
        if (!IsSelectedWorker(nameof(DeepEnumFieldThroughAttributeDecoderWorker)))
            return;

        // AttributeDecoder.TryDecode -> CustomAttribute.DecodeValue reads the
        // ctor's enum-typed argument, and SRM resolves the enum's size by
        // decoding the enum's first instance-field signature via
        // ArgTypeProvider.GetUnderlyingEnumType. An over-deep enum field blob
        // recurses on the native stack there before any provider callback, so
        // the prescan-and-degrade guard is the only thing between this and an
        // uncatchable StackOverflow. This worker crashes the child process
        // (nonzero exit) if that guard regresses in strength — the census only
        // pins its shape.
        var image = BuildDeepEnumAttributePe();
        using var peReader = new PEReader(new MemoryStream(image));
        var reader = peReader.GetMetadataReader();
        var attribute = reader.GetCustomAttribute(reader.CustomAttributes.Single());

        var decoded = AttributeDecoder.TryDecode(reader, attribute);

        // Containment also means graceful degrade: the guard trips, the enum is
        // read as Int32, and decoding completes with the single fixed argument
        // rather than throwing.
        Assert.NotNull(decoded);
        Assert.Single(decoded!.Value.FixedArguments);
    }

    static bool IsSelectedWorker(string methodName)
        => Environment.GetEnvironmentVariable(WorkerVariable) == methodName;

    static void RunWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(SignatureDecoderSafetyTests).Assembly.Location);
        startInfo.ArgumentList.Add("--filter-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[WorkerVariable] = workerMethod;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
    }

    static MetadataReader BuildTypeSpec(Action<BlobBuilder> writeSignature)
    {
        var signature = new BlobBuilder();
        writeSignature(signature);
        return BuildAssembly(metadata => metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature)));
    }

    static BlobBuilder DeepMethodSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY return type
        signature.WriteByte(0x08);     // I4
        return signature;
    }

    static BlobBuilder DeepPointerMethodSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY return type
        signature.WriteByte(0x0f);     // PTR
        signature.WriteByte(0x08);     // I4
        return signature;
    }

    static void WriteDeepType(BlobBuilder signature)
    {
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY
        signature.WriteByte(0x08);     // I4
    }

    static void AssertRejected<T>(
        SignatureDecodeResult<T> result,
        SignatureDecodeRejectionKind kind)
        where T : notnull
    {
        var rejected = Assert.IsType<SignatureDecodeResult<T>.Rejected>(result);
        Assert.Equal(kind, rejected.Rejection.Kind);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Rejection.Detail));
        Assert.False(result.TryGetValue(out _));
        var exception = Assert.Throws<BadImageFormatException>(
            () => result.GetValueOrThrow());
        Assert.Contains(rejected.Rejection.Detail, exception.Message, StringComparison.Ordinal);
    }

    static (MetadataReader Reader, TypeDefinitionHandle TypeHandle, MethodDefinitionHandle MethodHandle)
        BuildTypeWithMethod(BlobBuilder methodSignature)
    {
        MethodDefinitionHandle methodHandle = default;
        TypeDefinitionHandle typeHandle = default;
        var reader = BuildAssembly(metadata =>
        {
            methodHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(methodSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                methodHandle);
        });
        return (reader, typeHandle, methodHandle);
    }

    /// <summary>
    /// Builds a minimal PE image exposing a public type <c>N.C</c> with a single
    /// static field and/or method carrying the supplied signature blob, so
    /// PE-level scanners (ApiSurfaceExtractor, AssemblyDetailScanner) reach the
    /// crafted signature through their real entry points.
    /// </summary>
    static byte[] BuildSurfacePe(
        BlobBuilder? fieldSignature,
        BlobBuilder? methodSignature,
        BlobBuilder? typeSpecification = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        if (typeSpecification is not null)
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecification));

        if (fieldSignature is not null)
        {
            metadata.AddFieldDefinition(
                FieldAttributes.Public | FieldAttributes.Static,
                metadata.GetOrAddString("F"),
                metadata.GetOrAddBlob(fieldSignature));
        }

        if (methodSignature is not null)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(methodSignature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ApiMember ExtractDeclarationQueryMember(PEReader peReader)
    {
        var reader = peReader.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Last();
        return Assert.Single(
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true).Members);
    }

    sealed class NullReferenceResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => null;
    }

    static byte[] BuildApiSurfaceTypeSpecCycle()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var cyclicTypeSpec = new BlobBuilder();
        cyclicTypeSpec.WriteByte(0x1f); // CMOD_REQD
        cyclicTypeSpec.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
        cyclicTypeSpec.WriteByte(0x08); // I4
        metadata.AddTypeSpecification(metadata.GetOrAddBlob(cyclicTypeSpec));

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06); // field signature
        fieldSignature.WriteByte(0x1f); // CMOD_REQD
        fieldSignature.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
        fieldSignature.WriteByte(0x08); // I4
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("F"),
            metadata.GetOrAddBlob(fieldSignature));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Builds a PE with an enum <c>N.E</c> whose single instance field carries an
    /// over-deep signature blob, and an attribute <c>N.A</c> whose constructor
    /// takes an <c>N.E</c> argument, applied to <c>N.A</c>. Decoding that custom
    /// attribute drives <c>ArgTypeProvider.GetUnderlyingEnumType</c> through the
    /// deep enum-field signature — the exact path guarded in AttributeDecoder.
    /// </summary>
    static byte[] BuildDeepEnumAttributePe()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        // TypeDef row 2: the enum. Owns field row 1 (its deep instance field) and
        // no methods.
        var enumType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("E"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        // TypeDef row 3: the attribute. Owns no fields and method row 1 (.ctor).
        var attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));

        // Field row 1: the enum's instance field, with an over-deep signature.
        var enumFieldSignature = new BlobBuilder();
        enumFieldSignature.WriteByte(0x06); // field signature
        for (int i = 0; i < 100_000; i++)
            enumFieldSignature.WriteByte(0x1d); // SZARRAY
        enumFieldSignature.WriteByte(0x08);     // I4
        metadata.AddFieldDefinition(
            FieldAttributes.Public, // instance (non-static): GetUnderlyingEnumType reads the first such field
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(enumFieldSignature));

        // Method row 1: `.ctor(N.E)` on the attribute. HASTHIS, void return, one
        // VALUETYPE parameter referencing the enum TypeDef (coded index below).
        int enumCodedIndex = CodedIndex.TypeDefOrRefOrSpec(enumType);
        Assert.True(enumCodedIndex < 0x80); // single-byte compressed integer
        var ctorSignature = new BlobBuilder();
        ctorSignature.WriteByte(0x20);              // HASTHIS | DEFAULT
        ctorSignature.WriteByte(0x01);              // one parameter
        ctorSignature.WriteByte(0x01);              // return type VOID
        ctorSignature.WriteByte(0x11);              // ELEMENT_TYPE_VALUETYPE
        ctorSignature.WriteByte((byte)enumCodedIndex);
        var ctor = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(ctorSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        // A(default(E)) applied to the attribute type: prolog, a 4-byte enum
        // value (read as Int32 after the guard degrades), zero named arguments.
        var value = new BlobBuilder();
        value.WriteUInt16(0x0001); // prolog
        value.WriteInt32(0);       // fixed enum argument
        value.WriteUInt16(0);      // named argument count
        metadata.AddCustomAttribute(attributeType, ctor, metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MetadataReader BuildAssembly(Action<MetadataBuilder> addRows)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        addRows(metadata);

        var root = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider
            .FromMetadataImage(ImmutableArray.Create(image.ToArray()))
            .GetMetadataReader();
    }
}
