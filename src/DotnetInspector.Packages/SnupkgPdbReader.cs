using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;

namespace DotnetInspector.Packages;

/// <summary>
/// Outcome of scanning a symbol package (.snupkg) for a matching Portable PDB.
/// </summary>
/// <param name="PdbBytes">
/// The bytes of the matching Portable PDB, or <c>null</c> when none was found.
/// </param>
/// <param name="WindowsPdbDetected">
/// True when a Windows (non-portable) PDB for the assembly was seen. Windows
/// PDBs are not supported; this signals the caller so it can report accurately.
/// </param>
/// <param name="InvalidPdbDetected">
/// True when a same-name entry was neither a Portable nor Windows PDB.
/// </param>
/// <param name="MismatchedPortablePdbDetected">
/// True when a valid same-name Portable PDB belonged to another assembly
/// identity.
/// </param>
public readonly record struct SnupkgPdbResult(
    byte[]? PdbBytes,
    bool WindowsPdbDetected,
    bool InvalidPdbDetected,
    bool MismatchedPortablePdbDetected);

/// <summary>
/// Host-neutral extraction of a Portable PDB from a symbol package (.snupkg)
/// stream. Parses the archive entirely in memory — no temporary directories or
/// on-disk extraction — so it runs identically on desktop and in a browser/WASM
/// sandbox. Persistence of the resulting bytes is the caller's concern (see
/// <see cref="IPdbStore"/>).
/// </summary>
public static class SnupkgPdbReader
{
    private enum PdbHeaderKind { Unknown, Portable, Windows }
    internal enum PortablePdbIdentityResult { Match, Mismatch, Invalid }

    /// <summary>
    /// Scans <paramref name="snupkg"/> for a Portable PDB named
    /// <c>{assemblyName}.pdb</c> whose debug identity matches
    /// <paramref name="expectedGuid"/> and, when supplied,
    /// <paramref name="expectedStamp"/>, returning its bytes on success.
    /// </summary>
    /// <param name="snupkg">A readable stream over the .snupkg (ZIP) content.</param>
    /// <param name="assemblyName">Assembly name without extension (e.g. <c>System.Text.Json</c>).</param>
    /// <param name="expectedGuid">The PDB debug identity GUID that must match.</param>
    /// <param name="log">Optional diagnostic callback.</param>
    /// <param name="expectedStamp">
    /// The Portable PDB content-id stamp that must match, or <c>null</c> for the
    /// GUID-only compatibility behavior.
    /// </param>
    /// <remarks>
    /// <c>SnupkgPdbReaderTests.ExtractPortablePdb_MatchingGuidWithMismatchedStamp_ReturnsNull</c>
    /// gates comparison of the complete Portable PDB content identity.
    /// </remarks>
    public static SnupkgPdbResult ExtractPortablePdb(
        Stream snupkg,
        string assemblyName,
        Guid expectedGuid,
        Action<string>? log = null,
        uint? expectedStamp = null,
        SymbolAcquisitionLimits? limits = null) =>
        ExtractPortablePdbCancelable(
            snupkg,
            assemblyName,
            expectedGuid,
            log,
            expectedStamp,
            limits,
            CancellationToken.None);

    internal static SnupkgPdbResult ExtractPortablePdbCancelable(
        Stream snupkg,
        string assemblyName,
        Guid expectedGuid,
        Action<string>? log,
        uint? expectedStamp,
        SymbolAcquisitionLimits? limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snupkg);
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);

        var pdbFileName = $"{assemblyName}.pdb";
        bool windowsPdbDetected = false;
        bool invalidPdbDetected = false;
        bool mismatchedPortablePdbDetected = false;
        long expandedPdbBytes = 0;

        if (limits is not null)
        {
            if (!snupkg.CanSeek
                || snupkg.Length > limits.MaxSymbolPackageBytes)
            {
                throw new InvalidDataException(
                    "The symbol package exceeds the configured byte limit.");
            }
            ValidateArchiveEntryCount(
                snupkg,
                limits.MaxSymbolPackageEntries);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(snupkg, ZipArchiveMode.Read, leaveOpen: true);
        if (limits is not null
            && archive.Entries.Count > limits.MaxSymbolPackageEntries)
        {
            throw new InvalidDataException(
                "The symbol package exceeds the configured archive-entry limit.");
        }

        // Match by file name in any directory, mirroring the desktop behavior of
        // Directory.GetFiles(root, "{assembly}.pdb", AllDirectories). Order by the
        // full entry path (ordinal, case-insensitive) for a stable selection.
        var candidates = archive.Entries
            .Where(entry => Path.GetFileName(entry.FullName)
                .Equals(pdbFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long maxPdbBytes =
                limits?.MaxPortablePdbBytes
                ?? SymbolPackageDownloader.DefaultMaximumSymbolBytes;
            // ZipArchiveEntry.Length is a signed value copied verbatim from the
            // archive's ZIP64 extra field, so a hostile package can declare a
            // negative length that clears every ">" ceiling and then narrows,
            // unchecked, to a large positive allocation. Reject it explicitly,
            // as Storage/InMemoryPackageContent does at its own allocation site.
            ValidateDeclaredPdbLength(entry.Length, maxPdbBytes);
            if (limits is not null
                && expandedPdbBytes
                    > limits.MaxExpandedPdbBytes - entry.Length)
            {
                throw new InvalidDataException(
                    "Symbol-package PDB expansion exceeds the configured aggregate byte limit.");
            }
            expandedPdbBytes += entry.Length;

            byte[] bytes =
                GC.AllocateUninitializedArray<byte>((int)entry.Length);
            using (var entryStream = entry.Open())
            {
                ReadExactly(
                    entryStream,
                    bytes,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (entryStream.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        "A symbol-package PDB exceeds its declared length.");
                }
            }

            var header = CheckPdbHeader(bytes);
            if (header == PdbHeaderKind.Windows)
            {
                windowsPdbDetected = true;
                continue;
            }

            if (header != PdbHeaderKind.Portable)
            {
                invalidPdbDetected = true;
                continue;
            }

            using var pdbStream =
                new MemoryStream(bytes, writable: false);
            PortablePdbIdentityResult identity =
                ClassifyPortablePdbIdentity(
                    pdbStream,
                    expectedGuid,
                    expectedStamp,
                    log);
            if (identity == PortablePdbIdentityResult.Match)
            {
                return new SnupkgPdbResult(
                    bytes,
                    windowsPdbDetected,
                    invalidPdbDetected,
                    mismatchedPortablePdbDetected);
            }

            if (identity == PortablePdbIdentityResult.Mismatch)
                mismatchedPortablePdbDetected = true;
            else
                invalidPdbDetected = true;
        }

        return new SnupkgPdbResult(
            null,
            windowsPdbDetected,
            invalidPdbDetected,
            mismatchedPortablePdbDetected);
    }

    /// <summary>
    /// Rejects a declared symbol-package PDB length that must not reach the
    /// allocation site. The lower bound matters as much as the ceilings:
    /// <see cref="ZipArchiveEntry.Length"/> is a signed value taken verbatim
    /// from the archive's ZIP64 extra field, and a negative one clears every
    /// <c>&gt;</c> comparison before narrowing, unchecked, to a large positive
    /// allocation.
    /// </summary>
    /// <remarks>
    /// <c>SnupkgPdbReaderTests.ValidateDeclaredPdbLength_RejectsNegativeDeclaredLength</c>
    /// gates the lower bound on every runtime;
    /// <c>SnupkgPdbReaderTests.ExtractPortablePdb_RejectsNegativeZip64DeclaredLength</c>
    /// is the end-to-end canary. The end-to-end case is only load-bearing on
    /// runtimes whose <see cref="ZipArchive"/> surfaces a negative length —
    /// .NET 10, which official builds target — because .NET 11 rejects the
    /// archive while reading the central directory.
    /// </remarks>
    internal static void ValidateDeclaredPdbLength(
        long declaredLength,
        long maxPdbBytes)
    {
        if (declaredLength < 0
            || declaredLength > maxPdbBytes
            || declaredLength > Array.MaxLength)
        {
            throw new InvalidDataException(
                "A symbol-package PDB exceeds the configured byte limit.");
        }
    }

    static void ReadExactly(
        Stream source,
        byte[] destination,
        CancellationToken cancellationToken)    {
        int offset = 0;
        while (offset < destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(
                destination,
                offset,
                Math.Min(81920, destination.Length - offset));
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    static void ValidateArchiveEntryCount(
        Stream snupkg,
        int maxEntries)
    {
        if (!snupkg.CanSeek)
        {
            throw new InvalidDataException(
                "Bounded symbol-package inspection requires a seekable stream.");
        }

        const uint EndOfCentralDirectorySignature = 0x06054b50;
        const int MinimumRecordLength = 22;
        const int MaximumCommentLength = ushort.MaxValue;
        long originalPosition = snupkg.Position;
        try
        {
            int tailLength =
                (int)Math.Min(
                    snupkg.Length,
                    MinimumRecordLength + MaximumCommentLength);
            byte[] tail = GC.AllocateUninitializedArray<byte>(tailLength);
            snupkg.Position = snupkg.Length - tailLength;
            snupkg.ReadExactly(tail);

            for (int offset = tail.Length - MinimumRecordLength;
                 offset >= 0;
                 offset--)
            {
                ReadOnlySpan<byte> record = tail.AsSpan(offset);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record)
                        != EndOfCentralDirectorySignature)
                {
                    continue;
                }

                ushort commentLength =
                    BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
                if (offset + MinimumRecordLength + commentLength
                    != tail.Length)
                {
                    continue;
                }

                ushort diskNumber =
                    BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
                ushort centralDirectoryDisk =
                    BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
                ushort entriesOnDisk =
                    BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
                ushort entryCount =
                    BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
                uint centralDirectorySize =
                    BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                uint centralDirectoryOffset =
                    BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
                if (diskNumber == ushort.MaxValue
                    || centralDirectoryDisk == ushort.MaxValue
                    || entriesOnDisk == ushort.MaxValue
                    || entryCount == ushort.MaxValue
                    || centralDirectorySize == uint.MaxValue
                    || centralDirectoryOffset == uint.MaxValue)
                {
                    throw new InvalidDataException(
                        "ZIP64 symbol packages are unavailable under bounded inspection.");
                }
                if (entryCount > maxEntries)
                {
                    throw new InvalidDataException(
                        "The symbol package exceeds the configured archive-entry limit.");
                }

                snupkg.Position = originalPosition;
                return;
            }

            throw new InvalidDataException(
                "The symbol package has no valid end-of-central-directory record.");
        }
        finally
        {
            snupkg.Position = originalPosition;
        }
    }

    /// <summary>
    /// Classifies a PDB payload by its signature: <c>BSJB</c> for Portable,
    /// <c>Micr</c> for a Windows PDB.
    /// </summary>
    internal static bool IsPortablePdb(ReadOnlySpan<byte> bytes) =>
        CheckPdbHeader(bytes) == PdbHeaderKind.Portable;

    /// <summary>
    /// True when <paramref name="bytes"/> is a Windows (non-portable) PDB.
    /// </summary>
    internal static bool IsWindowsPdb(ReadOnlySpan<byte> bytes) =>
        CheckPdbHeader(bytes) == PdbHeaderKind.Windows;

    /// <summary>
    /// Reads the leading signature of <paramref name="stream"/> (from its
    /// current position) and classifies it as a Portable or Windows PDB.
    /// </summary>
    internal static (bool Portable, bool Windows) ClassifyHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        int read = stream.ReadAtLeast(header, 4, throwOnEndOfStream: false);
        if (read < 4)
            return (false, false);

        var kind = CheckPdbHeader(header);
        return (kind == PdbHeaderKind.Portable, kind == PdbHeaderKind.Windows);
    }

    private static PdbHeaderKind CheckPdbHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
            return PdbHeaderKind.Unknown;

        if (bytes[0] == 'B' && bytes[1] == 'S' && bytes[2] == 'J' && bytes[3] == 'B')
            return PdbHeaderKind.Portable;
        if (bytes[0] == 'M' && bytes[1] == 'i' && bytes[2] == 'c' && bytes[3] == 'r')
            return PdbHeaderKind.Windows;

        return PdbHeaderKind.Unknown;
    }

    internal static PortablePdbIdentityResult ClassifyPortablePdbIdentity(
        Stream pdbStream,
        Guid expectedGuid,
        uint? expectedStamp,
        Action<string>? log)
    {
        try
        {
            using var provider =
                MetadataReaderProvider.FromPortablePdbStream(
                    pdbStream,
                    MetadataStreamOptions.PrefetchMetadata);
            var reader = provider.GetMetadataReader();
            var id = reader.DebugMetadataHeader?.Id;
            int requiredLength = expectedStamp.HasValue ? 20 : 16;
            if (id is not { Length: var length }
                || length < requiredLength)
            {
                return PortablePdbIdentityResult.Invalid;
            }

            Span<byte> guidBytes = stackalloc byte[16];
            id.Value.AsSpan(0, 16).CopyTo(guidBytes);
            var actualGuid = new Guid(guidBytes);
            uint? actualStamp = id.Value.Length >= 20
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    id.Value.AsSpan(16, 4))
                : null;
            if (actualGuid == expectedGuid
                && (!expectedStamp.HasValue
                    || actualStamp == expectedStamp))
            {
                return PortablePdbIdentityResult.Match;
            }

            log?.Invoke(
                "Skipping mismatched Portable PDB: expected "
                + FormatIdentity(expectedGuid, expectedStamp)
                + "; found "
                + FormatIdentity(actualGuid, actualStamp));
            return PortablePdbIdentityResult.Mismatch;
        }
        catch (Exception ex)
            when (ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            log?.Invoke(
                $"Could not read Portable PDB identity: {ex.Message}");
            return PortablePdbIdentityResult.Invalid;
        }
    }

    private static string FormatIdentity(
        Guid guid,
        uint? stamp)
        => stamp.HasValue
            ? $"{guid:D}/{stamp.Value:x8}"
            : guid.ToString("D");
}
