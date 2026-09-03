using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace NuGetFetch;

internal sealed record LocalPackageArchive(
    PackageSourceCoordinate Coordinate,
    string AuthoredId,
    string AuthoredVersion,
    string? Description,
    string? Tags,
    ReadOnlyMemory<byte> Manifest);

internal static class LocalPackageArchiveReader
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint CentralDirectoryEntrySignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralDirectoryEntryLength = 46;
    private const int LocalFileHeaderLength = 30;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const ushort SupportedManifestFlags =
        (1 << 1) | (1 << 2) | (1 << 3) | (1 << 11);
    private const ushort CorrespondingManifestFlags =
        (1 << 3) | (1 << 11);

    public static async Task<LocalPackageArchive> ReadAsync(
        Stream stream,
        long advertisedLength,
        LocalPackageSourceOptions options,
        LocalPackageSourceLedger ledger,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(operation);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new IOException(
                "The local package source returned a non-seekable archive stream.");
        }

        long length;
        try
        {
            length = stream.Length;
        }
        catch (Exception exception) when (exception is NotSupportedException)
        {
            throw new IOException(
                "The local package source could not observe archive length.",
                exception);
        }

        if (length != advertisedLength)
        {
            throw new IOException(
                "The local package archive changed after it was observed.");
        }

        if (length > options.MaxPackageBytes)
            throw new LocalPackageSourceLimitExceededException();

        ZipDirectory directory = await ReadDirectoryAsync(
            stream,
            length,
            options,
            operation).ConfigureAwait(false);

        try
        {
            CentralDirectoryEntry manifest =
                await ReadCentralDirectoryEntryAsync(
                    stream,
                    directory,
                    options,
                    ledger.RemainingManifestBytes,
                    operation).ConfigureAwait(false);
            byte[] content = await ReadManifestContentAsync(
                stream,
                directory,
                manifest,
                Math.Min(
                    options.MaxManifestBytes,
                    ledger.RemainingManifestBytes),
                operation).ConfigureAwait(false);
            ledger.ChargeManifestBytes(content.Length);

            operation.ThrowIfExpired();
            return ParseManifest(content);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException(
                "The package manifest is not valid XML.",
                exception);
        }
    }

    private static async Task<ZipDirectory> ReadDirectoryAsync(
        Stream stream,
        long length,
        LocalPackageSourceOptions options,
        NuGetOperationDeadline operation)
    {
        if (length < EndOfCentralDirectoryLength)
        {
            throw new InvalidDataException(
                "The package archive has no end-of-central-directory record.");
        }

        int tailLength = checked((int)Math.Min(
            length,
            EndOfCentralDirectoryLength + MaximumZipCommentLength));
        var tail = new byte[tailLength];
        stream.Position = length - tailLength;
        await ReadExactlyAsync(stream, tail, operation).ConfigureAwait(false);

        for (int offset = tail.Length - EndOfCentralDirectoryLength;
             offset >= 0;
             offset--)
        {
            operation.ThrowIfExpired();
            ReadOnlySpan<byte> record = tail.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record)
                != EndOfCentralDirectorySignature)
            {
                continue;
            }

            ushort commentLength =
                BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
            if (offset + EndOfCentralDirectoryLength + commentLength
                != tail.Length)
            {
                continue;
            }

            ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
            ushort centralDisk =
                BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
            ushort entriesOnDisk =
                BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            ushort entryCount =
                BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            uint directoryLength =
                BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            uint directoryOffset =
                BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            if (entryCount > options.MaxArchiveEntries
                || directoryLength > options.MaxCentralDirectoryBytes)
            {
                throw new LocalPackageSourceLimitExceededException();
            }

            if (disk != 0
                || centralDisk != 0
                || entriesOnDisk != entryCount
                || entryCount == ushort.MaxValue
                || directoryLength == uint.MaxValue
                || directoryOffset == uint.MaxValue)
            {
                throw new InvalidDataException(
                    "The package archive uses an unsupported or inconsistent central directory.");
            }

            long recordOffset = length - tailLength + offset;
            if ((long)directoryOffset + directoryLength != recordOffset)
            {
                throw new InvalidDataException(
                    "The package archive central-directory extent is inconsistent.");
            }

            return new ZipDirectory(
                entryCount,
                directoryLength,
                directoryOffset);
        }

        throw new InvalidDataException(
            "The package archive has no valid end-of-central-directory record.");
    }

    private static async Task<CentralDirectoryEntry>
        ReadCentralDirectoryEntryAsync(
        Stream stream,
        ZipDirectory directory,
        LocalPackageSourceOptions options,
        long remainingManifestBytes,
        NuGetOperationDeadline operation)
    {
        var bytes = new byte[checked((int)directory.ByteLength)];
        stream.Position = directory.Offset;
        await ReadExactlyAsync(stream, bytes, operation).ConfigureAwait(false);

        int offset = 0;
        CentralDirectoryEntry? selected = null;
        for (int index = 0; index < directory.EntryCount; index++)
        {
            operation.ThrowIfExpired();
            if (offset > bytes.Length - CentralDirectoryEntryLength
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(offset))
                    != CentralDirectoryEntrySignature)
            {
                throw new InvalidDataException(
                    "The package archive central directory is malformed.");
            }

            ReadOnlySpan<byte> record = bytes.AsSpan(offset);
            ushort flags =
                BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            ushort method =
                BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            uint crc =
                BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            uint compressedLength =
                BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            uint expandedLength =
                BinaryPrimitives.ReadUInt32LittleEndian(record[24..]);
            ushort nameLength =
                BinaryPrimitives.ReadUInt16LittleEndian(record[28..]);
            ushort extraLength =
                BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
            ushort commentLength =
                BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
            ushort disk =
                BinaryPrimitives.ReadUInt16LittleEndian(record[34..]);
            uint localHeaderOffset =
                BinaryPrimitives.ReadUInt32LittleEndian(record[42..]);
            int recordLength = checked(
                CentralDirectoryEntryLength
                + nameLength
                + extraLength
                + commentLength);
            if (recordLength > bytes.Length - offset
                || disk != 0)
            {
                throw new InvalidDataException(
                    "The package archive central-directory entry is inconsistent.");
            }

            ReadOnlyMemory<byte> name = bytes.AsMemory(
                offset + CentralDirectoryEntryLength,
                nameLength);
            if (IsRootManifest(name.Span))
            {
                if (selected is not null)
                {
                    throw new InvalidDataException(
                        "The package archive contains multiple root manifests.");
                }

                if (HasUnsupportedManifestFlags(flags, method))
                {
                    throw new InvalidDataException(
                        "The package archive uses unsupported manifest flags.");
                }

                if (compressedLength > options.MaxManifestBytes
                    || expandedLength > options.MaxManifestBytes
                    || expandedLength > remainingManifestBytes)
                {
                    throw new LocalPackageSourceLimitExceededException();
                }

                selected = new CentralDirectoryEntry(
                    flags,
                    method,
                    crc,
                    compressedLength,
                    expandedLength,
                    localHeaderOffset,
                    name.ToArray());
            }

            offset += recordLength;
        }

        if (offset != bytes.Length)
        {
            throw new InvalidDataException(
                "The package archive central-directory extent is inconsistent.");
        }

        return selected
            ?? throw new InvalidDataException(
                "The package archive does not contain a root manifest.");
    }

    private static async Task<byte[]> ReadManifestContentAsync(
        Stream stream,
        ZipDirectory directory,
        CentralDirectoryEntry entry,
        long maximumExpandedBytes,
        NuGetOperationDeadline operation)
    {
        if (HasUnsupportedManifestFlags(entry.Flags, entry.Method)
            || entry.Method is not 0 and not 8)
        {
            throw new InvalidDataException(
                "The package archive uses an unsupported manifest encoding.");
        }

        var localHeader = new byte[LocalFileHeaderLength];
        stream.Position = entry.LocalHeaderOffset;
        await ReadExactlyAsync(
            stream,
            localHeader,
            operation).ConfigureAwait(false);
        ushort localFlags =
            BinaryPrimitives.ReadUInt16LittleEndian(
                localHeader.AsSpan(6));
        if (BinaryPrimitives.ReadUInt32LittleEndian(localHeader)
                != LocalFileHeaderSignature
            || HasUnsupportedManifestFlags(localFlags, entry.Method)
            || (localFlags & CorrespondingManifestFlags)
                != (entry.Flags & CorrespondingManifestFlags)
            || BinaryPrimitives.ReadUInt16LittleEndian(
                localHeader.AsSpan(8)) != entry.Method)
        {
            throw new InvalidDataException(
                "The package archive local manifest header is inconsistent.");
        }

        ushort nameLength =
            BinaryPrimitives.ReadUInt16LittleEndian(
                localHeader.AsSpan(26));
        ushort extraLength =
            BinaryPrimitives.ReadUInt16LittleEndian(
                localHeader.AsSpan(28));
        var localName = new byte[nameLength];
        await ReadExactlyAsync(
            stream,
            localName,
            operation).ConfigureAwait(false);
        if (!localName.AsSpan().SequenceEqual(entry.Name))
        {
            throw new InvalidDataException(
                "The package archive manifest name is inconsistent.");
        }

        long dataOffset = checked(
            (long)entry.LocalHeaderOffset
            + LocalFileHeaderLength
            + nameLength
            + extraLength);
        if (dataOffset + entry.CompressedLength > directory.Offset)
        {
            throw new InvalidDataException(
                "The package archive manifest extent is inconsistent.");
        }

        if ((entry.Flags & 8) == 0
            && (BinaryPrimitives.ReadUInt32LittleEndian(
                    localHeader.AsSpan(14)) != entry.Crc
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    localHeader.AsSpan(18)) != entry.CompressedLength
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    localHeader.AsSpan(22)) != entry.ExpandedLength))
        {
            throw new InvalidDataException(
                "The package archive manifest declaration is inconsistent.");
        }

        var compressed = new byte[checked((int)entry.CompressedLength)];
        stream.Position = dataOffset;
        await ReadExactlyAsync(
            stream,
            compressed,
            operation).ConfigureAwait(false);
        byte[] content = await ExpandAsync(
            compressed,
            entry.Method,
            maximumExpandedBytes,
            operation).ConfigureAwait(false);
        if (content.LongLength != entry.ExpandedLength
            || ComputeCrc32(content) != entry.Crc)
        {
            throw new InvalidDataException(
                "The package archive manifest content is inconsistent.");
        }

        return content;
    }

    private static bool HasUnsupportedManifestFlags(
        ushort flags,
        ushort method) =>
        (flags & ~SupportedManifestFlags) != 0
        || method != 8 && (flags & ((1 << 1) | (1 << 2))) != 0;

    private static async Task<byte[]> ExpandAsync(
        byte[] compressed,
        ushort method,
        long maximumBytes,
        NuGetOperationDeadline operation)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using Stream content = method == 0
            ? input
            : new DeflateStream(
                input,
                CompressionMode.Decompress,
                leaveOpen: true);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            operation.ThrowIfExpired();
            int read;
            try
            {
                read = await content.ReadAsync(
                    buffer,
                    operation.OperationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                operation.ThrowIfExpired();
                throw new IOException(
                    "The local package source interrupted manifest decompression.",
                    exception);
            }

            if (read == 0)
                break;

            if (output.Length + read > maximumBytes)
                throw new LocalPackageSourceLimitExceededException();

            output.Write(buffer, 0, read);
        }

        operation.ThrowIfExpired();
        return output.ToArray();
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc = Crc32Table[(byte)(crc ^ value)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static readonly uint[] Crc32Table = CreateCrc32Table();

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0
                    ? value >> 1
                    : (value >> 1) ^ 0xedb88320;
            }

            table[index] = value;
        }

        return table;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        NuGetOperationDeadline operation)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            operation.ThrowIfExpired();
            int read;
            try
            {
                read = await stream.ReadAsync(
                    destination[offset..],
                    operation.OperationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                operation.ThrowIfExpired();
                throw new IOException(
                    "The local package source interrupted an archive read.",
                    exception);
            }
            catch
            {
                operation.ThrowIfExpired();
                throw;
            }

            if (read == 0)
            {
                throw new InvalidDataException(
                    "The package archive ended before its declared extent.");
            }

            offset += read;
        }

        operation.ThrowIfExpired();
    }

    private static bool IsRootManifest(ReadOnlySpan<byte> name)
    {
        ReadOnlySpan<byte> suffix = ".nuspec"u8;
        if (name.Length < suffix.Length
            || name.Contains((byte)'/')
            || name.Contains((byte)'\\'))
        {
            return false;
        }

        ReadOnlySpan<byte> ending = name[^suffix.Length..];
        for (int index = 0; index < suffix.Length; index++)
        {
            byte value = ending[index];
            if (value is >= (byte)'A' and <= (byte)'Z')
                value = (byte)(value + ('a' - 'A'));

            if (value != suffix[index])
                return false;
        }

        return true;
    }

    private static LocalPackageArchive ParseManifest(byte[] content)
    {
        using var input = new MemoryStream(content, writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(input, settings);
        XDocument document = XDocument.Load(
            reader,
            LoadOptions.None);
        if (document.Root is null
            || !document.Root.Name.LocalName.Equals(
                "package",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The package manifest has no package root.");
        }

        XElement[] metadataElements = document.Root?.Elements()
            .Where(
                element => element.Name.LocalName.Equals(
                    "metadata",
                    StringComparison.Ordinal))
            .ToArray()
            ?? [];
        XElement metadata = metadataElements.Length switch
        {
            1 => metadataElements[0],
            0 => throw new InvalidDataException(
                "The package manifest has no metadata element."),
            _ => throw new InvalidDataException(
                "The package manifest contains multiple metadata elements."),
        };
        string id = GetSingleValue(metadata, "id");
        string version = GetSingleValue(metadata, "version");
        PackageSourceCoordinate coordinate;
        try
        {
            coordinate = PackageSourceCoordinate.Create(id, version);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The package manifest contains an invalid coordinate.",
                exception);
        }
        return new LocalPackageArchive(
            coordinate,
            id,
            version,
            GetOptionalSingleValue(metadata, "description"),
            GetOptionalSingleValue(metadata, "tags"),
            content);
    }

    private static string GetSingleValue(
        XElement metadata,
        string localName) =>
        GetOptionalSingleValue(metadata, localName)
        ?? throw new InvalidDataException(
            "The package manifest is missing coordinate metadata.");

    private static string? GetOptionalSingleValue(
        XElement metadata,
        string localName)
    {
        XElement[] matches = metadata.Elements()
            .Where(
                element => element.Name.LocalName.Equals(
                    localName,
                    StringComparison.Ordinal))
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Value,
            _ => throw new InvalidDataException(
                "The package manifest contains duplicate metadata."),
        };
    }

    private sealed record ZipDirectory(
        int EntryCount,
        long ByteLength,
        long Offset);

    private sealed record CentralDirectoryEntry(
        ushort Flags,
        ushort Method,
        uint Crc,
        uint CompressedLength,
        uint ExpandedLength,
        uint LocalHeaderOffset,
        byte[] Name);
}
