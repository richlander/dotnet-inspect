using System.Security.Cryptography;
using System.Text;

namespace DotnetInspector.Packages;

/// <summary>
/// The durable entry cache of ranged reads, beside an authority's complete
/// store: an archive's directory region and the expanded entries read from it,
/// each immutable once published (docs/design/package-cache-policy.md).
/// </summary>
internal interface IPackageEntryStore
{
    /// <summary>Whether this store keeps ranged reads at all.</summary>
    bool KeepsEntries { get; }

    bool TryReadDirectory(
        string packageId,
        string version,
        out ReadOnlyMemory<byte> region,
        out long archiveLength);

    void PublishDirectory(
        string packageId,
        string version,
        ReadOnlyMemory<byte> region,
        long archiveLength);

    bool TryReadEntry(
        string packageId,
        string version,
        string entryPath,
        out byte[] content);

    void PublishEntry(
        string packageId,
        string version,
        string entryPath,
        ReadOnlyMemory<byte> content);
}

internal static class PackageEntryStoreNames
{
    /// <summary>The versioned cache family of the entry cache.</summary>
    internal const string Category = "package-authority-entries-v1";

    internal const string CategoryPrefix = "package-authority-entries-v";

    /// <summary>
    /// An entry's file name: the lowercase hexadecimal SHA-256 of its exact
    /// archive path bytes. An archive path is untrusted package input and
    /// never becomes a filesystem path; the digest is the same on
    /// case-sensitive and case-insensitive filesystems.
    /// </summary>
    internal static string EntryFileName(string entryPath) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(entryPath)));
}
