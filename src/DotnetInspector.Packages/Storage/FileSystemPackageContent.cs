using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

/// <summary>
/// Filesystem-backed <see cref="IPackageContent"/> over an extracted package
/// directory. Entry reads map <c>/</c>-separated relative paths onto files under
/// <see cref="RootPath"/>. This is the desktop content: the extracted directory
/// itself is what the CLI's existing consumers open by path.
/// </summary>
public sealed class FileSystemPackageContent : IPackageContent
{
    private readonly string _root;

    public FileSystemPackageContent(string rootPath, string? nupkgPath, bool fromCache)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _root = rootPath;
        RootPath = rootPath;
        NupkgPath = nupkgPath;
        FromCache = fromCache;
    }

    /// <inheritdoc />
    public string? RootPath { get; }

    /// <inheritdoc />
    public string? NupkgPath { get; }

    /// <inheritdoc />
    public bool FromCache { get; }

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
    public IEnumerable<string> EnumerateEntries()
    {
        if (!Directory.Exists(_root))
            yield break;

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private string ResolveEntryPath(string relativePath)
        => StorePath.ResolveUnderRoot(_root, relativePath);
}
