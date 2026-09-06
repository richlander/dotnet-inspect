using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

/// <summary>
/// Filesystem-backed <see cref="IPackageContent"/> over an extracted package
/// directory. Entry reads map <c>/</c>-separated relative paths onto files under
/// <see cref="RootPath"/>. This is the desktop content: the extracted directory
/// itself is what the CLI's existing consumers open by path.
/// </summary>
/// <remarks>
/// The extracted file length is the declared entry length, so this content also
/// implements <see cref="IPackageContentEntryManifest"/>. Without it, a bounded
/// caller would only learn that an entry is over budget from the
/// <see cref="InvalidDataException"/> raised inside
/// <see cref="TryOpenEntry(string, long, out Stream?)"/>, which is
/// indistinguishable from an unrelated read failure. Gated by
/// <c>FileSystemPackageContentManifestTests.FileSystemLengthUsesManifestPreflight</c>.
/// </remarks>
public sealed class FileSystemPackageContent :
    IPackageContent,
    IPackageContentEntryManifest
{
    private readonly string _root;

    public FileSystemPackageContent(
        string rootPath,
        string? nupkgPath,
        bool fromCache,
        string producerKey,
        bool requiresArchiveTreeMatch = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentException.ThrowIfNullOrEmpty(producerKey);
        _root = rootPath;
        RootPath = rootPath;
        NupkgPath = nupkgPath;
        FromCache = fromCache;
        ProducerKey = producerKey;
        RequiresArchiveTreeMatch = requiresArchiveTreeMatch;
    }

    /// <inheritdoc />
    public string? RootPath { get; }

    /// <inheritdoc />
    public string? NupkgPath { get; }

    /// <inheritdoc />
    public bool FromCache { get; }

    /// <inheritdoc />
    public string ProducerKey { get; }

    /// <inheritdoc />
    public bool RequiresArchiveTreeMatch { get; }

    /// <inheritdoc />
    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        if (NupkgPath is null || !File.Exists(NupkgPath))
        {
            stream = null;
            return false;
        }

        stream = File.OpenRead(NupkgPath);
        return true;
    }

    /// <inheritdoc />
    public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
    {
        var path = ResolveEntryPath(relativePath);
        if (!File.Exists(path))
        {
            stream = null;
            return false;
        }

        stream = File.OpenRead(path);
        return true;
    }

    /// <inheritdoc />
    public bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        string path = ResolveEntryPath(relativePath);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            stream = null;
            return false;
        }
        if (file.Length > maxExpandedBytes)
            throw new InvalidDataException("Package entry exceeds the configured byte limit.");

        stream = file.OpenRead();
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateEntries()
    {
        if (!Directory.Exists(_root))
            yield break;

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    /// <inheritdoc />
    public bool TryGetEntryLength(string relativePath, out long length)
    {
        var file = new FileInfo(ResolveEntryPath(relativePath));
        if (!file.Exists)
        {
            length = 0;
            return false;
        }

        length = file.Length;
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths()
    {
        if (!Directory.Exists(_root))
            return [];

        var entries = new List<PackageContentEntry>();
        foreach (var file in Directory.EnumerateFiles(
            _root,
            "*",
            SearchOption.AllDirectories))
        {
            entries.Add(
                new PackageContentEntry(
                    Path.GetRelativePath(_root, file)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    new FileInfo(file).Length));
        }

        return entries;
    }

    private string ResolveEntryPath(string relativePath)
        => StorePath.ResolveUnderRoot(_root, relativePath);
}
