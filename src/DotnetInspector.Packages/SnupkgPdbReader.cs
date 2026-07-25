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
public readonly record struct SnupkgPdbResult(byte[]? PdbBytes, bool WindowsPdbDetected);

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

    /// <summary>
    /// Scans <paramref name="snupkg"/> for a Portable PDB named
    /// <c>{assemblyName}.pdb</c> whose debug identity matches
    /// <paramref name="expectedGuid"/>, returning its bytes on success.
    /// </summary>
    /// <param name="snupkg">A readable stream over the .snupkg (ZIP) content.</param>
    /// <param name="assemblyName">Assembly name without extension (e.g. <c>System.Text.Json</c>).</param>
    /// <param name="expectedGuid">The PDB debug identity GUID that must match.</param>
    /// <param name="log">Optional diagnostic callback.</param>
    public static SnupkgPdbResult ExtractPortablePdb(
        Stream snupkg,
        string assemblyName,
        Guid expectedGuid,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(snupkg);
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);

        var pdbFileName = $"{assemblyName}.pdb";
        bool windowsPdbDetected = false;

        using var archive = new ZipArchive(snupkg, ZipArchiveMode.Read, leaveOpen: true);

        // Match by file name in any directory, mirroring the desktop behavior of
        // Directory.GetFiles(root, "{assembly}.pdb", AllDirectories). Order by the
        // full entry path (ordinal, case-insensitive) for a stable selection.
        var candidates = archive.Entries
            .Where(entry => Path.GetFileName(entry.FullName)
                .Equals(pdbFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in candidates)
        {
            byte[] bytes;
            using (var entryStream = entry.Open())
            using (var buffer = new MemoryStream())
            {
                entryStream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            var header = CheckPdbHeader(bytes);
            if (header == PdbHeaderKind.Windows)
            {
                windowsPdbDetected = true;
                continue;
            }

            if (header != PdbHeaderKind.Portable)
                continue;

            if (PortablePdbMatchesGuid(bytes, expectedGuid, log))
                return new SnupkgPdbResult(bytes, windowsPdbDetected);
        }

        return new SnupkgPdbResult(null, windowsPdbDetected);
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

    private static bool PortablePdbMatchesGuid(byte[] pdbBytes, Guid expectedGuid, Action<string>? log)
    {
        try
        {
            using var stream = new MemoryStream(pdbBytes, writable: false);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            var id = reader.DebugMetadataHeader?.Id;
            if (id is not { Length: >= 16 })
                return false;

            Span<byte> guidBytes = stackalloc byte[16];
            id.Value.AsSpan(0, 16).CopyTo(guidBytes);
            var actualGuid = new Guid(guidBytes);
            if (actualGuid == expectedGuid)
                return true;

            log?.Invoke($"Skipping mismatched PDB from symbol package: expected {expectedGuid:D}; found {actualGuid:D}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not read PDB identity from symbol package entry: {ex.Message}");
            return false;
        }
    }
}
