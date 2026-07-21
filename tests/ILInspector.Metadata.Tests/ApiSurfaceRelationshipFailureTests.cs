using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;

namespace ILInspector.Metadata.Tests;

public class ApiSurfaceRelationshipFailureTests
{
    [Fact]
    public void ExtractApiSurface_CyclicTypePreservesValidSiblingAndFailure()
    {
        using var stream = new MemoryStream(BuildImage(
            cyclicTypeName: "Rejected",
            validTypeNames: ["Sibling"]));

        var surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true,
            typesOnly: true);

        Assert.NotNull(surface);
        var sibling = Assert.Single(surface.Types);
        Assert.Equal("Sibling", sibling.Name);
        Assert.Equal(1, surface.PublicTypeCount);
        var failure = Assert.Single(surface.InspectionFailures);
        Assert.Equal("type identity", failure.Operation);
        Assert.Equal(0x02000002, failure.SubjectToken);
        Assert.Equal(MetadataTypeNameFailureMechanism.Relationship, failure.Mechanism);
        Assert.Equal("Cycle", failure.Kind);
    }

    [Fact]
    public void ApiDiff_IncompleteNewIdentityDoesNotClaimOldTypeWasRemoved()
    {
        using var oldStream = new MemoryStream(BuildImage(
            cyclicTypeName: null,
            validTypeNames: ["Maybe"]));
        using var newStream = new MemoryStream(BuildImage(
            cyclicTypeName: "Maybe",
            validTypeNames: ["Sibling"]));

        var oldSurface = AssemblyReader.ExtractApiSurface(
            oldStream,
            includeAll: true,
            typesOnly: true);
        var newSurface = AssemblyReader.ExtractApiSurface(
            newStream,
            includeAll: true,
            typesOnly: true);

        Assert.NotNull(oldSurface);
        Assert.NotNull(newSurface);
        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.DoesNotContain(
            diff.TypeDiffs,
            type => type.TypeFullName == "Maybe" && type.IsRemoved);
        Assert.Contains(
            diff.TypeDiffs,
            type => type.TypeFullName == "Sibling" && type.IsAdded);
        var failure = Assert.Single(diff.InspectionFailures);
        Assert.Equal("new", failure.Side);
        Assert.Equal("Cycle", failure.Kind);
        Assert.False(diff.IsEmpty);

        var findings = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            new FindingSubject("api", "API surface"));
        // The Finding lane's surfaces already exclude the entities recorded in
        // InspectionFailures, so a failure on one type must not collapse the whole-side
        // comparison to Failed -- mirroring the legacy analyzer's per-type-skip granularity
        // asserted above (Sibling added, Maybe not falsely reported as removed).
        Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(findings.Types.Value);
        Assert.Contains(
            findings.ApiDiff.InspectionFailures,
            failure => failure.Side == "new" && failure.Kind == "Cycle");
        Assert.DoesNotContain(
            findings.ApiDiff.TypeDiffs,
            type => type.TypeFullName == "Maybe" && type.IsRemoved);
        Assert.Contains(
            findings.ApiDiff.TypeDiffs,
            type => type.TypeFullName == "Sibling" && type.IsAdded);
        Assert.False(findings.IsExact);
    }

    [Fact]
    public void EnumDefaultLookup_DoesNotAttributeUnrelatedFailureToValidType()
    {
        using var stream = new MemoryStream(BuildEnumDefaultImage());

        var surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true);

        Assert.NotNull(surface);
        var consumer = Assert.Single(
            surface.Types,
            type => type.Name == "Consumer");
        var method = Assert.Single(
            consumer.Members,
            member => member.Name == "M");
        Assert.Contains(
            "color = GoodEnum.Red",
            method.Signature,
            StringComparison.Ordinal);
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x01000001
                && failure.Kind == "Cycle");
        Assert.DoesNotContain(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x02000002);
    }

    [Fact]
    public void EnumDefaultLookup_MalformedCandidateDoesNotRejectConsumer()
    {
        using var stream = new MemoryStream(BuildMalformedEnumScanImage());

        var surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true);

        Assert.NotNull(surface);
        var consumer = Assert.Single(
            surface.Types,
            type => type.Name == "Consumer");
        var method = Assert.Single(
            consumer.Members,
            member => member.Name == "M");
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x02000002
                && failure.Kind == "MalformedMetadata");
        Assert.DoesNotContain(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x02000004);
        Assert.Contains(
            "color = (GoodEnum)0",
            method.Signature,
            StringComparison.Ordinal);
    }

    static byte[] BuildImage(
        string? cyclicTypeName,
        IReadOnlyList<string> validTypeNames)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        if (cyclicTypeName is not null)
        {
            var cyclic = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString(cyclicTypeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(cyclic, cyclic);
        }

        foreach (string name in validTypeNames)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString(name),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
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

    static byte[] BuildEnumDefaultImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        var coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        var cyclicBase = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(1),
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Loop"));
        var enumBase = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Consumer"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Rejected"),
            cyclicBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("GoodEnum"),
            enumBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));

        var valueField = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            FieldSignature(metadata));
        var redField = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal,
            metadata.GetOrAddString("Red"),
            FieldSignature(metadata));
        metadata.AddConstant(redField, 0);

        var parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("color"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, 0);

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            EnumDefaultMethodSignature(metadata),
            bodyOffset,
            parameter);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMalformedEnumScanImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        var coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        var enumBase = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("GoodEnum"),
            enumBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Sentinel"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(2),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Consumer"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(2),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            FieldSignature(metadata));

        var parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("color"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, 0);

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            MalformedEnumMethodSignature(metadata),
            bodyOffset,
            parameter);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        var bytes = image.ToArray();
        CorruptTypeDefinitionFieldList(bytes, rowNumber: 3);
        return bytes;
    }

    static BlobHandle FieldSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle EnumDefaultMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x11);
        signature.WriteCompressedInteger(4 << 2);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle MalformedEnumMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x11);
        signature.WriteCompressedInteger(2 << 2);
        return metadata.GetOrAddBlob(signature);
    }

    static void CorruptTypeDefinitionFieldList(
        byte[] image,
        int rowNumber)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var directory = headers.CorHeader!.MetadataDirectory;
        var section = headers.SectionHeaders.Single(section =>
            directory.RelativeVirtualAddress >= section.VirtualAddress
            && directory.RelativeVirtualAddress
                < section.VirtualAddress + Math.Max(
                    section.VirtualSize,
                    section.SizeOfRawData));
        int metadataOffset = section.PointerToRawData
            + directory.RelativeVirtualAddress
            - section.VirtualAddress;

        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataOffset + 12, 4));
        int streamHeader = Align4(metadataOffset + 16 + versionLength);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(streamHeader + 2, 2));
        streamHeader += 4;

        int tablesOffset = -1;
        for (int i = 0; i < streamCount; i++)
        {
            int relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(streamHeader, 4));
            int nameStart = streamHeader + 8;
            int nameEnd = nameStart;
            while (image[nameEnd] != 0)
                nameEnd++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                nameEnd - nameStart);
            if (name is "#~" or "#-")
                tablesOffset = metadataOffset + relativeOffset;
            streamHeader = Align4(nameEnd + 1);
        }

        Assert.True(tablesOffset >= 0);
        byte heapSizes = image[tablesOffset + 6];
        ulong validTables = BinaryPrimitives.ReadUInt64LittleEndian(
            image.AsSpan(tablesOffset + 8, 8));
        var rowCounts = new uint[64];
        int rowCountOffset = tablesOffset + 24;
        for (int table = 0; table < rowCounts.Length; table++)
        {
            if ((validTables & (1UL << table)) == 0)
                continue;
            rowCounts[table] = BinaryPrimitives.ReadUInt32LittleEndian(
                image.AsSpan(rowCountOffset, 4));
            rowCountOffset += 4;
        }

        int stringIndexSize = (heapSizes & 0x01) != 0 ? 4 : 2;
        int guidIndexSize = (heapSizes & 0x02) != 0 ? 4 : 2;
        int moduleRowSize = 2 + stringIndexSize + (3 * guidIndexSize);
        int resolutionScopeSize = CodedIndexSize(
            rowCounts,
            tagBits: 2,
            0,
            26,
            35,
            1);
        int typeRefRowSize = resolutionScopeSize + (2 * stringIndexSize);
        int typeDefOrRefSize = CodedIndexSize(
            rowCounts,
            tagBits: 2,
            2,
            1,
            27);
        int fieldIndexSize = rowCounts[4] < ushort.MaxValue ? 2 : 4;
        int methodIndexSize = rowCounts[6] < ushort.MaxValue ? 2 : 4;
        int typeDefRowSize = 4
            + (2 * stringIndexSize)
            + typeDefOrRefSize
            + fieldIndexSize
            + methodIndexSize;
        int typeDefTableOffset = rowCountOffset
            + ((int)rowCounts[0] * moduleRowSize)
            + ((int)rowCounts[1] * typeRefRowSize);
        int fieldListOffset = typeDefTableOffset
            + ((rowNumber - 1) * typeDefRowSize)
            + 4
            + (2 * stringIndexSize)
            + typeDefOrRefSize;
        if (fieldIndexSize == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(fieldListOffset, 2),
                ushort.MaxValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(fieldListOffset, 4),
                uint.MaxValue);
        }
    }

    static int CodedIndexSize(
        IReadOnlyList<uint> rowCounts,
        int tagBits,
        params int[] tables)
        => tables.Max(table => rowCounts[table])
            < (1U << (16 - tagBits))
            ? 2
            : 4;

    static int Align4(int value)
        => (value + 3) & ~3;
}
