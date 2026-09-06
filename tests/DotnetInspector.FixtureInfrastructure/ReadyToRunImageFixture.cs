using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Fixtures;

/// <summary>
/// Constructs synthetic ReadyToRun images by rewriting a caller-supplied managed assembly.
///
/// The bytes are built here rather than in one test project so the metadata layer's unit tests and
/// the CLI's public-command tests exercise the same geometry without either taking a dependency on
/// the other's test executable. Nothing here references product code: ReadyToRun section
/// identifiers, header flags, and COR flags are raw numbers, and each consuming suite maps them
/// back to its own enums. That keeps this project free of production dependencies, which is what
/// lets fixtures be built and shared before the product surface they describe exists.
/// </summary>
public static class ReadyToRunImageFixture
{
    /// <summary>The <c>RTR\0</c> ReadyToRun header signature.</summary>
    public const uint Signature = 0x00525452;

    /// <summary>Bytes preceding the section directory in a ReadyToRun header.</summary>
    public const int FixedHeaderSize = 16;

    /// <summary>Bytes in one ReadyToRun section-directory entry.</summary>
    public const int SectionEntrySize = 12;

    /// <summary>ReadyToRun section identifier for the compiler identifier blob.</summary>
    public const uint CompilerIdentifierSectionType = 100;

    /// <summary>ReadyToRun section identifier for manifest metadata.</summary>
    public const uint ManifestMetadataSectionType = 112;

    /// <summary>The <c>Partial</c> ReadyToRun header flag.</summary>
    public const uint PartialHeaderFlags = 0x0004;

    /// <summary>The <c>ILLibrary</c> COR header flag.</summary>
    public const uint ILLibraryCorFlag = 0x0004;

    /// <summary>The size of a PE export directory.</summary>
    public const int ExportDirectorySize = 40;

    /// <summary>
    /// Rewrites <paramref name="sourceAssemblyPath"/> into a synthetic ReadyToRun image.
    ///
    /// The source assembly is always caller-supplied: this project has no assembly of its own worth
    /// inspecting, and each consuming suite already knows which managed image its scenario needs.
    /// </summary>
    /// <param name="sourceAssemblyPath">A managed PE file to rewrite.</param>
    /// <param name="managedNative">Advertise the header through the managed native header directory.</param>
    /// <param name="exported">Advertise the header through an <c>RTR_HEADER</c> style export.</param>
    /// <param name="setIlLibrary">Set (or clear) the <c>ILLibrary</c> COR flag.</param>
    /// <param name="majorVersion">ReadyToRun major version to encode.</param>
    /// <param name="minorVersion">ReadyToRun minor version to encode.</param>
    /// <param name="flags">Raw ReadyToRun header flags to encode.</param>
    /// <param name="sections">Section directory entries; defaults to a compiler identifier plus a
    /// four-byte manifest stub.</param>
    /// <param name="exportName">The exported symbol name when <paramref name="exported"/> is set.</param>
    /// <param name="manifestAliasesCliMetadata">Point the manifest section at the CLI metadata
    /// directory instead of its own payload.</param>
    public static SyntheticReadyToRunImage Create(
        string sourceAssemblyPath,
        bool managedNative,
        bool exported,
        bool setIlLibrary = true,
        ushort majorVersion = 25,
        ushort minorVersion = 0,
        uint flags = PartialHeaderFlags,
        SyntheticReadyToRunSection[]? sections = null,
        string exportName = "RTR_HEADER",
        bool manifestAliasesCliMetadata = false)
    {
        ArgumentNullException.ThrowIfNull(sourceAssemblyPath);
        ArgumentNullException.ThrowIfNull(exportName);

        sections ??=
        [
            new(CompilerIdentifierSectionType, [1, 2, 3]),
            new(ManifestMetadataSectionType, "BSJB"u8.ToArray()),
        ];

        byte[] bytes = File.ReadAllBytes(sourceAssemblyPath);
        using var original = Open(bytes);
        PEHeaders headers = original.PEHeaders;
        PEHeader peHeader = headers.PEHeader!;

        int lastSectionIndex = headers.SectionHeaders
            .Select(static (section, index) => (Section: section, Index: index))
            .OrderBy(static item => item.Section.VirtualAddress)
            .Last()
            .Index;
        SectionHeader section = headers.SectionHeaders[lastSectionIndex];

        int sectionHeadersOffset = headers.PEHeaderStartOffset + headers.CoffHeader.SizeOfOptionalHeader;
        int sectionHeaderOffset = sectionHeadersOffset + (lastSectionIndex * 40);
        int exportDataDirectoryOffset =
            headers.PEHeaderStartOffset + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        int managedNativeDirectoryOffset = headers.CorHeaderStartOffset + 64;

        Array.Clear(bytes, exportDataDirectoryOffset, 8);
        Array.Clear(bytes, managedNativeDirectoryOffset, 8);

        uint corFlags = ReadUInt32(bytes, headers.CorHeaderStartOffset + 16);
        WriteUInt32(
            bytes,
            headers.CorHeaderStartOffset + 16,
            setIlLibrary ? corFlags | ILLibraryCorFlag : corFlags & ~ILLibraryCorFlag);

        int payloadOffset = Align(Math.Max(bytes.Length, section.PointerToRawData + section.SizeOfRawData), 4);
        int exportPrefixSize = exported
            ? Align(ExportDirectorySize + 4 + 4 + 2 + exportName.Length + 1, 4)
            : 0;
        int headerOffset = payloadOffset + exportPrefixSize;
        int headerSize = FixedHeaderSize + (sections.Length * SectionEntrySize);
        int contentOffset = Align(headerOffset + headerSize, 4);

        var sectionOffsets = new int[sections.Length];
        int cursor = contentOffset;
        for (int i = 0; i < sections.Length; i++)
        {
            sectionOffsets[i] = cursor;
            cursor = Align(cursor + sections[i].Content.Length, 4);
        }

        int rawEnd = cursor;
        int rawSize = Align(rawEnd - section.PointerToRawData, peHeader.FileAlignment);
        int imageLength = Math.Max(bytes.Length, section.PointerToRawData + rawSize);
        Array.Resize(ref bytes, imageLength);

        int virtualSize = Math.Max(section.VirtualSize, rawEnd - section.PointerToRawData);
        WriteUInt32(bytes, sectionHeaderOffset + 8, (uint)virtualSize);
        WriteUInt32(bytes, sectionHeaderOffset + 16, (uint)rawSize);
        WriteUInt32(
            bytes,
            headers.PEHeaderStartOffset + 56,
            (uint)Align(section.VirtualAddress + virtualSize, peHeader.SectionAlignment));

        int headerRva = ToRva(section, headerOffset);
        int exportDirectoryOffset = payloadOffset;
        int exportDirectoryRva = ToRva(section, exportDirectoryOffset);
        int exportFunctionOffset = exportDirectoryOffset + ExportDirectorySize;
        int exportNamesOffset = exportFunctionOffset + 4;
        int exportOrdinalOffset = exportNamesOffset + 4;
        int exportNameOffset = exportOrdinalOffset + 2;

        if (exported)
        {
            WriteUInt32(bytes, exportDataDirectoryOffset, (uint)exportDirectoryRva);
            WriteUInt32(bytes, exportDataDirectoryOffset + 4, ExportDirectorySize);

            WriteUInt32(bytes, exportDirectoryOffset + 20, 1);
            WriteUInt32(bytes, exportDirectoryOffset + 24, 1);
            WriteUInt32(bytes, exportDirectoryOffset + 28, (uint)ToRva(section, exportFunctionOffset));
            WriteUInt32(bytes, exportDirectoryOffset + 32, (uint)ToRva(section, exportNamesOffset));
            WriteUInt32(bytes, exportDirectoryOffset + 36, (uint)ToRva(section, exportOrdinalOffset));
            WriteUInt32(bytes, exportFunctionOffset, (uint)headerRva);
            WriteUInt32(bytes, exportNamesOffset, (uint)ToRva(section, exportNameOffset));
            WriteUInt16(bytes, exportOrdinalOffset, 0);
            System.Text.Encoding.ASCII.GetBytes(exportName, bytes.AsSpan(exportNameOffset));
            bytes[exportNameOffset + exportName.Length] = 0;
        }

        WriteUInt32(bytes, headerOffset, Signature);
        WriteUInt16(bytes, headerOffset + 4, majorVersion);
        WriteUInt16(bytes, headerOffset + 6, minorVersion);
        WriteUInt32(bytes, headerOffset + 8, flags);
        WriteUInt32(bytes, headerOffset + 12, (uint)sections.Length);

        for (int i = 0; i < sections.Length; i++)
        {
            SyntheticReadyToRunSection spec = sections[i];
            int entryOffset = headerOffset + FixedHeaderSize + (i * SectionEntrySize);
            bool aliasesCliMetadata =
                manifestAliasesCliMetadata &&
                spec.Type == ManifestMetadataSectionType;
            int sectionRva = aliasesCliMetadata
                ? headers.CorHeader!.MetadataDirectory.RelativeVirtualAddress
                : spec.RelativeVirtualAddress ?? ToRva(section, sectionOffsets[i]);
            int sectionSize = aliasesCliMetadata
                ? headers.CorHeader!.MetadataDirectory.Size
                : spec.Content.Length;

            WriteUInt32(bytes, entryOffset, spec.Type);
            WriteUInt32(bytes, entryOffset + 4, (uint)sectionRva);
            WriteUInt32(bytes, entryOffset + 8, (uint)sectionSize);
            if (!aliasesCliMetadata)
                spec.Content.CopyTo(bytes, sectionOffsets[i]);
        }

        if (managedNative)
        {
            WriteUInt32(bytes, managedNativeDirectoryOffset, (uint)headerRva);
            WriteUInt32(bytes, managedNativeDirectoryOffset + 4, (uint)headerSize);
        }

        return new SyntheticReadyToRunImage(
            bytes,
            headerOffset,
            headerRva,
            managedNativeDirectoryOffset,
            exportDataDirectoryOffset,
            exportDirectoryOffset,
            exportDirectoryRva,
            exportFunctionOffset,
            exportOrdinalOffset);
    }

    /// <summary>
    /// Builds a well-formed metadata root suitable for a manifest section.
    ///
    /// The rows are deliberately independent of any real assembly's rows, so a consumer can prove a
    /// projection came from the manifest rather than from the CLI metadata beside it: the module
    /// name, both assembly references, and the type reference exist nowhere else.
    /// </summary>
    public static byte[] BuildManifestMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0, metadata.GetOrAddString("ManifestModule"),
            metadata.GetOrAddGuid(new Guid("42f15aaf-e64c-492d-9707-5892c0e7c412")), default, default);
        var dependency = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Manifest.Dependency"), new Version(1, 0, 0, 0),
            default, default, 0, default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Manifest.Neighbor"), new Version(2, 0, 0, 0),
            default, default, 0, default);
        metadata.AddTypeReference(
            dependency, metadata.GetOrAddString("ManifestOnly"), metadata.GetOrAddString("Widget"));
        var blob = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(blob, 0, 0);
        return blob.ToArray();
    }

    /// <summary>The module name <see cref="BuildManifestMetadata"/> writes.</summary>
    public const string ManifestModuleName = "ManifestModule";

    /// <summary>The first assembly reference name <see cref="BuildManifestMetadata"/> writes.</summary>
    public const string ManifestDependencyName = "Manifest.Dependency";

    /// <summary>The second assembly reference name <see cref="BuildManifestMetadata"/> writes.</summary>
    public const string ManifestNeighborName = "Manifest.Neighbor";

    /// <summary>The type reference namespace <see cref="BuildManifestMetadata"/> writes.</summary>
    public const string ManifestTypeNamespace = "ManifestOnly";

    /// <summary>The type reference name <see cref="BuildManifestMetadata"/> writes.</summary>
    public const string ManifestTypeName = "Widget";

    static PEReader Open(byte[] bytes) => new(new MemoryStream(bytes));

    static int ToRva(SectionHeader section, int imageOffset)
        => checked(section.VirtualAddress + imageOffset - section.PointerToRawData);

    static int Align(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);

    static uint ReadUInt32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));

    static void WriteUInt16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

    static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);
}

/// <summary>
/// A synthetic ReadyToRun image plus the offsets a test needs to corrupt one field of it.
/// </summary>
public sealed record SyntheticReadyToRunImage(
    byte[] Bytes,
    int HeaderOffset,
    int HeaderRva,
    int ManagedNativeDirectoryOffset,
    int ExportDataDirectoryOffset,
    int ExportDirectoryOffset,
    int ExportDirectoryRva,
    int ExportFunctionOffset,
    int ExportOrdinalOffset);

/// <summary>
/// One requested ReadyToRun section. <paramref name="Type"/> is the raw section identifier so this
/// project needs no product enum; <paramref name="RelativeVirtualAddress"/> overrides the address
/// the payload would otherwise be written at, which is how a test describes a section pointing
/// outside the image.
/// </summary>
public readonly record struct SyntheticReadyToRunSection(
    uint Type,
    byte[] Content,
    int? RelativeVirtualAddress = null);
