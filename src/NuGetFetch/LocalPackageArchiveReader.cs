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
    private const int EndOfCentralDirectoryLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;

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

        stream.Position = 0;
        operation.ThrowIfExpired();
        try
        {
            using var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: true);
            operation.ThrowIfExpired();
            if (archive.Entries.Count != directory.EntryCount)
            {
                throw new InvalidDataException(
                    "The package archive central directory is inconsistent.");
            }

            ZipArchiveEntry? manifest = null;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                operation.ThrowIfExpired();
                if (!IsRootManifest(entry.FullName))
                    continue;

                if (manifest is not null)
                {
                    throw new InvalidDataException(
                        "The package archive contains multiple root manifests.");
                }

                manifest = entry;
            }

            if (manifest is null)
            {
                throw new InvalidDataException(
                    "The package archive does not contain a root manifest.");
            }
            if (manifest.CompressedLength > options.MaxManifestBytes
                || manifest.Length > options.MaxManifestBytes)
            {
                throw new LocalPackageSourceLimitExceededException();
            }

            ledger.ChargeManifestBytes(manifest.Length);
            byte[] content = new byte[checked((int)manifest.Length)];
            await using (Stream manifestStream = manifest.Open())
            {
                await ReadExactlyAsync(
                    manifestStream,
                    content,
                    operation).ConfigureAwait(false);
            }

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

            return new ZipDirectory(entryCount, directoryLength);
        }

        throw new InvalidDataException(
            "The package archive has no valid end-of-central-directory record.");
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

            if (read == 0)
            {
                throw new InvalidDataException(
                    "The package archive ended before its declared extent.");
            }

            offset += read;
        }

        operation.ThrowIfExpired();
    }

    private static bool IsRootManifest(string name) =>
        !name.Contains('/')
        && !name.Contains('\\')
        && name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);

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

    private sealed record ZipDirectory(int EntryCount, long ByteLength);
}
