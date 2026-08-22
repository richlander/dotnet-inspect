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

    static ImmutableArray<byte> BuildGuardRejectedUnsafeAssembly(
        GuardRejectedSignatureKind rejectedKind)
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
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("External"));
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

    enum GuardRejectedSignatureKind
    {
        Local,
        MemberReference,
        MethodSpecification,
    }
}
