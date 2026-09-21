using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using DotnetInspector.Packages;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    static byte[] SourceFileBytes(
        [CallerFilePath] string path = "") =>
        File.ReadAllBytes(path);

    static string SourceFileBytesPath(
        [CallerFilePath] string path = "") =>
        path;

    static byte[] CorruptEmbeddedPdb(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        using var stream =
            new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        DebugDirectoryEntry embedded =
            Assert.Single(
                reader.ReadDebugDirectory(),
                static entry =>
                    entry.Type
                    == DebugDirectoryEntryType
                        .EmbeddedPortablePdb);
        bytes[embedded.DataPointer] ^= 0xff;
        return bytes;
    }

    static byte[] WithoutDebugDirectory(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        using (var reader =
               new PEReader(
                   new MemoryStream(
                       bytes,
                       writable: false)))
        {
            PEHeader header =
                Assert.IsType<PEHeader>(
                    reader.PEHeaders.PEHeader);
            int directoryBase =
                reader.PEHeaders.PEHeaderStartOffset
                + (header.Magic == PEMagic.PE32Plus
                    ? 112
                    : 96);
            Array.Clear(
                bytes,
                directoryBase + (6 * 8),
                8);
        }

        using var mutated =
            new PEReader(
                new MemoryStream(
                    bytes,
                    writable: false));
        Assert.Empty(mutated.ReadDebugDirectory());
        return bytes;
    }

    static byte[] RemoveSourceLinkCustomDebugInformation(
        string pdbPath)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int kindOffset;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            CustomDebugInformationHandle informationHandle =
                Assert.Single(
                    reader.GetCustomDebugInformation(
                        EntityHandle.ModuleDefinition),
                    handle =>
                        reader.GetGuid(
                            reader
                                .GetCustomDebugInformation(
                                    handle)
                                .Kind)
                        == SourceLinkKind);
            CustomDebugInformation information =
                reader.GetCustomDebugInformation(
                    informationHandle);
            kindOffset =
                MetadataTokens.GetHeapOffset(
                    information.Kind);
        }

        int guidOffset =
            FindMetadataStreamOffset(bytes, "#GUID");
        bytes[
            checked(
                guidOffset
                + ((kindOffset - 1) * 16))] ^= 0xff;

        using var mutatedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader mutatedReader =
            mutatedProvider.GetMetadataReader();
        Assert.DoesNotContain(
            mutatedReader.GetCustomDebugInformation(
                EntityHandle.ModuleDefinition),
            handle =>
                mutatedReader.GetGuid(
                    mutatedReader
                        .GetCustomDebugInformation(
                            handle)
                        .Kind)
                == SourceLinkKind);
        return bytes;
    }

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] bytes)
    {
        using var stream =
            new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity
            .FromAssemblyDefinition(
                reader.GetMetadataReader());
    }

    static byte[] CorruptDocumentName(
        string pdbPath,
        string targetFileName,
        bool corruptTarget)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int documentNameOffset = -1;
        int documentNameLength = 0;
        int corruptedDocumentRow = 0;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                Document document =
                    reader.GetDocument(handle);
                string name =
                    reader.GetString(document.Name);
                bool isTarget =
                    name.EndsWith(
                        targetFileName,
                        StringComparison.Ordinal);
                if (isTarget != corruptTarget)
                {
                    continue;
                }

                var nameBlob =
                    (BlobHandle)document.Name;
                corruptedDocumentRow =
                    MetadataTokens.GetRowNumber(handle);
                documentNameOffset =
                    MetadataTokens.GetHeapOffset(nameBlob);
                documentNameLength =
                    reader.GetBlobBytes(nameBlob).Length;
                break;
            }
        }

        Assert.True(documentNameOffset >= 0);
        Assert.True(documentNameLength > 1);
        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + documentNameOffset);
        int payloadOffset =
            checked(
                blobEntryOffset
                + CompressedIntegerPrefixSize(
                    bytes[blobEntryOffset]));
        bytes[payloadOffset + 1] = 0xe0;

        int malformedDocuments = 0;
        bool targetDocumentReadable = false;
        bool targetDocumentMalformed = false;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                try
                {
                    string name =
                        reader.GetString(
                            reader.GetDocument(handle).Name);
                    targetDocumentReadable |=
                        name.EndsWith(
                            targetFileName,
                            StringComparison.Ordinal);
                }
                catch (BadImageFormatException)
                {
                    malformedDocuments++;
                    targetDocumentMalformed |=
                        MetadataTokens.GetRowNumber(handle)
                        == corruptedDocumentRow
                        && corruptTarget;
                }
            }
        }

        Assert.Equal(1, malformedDocuments);
        Assert.Equal(
            !corruptTarget,
            targetDocumentReadable);
        Assert.Equal(
            corruptTarget,
            targetDocumentMalformed);
        return bytes;
    }

    static byte[] EmptyDocumentName(
        string pdbPath,
        string targetFileName)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int documentNameOffset = -1;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                Document document =
                    reader.GetDocument(handle);
                string name =
                    reader.GetString(document.Name);
                if (!name.EndsWith(
                    targetFileName,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                documentNameOffset =
                    MetadataTokens.GetHeapOffset(
                        (BlobHandle)document.Name);
                break;
            }
        }

        Assert.True(documentNameOffset >= 0);
        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + documentNameOffset);
        Assert.Equal(
            1,
            CompressedIntegerPrefixSize(
                bytes[blobEntryOffset]));
        bytes[blobEntryOffset] = 1;

        using var corruptedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader corruptedReader =
            corruptedProvider.GetMetadataReader();
        Assert.Single(
            corruptedReader.Documents,
            handle =>
                corruptedReader.GetString(
                    corruptedReader
                        .GetDocument(handle).Name)
                    .Length == 0);
        return bytes;
    }

    static byte[] RejectUnrelatedTypeName(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        int metadataOffset;
        int typeNameOffset = -1;
        using (var stream =
               new MemoryStream(
                   bytes,
                   writable: false))
        using (var reader = new PEReader(stream))
        {
            metadataOffset =
                reader.PEHeaders.MetadataStartOffset;
            MetadataReader metadata =
                reader.GetMetadataReader();
            foreach (TypeDefinitionHandle handle
                in metadata.TypeDefinitions)
            {
                TypeDefinition type =
                    metadata.GetTypeDefinition(handle);
                string name =
                    metadata.GetString(type.Name);
                string typeNamespace =
                    metadata.GetString(type.Namespace);
                if (name is "<Module>"
                    or nameof(SourceFixture)
                    || typeNamespace.Length == 0
                    || !type.GetDeclaringType().IsNil)
                {
                    continue;
                }

                typeNameOffset =
                    MetadataTokens.GetHeapOffset(
                        type.Name);
                break;
            }
        }

        Assert.True(typeNameOffset > 0);
        int stringsOffset =
            checked(
                metadataOffset
                + FindMetadataStreamOffset(
                    bytes,
                    "#Strings",
                    metadataOffset));
        bytes[stringsOffset + typeNameOffset] = 0;

        using var corruptedStream =
            new MemoryStream(
                bytes,
                writable: false);
        using var corruptedReader =
            new PEReader(corruptedStream);
        MetadataReader corruptedMetadata =
            corruptedReader.GetMetadataReader();
        Assert.Contains(
            corruptedMetadata.TypeDefinitions,
            handle =>
                corruptedMetadata.GetString(
                    corruptedMetadata
                        .GetTypeDefinition(handle).Name)
                    .Length == 0);
        return bytes;
    }

    static byte[] CorruptMethodSequencePoints(
        string pdbPath,
        int metadataToken)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        var methodHandle =
            MetadataTokens.MethodDefinitionHandle(
                metadataToken & 0x00ff_ffff);
        int sequencePointsOffset;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            MethodDebugInformation debugInfo =
                reader.GetMethodDebugInformation(
                    methodHandle
                        .ToDebugInformationHandle());
            Assert.False(
                debugInfo.SequencePointsBlob.IsNil);
            sequencePointsOffset =
                MetadataTokens.GetHeapOffset(
                    debugInfo.SequencePointsBlob);
        }

        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + sequencePointsOffset);
        int payloadOffset =
            checked(
                blobEntryOffset
                + CompressedIntegerPrefixSize(
                    bytes[blobEntryOffset]));
        bytes[payloadOffset] = 0xff;

        using var corruptedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader corruptedReader =
            corruptedProvider.GetMetadataReader();
        Assert.Throws<BadImageFormatException>(
            () =>
            {
                foreach (SequencePoint _ in
                    corruptedReader
                        .GetMethodDebugInformation(
                            methodHandle
                                .ToDebugInformationHandle())
                        .GetSequencePoints())
                {
                }
            });
        return bytes;
    }

    static byte[] CorruptMethodBody(
        byte[] original,
        int metadataToken)
    {
        byte[] bytes = (byte[])original.Clone();
        int bodyRva;
        int bodyOffset;
        using (var reader =
               new PEReader(
                   new MemoryStream(
                       bytes,
                       writable: false)))
        {
            var handle =
                (MethodDefinitionHandle)
                    MetadataTokens.EntityHandle(
                        metadataToken);
            bodyRva =
                reader.GetMetadataReader()
                    .GetMethodDefinition(handle)
                    .RelativeVirtualAddress;
            SectionHeader section =
                Assert.Single(
                    reader.PEHeaders.SectionHeaders,
                    candidate =>
                        bodyRva >= candidate.VirtualAddress
                        && bodyRva
                            < candidate.VirtualAddress
                                + Math.Max(
                                    candidate.VirtualSize,
                                    candidate.SizeOfRawData));
            bodyOffset =
                checked(
                    section.PointerToRawData
                    + bodyRva
                    - section.VirtualAddress);
        }

        bytes[bodyOffset] = 0;
        using var corrupted =
            new PEReader(
                new MemoryStream(
                    bytes,
                    writable: false));
        Assert.Throws<BadImageFormatException>(
            () => corrupted.GetMethodBody(bodyRva));
        return bytes;
    }

    static int FindMetadataStreamOffset(
        byte[] metadata,
        string requestedName,
        int metadataOffset = 0)
    {
        int versionLength =
            BinaryPrimitives.ReadInt32LittleEndian(
                metadata.AsSpan(
                    metadataOffset + 12,
                    sizeof(int)));
        int cursor =
            checked(
                metadataOffset
                + 16
                + versionLength
                + 2);
        ushort streamCount =
            BinaryPrimitives.ReadUInt16LittleEndian(
                metadata.AsSpan(cursor, sizeof(ushort)));
        cursor += sizeof(ushort);
        for (int i = 0; i < streamCount; i++)
        {
            int offset =
                BinaryPrimitives.ReadInt32LittleEndian(
                    metadata.AsSpan(cursor, sizeof(int)));
            cursor += 2 * sizeof(int);
            int nameStart = cursor;
            while (metadata[cursor] != 0)
                cursor++;
            string name =
                Encoding.ASCII.GetString(
                    metadata,
                    nameStart,
                    cursor - nameStart);
            cursor =
                checked((cursor + 4) & ~3);
            if (name == requestedName)
                return offset;
        }

        throw new InvalidOperationException(
            $"Metadata stream '{requestedName}' is unavailable.");
    }

    static int CompressedIntegerPrefixSize(byte first)
        => first switch
        {
            < 0x80 => 1,
            _ when ((first & 0xc0) == 0x80) => 2,
            _ when ((first & 0xe0) == 0xc0) => 4,
            _ => throw new BadImageFormatException(
                "Invalid compressed integer."),
        };

    static byte[] UnsupportedMemorySafetyImage()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MemorySafetyRulesObservation observation = Assert.Single(
            MemorySafetyMetadataIndex.Create(reader)
                .Rules
                .Observations);
        CustomAttribute attribute = reader.GetCustomAttribute(
            (CustomAttributeHandle)MetadataTokens.EntityHandle(
                observation.AttributeToken));
        byte[] original = reader.GetBlobBytes(attribute.Value);
        int valueOffset = Assert.Single(
            Enumerable.Range(0, image.Length - original.Length + 1),
            offset => image
                    .AsSpan(offset, original.Length)
                    .SequenceEqual(original));
        BitConverter.TryWriteBytes(
            image.AsSpan(valueOffset + 2, sizeof(int)),
            99);
        return image;
    }
}
