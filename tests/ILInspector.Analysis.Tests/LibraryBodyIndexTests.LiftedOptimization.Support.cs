using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static void CorruptStateMachineClaim(
        byte[] image,
        string ownerName)
    {
        string? claimedType = null;
        using (var peReader = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            MetadataReader reader =
                peReader.GetMetadataReader();
            foreach (MethodDefinitionHandle methodHandle
                in reader.MethodDefinitions)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (!reader.StringComparer.Equals(
                        method.Name,
                        ownerName))
                {
                    continue;
                }
                foreach (CustomAttributeHandle attributeHandle
                    in method.GetCustomAttributes())
                {
                    CustomAttribute attribute =
                        reader.GetCustomAttribute(
                            attributeHandle);
                    BlobReader value =
                        reader.GetBlobReader(
                            attribute.Value);
                    if (value.Length < 3
                        || value.ReadUInt16() != 0x0001)
                    {
                        continue;
                    }
                    string? candidate =
                        value.ReadSerializedString();
                    if (candidate?.Contains(
                            ownerName,
                            StringComparison.Ordinal)
                        == true)
                    {
                        claimedType = candidate;
                        break;
                    }
                }
                if (claimedType is not null)
                    break;
            }
        }

        Assert.NotNull(claimedType);
        byte[] claim = Encoding.UTF8.GetBytes(
            claimedType);
        int offset = image.AsSpan().IndexOf(claim);
        Assert.True(offset >= 0);
        image[offset] = (byte)'Z';
    }

    static void SetRuntimeAsyncFlag(
        byte[] image,
        Func<MetadataReader, MethodDefinitionHandle>
            selectMethod)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader =
            peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            selectMethod(reader);
        int implFlagsOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(
                TableIndex.MethodDef)
            + (MetadataTokens.GetRowNumber(methodHandle) - 1)
                * reader.GetTableRowSize(
                    TableIndex.MethodDef)
            + sizeof(int);
        ushort implFlags =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(
                    implFlagsOffset,
                    sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(
                implFlagsOffset,
                sizeof(ushort)),
            (ushort)(implFlags
                | (ushort)MethodImplAttributes.Async));
    }

    static byte[] EmitLiftedMemberReferenceReplayAssembly(
        int referenceCount,
        int ownerCount,
        int parameterCount,
        bool distinctSignatureBlobs = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DuplicateMemberRefs.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DuplicateMemberRefs"),
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
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var liftedSignatureHandles =
            new BlobHandle[referenceCount];
        int liftedSignatureLength = 0;
        var referenceTokens = new int[referenceCount];
        for (int i = 0; i < referenceTokens.Length; i++)
        {
            var liftedSignature = new BlobBuilder();
            liftedSignature.WriteByte(0);
            liftedSignature.WriteCompressedInteger(parameterCount);
            liftedSignature.WriteByte(1);
            for (int parameter = 0;
                parameter < parameterCount;
                parameter++)
            {
                liftedSignature.WriteByte(
                    distinctSignatureBlobs
                        && parameter == parameterCount - 1
                            ? checked((byte)(i + 2))
                            : (byte)8);
            }
            liftedSignatureLength = liftedSignature.Count;
            liftedSignatureHandles[i] =
                metadata.GetOrAddBlob(liftedSignature);
            referenceTokens[i] = MetadataTokens.GetToken(
                metadata.AddMemberReference(
                    type,
                    metadata.GetOrAddString(
                        "<Owner>g__Core|0_0"),
                    liftedSignatureHandles[i]));
        }
        if (distinctSignatureBlobs)
        {
            Assert.Equal(
                referenceCount,
                liftedSignatureHandles.Distinct().Count());
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var ownerIl = new BlobBuilder();
        foreach (int token in referenceTokens)
        {
            ownerIl.WriteByte((byte)ILOpCode.Call);
            ownerIl.WriteInt32(token);
        }
        ownerIl.WriteByte((byte)ILOpCode.Ret);
        int ownerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(ownerIl),
            maxStack: 0);
        var liftedIl = new BlobBuilder();
        liftedIl.WriteByte((byte)ILOpCode.Ret);
        int liftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(liftedIl),
            maxStack: 0);
        var ownerSignature = new BlobBuilder();
        ownerSignature.WriteByte(0);
        ownerSignature.WriteByte(0);
        ownerSignature.WriteByte(1);
        BlobHandle ownerSignatureHandle =
            metadata.GetOrAddBlob(ownerSignature);
        for (int i = 0; i < ownerCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Owner"),
                ownerSignatureHandle,
                ownerBody,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("<Owner>g__Core|0_0"),
            liftedSignatureHandles[0],
            liftedBody,
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
        byte[] bytes = image.ToArray();
        if (distinctSignatureBlobs)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream);
            int blobStream = MetadataStreamOffset(
                bytes,
                reader.PEHeaders.MetadataStartOffset,
                "#Blob");
            int prefixLength = liftedSignatureLength switch
            {
                <= 0x7F => 1,
                <= 0x3FFF => 2,
                _ => 4,
            };
            foreach (BlobHandle handle in liftedSignatureHandles)
            {
                int lastByte = blobStream
                    + MetadataTokens.GetHeapOffset(handle)
                    + prefixLength
                    + liftedSignatureLength
                    - 1;
                bytes[lastByte] = 8;
            }
        }
        return bytes;
    }

    static int CountMethodReferenceResolutions(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int resolved = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            "DuplicateMemberRefs.dll",
            reader,
            peReader,
            resolver: null,
            methodReferenceResolved: (_, _) =>
                Interlocked.Increment(ref resolved));

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));
        return resolved;
    }

    static TypeRef ExactDefinition(
        string flattenedName,
        params string[] segments)
    {
        var result =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    [.. segments]));
        return TypeRef.Definition(
            "Fixture",
            "Sample",
            flattenedName,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                result.Name));
    }

    static byte[]
        BuildNestedLiftedInvalidAsyncSourceAssembly(
            out int sourceToken,
            out int liftedToken)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "NestedLiftedInvalidAsyncSource.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "NestedLiftedInvalidAsyncSource"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xB0, 0x3F, 0x5F, 0x7F,
                        0x11, 0xD5, 0x0A, 0x3A,
                    }),
                default,
                default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle asyncStateMachineAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "AsyncStateMachineAttribute"));
        TypeReferenceHandle compilerGeneratedAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "CompilerGeneratedAttribute"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle sourceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Class,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Source"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle displayClass =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Class
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("<>c"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        TypeDefinitionHandle invalidStateMachine =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Class
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(
                    "NotAStateMachine"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddNestedType(
            displayClass,
            sourceType);
        metadata.AddNestedType(
            invalidStateMachine,
            sourceType);

        BlobHandle signature =
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 });
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle lifted =
            MetadataTokens.MethodDefinitionHandle(2);
        var sourceIl = new BlobBuilder();
        sourceIl.WriteByte((byte)ILOpCode.Call);
        sourceIl.WriteInt32(
            MetadataTokens.GetToken(lifted));
        sourceIl.WriteByte((byte)ILOpCode.Ret);
        int sourceBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(sourceIl));
        var liftedIl = new BlobBuilder();
        liftedIl.WriteByte((byte)ILOpCode.Ret);
        int liftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(liftedIl));
        MethodDefinitionHandle source =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "BadSourceLambda"),
                signature,
                sourceBody,
                MetadataTokens.ParameterHandle(1));
        sourceToken =
            MetadataTokens.GetToken(source);
        MethodDefinitionHandle addedLifted =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "<BadSourceLambda>b__0_0"),
                signature,
                liftedBody,
                MetadataTokens.ParameterHandle(1));
        Assert.Equal(lifted, addedLifted);
        liftedToken =
            MetadataTokens.GetToken(addedLifted);

        MemberReferenceHandle asyncConstructor =
            metadata.AddMemberReference(
                asyncStateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20, 0x01, 0x01, 0x12,
                        (byte)CodedIndex
                            .TypeDefOrRefOrSpec(
                                systemType),
                    }));
        AddAsyncStateMachineAttribute(
            metadata,
            source,
            asyncConstructor,
            "Sample.Source+NotAStateMachine, "
                + "NestedLiftedInvalidAsyncSource");
        MemberReferenceHandle generatedConstructor =
            metadata.AddMemberReference(
                compilerGeneratedAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
        metadata.AddCustomAttribute(
            displayClass,
            generatedConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));

        return SerializeDirectionProbe(
            metadata,
            bodies);
    }

    static void ReplaceUniqueAscii(
        byte[] image,
        string oldValue,
        string newValue)
        => ReplaceAscii(
            image,
            oldValue,
            newValue,
            expectedReplacements: 1);

    static int MetadataStreamOffset(
        byte[] image,
        int metadataRoot,
        string streamName)
    {
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataRoot + 12, 4));
        int position = metadataRoot + 16
            + ((versionLength + 3) & ~3);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(position + 2, 2));
        position += 4;
        for (int i = 0; i < streamCount; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(position, 4));
            position += 8;
            int nameStart = position;
            while (image[position] != 0)
                position++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                position - nameStart);
            position = (position + 4) & ~3;
            if (name == streamName)
                return metadataRoot + offset;
        }

        throw new BadImageFormatException(
            $"Metadata stream {streamName} was not found.");
    }

}
