using System.Xml;
using System.Xml.Linq;
using BinaryFetch;
using ZipFetch;

namespace NuGetFetch;

internal sealed record LocalPackageArchive(
    PackageSourceCoordinate Coordinate,
    string AuthoredId,
    string AuthoredVersion,
    string? Description,
    string? Tags,
    ReadOnlyMemory<byte> Manifest);

/// <summary>
/// Reads one local package's root manifest through the shared ZIP reader,
/// projecting the reader's refusals into the local source's own outcomes:
/// a crossed bound is the local limit exception, malformed or unsupported
/// structure is invalid data, and an interrupted read is an I/O failure that
/// first re-checks the operation deadline.
/// </summary>
internal static class LocalPackageArchiveReader
{
    private const ushort SupportedManifestFlags =
        (1 << 1) | (1 << 2) | (1 << 3) | (1 << 11);

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

        var limits = new ZipReadLimits(
            maxArchiveBytes: options.MaxPackageBytes,
            maxEntryCount: options.MaxArchiveEntries,
            maxDirectoryBytes: options.MaxCentralDirectoryBytes,
            maxExpandedBytes: Math.Max(1, options.MaxManifestBytes));
        var source = new StreamRandomAccessSource(stream, leaveOpen: true);
        ZipDirectory directory = await ProjectAsync(
            () => ZipArchiveReader.ReadDirectoryAsync(
                source,
                limits,
                operation.OperationToken),
            operation).ConfigureAwait(false);
        operation.ThrowIfExpired();

        ZipEntry manifest = SelectManifest(
            directory,
            options,
            ledger.RemainingManifestBytes);
        long maximumExpandedBytes = Math.Min(
            options.MaxManifestBytes,
            ledger.RemainingManifestBytes);
        byte[] content = await ProjectAsync(
            () => ZipArchiveReader.ReadEntryAsync(
                source,
                directory,
                manifest,
                limits,
                maximumExpandedBytes,
                operation.OperationToken),
            operation).ConfigureAwait(false);
        ledger.ChargeManifestBytes(content.Length);
        operation.ThrowIfExpired();

        try
        {
            return ParseManifest(content);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException(
                "The package manifest is not valid XML.",
                exception);
        }
    }

    private static ZipEntry SelectManifest(
        ZipDirectory directory,
        LocalPackageSourceOptions options,
        long remainingManifestBytes)
    {
        ZipEntry? selected = null;
        foreach (ZipEntry entry in directory.Entries)
        {
            if (!IsRootManifest(entry.NameBytes.Span))
                continue;
            if (selected is not null)
            {
                throw new InvalidDataException(
                    "The package archive contains multiple root manifests.");
            }

            if (HasUnsupportedManifestFlags(entry.Flags, entry.Method)
                || entry.Method is not 0 and not 8)
            {
                throw new InvalidDataException(
                    "The package archive uses unsupported manifest flags.");
            }

            if (entry.CompressedLength > options.MaxManifestBytes
                || entry.ExpandedLength > options.MaxManifestBytes
                || entry.ExpandedLength > remainingManifestBytes)
            {
                throw new LocalPackageSourceLimitExceededException();
            }

            selected = entry;
        }

        return selected
            ?? throw new InvalidDataException(
                "The package archive does not contain a root manifest.");
    }

    /// <summary>
    /// Runs one shared-reader step and projects its refusals into the local
    /// source's outcomes, re-checking the operation deadline before reporting
    /// an interruption.
    /// </summary>
    private static async Task<T> ProjectAsync<T>(
        Func<Task<T>> read,
        NuGetOperationDeadline operation)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (ZipReadException exception)
        {
            operation.ThrowIfExpired();
            throw exception.Failure switch
            {
                ZipReadFailure.OverBound => new LocalPackageSourceLimitExceededException(),
                _ => new InvalidDataException(exception.Message, exception),
            };
        }
        catch (RangeFetchException exception)
        {
            operation.ThrowIfExpired();
            throw new InvalidDataException(
                "The package archive ended before its declared extent.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            operation.ThrowIfExpired();
            throw new IOException(
                "The local package source interrupted an archive read.",
                exception);
        }
        catch (IOException)
        {
            operation.ThrowIfExpired();
            throw;
        }
    }

    private static bool HasUnsupportedManifestFlags(
        ushort flags,
        ushort method) =>
        (flags & ~SupportedManifestFlags) != 0
        || method != 8 && (flags & ((1 << 1) | (1 << 2))) != 0;

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
}
