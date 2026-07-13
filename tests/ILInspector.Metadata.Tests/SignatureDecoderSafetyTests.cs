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
    public void SignatureBlobGuard_OldAssemblyIdentity_IsForwarded()
        => Assert.Equal(
            typeof(SignatureBlobGuard),
            Type.GetType("ILInspector.Metadata.SignatureBlobGuard, ILInspector.Metadata"));

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

        Assert.Equal(
            "int",
            TypeResolver.GetTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)));
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

        Assert.Equal(
            "object",
            TypeResolver.GetTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)));
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

        var decoded = MetadataDeclarationQuery.GetMethodReturnType(
            reader,
            reader.GetTypeDefinition(typeHandle),
            reader.GetMethodDefinition(methodHandle));

        Assert.Equal("object", decoded);
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
        _ = ApiMemberIdentity.CreateMethodAnchorInfo(
            reader, typeHandle, reader.GetMethodDefinition(methodHandle));
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
        startInfo.ArgumentList.Add("-method");
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
    static byte[] BuildSurfacePe(BlobBuilder? fieldSignature, BlobBuilder? methodSignature)
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
