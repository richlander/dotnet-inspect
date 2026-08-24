using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class UnsafeEvidencePresenceTests
{
    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerLocalFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedLocal.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe local signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMemberRefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.MemberReference);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedMemberRef.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodSpecFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind
                    .MethodSpecification);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedMethodSpec.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodDefCallFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: true,
                unsafeLookalikeType: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedMethodDefCall.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodDefDeclarationFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: false,
                unsafeLookalikeType: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedMethodDefDeclaration.dll",
                    image));

        Assert.Contains(
            "unsafe method signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedUnsafeLookalikeMethodDefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: true,
                unsafeLookalikeType: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedUnsafeLookalikeMethodDef.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeSignatureMarkerCache_RepeatedHandleScansOnce()
    {
        using MetadataReaderProvider provider =
            BuildMetadataWithBlobs(
                [[0x00, 0x01, 0x01, 0x08]],
                out ImmutableArray<BlobHandle> handles);
        int scans = 0;
        var cache = new UnsafeSignatureMarkerCache(
            provider.GetMetadataReader(),
            _ => scans++);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(
                UnsafeSignatureMarkers.None,
                cache.GetMarkers(handles[0]));
        }

        Assert.Equal(1, scans);
    }

    [Fact]
    public void
        UnsafeSignatureMarkerCache_RejectsCumulativeWorkAboveAssemblyBudget()
    {
        int blobLength =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars
            / 2
            + 1;
        var first = new byte[blobLength];
        var second = new byte[blobLength];
        Array.Fill(first, (byte)0x08);
        Array.Fill(second, (byte)0x08);
        second[^1] = 0x09;
        using MetadataReaderProvider provider =
            BuildMetadataWithBlobs(
                [first, second],
                out ImmutableArray<BlobHandle> handles);
        var cache = new UnsafeSignatureMarkerCache(
            provider.GetMetadataReader());

        Assert.Equal(
            UnsafeSignatureMarkers.None,
            cache.GetMarkers(handles[0]));
        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(
                () => cache.GetMarkers(handles[1]));

        Assert.Contains(
            "exceeds the assembly budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ConstructedGenericCallIsNotUnsafeByParentShape()
    {
        ImmutableArray<byte> image =
            BuildConstructedGenericCallAssembly();

        Assert.False(
            LibraryBodyIndex.HasUnsafeEvidence(
                "ConstructedGenericCall.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_UserDefinedUnsafeLookalikeDoesNotCountAsEvidence()
    {
        ImmutableArray<byte> image =
            BuildUnsafeLookalikeCallAssembly();

        Assert.False(
            LibraryBodyIndex.HasUnsafeEvidence(
                "UnsafeLookalike.dll",
                image));
        AssertNoUnsafeEvidenceInFullCensus(image);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ExternalUnsafeLookalikeDoesNotCountAsEvidence()
    {
        ImmutableArray<byte> image =
            BuildExternalUnsafeLookalikeCallAssembly();

        Assert.False(
            LibraryBodyIndex.HasUnsafeEvidence(
                "ExternalUnsafeLookalike.dll",
                image));
        AssertNoUnsafeEvidenceInFullCensus(image);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedUnsafeLookalikeMemberRefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.MemberReference,
                unsafeLookalikeParent: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "GuardRejectedUnsafeLookalike.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeEvidencePresence_RejectsAssemblyIlAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeBodyAssembly(
                unsafeFirst: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => LibraryBodyIndex.HasUnsafeEvidence(
                    "LargeSafeBody.dll",
                    image));

        Assert.Contains(
            "IL scanning exceeds the assembly budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_StopsBeforeCopyingOrMaterializingLargeSuffix()
    {
        ImmutableArray<byte> image =
            BuildLargeBodyAssembly(
                unsafeFirst: true);
        long before =
            GC.GetAllocatedBytesForCurrentThread();

        Assert.True(
            LibraryBodyIndex.HasUnsafeEvidence(
                "LargeUnsafeBody.dll",
                image));

        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - before;
        Assert.True(
            allocated
                < UnsafePresenceWorkBudget.MaxIlBytes / 4,
            $"Presence probing allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void
        UnsafeEvidencePresence_EarlierEvidenceIsNotScheduleDependent()
    {
        ImmutableArray<byte> image =
            BuildSchedulingSensitiveAssembly();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Assert.True(
                LibraryBodyIndex.HasUnsafeEvidence(
                    "SchedulingSensitive.dll",
                    image));
        }
    }

    static ImmutableArray<byte> BuildGuardRejectedUnsafeAssembly(
        GuardRejectedSignatureKind rejectedKind,
        bool unsafeLookalikeParent = false)
    {
        var metadata = CreateMetadata("GuardRejected");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var code = new BlobBuilder();
        StandaloneSignatureHandle localSignature = default;

        if (rejectedKind
            == GuardRejectedSignatureKind.Local)
        {
            localSignature = metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(
                    GuardRejectedSignature(
                        SignatureBlobGuard.Kind
                            .LocalVariables)));
        }
        else
        {
            AssemblyReferenceHandle reference =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("External"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle external =
                metadata.AddTypeReference(
                    reference,
                    metadata.GetOrAddString(
                        unsafeLookalikeParent
                            ? "System.Runtime.CompilerServices"
                            : "N"),
                    metadata.GetOrAddString(
                        unsafeLookalikeParent
                            ? "Unsafe"
                            : "External"));
            BlobHandle memberSignature =
                rejectedKind
                    == GuardRejectedSignatureKind
                        .MethodSpecification
                    ? metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x10,
                            0x01,
                            0x00,
                            0x01,
                        })
                    : metadata.GetOrAddBlob(
                        GuardRejectedSignature(
                            SignatureBlobGuard.Kind.Method));
            EntityHandle member =
                metadata.AddMemberReference(
                    external,
                    metadata.GetOrAddString("Invoke"),
                    memberSignature);
            if (rejectedKind
                == GuardRejectedSignatureKind
                    .MethodSpecification)
            {
                member = metadata.AddMethodSpecification(
                    member,
                    metadata.GetOrAddBlob(
                        GuardRejectedSignature(
                            SignatureBlobGuard.Kind
                                .MethodSpecification)));
            }
            code.WriteByte((byte)ILOpCode.Call);
            code.WriteInt32(
                MetadataTokens.GetToken(member));
        }
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1,
            localVariablesSignature: localSignature,
            attributes: localSignature.IsNil
                ? MethodBodyAttributes.None
                : MethodBodyAttributes.InitLocals);

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static byte[] GuardRejectedSignature(
        SignatureBlobGuard.Kind kind)
    {
        var signature = new BlobBuilder();
        if (kind == SignatureBlobGuard.Kind.LocalVariables)
        {
            signature.WriteByte(0x07);
            signature.WriteByte(0x01);
        }
        else if (kind
            == SignatureBlobGuard.Kind.MethodSpecification)
        {
            signature.WriteByte(0x0A);
            signature.WriteByte(0x01);
        }
        else
        {
            signature.WriteByte(0x00);
            signature.WriteByte(0x01);
            signature.WriteByte(0x01);
        }
        for (int i = 0;
            i <= SignatureBlobGuard.DefaultMaxDepth;
            i++)
        {
            signature.WriteByte(0x0F);
        }
        signature.WriteByte(0x08);
        return signature.ToArray();
    }

    static ImmutableArray<byte>
        BuildGuardRejectedMethodDefinitionAssembly(
            bool called,
            bool unsafeLookalikeType)
    {
        var metadata = CreateMetadata(
            "GuardRejectedMethodDefinition");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(
                unsafeLookalikeType
                    ? "System.Runtime.CompilerServices"
                    : "N"),
            metadata.GetOrAddString(
                unsafeLookalikeType
                    ? "Unsafe"
                    : "Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        if (called)
        {
            var code = new BlobBuilder();
            code.WriteByte((byte)ILOpCode.Call);
            code.WriteInt32(
                MetadataTokens.GetToken(
                    MetadataTokens.MethodDefinitionHandle(2)));
            code.WriteByte((byte)ILOpCode.Ret);
            int bodyOffset =
                new MethodBodyStreamEncoder(bodies)
                    .AddMethodBody(
                        new InstructionEncoder(code),
                        maxStack: 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Caller"),
                AddVoidMethodSignature(metadata),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(
                GuardRejectedSignature(
                    SignatureBlobGuard.Kind.Method)),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static BlobHandle AddVoidMethodSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte> BuildUnsafeLookalikeCallAssembly()
    {
        var metadata = CreateMetadata("UnsafeLookalike");
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("Unsafe"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var identityCode = new BlobBuilder();
        identityCode.WriteByte(
            (byte)ILOpCode.Ldc_i4_1);
        identityCode.WriteByte((byte)ILOpCode.Ret);
        int identityBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(identityCode),
            maxStack: 1);
        MethodDefinitionHandle identity =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Identity"),
                AddIntMethodSignature(metadata),
                identityBody,
                MetadataTokens.ParameterHandle(1));

        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(identity));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddIntMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildExternalUnsafeLookalikeCallAssembly()
    {
        var metadata = CreateMetadata(
            "ExternalUnsafeLookalike");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle reference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("UnsafeLookalike"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle unsafeType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("Unsafe"));
        MemberReferenceHandle method =
            metadata.AddMemberReference(
                unsafeType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(metadata));
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(MetadataTokens.GetToken(method));
        code.WriteByte((byte)ILOpCode.Ret);
        var bodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(bodies)
            .AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte> BuildLargeBodyAssembly(
        bool unsafeFirst)
    {
        var metadata = CreateMetadata("LargeBody");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("LargeBody"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        byte[] il = new byte[
            UnsafePresenceWorkBudget.MaxIlBytes + 2];
        if (unsafeFirst)
            il[0] = (byte)ILOpCode.Calli;
        il[^1] = (byte)ILOpCode.Ret;
        var code = new BlobBuilder(il.Length);
        code.WriteBytes(il);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildSchedulingSensitiveAssembly()
    {
        var metadata = CreateMetadata(
            "SchedulingSensitive");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Probe"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StandaloneSignatureHandle calliSignature =
            metadata.AddStandaloneSignature(
                AddVoidMethodSignature(metadata));
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var unsafeCode = new BlobBuilder();
        unsafeCode.WriteByte((byte)ILOpCode.Calli);
        unsafeCode.WriteInt32(
            MetadataTokens.GetToken(calliSignature));
        unsafeCode.WriteByte((byte)ILOpCode.Ret);
        int unsafeBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(unsafeCode),
            maxStack: 1);

        const int switchTargets = 262_144;
        var safeCode = new BlobBuilder(
            1 + sizeof(int)
                + switchTargets * sizeof(int)
                + 1);
        safeCode.WriteByte((byte)ILOpCode.Switch);
        safeCode.WriteInt32(switchTargets);
        safeCode.WriteBytes(
            new byte[
                switchTargets * sizeof(int)]);
        safeCode.WriteByte((byte)ILOpCode.Ret);
        int safeBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(safeCode),
            maxStack: 1);

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("UnsafeFirst"),
            AddVoidMethodSignature(metadata),
            unsafeBody,
            MetadataTokens.ParameterHandle(1));
        for (int index = 0; index < 200; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    $"Safe{index}"),
                AddVoidMethodSignature(metadata),
                safeBody,
                MetadataTokens.ParameterHandle(1));
        }

        return Serialize(metadata, bodies);
    }

    static BlobHandle AddIntMethodSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Type().Int32(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte> BuildConstructedGenericCallAssembly()
    {
        var metadata = CreateMetadata("ConstructedGenericCall");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle reference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle genericType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Container`1"));
        int genericTypeCode =
            CodedIndex.TypeDefOrRefOrSpec(
                genericType);
        var constructedType = new BlobBuilder();
        constructedType.WriteByte(0x15);
        constructedType.WriteByte(0x12);
        constructedType.WriteCompressedInteger(
            genericTypeCode);
        constructedType.WriteByte(0x01);
        constructedType.WriteByte(0x08);
        TypeSpecificationHandle parent =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    constructedType));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                parent,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(
            MetadataTokens.GetToken(member));
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static MetadataBuilder CreateMetadata(
        string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static MetadataReaderProvider BuildMetadataWithBlobs(
        IReadOnlyList<byte[]> blobs,
        out ImmutableArray<BlobHandle> handles)
    {
        MetadataBuilder metadata =
            CreateMetadata("SignatureMarkers");
        handles = blobs
            .Select(metadata.GetOrAddBlob)
            .ToImmutableArray();
        var root = new MetadataRootBuilder(
            metadata,
            suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(
            image,
            methodBodyStreamRva: 0,
            mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(
            ImmutableArray.Create(image.ToArray()));
    }

    static ImmutableArray<byte> Serialize(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static void AssertNoUnsafeEvidenceInFullCensus(
        ImmutableArray<byte> image)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsafe-lookalike-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image.AsSpan());
        try
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            Assert.Empty(index.UnsafeEvidence);
            Assert.Empty(index.Diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    enum GuardRejectedSignatureKind
    {
        Local,
        MemberReference,
        MethodSpecification,
    }
}
