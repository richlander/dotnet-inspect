using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Discovers and validates the ReadyToRun header and section directory of one
/// Portable Executable image.
/// </summary>
public static class ReadyToRunImageInspector
{
    public const uint Signature = 0x00525452;
    public const int FixedHeaderSize = 16;
    public const int SectionEntrySize = 12;
    public const int MaxSectionCount = 4_096;
    public const int MaxExportNameCount = 65_536;

    const int ExportDirectorySize = 40;

    /// <summary>
    /// Describes the image's ReadyToRun envelope, or returns
    /// <see langword="null"/> when neither canonical advertisement is present.
    ///
    /// Once an advertisement is present, malformed structures and inspection
    /// budget exhaustion throw <see cref="BadImageFormatException"/> rather
    /// than becoming an absent result.
    /// </summary>
    public static ReadyToRunImageOverview? Describe(PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        DirectoryEntry? managedNative = GetManagedNativeAdvertisement(peReader);
        int? exportedHeaderRva = FindExportedHeaderRva(peReader);

        if (managedNative is null && exportedHeaderRva is null)
            return null;

        if (managedNative is { } managed &&
            exportedHeaderRva is { } exported &&
            managed.RelativeVirtualAddress != exported)
        {
            throw Malformed(
                $"The managed-native header RVA 0x{managed.RelativeVirtualAddress:X8} " +
                $"does not match RTR_HEADER RVA 0x{exported:X8}.");
        }

        int headerRva = exportedHeaderRva ?? managedNative!.Value.RelativeVirtualAddress;
        return ReadOverview(peReader, headerRva, managedNative, exportedHeaderRva);
    }

    static DirectoryEntry? GetManagedNativeAdvertisement(PEReader peReader)
    {
        PEHeaders headers = peReader.PEHeaders;
        if (headers.CorHeader is not { } corHeader)
            return null;

        DirectoryEntry directory = corHeader.ManagedNativeHeaderDirectory;
        bool hasRva = directory.RelativeVirtualAddress != 0;
        bool hasSize = directory.Size != 0;

        if (hasRva != hasSize)
            throw Malformed("The CLI managed-native header directory is only partially populated.");

        if (!hasRva)
            return null;

        if (directory.RelativeVirtualAddress < 0 || directory.Size < 0)
            throw Malformed("The CLI managed-native header directory has an unsupported 32-bit extent.");

        // ManagedNativeHeaderDirectory is a generic CLI slot also used by
        // legacy native-image formats. It becomes an R2R advertisement only
        // when it is large enough for the fixed header and carries RTR\0.
        if (directory.Size < FixedHeaderSize)
            return null;

        BlobReader candidate = GetRawReader(
            peReader,
            directory.RelativeVirtualAddress,
            sizeof(uint),
            "CLI managed-native header signature");

        return candidate.ReadUInt32() == Signature ? directory : null;
    }

    static int? FindExportedHeaderRva(PEReader peReader)
    {
        DirectoryEntry directory = peReader.PEHeaders.PEHeader?.ExportTableDirectory ?? default;
        bool hasRva = directory.RelativeVirtualAddress != 0;
        bool hasSize = directory.Size != 0;

        if (hasRva != hasSize)
            throw Malformed("The PE export directory is only partially populated.");

        if (!hasRva)
            return null;

        if (directory.RelativeVirtualAddress < 0 || directory.Size < ExportDirectorySize)
            throw Malformed("The PE export directory cannot contain its fixed header.");

        ValidateRawExtent(
            peReader,
            directory.RelativeVirtualAddress,
            directory.Size,
            "PE export directory");

        BlobReader export = GetRawReader(
            peReader,
            directory.RelativeVirtualAddress,
            ExportDirectorySize,
            "PE export directory header");

        export.Offset = 20;
        uint functionCount = export.ReadUInt32();
        uint nameCount = export.ReadUInt32();
        int functionsRva = ReadSupportedRva(ref export, "export address table");
        int namesRva = ReadSupportedRva(ref export, "export name-pointer table");
        int ordinalsRva = ReadSupportedRva(ref export, "export name-ordinal table");

        if (nameCount > MaxExportNameCount)
        {
            throw Malformed(
                $"The PE export directory declares {nameCount} names, above the " +
                $"{MaxExportNameCount} inspection bound.");
        }

        if (nameCount == 0)
            return null;

        int nameTableSize = CheckedTableSize(nameCount, sizeof(uint), "export name-pointer table");
        int ordinalTableSize = CheckedTableSize(nameCount, sizeof(ushort), "export name-ordinal table");
        BlobReader names = GetRawReader(peReader, namesRva, nameTableSize, "export name-pointer table");
        BlobReader ordinals = GetRawReader(peReader, ordinalsRva, ordinalTableSize, "export name-ordinal table");

        for (uint i = 0; i < nameCount; i++)
        {
            int nameRva = ReadSupportedRva(ref names, "export name");
            ushort ordinalIndex = ordinals.ReadUInt16();

            if (!IsCanonicalHeaderName(peReader, nameRva))
                continue;

            if (ordinalIndex >= functionCount)
            {
                throw Malformed(
                    $"RTR_HEADER has address-table index {ordinalIndex}, but the table " +
                    $"contains {functionCount} entries.");
            }

            int functionEntryRva = CheckedTableEntryRva(
                functionsRva,
                ordinalIndex,
                sizeof(uint),
                "export address table");
            BlobReader functionEntry = GetRawReader(
                peReader,
                functionEntryRva,
                sizeof(uint),
                "RTR_HEADER export address");
            int headerRva = ReadSupportedRva(ref functionEntry, "RTR_HEADER");

            long exportEnd = (long)directory.RelativeVirtualAddress + directory.Size;
            if (headerRva >= directory.RelativeVirtualAddress && headerRva < exportEnd)
                throw Malformed("RTR_HEADER is a forwarded export rather than an image RVA.");

            return headerRva;
        }

        return null;
    }

    static ReadyToRunImageOverview ReadOverview(
        PEReader peReader,
        int headerRva,
        DirectoryEntry? managedNative,
        int? exportedHeaderRva)
    {
        if (managedNative is { } managed)
        {
            ValidateRawExtent(
                peReader,
                managed.RelativeVirtualAddress,
                managed.Size,
                "CLI managed-native header directory");
        }

        BlobReader fixedHeader = GetRawReader(
            peReader,
            headerRva,
            FixedHeaderSize,
            "ReadyToRun fixed header");

        uint signature = fixedHeader.ReadUInt32();
        if (signature != Signature)
        {
            throw Malformed(
                $"The advertised ReadyToRun header has signature 0x{signature:X8}, " +
                $"expected 0x{Signature:X8}.");
        }

        ushort majorVersion = fixedHeader.ReadUInt16();
        ushort minorVersion = fixedHeader.ReadUInt16();
        var flags = (ReadyToRunHeaderFlags)fixedHeader.ReadUInt32();
        uint sectionCount = fixedHeader.ReadUInt32();

        if (sectionCount > MaxSectionCount)
        {
            throw Malformed(
                $"The ReadyToRun header declares {sectionCount} sections, above the " +
                $"{MaxSectionCount} inspection bound.");
        }

        int encodedSize = checked(FixedHeaderSize + ((int)sectionCount * SectionEntrySize));
        if (managedNative is { } advertised && encodedSize > advertised.Size)
        {
            throw Malformed(
                $"The ReadyToRun header requires {encodedSize} bytes, but the " +
                $"managed-native directory advertises {advertised.Size}.");
        }

        BlobReader header = GetRawReader(
            peReader,
            headerRva,
            encodedSize,
            "ReadyToRun header and section directory");
        header.Offset = FixedHeaderSize;

        var sections = ImmutableArray.CreateBuilder<ReadyToRunSectionSummary>((int)sectionCount);
        uint previousType = 0;
        DirectoryEntry cliMetadata = peReader.PEHeaders.CorHeader?.MetadataDirectory ?? default;

        for (uint i = 0; i < sectionCount; i++)
        {
            uint type = header.ReadUInt32();
            int sectionRva = ReadSupportedRva(ref header, $"ReadyToRun section {type}");
            int sectionSize = ReadSupportedSize(ref header, $"ReadyToRun section {type}");

            if (i > 0 && type <= previousType)
            {
                throw Malformed(
                    $"ReadyToRun section type {type} is not strictly greater than " +
                    $"the preceding type {previousType}.");
            }

            if (sectionSize != 0)
            {
                ValidateRawExtent(
                    peReader,
                    sectionRva,
                    sectionSize,
                    $"ReadyToRun section {type}");
            }

            sections.Add(new ReadyToRunSectionSummary(
                (ReadyToRunSectionType)type,
                sectionRva,
                sectionSize,
                sectionSize != 0 &&
                sectionRva == cliMetadata.RelativeVirtualAddress &&
                sectionSize == cliMetadata.Size));
            previousType = type;
        }

        ImmutableArray<ReadyToRunSectionSummary> sectionArray = sections.MoveToImmutable();

        ReadyToRunAdvertisement advertisements = ReadyToRunAdvertisement.None;
        if (managedNative is not null)
            advertisements |= ReadyToRunAdvertisement.ManagedNativeHeader;
        if (exportedHeaderRva is not null)
            advertisements |= ReadyToRunAdvertisement.Export;

        ReadyToRunImageRole role = ClassifyRole(flags, sectionArray, exportedHeaderRva is not null);

        return new ReadyToRunImageOverview(
            role,
            advertisements,
            managedNative,
            exportedHeaderRva,
            headerRva,
            encodedSize,
            signature,
            majorVersion,
            minorVersion,
            flags,
            sectionArray);
    }

    static ReadyToRunImageRole ClassifyRole(
        ReadyToRunHeaderFlags flags,
        ImmutableArray<ReadyToRunSectionSummary> sections,
        bool hasHeaderExport)
    {
        bool hasComponentEvidence =
            flags.HasFlag(ReadyToRunHeaderFlags.Component) ||
            sections.Any(static section => section.Type == ReadyToRunSectionType.OwnerCompositeExecutable);
        bool hasCompositeEvidence =
            hasHeaderExport ||
            sections.Any(static section => section.Type == ReadyToRunSectionType.ComponentAssemblies);

        if (hasComponentEvidence && hasCompositeEvidence)
            return ReadyToRunImageRole.Ambiguous;

        if (hasComponentEvidence)
            return ReadyToRunImageRole.Component;
        if (hasCompositeEvidence)
            return ReadyToRunImageRole.Composite;
        return ReadyToRunImageRole.Standalone;
    }

    static bool IsCanonicalHeaderName(PEReader peReader, int nameRva)
    {
        BlobReader name = GetRawReaderToSectionEnd(peReader, nameRva, "PE export name");

        ReadOnlySpan<byte> expected = "RTR_HEADER"u8;
        foreach (byte value in expected)
        {
            if (name.RemainingBytes == 0)
                throw Malformed("A PE export name is not null-terminated in raw image bytes.");
            if (name.ReadByte() != value)
                return false;
        }

        if (name.RemainingBytes == 0)
            throw Malformed("A PE export name is not null-terminated in raw image bytes.");
        return name.ReadByte() == 0;
    }

    static int CheckedTableSize(uint count, int entrySize, string description)
    {
        long size = (long)count * entrySize;
        if (size > int.MaxValue)
            throw Malformed($"The {description} exceeds the supported image size.");
        return (int)size;
    }

    static int CheckedTableEntryRva(int tableRva, uint index, int entrySize, string description)
    {
        long rva = (long)tableRva + ((long)index * entrySize);
        if (rva > int.MaxValue)
            throw Malformed($"The {description} entry RVA exceeds the supported image range.");
        return (int)rva;
    }

    static int ReadSupportedRva(ref BlobReader reader, string description)
    {
        uint value = reader.ReadUInt32();
        if (value > int.MaxValue)
            throw Malformed($"The {description} RVA exceeds the supported image range.");
        return (int)value;
    }

    static int ReadSupportedSize(ref BlobReader reader, string description)
    {
        uint value = reader.ReadUInt32();
        if (value > int.MaxValue)
            throw Malformed($"The {description} size exceeds the supported image range.");
        return (int)value;
    }

    static BlobReader GetRawReader(
        PEReader peReader,
        int relativeVirtualAddress,
        int size,
        string description)
    {
        int offset = ValidateRawExtent(peReader, relativeVirtualAddress, size, description);
        return peReader.GetEntireImage().GetReader(offset, size);
    }

    static BlobReader GetRawReaderToSectionEnd(
        PEReader peReader,
        int relativeVirtualAddress,
        string description)
    {
        if (relativeVirtualAddress < 0)
            throw Malformed($"The {description} has a negative RVA.");

        int sectionIndex = peReader.PEHeaders.GetContainingSectionIndex(relativeVirtualAddress);
        if (sectionIndex < 0)
            throw Malformed($"The {description} RVA does not belong to a PE section.");

        SectionHeader section = peReader.PEHeaders.SectionHeaders[sectionIndex];
        long sectionOffset = (long)relativeVirtualAddress - section.VirtualAddress;
        long remaining = (long)section.SizeOfRawData - sectionOffset;
        if (sectionOffset < 0 || remaining <= 0 || remaining > int.MaxValue)
            throw Malformed($"The {description} is not backed by PE section bytes.");

        int imageOffset = ValidateRawExtent(
            peReader,
            relativeVirtualAddress,
            (int)remaining,
            description);
        return peReader.GetEntireImage().GetReader(imageOffset, (int)remaining);
    }

    static int ValidateRawExtent(
        PEReader peReader,
        int relativeVirtualAddress,
        int size,
        string description)
    {
        if (relativeVirtualAddress < 0 || size < 0)
            throw Malformed($"The {description} has a negative RVA or size.");

        int sectionIndex = peReader.PEHeaders.GetContainingSectionIndex(relativeVirtualAddress);
        if (sectionIndex < 0)
            throw Malformed($"The {description} RVA does not belong to a PE section.");

        SectionHeader section = peReader.PEHeaders.SectionHeaders[sectionIndex];
        long sectionOffset = (long)relativeVirtualAddress - section.VirtualAddress;
        long sectionEnd = sectionOffset + size;
        if (sectionOffset < 0 || sectionEnd > section.SizeOfRawData)
            throw Malformed($"The {description} is not fully backed by PE section bytes.");

        long imageOffset = (long)section.PointerToRawData + sectionOffset;
        long imageEnd = imageOffset + size;
        if (imageOffset < 0 || imageEnd > peReader.GetEntireImage().Length)
            throw Malformed($"The {description} extends beyond the PE image.");

        return (int)imageOffset;
    }

    static BadImageFormatException Malformed(string message)
        => new($"Malformed ReadyToRun image: {message}");
}
